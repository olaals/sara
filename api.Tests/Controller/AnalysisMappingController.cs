using System;
using System.Threading.Tasks;
using api.Database.Context;
using api.Database.Models;
using api.Services;
using api.Tests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace api.Controllers.Tests
{
    public class AnalysisMappingControllerTest
    {
        private readonly PlantDataService _plantDataService;
        private readonly AnalysisMappingService _analysisMappingService;
        private readonly AnalysisMappingController _analysisMappingController;

        private static SaraDbContext CreateInMemoryContext()
        {
            var options = new DbContextOptionsBuilder<SaraDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new SaraDbContext(options);
        }

        public AnalysisMappingControllerTest()
        {
            var context = CreateInMemoryContext();

            var configurationMock = new Mock<IConfiguration>();
            var storageSectionMock = new Mock<IConfigurationSection>();
            storageSectionMock.Setup(s => s["RawStorageAccount"]).Returns("dummyRawStorageAccount");
            storageSectionMock
                .Setup(s => s["AnonStorageAccount"])
                .Returns("dummyAnonStorageAccount");
            storageSectionMock.Setup(s => s["VisStorageAccount"]).Returns("dummyVisStorageAccount");
            configurationMock
                .Setup(c => c.GetSection("Storage"))
                .Returns(storageSectionMock.Object);

            var blobService = new BlobService(
                new Mock<ILogger<BlobService>>().Object,
                null!,
                configurationMock.Object
            );

            _analysisMappingService = new AnalysisMappingService(
                context,
                new Mock<ILogger<AnalysisMappingService>>().Object
            );

            _plantDataService = new PlantDataService(
                context,
                _analysisMappingService,
                blobService,
                new Mock<ILogger<PlantDataService>>().Object
            );

            _analysisMappingController = new AnalysisMappingController(
                new Mock<ILogger<AnalysisMappingController>>().Object,
                _analysisMappingService,
                _plantDataService
            );
        }

        [Fact]
        public async Task AddOrCreateAnalysisMapping_AnalysisMappingAdded_WhenPlantDataExist()
        {
            //Arrange
            await _plantDataService.CreatePlantData(
                inspectionId: "dummyInsp-001",
                installationCode: "dummyInst-001",
                tagID: "dummyTAG-001",
                inspectionDescription: "Oil Level",
                rawStorageAccount: "dummyRawStorageAccount",
                rawBlobContainer: "dummtContainer",
                rawBlobName: "dummtBlobName.jpg"
            );

            //Act
            await _analysisMappingController.AddOrCreateAnalysisMapping(
                "dummyTAG-001",
                "Oil Level",
                AnalysisType.ConstantLevelOiler
            );

            //Assert
            var mapping = await _analysisMappingService.ReadByTagAndInspectionDescription(
                "dummyTAG-001",
                "Oil Level"
            );
            Assert.NotNull(mapping);
            Assert.Contains(AnalysisType.ConstantLevelOiler, mapping.AnalysesToBeRun);

            var updatedPlantData = await _plantDataService.ReadByInspectionId("dummyInsp-001");
            Assert.NotNull(updatedPlantData);
            Assert.NotNull(updatedPlantData.GetWorkflowStep(WorkflowStepType.CLOEAnalysis));
        }

        [Fact]
        public async Task AddOrCreateAnalysisMapping_Conflict_WhenAnalysisMappingAlreadyExists()
        {
            //Arrange
            await _plantDataService.CreatePlantData(
                inspectionId: "dummyInsp-001",
                installationCode: "dummyInst-001",
                tagID: "dummyTAG-001",
                inspectionDescription: "Oil Level",
                rawStorageAccount: "dummyRawStorageAccount",
                rawBlobContainer: "dummtContainer",
                rawBlobName: "dummtBlobName.jpg"
            );

            //Act
            await _analysisMappingController.AddOrCreateAnalysisMapping(
                "dummyTAG-001",
                "Oil Level",
                AnalysisType.ConstantLevelOiler
            );

            IActionResult result = await _analysisMappingController.AddOrCreateAnalysisMapping(
                "dummyTAG-001",
                "Oil Level",
                AnalysisType.ConstantLevelOiler
            );

            var statusResult = Assert.IsType<ConflictObjectResult>(result);
            Assert.Equal(409, statusResult.StatusCode);

            //Assert
            var mapping = await _analysisMappingService.ReadByTagAndInspectionDescription(
                "dummyTAG-001",
                "Oil Level"
            );
            Assert.NotNull(mapping);
            Assert.Contains(AnalysisType.ConstantLevelOiler, mapping.AnalysesToBeRun);

            var updatedPlantData = await _plantDataService.ReadByInspectionId("dummyInsp-001");
            Assert.NotNull(updatedPlantData);
            Assert.NotNull(updatedPlantData.GetWorkflowStep(WorkflowStepType.CLOEAnalysis));
        }
    }
}

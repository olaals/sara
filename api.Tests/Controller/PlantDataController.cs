using System;
using System.Threading.Tasks;
using api.Controllers.Models;
using api.Database.Context;
using api.Database.Models;
using api.Services;
using api.Tests;
using api.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace api.Controllers.Tests
{
    public class PlantDataControllerTest
    {
        private readonly PlantDataService _plantDataService;
        private readonly AnalysisMappingService _analysisMappingService;
        private readonly PlantDataController _plantDataController;

        private static SaraDbContext CreateInMemoryContext()
        {
            var options = new DbContextOptionsBuilder<SaraDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new SaraDbContext(options);
        }

        public PlantDataControllerTest()
        {
            var context = CreateInMemoryContext();
            var loggerServiceMock = new Mock<ILogger<PlantDataService>>();
            var loggerControllerMock = new Mock<ILogger<PlantDataController>>();
            var loggerAnalysisMappingServiceMock = new Mock<ILogger<AnalysisMappingService>>();
            var blobServiceMock = new Mock<IBlobService>();
            blobServiceMock
                .Setup(service =>
                    service.CreateRawBlobStorageLocation(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    )
                )
                .Returns(
                    (string storageAccount, string blobContainer, string blobName) =>
                        new BlobStorageLocation
                        {
                            StorageAccount = storageAccount,
                            BlobContainer = blobContainer,
                            BlobName = blobName,
                        }
                );
            blobServiceMock
                .Setup(service =>
                    service.CreateAnonymizedBlobStorageLocation(
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    )
                )
                .Returns(
                    (string blobContainer, string blobName) =>
                        new BlobStorageLocation
                        {
                            StorageAccount = "dummy-anonymized",
                            BlobContainer = blobContainer,
                            BlobName = blobName,
                        }
                );
            blobServiceMock
                .Setup(service =>
                    service.CreateVisualizedBlobStorageLocation(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    )
                )
                .Returns(
                    (string blobContainer, string blobName, string prefix) =>
                        new BlobStorageLocation
                        {
                            StorageAccount = $"dummy-{prefix}",
                            BlobContainer = blobContainer,
                            BlobName = blobName,
                        }
                );
            blobServiceMock
                .Setup(service =>
                    service.CreatePreProcessedBlobStorageLocation(
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    )
                )
                .Returns(
                    (string blobContainer, string blobName) =>
                        new BlobStorageLocation
                        {
                            StorageAccount = "dummy-preprocessed",
                            BlobContainer = blobContainer,
                            BlobName = blobName,
                        }
                );

            _analysisMappingService = new AnalysisMappingService(
                context,
                loggerAnalysisMappingServiceMock.Object
            );

            _plantDataService = new PlantDataService(
                context,
                _analysisMappingService,
                blobServiceMock.Object,
                loggerServiceMock.Object
            );

            _plantDataController = new PlantDataController(
                loggerControllerMock.Object,
                _plantDataService
            );
        }

        [Fact]
        public async Task CreatePlantData_ReturnsCreated_WhenPlantDataCreated()
        {
            // Arrange
            var request = new PlantDataRequest
            {
                InspectionId = "dummyInspectionId",
                InstallationCode = "dummyInstallationCode",
                TagId = "dummyTagId",
                InspectionDescription = "dummyInspectionDescription",
                RawDataBlobStorageLocation = new BlobStorageLocation
                {
                    StorageAccount = "dummy",
                    BlobContainer = "dummy",
                    BlobName = "dummy.jpg",
                },
            };
            // Act
            var result = await _plantDataController.CreatePlantData(request);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result);
            Assert.Equal(nameof(_plantDataController.GetPlantDataById), createdResult.ActionName);

            var createdPlantData = createdResult.Value as PlantDataResponse;
            Assert.NotNull(createdPlantData);
            Assert.Equal("dummyInspectionId", createdPlantData.InspectionId);
            Assert.Equal("dummyInstallationCode", createdPlantData.InstallationCode);
            Assert.Equal("dummyTagId", createdPlantData.Tag);
            Assert.Equal("dummyInspectionDescription", createdPlantData.InspectionDescription);
            Assert.NotNull(createdPlantData.Workflow);
            Assert.NotNull(
                createdPlantData.Workflow!.Steps.Find(step =>
                    step.Type == WorkflowStepType.Anonymization
                )
            );
            Assert.Null(
                createdPlantData.Workflow.Steps.Find(step =>
                    step.Type == WorkflowStepType.CLOEAnalysis
                )
            );
            Assert.Null(
                createdPlantData.Workflow.Steps.Find(step =>
                    step.Type == WorkflowStepType.FencillaAnalysis
                )
            );
            Assert.Null(
                createdPlantData.Workflow.Steps.Find(step =>
                    step.Type == WorkflowStepType.ThermalReadingAnalysis
                )
            );
        }

        [Fact]
        public async Task CreatePlantData_AddsAnalysesFromMapping_WhenMappingExists()
        {
            // Arrange
            var request = new PlantDataRequest
            {
                InspectionId = "dummyInspectionId",
                InstallationCode = "dummyInstallationCode",
                TagId = "TAG-001",
                InspectionDescription = "Oil Level",
                RawDataBlobStorageLocation = new BlobStorageLocation
                {
                    StorageAccount = "dummy",
                    BlobContainer = "dummy",
                    BlobName = "dummy.jpg",
                },
            };

            await _analysisMappingService.CreateAnalysisMapping(
                "TAG-001",
                "Oil Level",
                AnalysisType.ConstantLevelOiler
            );

            // Act
            var result = await _plantDataController.CreatePlantData(request);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result);
            var returnedPlantData = Assert.IsType<PlantDataResponse>(createdResult.Value);

            Assert.NotNull(returnedPlantData.Workflow);
            Assert.NotNull(
                returnedPlantData.Workflow!.Steps.Find(step =>
                    step.Type == WorkflowStepType.CLOEAnalysis
                )
            );
            Assert.Null(
                returnedPlantData.Workflow.Steps.Find(step =>
                    step.Type == WorkflowStepType.FencillaAnalysis
                )
            );
            Assert.Null(
                returnedPlantData.Workflow.Steps.Find(step =>
                    step.Type == WorkflowStepType.ThermalReadingAnalysis
                )
            );
        }

        [Fact]
        public async Task CreatePlantData_ReturnsBadRequest_WhenRequiredFieldsAreEmpty()
        {
            // Arrange
            var request = new PlantDataRequest
            {
                InspectionId = "",
                InstallationCode = "",
                TagId = "",
                InspectionDescription = "",
                RawDataBlobStorageLocation = new BlobStorageLocation
                {
                    StorageAccount = "dummy",
                    BlobContainer = "dummy",
                    BlobName = "dummy.jpg",
                },
            };

            // Act
            IActionResult result = await _plantDataController.CreatePlantData(request);

            // Assert
            var statusResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, statusResult.StatusCode);
        }
    }
}

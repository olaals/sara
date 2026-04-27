using System;
using System.Threading.Tasks;
using api.Database.Models;
using api.Services;
using api.Tests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace api.Controllers.Tests
{
    public class TriggerAnalysisControllerTest
    {
        private readonly Mock<ILogger<TriggerAnalysisController>> _loggerMock;
        private readonly Mock<IArgoWorkflowService> _argoWorkflowServiceMock;
        private readonly Mock<IPlantDataService> _plantDataServiceMock;
        private readonly TriggerAnalysisController _triggerAnalysisController;

        public TriggerAnalysisControllerTest()
        {
            _loggerMock = new Mock<ILogger<TriggerAnalysisController>>();
            _argoWorkflowServiceMock = new Mock<IArgoWorkflowService>();
            _plantDataServiceMock = new Mock<IPlantDataService>();
            _triggerAnalysisController = new TriggerAnalysisController(
                _argoWorkflowServiceMock.Object,
                _plantDataServiceMock.Object,
                _loggerMock.Object
            );
        }

        [Fact]
        public async Task TriggerAnonymizer_ReturnsNotFound_WhenPlantDataDoesNotExist()
        {
            var plantDataId = Guid.NewGuid();
            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync((PlantData?)null);

            var result = await _triggerAnalysisController.TriggerAnonymizer(plantDataId);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task TriggerAnonymizer_ReturnsConflict_WhenWorkflowsAlreadyTriggered()
        {
            var plantDataId = Guid.NewGuid();
            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync(
                    WorkflowTestFactory.CreatePlantData(
                        "dummyInspectionId",
                        "dummyInstallationCode",
                        WorkflowTestFactory.CreateAnonymizationStep(status: WorkflowStatus.Started)
                    )
                );

            var result = await _triggerAnalysisController.TriggerAnonymizer(plantDataId);

            var conflictResult = Assert.IsType<ConflictObjectResult>(result);
            Assert.Equal(
                "A workflow in the analysis chain is still running. Wait until the full chain has completed before triggering it again.",
                conflictResult.Value
            );
        }

        [Fact]
        public async Task TriggerAnonymizer_ReturnsConflict_WhenDownstreamWorkflowIsStillRunning()
        {
            var plantDataId = Guid.NewGuid();

            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync(
                    WorkflowTestFactory.CreatePlantData(
                        "dummyInspectionId",
                        "dummyInstallationCode",
                        WorkflowTestFactory.CreateAnonymizationStep(
                            status: WorkflowStatus.ExitSuccess
                        ),
                        WorkflowTestFactory.CreateFencillaStep(status: WorkflowStatus.Started)
                    )
                );

            var result = await _triggerAnalysisController.TriggerAnonymizer(plantDataId);

            var conflictResult = Assert.IsType<ConflictObjectResult>(result);
            Assert.Equal(
                "A workflow in the analysis chain is still running. Wait until the full chain has completed before triggering it again.",
                conflictResult.Value
            );
        }

        [Fact]
        public async Task TriggerAnonymizer_TriggersWorkflow_WhenPlantDataExistsAndNotStarted()
        {
            var plantDataId = Guid.NewGuid();
            var plantData = WorkflowTestFactory.CreatePlantData(
                "dummyInspectionId",
                "dummyInstallationCode",
                WorkflowTestFactory.CreateAnonymizationStep()
            );

            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync(plantData);
            _argoWorkflowServiceMock.Setup(service =>
                service.TriggerAnonymizer(It.IsAny<string>(), It.IsAny<WorkflowStep>())
            );

            var result = await _triggerAnalysisController.TriggerAnonymizer(plantDataId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Workflow chain triggered successfully.", okResult.Value);
        }

        [Fact]
        public async Task TriggerAnonymizer_TriggersWorkflow_WhenWorkflowChainPreviouslySucceeded()
        {
            var plantDataId = Guid.NewGuid();
            var plantData = WorkflowTestFactory.CreatePlantData(
                "dummyInspectionId",
                "dummyInstallationCode",
                WorkflowTestFactory.CreateAnonymizationStep(status: WorkflowStatus.ExitSuccess),
                WorkflowTestFactory.CreateCLOEStep(status: WorkflowStatus.ExitSuccess)
            );
            var anonymizationStep = plantData.GetRequiredWorkflowStep(
                WorkflowStepType.Anonymization,
                plantData.InspectionId
            );

            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync(plantData);

            var result = await _triggerAnalysisController.TriggerAnonymizer(plantDataId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Workflow chain triggered successfully.", okResult.Value);
            _argoWorkflowServiceMock.Verify(
                service =>
                    service.TriggerAnonymizer(
                        plantData.InspectionId,
                        It.Is<WorkflowStep>(step =>
                            step.SourceBlobStorageLocation.BlobName
                                == anonymizationStep.SourceBlobStorageLocation.BlobName
                            && step.DestinationBlobStorageLocation.BlobName
                                == anonymizationStep.DestinationBlobStorageLocation.BlobName
                            && step.Status == anonymizationStep.Status
                        )
                    ),
                Times.Once
            );
        }

        [Fact]
        public async Task TriggerAnonymizer_TriggersWorkflow_WhenWorkflowChainPreviouslyFailed()
        {
            var plantDataId = Guid.NewGuid();
            var plantData = WorkflowTestFactory.CreatePlantData(
                "dummyInspectionId",
                "dummyInstallationCode",
                WorkflowTestFactory.CreateAnonymizationStep(status: WorkflowStatus.ExitFailure),
                WorkflowTestFactory.CreateThermalReadingStep(status: WorkflowStatus.ExitFailure)
            );
            var anonymizationStep = plantData.GetRequiredWorkflowStep(
                WorkflowStepType.Anonymization,
                plantData.InspectionId
            );

            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync(plantData);

            var result = await _triggerAnalysisController.TriggerAnonymizer(plantDataId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Workflow chain triggered successfully.", okResult.Value);
            _argoWorkflowServiceMock.Verify(
                service =>
                    service.TriggerAnonymizer(
                        plantData.InspectionId,
                        It.Is<WorkflowStep>(step =>
                            step.SourceBlobStorageLocation.BlobName
                                == anonymizationStep.SourceBlobStorageLocation.BlobName
                            && step.DestinationBlobStorageLocation.BlobName
                                == anonymizationStep.DestinationBlobStorageLocation.BlobName
                            && step.Status == anonymizationStep.Status
                        )
                    ),
                Times.Once
            );
        }

        [Fact]
        public async Task TriggerAnalysis_ReturnsNotFound_WhenPlantDataDoesNotExist()
        {
            var plantDataId = Guid.NewGuid();

            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync((PlantData?)null);

            var result = await _triggerAnalysisController.TriggerAnalysis(plantDataId);

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal($"Could not find plant data with id {plantDataId}", notFound.Value);
        }

        [Fact]
        public async Task TriggerAnalysis_ReturnsOk_WhenServiceSucceeds()
        {
            var plantDataId = Guid.NewGuid();

            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync(
                    WorkflowTestFactory.CreatePlantData(
                        "inspection-id",
                        "dummy-installation-code",
                        WorkflowTestFactory.CreateAnonymizationStep()
                    )
                );

            var result = await _triggerAnalysisController.TriggerAnalysis(plantDataId);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(
                "Triggering anonymization workflow which will trigger analysis workflows.",
                ok.Value
            );
        }

        [Fact]
        public async Task TriggerAnalysis_ReturnsConflict_WhenAnonymizationStarted()
        {
            var plantDataId = Guid.NewGuid();

            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync(
                    WorkflowTestFactory.CreatePlantData(
                        "inspection-id",
                        "dummy-installation-code",
                        WorkflowTestFactory.CreateAnonymizationStep(status: WorkflowStatus.Started)
                    )
                );

            var result = await _triggerAnalysisController.TriggerAnalysis(plantDataId);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.Equal(
                "Anonymization is still in progress. Analysis workflows will be triggered once it completes.",
                conflict.Value
            );
        }

        [Fact]
        public async Task TriggerAnalysis_ReturnsOk_WhenAnonymizationExitSuccessAndNoConfiguredAnalyses()
        {
            var plantDataId = Guid.NewGuid();

            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync(
                    WorkflowTestFactory.CreatePlantData(
                        "inspection-id",
                        "dummy-installation-code",
                        WorkflowTestFactory.CreateAnonymizationStep(
                            status: WorkflowStatus.ExitSuccess
                        )
                    )
                );

            var result = await _triggerAnalysisController.TriggerAnalysis(plantDataId);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(
                $"No analysis workflows configured for plant data with Id {plantDataId}.",
                ok.Value
            );
        }

        [Fact]
        public async Task TriggerAnalysis_ReturnsOk_WhenAnonymizationExitSuccessAndConfiguredCLOEAnalysis()
        {
            var plantDataId = Guid.NewGuid();

            _plantDataServiceMock
                .Setup(service => service.ReadById(plantDataId))
                .ReturnsAsync(
                    WorkflowTestFactory.CreatePlantData(
                        "inspection-id",
                        "dummy-installation-code",
                        WorkflowTestFactory.CreateAnonymizationStep(
                            status: WorkflowStatus.ExitSuccess
                        ),
                        WorkflowTestFactory.CreateCLOEStep(status: WorkflowStatus.NotStarted)
                    )
                );

            var result = await _triggerAnalysisController.TriggerAnalysis(plantDataId);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Triggered analysis workflows: CLOE analysis", ok.Value);
        }
    }
}

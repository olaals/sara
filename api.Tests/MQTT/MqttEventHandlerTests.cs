using System;
using api.Database.Context;
using api.MQTT;
using api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace api.Tests.MQTT
{
    public class MqttEventHandlerTests
    {
        private static SaraDbContext CreateInMemoryContext()
        {
            var options = new DbContextOptionsBuilder<SaraDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new SaraDbContext(options);
        }

        private class MockedServices
        {
            public Mock<IServiceProvider> ServiceProviderMock { get; } = new();
            public Mock<IAnalysisMappingService> AnalysisMappingServiceMock { get; } = new();
            public Mock<IBlobService> BlobServiceMock { get; } = new();
            public Mock<IArgoWorkflowService> ArgoWorkflowServiceMock { get; } = new();
            public Mock<ILogger<PlantDataService>> LoggerPlantDataServiceMock { get; } = new();
            public Mock<ILogger<MqttEventHandler>> LoggerMock { get; } = new();
            public Mock<ILogger<MqttMessageService>> MqttMessageServiceLoggerMock { get; } = new();
            public MqttEventHandler MqttEventHandler { get; }

            public MockedServices()
            {
                var context = CreateInMemoryContext();

                IPlantDataService plantDataService;
                plantDataService = new PlantDataService(
                    context,
                    AnalysisMappingServiceMock.Object,
                    BlobServiceMock.Object,
                    LoggerPlantDataServiceMock.Object
                );

                // Mock AnalysisMappingService
                ServiceProviderMock
                    .Setup(sp => sp.GetService(typeof(IAnalysisMappingService)))
                    .Returns(AnalysisMappingServiceMock.Object);
                AnalysisMappingServiceMock
                    .Setup(s => s.GetAnalysesToBeRun(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync([]);

                // Mock ArgoWorkflowService to return null and do nothing
                ServiceProviderMock
                    .Setup(sp => sp.GetService(typeof(IArgoWorkflowService)))
                    .Returns(ArgoWorkflowServiceMock.Object);

                var configurationMock = new Mock<IConfiguration>();
                var storageSectionMock = new Mock<IConfigurationSection>();
                storageSectionMock
                    .Setup(s => s["RawStorageAccount"])
                    .Returns("dummyRawStorageAccount");
                storageSectionMock
                    .Setup(s => s["AnonStorageAccount"])
                    .Returns("dummyAnonStorageAccount");
                storageSectionMock
                    .Setup(s => s["VisStorageAccount"])
                    .Returns("dummyVisStorageAccount");
                configurationMock
                    .Setup(c => c.GetSection("Storage"))
                    .Returns(storageSectionMock.Object);

                IMqttMessageService mqttMessageService;
                mqttMessageService = new MqttMessageService(plantDataService);

                ServiceProviderMock
                    .Setup(sp => sp.GetService(typeof(IMqttMessageService)))
                    .Returns(mqttMessageService);

                // Setup scope factory to return service provider
                var scopeMock = new Mock<IServiceScope>();
                scopeMock.Setup(s => s.ServiceProvider).Returns(ServiceProviderMock.Object);
                var scopeFactoryMock = new Mock<IServiceScopeFactory>();
                scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

                MqttEventHandler = new MqttEventHandler(LoggerMock.Object, scopeFactoryMock.Object);
            }
        }

        [Fact]
        public void OnIsarInspectionResult_ValidMessage_TriggersAnalysis()
        {
            // Arrange
            var mockedServices = new MockedServices();

            var dummyMessage = new IsarInspectionResultMessage
            {
                ISARID = "dummy",
                RobotName = "dummy",
                InspectionId = "dummy",
                InspectionDataPath = new InspectionPathMessage
                {
                    StorageAccount = "dummyRawStorageAccount",
                    BlobContainer = "dummy",
                    BlobName = "dummy",
                },
                InspectionMetadataPath = new InspectionPathMessage
                {
                    StorageAccount = "dummyAnonStorageAccount",
                    BlobContainer = "dummy",
                    BlobName = "dummy",
                },
                InstallationCode = "dummy",
                TagID = "dummy",
                InspectionType = "dummy",
                InspectionDescription = "dummy",
                Timestamp = DateTime.UtcNow,
            };
            var mqttArgs = new MqttReceivedArgs(dummyMessage);

            // Act
            var methodInfo = mockedServices
                .MqttEventHandler.GetType()
                .GetMethod(
                    "OnIsarInspectionResult",
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                );
            methodInfo?.Invoke(mockedServices.MqttEventHandler, [null, mqttArgs]);

            // Assert
            mockedServices.AnalysisMappingServiceMock.Verify(
                s => s.GetAnalysesToBeRun("dummy", "dummy"),
                Times.Once
            );
            mockedServices.ArgoWorkflowServiceMock.Verify(
                s =>
                    s.TriggerAnonymizer(
                        It.IsAny<string>(),
                        It.IsAny<Database.Models.WorkflowStep>()
                    ),
                Times.Once
            );
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using api.Database.Context;
using api.Database.Models;
using api.MQTT;
using api.Services;
using Api.Test.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Api.Test.MQTT;

public class MqttEventHandlerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("invalid-operation")]
    [InlineData("database")]
    public async Task ProcessIsarInspectionResult_DuplicateIsInformation_OtherFailuresAreErrors(
        string? failureType
    )
    {
        using var context = new SaraDbContext(new DbContextOptionsBuilder<SaraDbContext>().Options);
        var message = new DatabaseUtilities(context).NewIsarInspectionResultMessage();
        var records = new Mock<IInspectionRecordService>();
        Exception? failure = failureType switch
        {
            "invalid-operation" => new InvalidOperationException("Creation failed"),
            "database" => new DbUpdateException("Database unavailable"),
            _ => null,
        };
        if (failure is null)
        {
            records
                .Setup(service => service.CreateFromMqttMessage(message))
                .ReturnsAsync((InspectionRecord?)null);
        }
        else
        {
            records.Setup(service => service.CreateFromMqttMessage(message)).ThrowsAsync(failure);
        }

        var blobs = new Mock<IBlobStorageService>();
        blobs
            .Setup(service => service.ExistsAsync(It.IsAny<BlobStorageLocation>()))
            .ReturnsAsync(true);
        var analyses = new Mock<IAnalysisTriggerService>();
        var logger = new Mock<ILogger<MqttEventHandler>>();
        using var services = new ServiceCollection()
            .AddSingleton(records.Object)
            .AddSingleton(blobs.Object)
            .AddSingleton(analyses.Object)
            .BuildServiceProvider();
        using var handler = new MqttEventHandler(
            logger.Object,
            services.GetRequiredService<IServiceScopeFactory>()
        );

        try
        {
            await handler.ProcessIsarInspectionResult(message);
        }
        finally
        {
            handler.Unsubscribe();
        }

        analyses.Verify(
            service => service.OnInspectionRecordCreated(It.IsAny<InspectionRecord>()),
            Times.Never
        );
        var logs = logger.Invocations.Where(invocation => invocation.Method.Name == "Log").ToList();
        if (failure is null)
        {
            var duplicateLog = Assert.Single(
                logs,
                log =>
                    log.Arguments[2]
                        .ToString()!
                        .Contains("Skipping duplicate ISAR inspection result")
            );
            Assert.Equal(LogLevel.Information, duplicateLog.Arguments[0]);
            Assert.Contains(message.InspectionId, duplicateLog.Arguments[2].ToString());
            Assert.Null(duplicateLog.Arguments[3]);
            Assert.DoesNotContain(logs, log => (LogLevel)log.Arguments[0] >= LogLevel.Error);
        }
        else
        {
            var errorLog = Assert.Single(logs, log => (LogLevel)log.Arguments[0] == LogLevel.Error);
            Assert.Same(failure, errorLog.Arguments[3]);
        }
    }
}

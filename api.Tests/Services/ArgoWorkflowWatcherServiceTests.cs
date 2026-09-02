using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using api.Services;
using api.Services.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Api.Test.Services;

public class ArgoWorkflowWatcherServiceTests
{
    [Fact]
    public async Task WatchOnce_RelistsThenWatchesFromSnapshotForBoundedDuration()
    {
        var listed = Workflow("listed", "uid-1");
        var watched = Workflow("watched", "uid-2");
        var client = new RecordingClient([listed], [watched]);
        var processor = new RecordingProcessor();
        var service = CreateService(client, processor);

        await service.WatchOnce(TestContext.Current.CancellationToken);

        Assert.Equal(["listed", "watched"], processor.ProcessedNames);
        Assert.Equal("42", client.WatchedResourceVersion);
        Assert.Equal(300, client.WatchTimeoutSeconds);
    }

    [Fact]
    public async Task WatchOnce_ProcessingFailureDoesNotStopRemainingEvents()
    {
        var poison = Workflow("poison", "uid-1");
        var healthy = Workflow("healthy", "uid-2");
        var client = new RecordingClient([], [poison, healthy]);
        var processor = new RecordingProcessor { ThrowForName = "poison" };
        var logger = new RecordingLogger();
        var service = CreateService(client, processor, logger);

        await service.WatchOnce(TestContext.Current.CancellationToken);

        Assert.Equal(["poison", "healthy"], processor.ProcessedNames);
        var log = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("poison", log.Message);
        Assert.Contains("uid-1", log.Message);
    }

    [Fact]
    public async Task WatchOnce_WatchFailureIsPropagatedForReconnectHandling()
    {
        var expected = new ArgoWorkflowWatchException(410, "expired");
        var client = new RecordingClient([], [], expected);
        var service = CreateService(client, new RecordingProcessor());

        var actual = await Assert.ThrowsAsync<ArgoWorkflowWatchException>(() =>
            service.WatchOnce(TestContext.Current.CancellationToken)
        );

        Assert.Same(expected, actual);
    }

    [Fact]
    public void RetryDelay_UsesCappedExponentialBackoffWithJitter()
    {
        Assert.InRange(ArgoWorkflowWatcherService.GetRetryDelay(1).TotalSeconds, 0.8, 1.2);
        Assert.InRange(ArgoWorkflowWatcherService.GetRetryDelay(4).TotalSeconds, 6.4, 9.6);
        Assert.InRange(ArgoWorkflowWatcherService.GetRetryDelay(20).TotalSeconds, 48, 72);
    }

    private static ArgoWorkflowWatcherService CreateService(
        IArgoWorkflowClient client,
        IArgoWorkflowEventProcessor processor,
        ILogger<ArgoWorkflowWatcherService>? logger = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(processor);
        return new ArgoWorkflowWatcherService(
            client,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            logger ?? new RecordingLogger()
        );
    }

    private static ArgoWorkflowResource Workflow(string name, string uid) =>
        new() { Metadata = new ArgoObjectMetadata { Name = name, Uid = uid } };

    private sealed class RecordingClient(
        IReadOnlyList<ArgoWorkflowResource> listed,
        IReadOnlyList<ArgoWorkflowResource> watched,
        Exception? watchException = null
    ) : IArgoWorkflowClient
    {
        public string? WatchedResourceVersion { get; private set; }
        public int? WatchTimeoutSeconds { get; private set; }

        public Task<CreatedArgoWorkflow> CreateWorkflow(
            string workflowName,
            string workflowTemplateName,
            Guid workflowId,
            IReadOnlyDictionary<string, string> arguments,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ArgoWorkflowSnapshot> ListWorkflows(CancellationToken cancellationToken) =>
            Task.FromResult(new ArgoWorkflowSnapshot(listed, "42"));

        public async IAsyncEnumerable<ArgoWorkflowResource> WatchWorkflows(
            string resourceVersion,
            int timeoutSeconds,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            WatchedResourceVersion = resourceVersion;
            WatchTimeoutSeconds = timeoutSeconds;
            foreach (var workflow in watched)
            {
                yield return workflow;
            }

            if (watchException is not null)
            {
                await Task.Yield();
                throw watchException;
            }
        }
    }

    private sealed class RecordingProcessor : IArgoWorkflowEventProcessor
    {
        public List<string> ProcessedNames { get; } = [];
        public string? ThrowForName { get; init; }

        public Task Process(
            ArgoWorkflowResource resource,
            CancellationToken cancellationToken = default
        )
        {
            var name = resource.Metadata.Name!;
            ProcessedNames.Add(name);
            return name == ThrowForName
                ? Task.FromException(new InvalidOperationException("processor failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<ArgoWorkflowWatcherService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}

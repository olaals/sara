using k8s.Autorest;

namespace api.Services.HostedServices;

public class ArgoWorkflowWatcherService(
    IArgoWorkflowClient client,
    IServiceScopeFactory scopeFactory,
    ILogger<ArgoWorkflowWatcherService> logger
) : BackgroundService
{
    private const int WatchTimeoutSeconds = 300;
    private const int MaximumRetryDelaySeconds = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WatchOnce(stoppingToken);
                consecutiveFailures = 0;
                logger.LogDebug("Argo Workflow watch ended; relisting");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpOperationException ex)
                when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                consecutiveFailures = 0;
                logger.LogInformation("Argo Workflow watch resource version expired; relisting");
            }
            catch (ArgoWorkflowWatchException ex) when (ex.StatusCode == 410)
            {
                consecutiveFailures = 0;
                logger.LogInformation("Argo Workflow watch resource version expired; relisting");
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                var retryDelay = GetRetryDelay(consecutiveFailures);
                logger.LogWarning(
                    ex,
                    "Argo Workflow list/watch failed; retrying in {RetryDelaySeconds:F1} seconds",
                    retryDelay.TotalSeconds
                );
                await Task.Delay(retryDelay, stoppingToken);
            }
        }
    }

    internal async Task WatchOnce(CancellationToken cancellationToken)
    {
        var snapshot = await client.ListWorkflows(cancellationToken);
        foreach (var workflow in snapshot.Items)
        {
            await Process(workflow, cancellationToken);
        }

        await foreach (
            var workflow in client.WatchWorkflows(
                snapshot.ResourceVersion,
                WatchTimeoutSeconds,
                cancellationToken
            )
        )
        {
            await Process(workflow, cancellationToken);
        }
    }

    private async Task Process(ArgoWorkflowResource workflow, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope
                .ServiceProvider.GetRequiredService<IArgoWorkflowEventProcessor>()
                .Process(workflow, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to process Argo Workflow {WorkflowName} ({WorkflowUid}); continuing watch",
                workflow.Metadata.Name,
                workflow.Metadata.Uid
            );
        }
    }

    internal static TimeSpan GetRetryDelay(int consecutiveFailures)
    {
        var exponentialSeconds = Math.Min(
            Math.Pow(2, Math.Min(consecutiveFailures - 1, 10)),
            MaximumRetryDelaySeconds
        );
        return TimeSpan.FromSeconds(exponentialSeconds * (0.8 + Random.Shared.NextDouble() * 0.4));
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using api.Services;

namespace Api.Test.Mocks;

public record ArgoCreateRequest(
    string WorkflowName,
    string WorkflowTemplateName,
    Guid WorkflowId,
    IReadOnlyDictionary<string, string> Arguments
);

public class FakeArgoWorkflowClient : IArgoWorkflowClient
{
    public List<ArgoCreateRequest> Requests { get; } = [];
    public Exception? CreateException { get; set; }
    public Func<ArgoCreateRequest, Task>? BeforeCreate { get; set; }

    public async Task<CreatedArgoWorkflow> CreateWorkflow(
        string workflowName,
        string workflowTemplateName,
        Guid workflowId,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default
    )
    {
        if (CreateException is not null)
        {
            throw CreateException;
        }
        var request = new ArgoCreateRequest(
            workflowName,
            workflowTemplateName,
            workflowId,
            arguments
        );
        if (BeforeCreate is not null)
        {
            await BeforeCreate(request);
        }
        Requests.Add(request);
        return new CreatedArgoWorkflow(workflowName, Guid.NewGuid().ToString());
    }

    public Task<ArgoWorkflowSnapshot> ListWorkflows(CancellationToken cancellationToken) =>
        Task.FromResult(new ArgoWorkflowSnapshot([], "1"));

    public async IAsyncEnumerable<ArgoWorkflowResource> WatchWorkflows(
        string resourceVersion,
        int timeoutSeconds,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await Task.CompletedTask;
        yield break;
    }
}

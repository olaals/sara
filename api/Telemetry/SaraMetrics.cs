using System.Diagnostics.Metrics;

namespace api.Telemetry;

/// <summary>Application operation outcomes; identifiers and error text belong in logs, not labels.</summary>
internal static class SaraMetrics
{
    public const string MeterName = "Sara.Operations";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> WorkflowStarts = Meter.CreateCounter<long>(
        "sara_workflow_start",
        description: "Workflow start stage outcomes"
    );
    private static readonly Counter<long> ResultProcessing = Meter.CreateCounter<long>(
        "sara_result_processing",
        description: "Result processing stage outcomes"
    );
    private static readonly Counter<long> DownstreamDeliveries = Meter.CreateCounter<long>(
        "sara_downstream_delivery",
        description: "Downstream operation outcomes; MQTT success means locally enqueued, not delivered"
    );

    private static readonly string[] StartStages =
    [
        "build",
        "persist_inputs",
        "submit",
        "persist_identity",
        "unknown",
    ];
    private static readonly string[] ResultStages =
    [
        "validate",
        "deserialize",
        "handle",
        "analysis_handle",
        "unknown",
    ];
    private static readonly string[] WorkflowTypes =
    [
        "anonymizer",
        "rain-drop",
        "fencilla",
        "cloe",
        "thermal-reading",
        "copy-raw-to-visualized",
        "unknown",
    ];
    private static readonly (string Destination, string Operation)[] Deliveries =
    [
        ("timeseries", "upload"),
        ("timeseries", "fetch_co2"),
        ("mqtt", "enqueue_inspection_record"),
        ("mqtt", "enqueue_visualization"),
        ("mqtt", "enqueue_analysis_result"),
        ("email", "send"),
        ("unknown", "unknown"),
    ];

    /// <summary>
    /// Precreates bounded series after the provider starts. First-event detection still requires
    /// a zero baseline to reach Mimir before the failure; these counters are not a durable ledger.
    /// </summary>
    public static void InitializeBaselines()
    {
        foreach (var outcome in Enum.GetValues<OperationOutcome>())
        {
            foreach (var stage in StartStages)
                RecordWorkflowStart(stage, outcome, 0);
            foreach (var workflowType in WorkflowTypes)
            foreach (var stage in ResultStages)
                RecordResultProcessing(workflowType, stage, outcome, 0);
            foreach (var (destination, operation) in Deliveries)
                RecordDownstreamDelivery(destination, operation, outcome, 0);
        }
    }

    public static void RecordWorkflowStart(
        string stage,
        OperationOutcome outcome,
        long count = 1
    ) =>
        WorkflowStarts.Add(
            count,
            new("stage", Normalize(stage, StartStages)),
            new("outcome", OutcomeLabel(outcome))
        );

    public static void RecordResultProcessing(
        string workflowType,
        string stage,
        OperationOutcome outcome,
        long count = 1
    ) =>
        ResultProcessing.Add(
            count,
            new("workflow_type", Normalize(workflowType, WorkflowTypes)),
            new("stage", Normalize(stage, ResultStages)),
            new("outcome", OutcomeLabel(outcome))
        );

    public static void RecordDownstreamDelivery(
        string destination,
        string operation,
        OperationOutcome outcome,
        long count = 1
    )
    {
        if (!Deliveries.Contains((destination, operation)))
            (destination, operation) = ("unknown", "unknown");
        DownstreamDeliveries.Add(
            count,
            new("destination", destination),
            new("operation", operation),
            new("outcome", OutcomeLabel(outcome))
        );
    }

    private static string Normalize(string value, string[] allowed) =>
        allowed.FirstOrDefault(candidate =>
            string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)
        ) ?? "unknown";

    private static string OutcomeLabel(OperationOutcome outcome) =>
        outcome switch
        {
            OperationOutcome.Success => "success",
            OperationOutcome.Failure => "failure",
            _ => "skipped",
        };
}

internal enum OperationOutcome
{
    Success,
    Failure,
    Skipped,
}

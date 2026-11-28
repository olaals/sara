using System.Globalization;
using System.Text.RegularExpressions;
using api.Configurations;
using api.Controllers.Models;

namespace api.Services.BenchmarkServices;

internal static partial class BenchmarkWorkflowMapper
{
    private static readonly IReadOnlyList<string> DefaultDevices = ["cpu", "gpu"];
    private static readonly HashSet<string> StandardWorkflowParameters = new(
        ["source", "destination", "version", "split", "run-id", "device", "method"],
        StringComparer.OrdinalIgnoreCase
    );

    public static IReadOnlyList<string> ResolveDevices(BenchmarkDefinition definition) =>
        definition.Devices.Count > 0 ? definition.Devices : DefaultDevices;

    public static bool IsActiveWorkflowPhase(string? phase) =>
        string.IsNullOrWhiteSpace(phase)
        || string.Equals(phase, "Pending", StringComparison.OrdinalIgnoreCase)
        || string.Equals(phase, "Running", StringComparison.OrdinalIgnoreCase);

    public static void ValidateSegment(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || !PathSegmentRegex().IsMatch(value))
        {
            throw new ArgumentException($"Invalid {paramName}: '{value}'.");
        }
    }

    public static (string? Method, Dictionary<string, string> Parameters) ValidateRunOptions(
        BenchmarkDefinition definition,
        TriggerBenchmarkRequest request
    )
    {
        string? method = null;
        if (definition.Methods.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(request.Method))
            {
                throw new ArgumentException(
                    $"Dataset '{definition.Id}' does not support method selection."
                );
            }
        }
        else
        {
            method = string.IsNullOrWhiteSpace(request.Method)
                ? definition.Methods[0]
                : request.Method.Trim().ToLowerInvariant();
            if (!definition.Methods.Contains(method, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Unsupported method '{request.Method}' for dataset '{definition.Id}'."
                );
            }
        }

        var allowedParams = definition.Parameters.ToDictionary(
            p => p.Name,
            StringComparer.OrdinalIgnoreCase
        );
        var resolvedParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (request.Parameters is null)
        {
            return (method, resolvedParameters);
        }

        foreach (var (rawKey, rawValue) in request.Parameters)
        {
            if (rawValue is null)
            {
                continue;
            }

            if (!allowedParams.TryGetValue(rawKey, out var paramDef))
            {
                throw new ArgumentException(
                    $"Unsupported parameter '{rawKey}' for dataset '{definition.Id}'."
                );
            }

            if (
                paramDef.OnlyForMethods.Count > 0
                && (
                    method is null
                    || !paramDef.OnlyForMethods.Contains(method, StringComparer.OrdinalIgnoreCase)
                )
            )
            {
                throw new ArgumentException(
                    $"Parameter '{paramDef.Name}' is only valid for methods: {string.Join(", ", paramDef.OnlyForMethods)}."
                );
            }

            ValidateParameterBounds(paramDef, rawValue.Value);
            resolvedParameters[paramDef.Name] = rawValue.Value.ToString(
                CultureInfo.InvariantCulture
            );
        }

        return (method, resolvedParameters);
    }

    public static ArgoWorkflowResource BuildWorkflowResource(
        BenchmarkDefinition definition,
        string containerUrl,
        string version,
        string split,
        string runId,
        string device,
        string? method,
        IReadOnlyDictionary<string, string> resolvedParameters
    )
    {
        List<ArgoParameter> workflowParameters =
        [
            new ArgoParameter { Name = "source", Value = containerUrl },
            new ArgoParameter { Name = "destination", Value = containerUrl },
            new ArgoParameter { Name = "version", Value = version },
            new ArgoParameter { Name = "split", Value = split },
            new ArgoParameter { Name = "run-id", Value = runId },
            new ArgoParameter { Name = "device", Value = device },
        ];
        if (!string.IsNullOrEmpty(method))
        {
            workflowParameters.Add(new ArgoParameter { Name = "method", Value = method });
        }

        foreach (var paramDef in definition.Parameters)
        {
            var value = resolvedParameters.GetValueOrDefault(paramDef.Name, "");
            workflowParameters.Add(new ArgoParameter { Name = paramDef.Name, Value = value });
        }

        return new ArgoWorkflowResource
        {
            Metadata = new ArgoObjectMetadata
            {
                GenerateName = $"{definition.WorkflowTemplate}-",
                Labels = new Dictionary<string, string>
                {
                    [ArgoWorkflowClient.ManagedByLabel] = "sara",
                    [BenchmarkService.BenchmarkLabel] = "true",
                    [BenchmarkService.DatasetLabel] = definition.Id,
                },
            },
            Spec = new ArgoWorkflowSpec
            {
                WorkflowTemplateRef = new ArgoWorkflowTemplateRef
                {
                    Name = definition.WorkflowTemplate,
                },
                Arguments = new ArgoArguments { Parameters = workflowParameters },
            },
        };
    }

    public static BenchmarkJobDto MapWorkflowToJobDto(ArgoWorkflowResource workflow)
    {
        var parameters = (workflow.Spec?.Arguments?.Parameters ?? []).ToDictionary(
            p => p.Name,
            p => p.Value ?? "",
            StringComparer.OrdinalIgnoreCase
        );

        var name = workflow.Metadata.Name ?? "";
        var datasetId = workflow.Metadata.Labels.TryGetValue(
            BenchmarkService.DatasetLabel,
            out var labelDataset
        )
            ? labelDataset
            : "";
        var configuredRunId = parameters.GetValueOrDefault("run-id", "");
        var method =
            parameters.TryGetValue("method", out var rawMethod)
            && !string.IsNullOrWhiteSpace(rawMethod)
                ? rawMethod
                : null;
        var extraParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            if (StandardWorkflowParameters.Contains(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            extraParameters[key] = value;
        }

        return new BenchmarkJobDto(
            WorkflowName: name,
            WorkflowUid: workflow.Metadata.Uid,
            DatasetId: datasetId,
            Version: parameters.GetValueOrDefault("version", ""),
            Split: parameters.GetValueOrDefault("split", ""),
            Device: parameters.GetValueOrDefault("device", ""),
            Method: method,
            Parameters: extraParameters,
            RunId: string.IsNullOrEmpty(configuredRunId) ? name : configuredRunId,
            Phase: workflow.Status?.Phase ?? "Pending",
            Message: workflow.Status?.Message,
            CreatedAt: workflow.Metadata.CreationTimestamp,
            StartedAt: workflow.Status?.StartedAt,
            FinishedAt: workflow.Status?.FinishedAt
        );
    }

    private static void ValidateParameterBounds(BenchmarkParameterDefinition paramDef, double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException($"Parameter '{paramDef.Name}' must be a finite number.");
        }

        if (paramDef.Min is double min)
        {
            var belowMin = paramDef.MinExclusive ? value <= min : value < min;
            if (belowMin)
            {
                var bound = paramDef.MinExclusive ? ">" : ">=";
                throw new ArgumentException(
                    $"Parameter '{paramDef.Name}' must be {bound} {min.ToString(CultureInfo.InvariantCulture)}."
                );
            }
        }

        if (paramDef.Max is double max && value > max)
        {
            throw new ArgumentException(
                $"Parameter '{paramDef.Name}' must be <= {max.ToString(CultureInfo.InvariantCulture)}."
            );
        }
    }

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._-]*$")]
    private static partial Regex PathSegmentRegex();
}

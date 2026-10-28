using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using api.Telemetry;
using Xunit;

namespace Api.Test.Telemetry;

// Process-wide instruments must not receive unrelated test operations during assertions.
[CollectionDefinition("Operation metrics", DisableParallelization = true)]
public class OperationMetricsCollection;

internal sealed class MetricsCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<Measurement> _measurements = new();

    public MetricsCapture()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SaraMetrics.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) =>
                _measurements.Enqueue(
                    new Measurement(
                        instrument.Name,
                        value,
                        tags.ToArray()
                            .ToDictionary(tag => tag.Key, tag => tag.Value?.ToString() ?? "")
                    )
                )
        );
        _listener.Start();
    }

    public Measurement[] Measurements => _measurements.ToArray();

    public long Count(string metric, params (string Key, string Value)[] tags) =>
        _measurements
            .Where(measurement =>
                measurement.Name == metric
                && tags.All(tag => measurement.Tags.GetValueOrDefault(tag.Key) == tag.Value)
            )
            .Sum(measurement => measurement.Value);

    public void Dispose() => _listener.Dispose();

    internal sealed record Measurement(string Name, long Value, Dictionary<string, string> Tags);
}

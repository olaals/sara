import styled from "styled-components";
import type { BenchmarkMetrics } from "../../../../api/benchmarkClient";
import StatCard from "../../../../components/StatCard";
import { formatMetricValue, formatNumber } from "../benchmarkFormatting";
import { getSummaryMetricDescription } from "./benchmarkMetricDescriptions";

const StatsGrid = styled.div`
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
  gap: 0.75rem;
  margin-bottom: 1rem;
`;

interface BenchmarkRunSummaryCardsProps {
  metrics: BenchmarkMetrics;
}

export default function BenchmarkRunSummaryCards({
  metrics,
}: BenchmarkRunSummaryCardsProps) {
  return (
    <StatsGrid>
      <StatCard
        title="Throughput"
        value={`${formatNumber(metrics.processing.per_second, 2)} ${metrics.processing.unit}/s`}
        subtitle={`${metrics.processing.count} in ${formatNumber(metrics.processing.seconds, 1)}s`}
        tooltip={getSummaryMetricDescription("throughput")}
      />
      {metrics.summary_metrics.map((metric) => (
        <StatCard
          key={metric.key}
          title={metric.label}
          value={formatMetricValue(metric.value, metric.format)}
          subtitle={metric.subtitle ?? undefined}
          tooltip={metric.description ?? getSummaryMetricDescription(metric.key)}
        />
      ))}
    </StatsGrid>
  );
}

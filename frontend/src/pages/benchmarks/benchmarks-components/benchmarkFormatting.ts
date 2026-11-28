import type {
  BenchmarkCellValue,
  BenchmarkJob,
  BenchmarkMetricFormat,
  BenchmarkParameterDefinition,
  BenchmarkResult,
  BenchmarkRunConfiguration,
} from "../../../api/benchmarkClient";

export function formatPercent(value: number | null | undefined): string {
  return value == null ? "–" : `${(value * 100).toFixed(1)}%`;
}

export function formatNumber(value: number | null | undefined, digits = 2): string {
  return value == null ? "–" : value.toFixed(digits);
}

export function formatMetricValue(
  value: BenchmarkCellValue,
  format: BenchmarkMetricFormat
): string {
  if (value == null) return "–";
  switch (format) {
    case "percent":
      return typeof value === "number" ? formatPercent(value) : String(value);
    case "number":
      return typeof value === "number" ? formatNumber(value, 3) : String(value);
    case "integer":
      return typeof value === "number" ? String(Math.round(value)) : String(value);
    case "boolean":
      return typeof value === "boolean" ? (value ? "Yes" : "No") : String(value);
    case "celsius":
      return typeof value === "number" ? `${formatNumber(value, 2)} °C` : String(value);
    case "pixels":
      return typeof value === "number" ? `${formatNumber(value, 1)} px` : String(value);
    default:
      return String(value);
  }
}

export function formatMethodLabel(method?: string | null): string {
  if (!method) return "–";
  switch (method) {
    case "segmentation":
      return "Segmentation";
    case "bounding_box":
      return "Bounding box";
    case "phase_correlation":
      return "Phase correlation";
    case "orb_bf":
      return "ORB + BFMatcher";
    default:
      return method;
  }
}

function formatConfigEntry(key: string, value: BenchmarkCellValue): string | null {
  if (value == null) return null;
  if (key === "model") return String(value);
  if (key === "confidence_threshold" || key === "confidence-threshold") {
    return `conf ${value}`;
  }
  if (key === "iou_threshold" || key === "iou-threshold") {
    return `IoU >= ${value}`;
  }
  if (key === "temperature_percentile") {
    return `P${value}`;
  }
  if (key === "box_padding" || key === "box-padding") {
    const num = Number(value);
    return Number.isFinite(num) ? `pad ${(num * 100).toFixed(0)}%` : `pad ${value}`;
  }
  if (key === "compiled") {
    return value ? "compiled" : null;
  }
  return `${key} ${value}`;
}

export function formatConfigurationSummary(config?: BenchmarkRunConfiguration | null): {
  primary: string;
  details: string;
} | null {
  if (!config) return null;
  const method = typeof config.method === "string" ? config.method : null;
  const parts: string[] = [];
  for (const [key, value] of Object.entries(config)) {
    if (key === "method") continue;
    const formatted = formatConfigEntry(key, value);
    if (formatted) parts.push(formatted);
  }
  if (method) {
    return { primary: formatMethodLabel(method), details: parts.join(" · ") };
  }
  if (parts.length === 0) return null;
  return { primary: parts[0], details: parts.slice(1).join(" · ") };
}

export function formatJobConfigSummary(job: BenchmarkJob): string {
  const parts: string[] = [];
  if (job.method) {
    parts.push(formatMethodLabel(job.method));
  }
  for (const [key, value] of Object.entries(job.parameters ?? {})) {
    const formatted = formatConfigEntry(key, value);
    if (formatted) parts.push(formatted);
  }
  return parts.length > 0 ? parts.join(" · ") : "–";
}

export function isParameterApplicable(
  param: BenchmarkParameterDefinition,
  method: string
): boolean {
  if (param.onlyForMethods.length === 0) return true;
  return param.onlyForMethods.some((m) => m.toLowerCase() === method.toLowerCase());
}

export interface PrimaryMetricColumn {
  key: string;
  label: string;
  format: BenchmarkMetricFormat;
}

export function collectPrimaryMetricColumns(
  results: BenchmarkResult[]
): PrimaryMetricColumn[] {
  const seen = new Map<string, PrimaryMetricColumn>();
  for (const result of results) {
    for (const metric of result.metrics?.summary_metrics ?? []) {
      if (metric.primary === false || seen.has(metric.key)) continue;
      seen.set(metric.key, {
        key: metric.key,
        label: metric.label,
        format: metric.format,
      });
    }
  }
  return [...seen.values()];
}

export function shortArtifact(artifact: string): string {
  const lastSlash = artifact.lastIndexOf("/");
  return lastSlash >= 0 ? artifact.slice(lastSlash + 1) : artifact;
}

export function benchmarkProjectPath(datasetId: string): string {
  return `/benchmarks/${encodeURIComponent(datasetId)}`;
}

export function benchmarkRunDetailPath(
  datasetId: string,
  version: string,
  split: string,
  runId: string
): string {
  return `/benchmarks/${encodeURIComponent(datasetId)}/${encodeURIComponent(version)}/${encodeURIComponent(split)}/${encodeURIComponent(runId)}`;
}

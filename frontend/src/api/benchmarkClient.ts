import { useEffect, useRef } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  apiFetch,
  fetchAuthenticatedBlobUrl,
  fetchOptionalAuthenticatedBlobUrl,
} from "./client";
import { apiUrl } from "../utils/routing";

export interface BenchmarkParameterDefinition {
  name: string;
  label: string;
  min?: number | null;
  minExclusive: boolean;
  max?: number | null;
  step?: number | null;
  placeholder?: string | null;
  onlyForMethods: string[];
}

export interface BenchmarkDataset {
  id: string;
  label: string;
  description: string;
  container: string;
  format: string;
  defaultSplit: string;
  devices: string[];
  methods: string[];
  parameters: BenchmarkParameterDefinition[];
  versions: string[];
}

export interface TriggerBenchmarkRequest {
  datasetId: string;
  version: string;
  split: string;
  device: string;
  runId?: string;
  method?: string;
  parameters?: Record<string, number | null>;
}

export interface BenchmarkJob {
  workflowName: string;
  workflowUid?: string | null;
  datasetId: string;
  version: string;
  split: string;
  device: string;
  method?: string | null;
  parameters: Record<string, string>;
  runId: string;
  phase: string;
  message?: string | null;
  createdAt?: string | null;
  startedAt?: string | null;
  finishedAt?: string | null;
}

export type BenchmarkMetricFormat =
  | "percent"
  | "number"
  | "integer"
  | "boolean"
  | "celsius"
  | "pixels"
  | "text"
  | string;

export interface BenchmarkSummaryMetric {
  key: string;
  label: string;
  value: number | null;
  format: BenchmarkMetricFormat;
  subtitle?: string | null;
  description?: string | null;
  primary: boolean;
}

export interface BenchmarkEvaluation {
  labels: string[];
  confusion_matrix: (number | null)[][];
}

export type BenchmarkRunConfiguration = Record<
  string,
  string | number | boolean | null | undefined
>;

export interface BenchmarkRunMetadata {
  schema_version: number;
  run_id: string;
  dataset: {
    id: string;
    version: string;
    split: string;
    manifest_sha256: string;
  };
  artifact: string;
  evaluator: string;
  protocol: string;
  configuration?: BenchmarkRunConfiguration | null;
  execution: {
    hardware: string;
    timing_scope: string;
  };
}

export interface BenchmarkMetrics {
  schema_version: number;
  run_id: string;
  processing: {
    unit: string;
    count: number;
    seconds: number;
    per_second: number;
  };
  summary_metrics: BenchmarkSummaryMetric[];
  evaluations: Record<string, BenchmarkEvaluation>;
}

export interface BenchmarkResult {
  runId: string;
  run?: BenchmarkRunMetadata | null;
  metrics?: BenchmarkMetrics | null;
  error?: string | null;
}

export interface BenchmarkPerImageColumn {
  key: string;
  label: string;
  format: BenchmarkMetricFormat;
}

export type BenchmarkCellValue = string | number | boolean | null | undefined;

export interface BenchmarkImageResult {
  image_id: number;
  file_name: string;
  width: number;
  height: number;
  passed: boolean;
  missed: boolean;
  false_positive: boolean;
  values: Record<string, BenchmarkCellValue>;
}

export interface BenchmarkPerImageTable {
  columns: BenchmarkPerImageColumn[];
  items: BenchmarkImageResult[];
}

export interface BenchmarkRunDetail {
  runId: string;
  run?: BenchmarkRunMetadata | null;
  metrics?: BenchmarkMetrics | null;
  perImage?: BenchmarkPerImageTable | null;
  hasPredictions: boolean;
  error?: string | null;
}

export interface CocoRleSegmentation {
  size: [number, number];
  counts: string | number[];
}

export type CocoSegmentation = CocoRleSegmentation | number[][];

export interface BenchmarkCocoAnnotation {
  id: number;
  imageId: number;
  categoryId: number;
  categoryName: string;
  bbox?: number[] | null;
  area?: number | null;
  score?: number | null;
  segmentation?: CocoSegmentation | null;
}

export interface BenchmarkReferenceImage {
  referenceId: string;
  fileName: string;
  width: number;
  height: number;
  annotations: BenchmarkCocoAnnotation[];
}

export interface BenchmarkImageComparison {
  imageId: number;
  fileName: string;
  width: number;
  height: number;
  format: string;
  summaryColumns: BenchmarkPerImageColumn[];
  summary?: BenchmarkImageResult | null;
  groundTruth: BenchmarkCocoAnnotation[];
  predictions: BenchmarkCocoAnnotation[];
  reference?: BenchmarkReferenceImage | null;
}

const ACTIVE_PHASES = new Set(["Pending", "Running", ""]);

export function isBenchmarkJobActive(phase: string): boolean {
  return ACTIVE_PHASES.has(phase);
}

export function getBenchmarkDatasets(signal?: AbortSignal): Promise<BenchmarkDataset[]> {
  return apiFetch(apiUrl("/api/benchmark/datasets"), { signal });
}

export function getBenchmarkCoverImageUrl(
  datasetId: string,
  signal?: AbortSignal
): Promise<string | null> {
  return fetchOptionalAuthenticatedBlobUrl(
    apiUrl(`/api/benchmark/datasets/${encodeURIComponent(datasetId)}/cover-image`),
    signal
  );
}

export function uploadBenchmarkCoverImage(datasetId: string, file: File): Promise<void> {
  const formData = new FormData();
  formData.append("file", file);
  return apiFetch(
    apiUrl(`/api/benchmark/datasets/${encodeURIComponent(datasetId)}/cover-image`),
    {
      method: "PUT",
      body: formData,
    }
  );
}

export function resetBenchmarkCoverImage(datasetId: string): Promise<void> {
  return apiFetch(
    apiUrl(`/api/benchmark/datasets/${encodeURIComponent(datasetId)}/cover-image`),
    {
      method: "DELETE",
    }
  );
}

export function getBenchmarkJobs(signal?: AbortSignal): Promise<BenchmarkJob[]> {
  return apiFetch(apiUrl("/api/benchmark/jobs"), { signal });
}

export function triggerBenchmarkRun(
  request: TriggerBenchmarkRequest
): Promise<BenchmarkJob> {
  return apiFetch(apiUrl("/api/benchmark/runs"), {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function getBenchmarkResults(
  datasetId: string,
  version: string,
  split: string,
  signal?: AbortSignal
): Promise<BenchmarkResult[]> {
  const query = new URLSearchParams({ datasetId, version, split });
  return apiFetch(apiUrl(`/api/benchmark/results?${query}`), { signal });
}

export function getBenchmarkRunDetail(
  datasetId: string,
  version: string,
  split: string,
  runId: string,
  signal?: AbortSignal
): Promise<BenchmarkRunDetail> {
  const query = new URLSearchParams({ datasetId, version, split });
  return apiFetch(
    apiUrl(`/api/benchmark/results/${encodeURIComponent(runId)}?${query}`),
    { signal }
  );
}

export function getBenchmarkImageComparison(
  datasetId: string,
  version: string,
  split: string,
  runId: string,
  imageId: number,
  signal?: AbortSignal
): Promise<BenchmarkImageComparison> {
  const query = new URLSearchParams({ datasetId, version, split });
  return apiFetch(
    apiUrl(
      `/api/benchmark/results/${encodeURIComponent(runId)}/images/${imageId}?${query}`
    ),
    { signal }
  );
}

export function getBenchmarkImageUrl(
  datasetId: string,
  version: string,
  split: string,
  runId: string,
  imageId: number
): Promise<string> {
  const query = new URLSearchParams({ datasetId, version, split });
  return fetchAuthenticatedBlobUrl(
    apiUrl(
      `/api/benchmark/results/${encodeURIComponent(runId)}/images/${imageId}/image?${query}`
    )
  );
}

export function getBenchmarkReferenceImageUrl(
  datasetId: string,
  version: string,
  split: string,
  runId: string,
  imageId: number
): Promise<string> {
  const query = new URLSearchParams({ datasetId, version, split });
  return fetchAuthenticatedBlobUrl(
    apiUrl(
      `/api/benchmark/results/${encodeURIComponent(runId)}/images/${imageId}/reference-image?${query}`
    )
  );
}

export function useBenchmarkDatasets() {
  return useQuery({
    queryKey: ["sara", "benchmarks", "datasets"],
    queryFn: ({ signal }) => getBenchmarkDatasets(signal),
    staleTime: 60 * 1000,
  });
}

export function useBenchmarkJobs() {
  const queryClient = useQueryClient();
  const previousActiveRef = useRef<Set<string>>(new Set());

  const query = useQuery({
    queryKey: ["sara", "benchmarks", "jobs"],
    queryFn: ({ signal }) => getBenchmarkJobs(signal),
    refetchInterval: (q) => {
      const jobs = q.state.data ?? [];
      return jobs.some((j) => isBenchmarkJobActive(j.phase)) ? 5000 : false;
    },
  });

  useEffect(() => {
    const jobs = query.data ?? [];
    const currentActive = new Set(
      jobs.filter((j) => isBenchmarkJobActive(j.phase)).map((j) => j.workflowName)
    );
    const anyCompleted = [...previousActiveRef.current].some(
      (name) => !currentActive.has(name)
    );
    previousActiveRef.current = currentActive;

    if (anyCompleted) {
      queryClient.invalidateQueries({ queryKey: ["sara", "benchmarks", "results"] });
    }
  }, [query.data, queryClient]);

  return query;
}

export function useBenchmarkResults(datasetId: string, version: string, split: string) {
  return useQuery({
    queryKey: ["sara", "benchmarks", "results", datasetId, version, split],
    queryFn: ({ signal }) => getBenchmarkResults(datasetId, version, split, signal),
    enabled: Boolean(datasetId && version && split),
  });
}

export function useBenchmarkRunDetail(
  datasetId: string,
  version: string,
  split: string,
  runId: string
) {
  return useQuery({
    queryKey: ["sara", "benchmarks", "detail", datasetId, version, split, runId],
    queryFn: ({ signal }) =>
      getBenchmarkRunDetail(datasetId, version, split, runId, signal),
    enabled: Boolean(datasetId && version && split && runId),
  });
}

export function useBenchmarkImageComparison(
  datasetId: string,
  version: string,
  split: string,
  runId: string,
  imageId: number | null
) {
  return useQuery({
    queryKey: [
      "sara",
      "benchmarks",
      "image-comparison",
      datasetId,
      version,
      split,
      runId,
      imageId,
    ],
    queryFn: ({ signal }) =>
      getBenchmarkImageComparison(datasetId, version, split, runId, imageId!, signal),
    enabled: Boolean(datasetId && version && split && runId && imageId !== null),
  });
}

export function useTriggerBenchmark() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: triggerBenchmarkRun,
    networkMode: "always",
    retry: false,
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["sara", "benchmarks", "jobs"] }),
  });
}

export function useUploadBenchmarkCoverImage() {
  return useMutation({
    mutationFn: ({ datasetId, file }: { datasetId: string; file: File }) =>
      uploadBenchmarkCoverImage(datasetId, file),
    networkMode: "always",
    retry: false,
  });
}

export function useResetBenchmarkCoverImage() {
  return useMutation({
    mutationFn: (datasetId: string) => resetBenchmarkCoverImage(datasetId),
    networkMode: "always",
    retry: false,
  });
}

import { useEffect, useState } from "react";
import {
  getBenchmarkImageUrl,
  getBenchmarkReferenceImageUrl,
} from "../../../../api/benchmarkClient";

function useManagedObjectUrl(
  enabled: boolean,
  fetcher: () => Promise<string>,
  deps: readonly unknown[]
) {
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [imageError, setImageError] = useState<string | null>(null);

  useEffect(() => {
    setImageError(null);
    setImageUrl(null);
    if (!enabled) return;
    let cancelled = false;
    let objectUrl: string | null = null;

    fetcher()
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        objectUrl = url;
        setImageUrl(url);
      })
      .catch((err) => {
        if (!cancelled) {
          setImageError(err instanceof Error ? err.message : "Failed to load image");
        }
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, deps);

  return { imageUrl, imageError };
}

export function useBenchmarkImageObjectUrl(
  datasetId: string,
  version: string,
  split: string,
  runId: string,
  imageId: number | null
) {
  return useManagedObjectUrl(
    imageId !== null,
    () => getBenchmarkImageUrl(datasetId, version, split, runId, imageId!),
    [datasetId, version, split, runId, imageId]
  );
}

export function useBenchmarkReferenceImageObjectUrl(
  datasetId: string,
  version: string,
  split: string,
  runId: string,
  imageId: number | null,
  enabled: boolean
) {
  return useManagedObjectUrl(
    enabled && imageId !== null,
    () => getBenchmarkReferenceImageUrl(datasetId, version, split, runId, imageId!),
    [datasetId, version, split, runId, imageId, enabled]
  );
}

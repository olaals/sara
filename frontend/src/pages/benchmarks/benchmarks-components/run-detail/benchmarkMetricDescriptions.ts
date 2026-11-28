const SUMMARY_METRIC_DESCRIPTIONS: Record<string, string> = {
  throughput:
    "Processing speed during the benchmark run: total items evaluated divided by inference wall time.",
  person_recall:
    "Share of ground-truth people adequately covered by predicted anonymization masks. Higher means fewer exposed people.",
  mean_person_coverage:
    "Average fraction of each ground-truth person's pixels covered by predicted masks. Subtitle shows the average worst-covered person per image.",
  mean_background_masked_fraction:
    "Average fraction of non-person (background) pixels masked out per image. Lower means less unnecessary obscuring of inspection imagery.",
  mean_iou:
    "Mean Intersection-over-Union between predicted and ground-truth masks or polygons on positive samples (1.0 = exact overlap).",
  precision:
    "Share of predicted positives that are true positives: TP / (TP + FP). Higher means fewer false alarms.",
  recall:
    "Share of actual positives that were detected or matched: TP / (TP + FN). Higher means fewer misses.",
  f1: "Harmonic mean of Precision and Recall: 2 × (P × R) / (P + R). Balances false alarms and misses in a single score.",
  fp_rate:
    "Share of negative samples incorrectly flagged as positive: FP / (FP + TN). Lower is better.",
  box_recall:
    "Share of ground-truth bounding boxes matched by a predicted box above the IoU threshold.",
  box_precision:
    "Share of predicted bounding boxes that match a ground-truth box above the IoU threshold.",
  box_mean_iou:
    "Average Intersection-over-Union across all matched predicted and ground-truth bounding boxes.",
  temp_mae_c:
    "Mean absolute error in P95 temperature (°C) between predicted and ground-truth polygons on matched samples. Lower is better.",
  temp_max_err_c:
    "Worst-case absolute P95 temperature error (°C) observed across all matched samples.",
  mean_centroid_error_px:
    "Average Euclidean distance in pixels between predicted and ground-truth polygon centers on matchable samples. Lower is better.",
};

export function getSummaryMetricDescription(key: string): string | undefined {
  return SUMMARY_METRIC_DESCRIPTIONS[key];
}

export interface BinaryClassificationDescriptions {
  accuracy: string;
  precision: string;
  recall: string;
  specificity: string;
  f1: string;
  falseNegativeRate: string;
  falsePositiveRate: string;
  classBalance: string;
}

export function getBinaryClassificationDescriptions(
  positiveLabel: string,
  negativeLabel: string
): BinaryClassificationDescriptions {
  return {
    accuracy: `Share of all samples classified correctly: (TP + TN) / Total. Can look inflated if ${positiveLabel} and ${negativeLabel} classes are imbalanced.`,
    precision: `Of samples predicted as ${positiveLabel}, the share that truly are ${positiveLabel}: TP / (TP + FP). Higher means fewer false alarms.`,
    recall: `Of all true ${positiveLabel} samples, the share correctly detected: TP / (TP + FN). Higher means fewer missed detections.`,
    specificity: `Of all true ${negativeLabel} samples, the share correctly left negative: TN / (TN + FP). Higher means fewer false triggers on ${negativeLabel} images.`,
    f1: "Harmonic mean of Precision and Recall: 2 × (P × R) / (P + R). Summarizes the trade-off between misses and false positives.",
    falseNegativeRate: `Share of true ${positiveLabel} samples that were missed: FN / (TP + FN), equal to 1 − Recall. Lower is better.`,
    falsePositiveRate: `Share of true ${negativeLabel} samples falsely flagged as ${positiveLabel}: FP / (FP + TN), equal to 1 − Specificity. Lower is better.`,
    classBalance: `Ground-truth split between ${positiveLabel} and ${negativeLabel} samples in this dataset split. Helps interpret Accuracy and error rates.`,
  };
}

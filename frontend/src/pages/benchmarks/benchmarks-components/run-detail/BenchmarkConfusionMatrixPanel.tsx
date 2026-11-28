import { Table, Typography } from "@equinor/eds-core-react";
import { tokens } from "@equinor/eds-tokens";
import styled from "styled-components";
import type { BenchmarkEvaluation } from "../../../../api/benchmarkClient";
import DashboardPanel from "../../../../components/DashboardPanel";
import StatCard from "../../../../components/StatCard";
import { statusColors, TableScroller } from "../../../../components/Styles";
import { formatPercent } from "../benchmarkFormatting";
import { getBinaryClassificationDescriptions } from "./benchmarkMetricDescriptions";
import {
  computeBinaryClassificationStats,
  computeMatrixTotals,
  formatEvaluationLabel,
  safeRatio,
} from "./confusionMatrixStats";
import type { BinaryClassificationStats, MatrixTotals } from "./confusionMatrixStats";

const StatsGrid = styled.div`
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
  gap: 0.75rem;
  margin-bottom: 1rem;
`;

const BalanceSection = styled.div`
  margin-bottom: 1rem;
`;

const BalanceHeader = styled.div`
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-bottom: 0.35rem;
`;

const BalanceBar = styled.div`
  display: flex;
  width: 100%;
  height: 8px;
  border-radius: 4px;
  overflow: hidden;
  background: ${tokens.colors.ui.background__medium.hex};
`;

const BalanceSegment = styled.div<{ $percent: number; $color: string }>`
  width: ${(p) => `${p.$percent}%`};
  background: ${(p) => p.$color};
`;

const MatrixCell = styled(Table.Cell)<{ $tone?: "correct" | "error" | "total" }>`
  background: ${(p) => {
    if (p.$tone === "correct") return "rgba(0, 112, 121, 0.08)";
    if (p.$tone === "error") return "rgba(235, 0, 0, 0.08)";
    if (p.$tone === "total") return tokens.colors.ui.background__light.hex;
    return "transparent";
  }};
  font-weight: ${(p) => (p.$tone === "total" ? 600 : 400)};
`;

const CellPercent = styled.span`
  color: ${tokens.colors.text.static_icons__tertiary.hex};
  margin-left: 0.35rem;
  font-size: 0.85em;
  font-weight: 400;
`;

interface BenchmarkConfusionMatrixPanelProps {
  name: string;
  evaluation: BenchmarkEvaluation;
}

function BinaryClassificationStatsGrid({
  stats,
}: {
  stats: BinaryClassificationStats;
}) {
  const positivePct = (stats.positiveShare ?? 0) * 100;
  const negativePct = (stats.negativeShare ?? 0) * 100;
  const descriptions = getBinaryClassificationDescriptions(
    stats.positiveLabel,
    stats.negativeLabel
  );

  return (
    <>
      <StatsGrid>
        <StatCard
          title="Accuracy"
          value={formatPercent(stats.accuracy)}
          subtitle={`${stats.tp + stats.tn} / ${stats.total} images`}
          tooltip={descriptions.accuracy}
        />
        <StatCard
          title="Precision"
          value={formatPercent(stats.precision)}
          subtitle={`TP ${stats.tp} / ${stats.tp + stats.fp} pred`}
          tooltip={descriptions.precision}
        />
        <StatCard
          title="Recall / Sensitivity"
          value={formatPercent(stats.recall)}
          subtitle={`TP ${stats.tp} / ${stats.positives} ${stats.positiveLabel}`}
          tooltip={descriptions.recall}
        />
        <StatCard
          title="Specificity"
          value={formatPercent(stats.specificity)}
          subtitle={`TN ${stats.tn} / ${stats.negatives} ${stats.negativeLabel}`}
          tooltip={descriptions.specificity}
        />
        <StatCard
          title="F1 Score"
          value={formatPercent(stats.f1)}
          subtitle="Harmonic mean of P & R"
          tooltip={descriptions.f1}
        />
        <StatCard
          title="Miss Rate (FNR)"
          value={formatPercent(stats.falseNegativeRate)}
          tone={
            stats.falseNegativeRate === null
              ? "default"
              : stats.fn > 0
                ? "error"
                : "success"
          }
          subtitle={`FN ${stats.fn} / ${stats.positives} ${stats.positiveLabel}`}
          tooltip={descriptions.falseNegativeRate}
        />
        <StatCard
          title="False Positive Rate"
          value={formatPercent(stats.falsePositiveRate)}
          tone={
            stats.falsePositiveRate === null
              ? "default"
              : stats.fp > 0
                ? "warning"
                : "success"
          }
          subtitle={`FP ${stats.fp} / ${stats.negatives} ${stats.negativeLabel}`}
          tooltip={descriptions.falsePositiveRate}
        />
        <StatCard
          title="Class Balance"
          value={`${formatPercent(stats.positiveShare)} / ${formatPercent(stats.negativeShare)}`}
          tone="info"
          subtitle={`${stats.positives} ${stats.positiveLabel} · ${stats.negatives} ${stats.negativeLabel}`}
          tooltip={descriptions.classBalance}
        />
      </StatsGrid>

      {stats.total > 0 && (
        <BalanceSection>
          <BalanceHeader>
            <Typography variant="caption">
              Class balance: {stats.positives} {stats.positiveLabel} (
              {formatPercent(stats.positiveShare)}) · {stats.negatives}{" "}
              {stats.negativeLabel} ({formatPercent(stats.negativeShare)})
            </Typography>
            <Typography variant="caption">Total: {stats.total} images</Typography>
          </BalanceHeader>
          <BalanceBar>
            <BalanceSegment
              $percent={positivePct}
              $color={statusColors.info.accent}
            />
            <BalanceSegment
              $percent={negativePct}
              $color={statusColors.default.accent}
            />
          </BalanceBar>
        </BalanceSection>
      )}
    </>
  );
}

function ConfusionMatrixTable({
  evaluation,
  totals,
}: {
  evaluation: BenchmarkEvaluation;
  totals: MatrixTotals;
}) {
  return (
    <TableScroller>
      <Table style={{ width: "100%" }}>
        <Table.Head>
          <Table.Row>
            <Table.Cell>Ground truth \ Predicted</Table.Cell>
            {evaluation.labels.map((label) => (
              <Table.Cell key={label}>{formatEvaluationLabel(label)}</Table.Cell>
            ))}
            <Table.Cell>Total</Table.Cell>
          </Table.Row>
        </Table.Head>
        <Table.Body>
          {evaluation.labels.map((rowLabel, rowIdx) => {
            const rowTotal = totals.rowTotals[rowIdx] ?? 0;
            const rowShare = safeRatio(rowTotal, totals.grandTotal);
            return (
              <Table.Row key={rowLabel}>
                <Table.Cell>
                  <strong>{formatEvaluationLabel(rowLabel)}</strong>
                </Table.Cell>
                {evaluation.labels.map((_, colIdx) => {
                  const cell = evaluation.confusion_matrix[rowIdx]?.[colIdx] ?? null;
                  const cellPercent =
                    cell === null ? null : safeRatio(cell, rowTotal);
                  const tone =
                    cell !== null && cell > 0
                      ? rowIdx === colIdx
                        ? "correct"
                        : "error"
                      : undefined;
                  return (
                    <MatrixCell key={colIdx} $tone={tone}>
                      {cell === null ? (
                        "–"
                      ) : (
                        <>
                          {cell}
                          {cellPercent !== null && (
                            <CellPercent>({formatPercent(cellPercent)})</CellPercent>
                          )}
                        </>
                      )}
                    </MatrixCell>
                  );
                })}
                <MatrixCell $tone="total">
                  {rowTotal}
                  {rowShare !== null && (
                    <CellPercent>({formatPercent(rowShare)})</CellPercent>
                  )}
                </MatrixCell>
              </Table.Row>
            );
          })}
          <Table.Row>
            <MatrixCell $tone="total">Total</MatrixCell>
            {evaluation.labels.map((colLabel, colIdx) => {
              const colTotal = totals.colTotals[colIdx] ?? 0;
              const colShare = safeRatio(colTotal, totals.grandTotal);
              return (
                <MatrixCell key={colLabel} $tone="total">
                  {colTotal}
                  {colShare !== null && (
                    <CellPercent>({formatPercent(colShare)})</CellPercent>
                  )}
                </MatrixCell>
              );
            })}
            <MatrixCell $tone="total">{totals.grandTotal}</MatrixCell>
          </Table.Row>
        </Table.Body>
      </Table>
    </TableScroller>
  );
}

export default function BenchmarkConfusionMatrixPanel({
  name,
  evaluation,
}: BenchmarkConfusionMatrixPanelProps) {
  const binaryStats = computeBinaryClassificationStats(evaluation);
  const totals = computeMatrixTotals(
    evaluation.confusion_matrix,
    evaluation.labels.length
  );

  return (
    <DashboardPanel
      title={`Evaluation & confusion matrix (${name})`}
      style={{ marginBottom: "1rem" }}
    >
      {binaryStats && <BinaryClassificationStatsGrid stats={binaryStats} />}
      <ConfusionMatrixTable evaluation={evaluation} totals={totals} />
    </DashboardPanel>
  );
}

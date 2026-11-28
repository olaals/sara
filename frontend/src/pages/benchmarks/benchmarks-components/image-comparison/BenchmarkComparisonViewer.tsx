import { useEffect, useMemo, useState } from "react";
import { Checkbox, Typography } from "@equinor/eds-core-react";
import styled from "styled-components";
import type { BenchmarkImageComparison } from "../../../../api/benchmarkClient";
import DashboardPanel from "../../../../components/DashboardPanel";
import SegmentedToggle from "../../../../components/SegmentedToggle";
import BenchmarkMaskCanvas, {
  GT_COLOR,
  LegendSwatch,
  PRED_COLOR,
  REF_COLOR,
  type OverlayLayer,
} from "./BenchmarkMaskCanvas";

type ViewMode = "side-by-side" | "overlay";

const ControlsBar = styled.div`
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 1rem;
`;

const ToggleGroup = styled.div`
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
`;

const SideBySideGrid = styled.div`
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(360px, 1fr));
  gap: 1rem;
`;

interface BenchmarkComparisonViewerProps {
  comparison?: BenchmarkImageComparison;
  imageUrl: string | null;
  referenceImageUrl?: string | null;
}

export default function BenchmarkComparisonViewer({
  comparison,
  imageUrl,
  referenceImageUrl,
}: BenchmarkComparisonViewerProps) {
  const [viewMode, setViewMode] = useState<ViewMode>("side-by-side");
  const [showGt, setShowGt] = useState(true);
  const [showPred, setShowPred] = useState(true);
  const [showMasks, setShowMasks] = useState(true);
  const [showBoxes, setShowBoxes] = useState(true);

  useEffect(() => {
    if (comparison?.format) {
      setShowBoxes(comparison.format !== "thermal");
    }
  }, [comparison?.format]);

  const hasAnyMasks = useMemo(() => {
    if (!comparison) return false;
    return [...comparison.groundTruth, ...comparison.predictions].some((ann) => {
      if (!ann.segmentation) return false;
      return Array.isArray(ann.segmentation) ? ann.segmentation.length > 0 : true;
    });
  }, [comparison]);

  const overlayLayers = useMemo<OverlayLayer[]>(() => {
    if (!comparison) return [];
    const layers: OverlayLayer[] = [];
    if (showGt) {
      layers.push({ annotations: comparison.groundTruth, color: GT_COLOR });
    }
    if (showPred) {
      layers.push({ annotations: comparison.predictions, color: PRED_COLOR });
    }
    return layers;
  }, [comparison, showGt, showPred]);

  if (!comparison) return null;

  const reference = comparison.reference;
  const referencePanel = reference ? (
    referenceImageUrl ? (
      <BenchmarkMaskCanvas
        title={`Reference (${reference.referenceId})`}
        imageUrl={referenceImageUrl}
        width={reference.width}
        height={reference.height}
        layers={[{ annotations: reference.annotations, color: REF_COLOR }]}
        showMasks={showMasks}
        showBoxes={showBoxes}
      />
    ) : (
      <Typography variant="body_short">Loading reference image…</Typography>
    )
  ) : null;

  return (
    <DashboardPanel title="Ground truth vs. prediction" style={{ marginBottom: "1rem" }}>
      <ControlsBar>
        <SegmentedToggle<ViewMode>
          value={viewMode}
          onChange={setViewMode}
          options={[
            { value: "side-by-side", label: "Side by side" },
            { value: "overlay", label: "Overlay" },
          ]}
        />
        <ToggleGroup>
          {viewMode === "overlay" && (
            <>
              <span style={{ display: "inline-flex", alignItems: "center" }}>
                <LegendSwatch $color={GT_COLOR.stroke} />
                <Checkbox
                  label={`Ground truth (${comparison.groundTruth.length})`}
                  checked={showGt}
                  onChange={(e) => setShowGt(e.target.checked)}
                />
              </span>
              <span style={{ display: "inline-flex", alignItems: "center" }}>
                <LegendSwatch $color={PRED_COLOR.stroke} />
                <Checkbox
                  label={`Predictions (${comparison.predictions.length})`}
                  checked={showPred}
                  onChange={(e) => setShowPred(e.target.checked)}
                />
              </span>
            </>
          )}
          {hasAnyMasks && (
            <Checkbox
              label="Masks"
              checked={showMasks}
              onChange={(e) => setShowMasks(e.target.checked)}
            />
          )}
          <Checkbox
            label="Boxes"
            checked={showBoxes}
            onChange={(e) => setShowBoxes(e.target.checked)}
          />
        </ToggleGroup>
      </ControlsBar>

      {imageUrl ? (
        viewMode === "side-by-side" ? (
          <SideBySideGrid>
            {referencePanel}
            <BenchmarkMaskCanvas
              title={`Ground truth (${comparison.groundTruth.length})`}
              imageUrl={imageUrl}
              width={comparison.width}
              height={comparison.height}
              layers={[{ annotations: comparison.groundTruth, color: GT_COLOR }]}
              showMasks={showMasks}
              showBoxes={showBoxes}
            />
            <BenchmarkMaskCanvas
              title={`Predictions (${comparison.predictions.length})`}
              imageUrl={imageUrl}
              width={comparison.width}
              height={comparison.height}
              layers={[{ annotations: comparison.predictions, color: PRED_COLOR }]}
              showMasks={showMasks}
              showBoxes={showBoxes}
            />
          </SideBySideGrid>
        ) : referencePanel ? (
          <SideBySideGrid>
            {referencePanel}
            <BenchmarkMaskCanvas
              title="Source overlay (GT vs. prediction)"
              imageUrl={imageUrl}
              width={comparison.width}
              height={comparison.height}
              layers={overlayLayers}
              showMasks={showMasks}
              showBoxes={showBoxes}
            />
          </SideBySideGrid>
        ) : (
          <BenchmarkMaskCanvas
            imageUrl={imageUrl}
            width={comparison.width}
            height={comparison.height}
            layers={overlayLayers}
            showMasks={showMasks}
            showBoxes={showBoxes}
          />
        )
      ) : (
        <Typography variant="body_short">Loading image…</Typography>
      )}
    </DashboardPanel>
  );
}

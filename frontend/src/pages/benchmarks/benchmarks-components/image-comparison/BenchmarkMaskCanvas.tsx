import { useEffect, useRef } from "react";
import { Typography } from "@equinor/eds-core-react";
import styled from "styled-components";
import type { BenchmarkCocoAnnotation } from "../../../../api/benchmarkClient";
import { drawCocoAnnotations, type OverlayColor } from "../../../../utils/cocoMask";

export const GT_COLOR: OverlayColor = {
  fill: [0, 170, 90, 110],
  stroke: "#00c864",
};

export const PRED_COLOR: OverlayColor = {
  fill: [235, 0, 80, 110],
  stroke: "#ff2a6d",
};

export const REF_COLOR: OverlayColor = {
  fill: [0, 180, 216, 100],
  stroke: "#00d4ff",
};

export const LegendSwatch = styled.span<{ $color: string }>`
  display: inline-block;
  width: 12px;
  height: 12px;
  border-radius: 2px;
  background: ${(p) => p.$color};
  margin-right: 0.35rem;
  vertical-align: middle;
`;

const StageWrapper = styled.div`
  position: relative;
  width: 100%;
  background: #111;
  border-radius: 4px;
  overflow: hidden;
`;

const BaseImage = styled.img`
  display: block;
  width: 100%;
  height: auto;
`;

const OverlayCanvas = styled.canvas`
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
`;

export interface OverlayLayer {
  annotations: BenchmarkCocoAnnotation[];
  color: OverlayColor;
}

interface BenchmarkMaskCanvasProps {
  title?: string;
  imageUrl: string;
  width: number;
  height: number;
  layers: OverlayLayer[];
  showMasks: boolean;
  showBoxes: boolean;
}

export default function BenchmarkMaskCanvas({
  title,
  imageUrl,
  width,
  height,
  layers,
  showMasks,
  showBoxes,
}: BenchmarkMaskCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || width <= 0 || height <= 0) return;
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.clearRect(0, 0, width, height);

    for (const layer of layers) {
      drawCocoAnnotations(ctx, width, height, layer.annotations, layer.color, {
        showMasks,
        showBoxes,
      });
    }
  }, [width, height, layers, showMasks, showBoxes]);

  return (
    <div>
      {title && (
        <Typography variant="h6" style={{ marginBottom: "0.4rem" }}>
          {title}
        </Typography>
      )}
      <StageWrapper>
        <BaseImage src={imageUrl} alt={title ?? "Benchmark image"} />
        <OverlayCanvas ref={canvasRef} />
      </StageWrapper>
    </div>
  );
}

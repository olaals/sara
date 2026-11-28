import type {
  BenchmarkCocoAnnotation,
  CocoRleSegmentation,
} from "../api/benchmarkClient";

export interface OverlayColor {
  fill: [number, number, number, number];
  stroke: string;
}

export function decodeCompressedRleCounts(encoded: string): number[] {
  const counts: number[] = [];
  let p = 0;
  let m = 0;
  while (p < encoded.length) {
    let x = 0;
    let k = 0;
    let more = true;
    while (more && p < encoded.length) {
      const c = encoded.charCodeAt(p) - 48;
      x |= (c & 0x1f) << (5 * k);
      more = (c & 0x20) !== 0;
      p += 1;
      k += 1;
      if (!more && (c & 0x10) !== 0) {
        x |= -1 << (5 * k);
      }
    }
    if (m > 2) {
      x += counts[m - 2];
    }
    counts.push(x);
    m += 1;
  }
  return counts;
}

function isRleSegmentation(seg: unknown): seg is CocoRleSegmentation {
  return (
    typeof seg === "object" &&
    seg !== null &&
    !Array.isArray(seg) &&
    "size" in seg &&
    "counts" in seg
  );
}

function paintRleMask(
  imageData: ImageData,
  rle: CocoRleSegmentation,
  fill: [number, number, number, number]
) {
  const [height, width] = rle.size;
  if (height !== imageData.height || width !== imageData.width) {
    return;
  }
  const counts =
    typeof rle.counts === "string"
      ? decodeCompressedRleCounts(rle.counts)
      : rle.counts;

  const data = imageData.data;
  const totalPixels = height * width;
  let colMajorIdx = 0;
  let foreground = false;

  for (const runLength of counts) {
    if (runLength <= 0) {
      foreground = !foreground;
      continue;
    }
    const end = Math.min(colMajorIdx + runLength, totalPixels);
    if (foreground) {
      for (let i = colMajorIdx; i < end; i += 1) {
        const x = Math.floor(i / height);
        const y = i - x * height;
        const offset = (y * width + x) * 4;
        data[offset] = fill[0];
        data[offset + 1] = fill[1];
        data[offset + 2] = fill[2];
        data[offset + 3] = fill[3];
      }
    }
    colMajorIdx = end;
    foreground = !foreground;
  }
}

export function drawCocoAnnotations(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  annotations: BenchmarkCocoAnnotation[],
  color: OverlayColor,
  options: { showMasks: boolean; showBoxes: boolean }
) {
  if (options.showMasks) {
    const maskCanvas = document.createElement("canvas");
    maskCanvas.width = width;
    maskCanvas.height = height;
    const maskCtx = maskCanvas.getContext("2d");
    if (maskCtx) {
      const imageData = maskCtx.createImageData(width, height);
      let hasRle = false;
      for (const ann of annotations) {
        if (isRleSegmentation(ann.segmentation)) {
          paintRleMask(imageData, ann.segmentation, color.fill);
          hasRle = true;
        }
      }
      if (hasRle) {
        maskCtx.putImageData(imageData, 0, 0);
        ctx.drawImage(maskCanvas, 0, 0);
      }

      const [r, g, b, a] = color.fill;
      ctx.fillStyle = `rgba(${r}, ${g}, ${b}, ${a / 255})`;
      ctx.strokeStyle = color.stroke;
      ctx.lineWidth = Math.max(2, Math.round(Math.max(width, height) / 400));
      for (const ann of annotations) {
        if (!Array.isArray(ann.segmentation)) continue;
        for (const poly of ann.segmentation) {
          if (poly.length < 6) continue;
          ctx.beginPath();
          ctx.moveTo(poly[0], poly[1]);
          for (let i = 2; i < poly.length; i += 2) {
            ctx.lineTo(poly[i], poly[i + 1]);
          }
          ctx.closePath();
          ctx.fill();
          ctx.stroke();
        }
      }
    }
  }

  if (options.showBoxes) {
    ctx.strokeStyle = color.stroke;
    ctx.lineWidth = Math.max(2, Math.round(Math.max(width, height) / 400));
    for (const ann of annotations) {
      if (!ann.bbox || ann.bbox.length !== 4) continue;
      const [x, y, w, h] = ann.bbox;
      ctx.strokeRect(x, y, w, h);
    }
  }
}

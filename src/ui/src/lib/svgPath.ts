/**
 * Smoothed bezier line path through a value series, scaled into a w×h box.
 * Min defaults to 0 so the series sits anchored at the bottom of the area.
 */
export function linePath(values: number[], w: number, h: number, padY = 4): string {
  if (!values.length) {
    return '';
  }

  const max = Math.max(...values, 1);
  const min = 0;
  const sx = (i: number) => (i / (values.length - 1 || 1)) * w;
  const sy = (v: number) => h - padY - ((v - min) / (max - min || 1)) * (h - padY * 2);
  let d = `M ${sx(0).toFixed(2)} ${sy(values[0]).toFixed(2)}`;

  for (let i = 1; i < values.length; i++) {
    const x0 = sx(i - 1);
    const y0 = sy(values[i - 1]);
    const x1 = sx(i);
    const y1 = sy(values[i]);
    const cx = (x0 + x1) / 2;
    d += ` Q ${cx.toFixed(2)} ${y0.toFixed(2)} ${((cx + x1) / 2).toFixed(2)} ${((y0 + y1) / 2).toFixed(2)}`;
    d += ` T ${x1.toFixed(2)} ${y1.toFixed(2)}`;
  }

  return d;
}

/** Same as linePath but closes the shape into a filled area. */
export function areaPath(values: number[], w: number, h: number, padY = 4): string {
  const line = linePath(values, w, h, padY);
  if (!line) {
    return '';
  }

  return `${line} L ${w} ${h} L 0 ${h} Z`;
}

/** Deterministic pseudo-random generator for stable demo series. */
export function seeded(seed: number): () => number {
  let s = seed >>> 0;

  return () => {
    s = (s * 1664525 + 1013904223) >>> 0;

    return s / 0xffffffff;
  };
}

export function sparkSeries(n = 18, seed = 1, swing = 0.5): number[] {
  const r = seeded(seed);
  const out: number[] = [];
  let v = 0.5;
  for (let i = 0; i < n; i++) {
    v += (r() - 0.5) * swing;
    v = Math.max(0.05, Math.min(0.95, v));
    out.push(v);
  }

  return out;
}

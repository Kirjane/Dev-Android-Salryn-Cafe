import { useState } from "react";
import { TrendPoint } from "../../api/ownerAnalytics";
import { formatMoney } from "../../utils/format";

const peso = (cents: number | null | undefined) =>
  cents == null ? "Unavailable" : formatMoney(cents / 100);

// --- 1. SALES TREND & COLLECTIONS TIME-SERIES SVG CHART ---
interface SalesTrendChartProps {
  points: TrendPoint[];
  onSelectDay?: (day: string) => void;
}

export function SalesTrendChart({ points, onSelectDay }: SalesTrendChartProps) {
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null);

  if (points.length === 0) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-slate-500">
        No sales recorded in the selected period.
      </div>
    );
  }

  const svgWidth = 720;
  const svgHeight = 280;
  const paddingLeft = 65;
  const paddingRight = 20;
  const paddingTop = 30;
  const paddingBottom = 45;

  const chartWidth = svgWidth - paddingLeft - paddingRight;
  const chartHeight = svgHeight - paddingTop - paddingBottom;

  const maxVal = Math.max(
    1,
    ...points.map((p) => Math.max(p.salesTotalCents, p.cashOnSalesCents, p.creditOriginatedCents))
  );

  const stepX = points.length === 1 ? chartWidth / 2 : chartWidth / (points.length - 1);
  const barWidth = Math.max(8, Math.min(32, chartWidth / points.length - 8));

  const getY = (val: number) => paddingTop + chartHeight - (val / maxVal) * chartHeight;
  const getX = (idx: number) =>
    points.length === 1 ? paddingLeft + chartWidth / 2 : paddingLeft + idx * stepX;

  // Grid lines
  const gridLines = [0, 0.25, 0.5, 0.75, 1.0].map((pct) => {
    const val = maxVal * pct;
    const y = getY(val);
    return { val, y };
  });

  // Polyline points
  const salesBarPositions = points.map((p, i) => {
    const x = points.length === 1 ? paddingLeft + chartWidth / 2 : paddingLeft + i * stepX;
    const y = getY(p.salesTotalCents);
    const height = Math.max(2, paddingTop + chartHeight - y);
    return { x: x - barWidth / 2, y, width: barWidth, height, point: p, idx: i };
  });

  const cashLinePoints = points
    .map((p, i) => `${getX(i)},${getY(p.cashOnSalesCents)}`)
    .join(" ");

  const creditLinePoints = points
    .map((p, i) => `${getX(i)},${getY(p.creditOriginatedCents)}`)
    .join(" ");

  const activePoint = hoveredIdx !== null ? points[hoveredIdx] : null;
  const activeX = hoveredIdx !== null ? getX(hoveredIdx) : 0;

  return (
    <div className="relative w-full overflow-hidden">
      {/* Legend */}
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2 text-xs">
        <div className="flex items-center gap-4">
          <span className="flex items-center gap-1.5 font-medium text-slate-700">
            <span className="inline-block h-3 w-3 rounded-sm bg-blue-600" />
            Total Sales
          </span>
          <span className="flex items-center gap-1.5 font-medium text-slate-700">
            <span className="inline-block h-2 w-3 rounded-full bg-emerald-600" />
            Cash on Sales
          </span>
          <span className="flex items-center gap-1.5 font-medium text-slate-700">
            <span className="inline-block h-2 w-3 rounded-full bg-amber-500" />
            Utang Originated
          </span>
        </div>
        <span className="text-slate-500">
          Click any date to view day details
        </span>
      </div>

      <div className="w-full overflow-x-auto">
        <svg
          viewBox={`0 0 ${svgWidth} ${svgHeight}`}
          className="h-auto w-full select-none"
          style={{ minWidth: "520px" }}
        >
          {/* Grid lines & Y-axis labels */}
          {gridLines.map((g, idx) => (
            <g key={idx}>
              <line
                x1={paddingLeft}
                y1={g.y}
                x2={svgWidth - paddingRight}
                y2={g.y}
                stroke="#e2e8f0"
                strokeDasharray={idx === 0 ? "none" : "3,3"}
              />
              <text
                x={paddingLeft - 8}
                y={g.y + 4}
                textAnchor="end"
                className="fill-slate-600 text-[10px] font-medium"
              >
                {peso(g.val)}
              </text>
            </g>
          ))}

          {/* Sales Total Bars */}
          {salesBarPositions.map((bar) => (
            <rect
              key={`bar-${bar.idx}`}
              x={bar.x}
              y={bar.y}
              width={bar.width}
              height={bar.height}
              rx={3}
              className={`transition-colors cursor-pointer ${
                hoveredIdx === bar.idx
                  ? "fill-blue-700 opacity-90"
                  : "fill-blue-500 opacity-75"
              }`}
              onMouseEnter={() => setHoveredIdx(bar.idx)}
              onMouseLeave={() => setHoveredIdx(null)}
              onClick={() => onSelectDay?.(bar.point.storeDate)}
            />
          ))}

          {/* Cash Line & Dots */}
          {points.length > 1 && (
            <polyline
              fill="none"
              stroke="#059669"
              strokeWidth={2.5}
              strokeLinecap="round"
              strokeLinejoin="round"
              points={cashLinePoints}
            />
          )}

          {/* Utang Line & Dots */}
          {points.length > 1 && (
            <polyline
              fill="none"
              stroke="#d97706"
              strokeWidth={2.5}
              strokeDasharray="4,3"
              strokeLinecap="round"
              strokeLinejoin="round"
              points={creditLinePoints}
            />
          )}

          {/* Points & Interactive Columns */}
          {points.map((p, i) => {
            const cx = getX(i);
            const cashY = getY(p.cashOnSalesCents);
            const creditY = getY(p.creditOriginatedCents);
            const isHovered = hoveredIdx === i;

            return (
              <g key={`col-${i}`}>
                {/* Full-height invisible interactive touch zone */}
                <rect
                  x={cx - (barWidth + 8) / 2}
                  y={paddingTop}
                  width={barWidth + 8}
                  height={chartHeight}
                  fill="transparent"
                  className="cursor-pointer"
                  onMouseEnter={() => setHoveredIdx(i)}
                  onMouseLeave={() => setHoveredIdx(null)}
                  onClick={() => onSelectDay?.(p.storeDate)}
                />

                {/* Cash Dot */}
                <circle
                  cx={cx}
                  cy={cashY}
                  r={isHovered ? 5 : 3.5}
                  fill="#059669"
                  stroke="#ffffff"
                  strokeWidth={1.5}
                  className="cursor-pointer transition-all"
                  onMouseEnter={() => setHoveredIdx(i)}
                  onClick={() => onSelectDay?.(p.storeDate)}
                />

                {/* Credit Dot */}
                <circle
                  cx={cx}
                  cy={creditY}
                  r={isHovered ? 5 : 3.5}
                  fill="#d97706"
                  stroke="#ffffff"
                  strokeWidth={1.5}
                  className="cursor-pointer transition-all"
                  onMouseEnter={() => setHoveredIdx(i)}
                  onClick={() => onSelectDay?.(p.storeDate)}
                />

                {/* X-axis date label */}
                <text
                  x={cx}
                  y={svgHeight - 12}
                  textAnchor="middle"
                  className={`text-[10px] font-medium cursor-pointer transition-colors ${
                    isHovered ? "fill-blue-700 font-bold" : "fill-slate-600"
                  }`}
                  onClick={() => onSelectDay?.(p.storeDate)}
                >
                  {p.storeDate.slice(5)}
                </text>
              </g>
            );
          })}

          {/* Active tooltip on hover */}
          {activePoint && (
            <g
              transform={`translate(${Math.min(
                svgWidth - 170,
                Math.max(paddingLeft, activeX - 75)
              )}, ${Math.max(paddingTop, getY(activePoint.salesTotalCents) - 75)})`}
              className="pointer-events-none"
            >
              <rect
                x={0}
                y={0}
                width={150}
                height={68}
                rx={6}
                fill="#1e293b"
                opacity={0.94}
                filter="drop-shadow(0 4px 6px rgba(0, 0, 0, 0.15))"
              />
              <text x={8} y={16} fill="#f8fafc" className="text-[11px] font-bold">
                Store Day: {activePoint.storeDate}
              </text>
              <text x={8} y={32} fill="#93c5fd" className="text-[10px] font-medium">
                Sales: {peso(activePoint.salesTotalCents)}
              </text>
              <text x={8} y={46} fill="#86efac" className="text-[10px] font-medium">
                Cash: {peso(activePoint.cashOnSalesCents)}
              </text>
              <text x={8} y={60} fill="#fde68a" className="text-[10px] font-medium">
                Utang: {peso(activePoint.creditOriginatedCents)}
              </text>
            </g>
          )}
        </svg>
      </div>

      <div className="mt-3 grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-7 gap-2">
        {points.map((p) => (
          <button
            key={p.storeDate}
            type="button"
            onClick={() => onSelectDay?.(p.storeDate)}
            className="flex flex-col rounded-lg border border-slate-200 bg-slate-50/70 p-2 text-left hover:border-blue-400 hover:bg-blue-50/50 transition-colors"
          >
            <span className="text-[11px] font-semibold text-slate-700">{p.storeDate}</span>
            <span className="text-xs font-bold text-slate-900 tabular-nums">{peso(p.salesTotalCents)}</span>
          </button>
        ))}
      </div>

      <details className="mt-2 text-xs">
        <summary className="cursor-pointer font-semibold text-blue-700 hover:text-blue-800">
          Tabular sales trend data
        </summary>
        <div className="mt-2 max-h-48 overflow-y-auto rounded-lg border border-slate-200">
          <table className="w-full text-left text-xs">
            <thead className="bg-slate-50 text-slate-500">
              <tr>
                <th className="px-3 py-2 font-medium">Store Day</th>
                <th className="px-3 py-2 text-right font-medium">Total Sales</th>
                <th className="px-3 py-2 text-right font-medium">Cash</th>
                <th className="px-3 py-2 text-right font-medium">Utang</th>
                <th className="px-3 py-2 text-center font-medium">Sales Count</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {points.map((p) => (
                <tr
                  key={p.storeDate}
                  className="cursor-pointer hover:bg-blue-50/50"
                  onClick={() => onSelectDay?.(p.storeDate)}
                >
                  <td className="px-3 py-1.5 font-medium text-slate-800">{p.storeDate}</td>
                  <td className="px-3 py-1.5 text-right font-semibold text-slate-900 tabular-nums">
                    {peso(p.salesTotalCents)}
                  </td>
                  <td className="px-3 py-1.5 text-right text-emerald-700 tabular-nums">
                    {peso(p.cashOnSalesCents)}
                  </td>
                  <td className="px-3 py-1.5 text-right text-amber-700 tabular-nums">
                    {peso(p.creditOriginatedCents)}
                  </td>
                  <td className="px-3 py-1.5 text-center text-slate-600">{p.completedSaleCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  );
}

// --- 2. SALES VS VOID ACTIVITY CHART ---
interface SalesVsVoidChartProps {
  points: TrendPoint[];
  onSelectDay?: (day: string) => void;
}

export function SalesVsVoidChart({ points, onSelectDay }: SalesVsVoidChartProps) {
  const maxCount = Math.max(1, ...points.map((p) => Math.max(p.completedSaleCount, p.voidedSaleCount)));

  const svgWidth = 600;
  const svgHeight = 220;
  const paddingLeft = 45;
  const paddingRight = 20;
  const paddingTop = 25;
  const paddingBottom = 40;

  const chartWidth = svgWidth - paddingLeft - paddingRight;
  const chartHeight = svgHeight - paddingTop - paddingBottom;
  const stepX = points.length === 1 ? chartWidth / 2 : chartWidth / points.length;
  const groupBarWidth = Math.max(6, Math.min(20, stepX / 2 - 4));

  const getY = (count: number) => paddingTop + chartHeight - (count / maxCount) * chartHeight;

  return (
    <div className="relative w-full">
      <div className="mb-2 flex items-center justify-between text-xs">
        <div className="flex items-center gap-4">
          <span className="flex items-center gap-1.5 font-medium text-slate-700">
            <span className="inline-block h-3 w-3 rounded-sm bg-blue-600" />
            Completed Sales
          </span>
          <span className="flex items-center gap-1.5 font-medium text-slate-700">
            <span className="inline-block h-3 w-3 rounded-sm bg-rose-500" />
            Voided Audits
          </span>
        </div>
        <span className="text-slate-500">Transaction counts</span>
      </div>

      <div className="w-full overflow-x-auto">
        <svg
          viewBox={`0 0 ${svgWidth} ${svgHeight}`}
          className="h-auto w-full select-none"
          style={{ minWidth: "480px" }}
        >
          {/* Horizontal lines */}
          {[0, 0.5, 1.0].map((pct, idx) => {
            const count = Math.round(maxCount * pct);
            const y = getY(count);
            return (
              <g key={idx}>
                <line
                  x1={paddingLeft}
                  y1={y}
                  x2={svgWidth - paddingRight}
                  y2={y}
                  stroke="#e2e8f0"
                  strokeDasharray="2,2"
                />
                <text
                  x={paddingLeft - 6}
                  y={y + 4}
                  textAnchor="end"
                  className="fill-slate-600 text-[10px]"
                >
                  {count}
                </text>
              </g>
            );
          })}

          {/* Grouped bars */}
          {points.map((p, i) => {
            const colCenterX = paddingLeft + (i + 0.5) * stepX;
            const completedY = getY(p.completedSaleCount);
            const completedH = Math.max(0, paddingTop + chartHeight - completedY);
            const voidY = getY(p.voidedSaleCount);
            const voidH = Math.max(0, paddingTop + chartHeight - voidY);

            return (
              <g
                key={p.storeDate}
                className="cursor-pointer"
                onClick={() => onSelectDay?.(p.storeDate)}
              >
                {/* Completed Bar */}
                <rect
                  x={colCenterX - groupBarWidth - 1}
                  y={completedY}
                  width={groupBarWidth}
                  height={completedH}
                  rx={2}
                  className="fill-blue-600 hover:fill-blue-700 transition-colors"
                >
                  <title>{`${p.storeDate}: ${p.completedSaleCount} completed sales`}</title>
                </rect>

                {/* Voided Bar */}
                <rect
                  x={colCenterX + 1}
                  y={voidY}
                  width={groupBarWidth}
                  height={voidH}
                  rx={2}
                  className="fill-rose-500 hover:fill-rose-600 transition-colors"
                >
                  <title>{`${p.storeDate}: ${p.voidedSaleCount} void audits`}</title>
                </rect>

                <text
                  x={colCenterX}
                  y={svgHeight - 12}
                  textAnchor="middle"
                  className="fill-slate-600 text-[10px] font-medium"
                >
                  {p.storeDate.slice(5)}
                </text>
              </g>
            );
          })}
        </svg>
      </div>
    </div>
  );
}

// --- 3. PAYMENT MIX SVG DONUT & BREAKDOWN ---
interface PaymentMixChartProps {
  rows: { method: string; amountCents: number; scope: string }[];
}

export function PaymentMixChart({ rows }: PaymentMixChartProps) {
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null);

  const total = rows.reduce((sum, r) => sum + r.amountCents, 0);

  if (rows.length === 0 || total === 0) {
    return (
      <div className="flex h-56 items-center justify-center text-sm text-slate-500">
        No payment mix data recorded for this period.
      </div>
    );
  }

  const palette = [
    "#059669", // Emerald for Cash
    "#d97706", // Amber for Utang / Credit
    "#2563eb", // Blue
    "#7c3aed", // Purple
    "#0891b2", // Cyan
  ];

  // Calculate donut arcs
  const size = 180;
  const center = size / 2;
  const radius = 70;
  const innerRadius = 45;

  let startAngle = 0;
  const slices = rows.map((r, i) => {
    const fraction = r.amountCents / total;
    const angle = fraction * 2 * Math.PI;
    const endAngle = startAngle + angle;

    const x1 = center + radius * Math.cos(startAngle);
    const y1 = center + radius * Math.sin(startAngle);
    const x2 = center + radius * Math.cos(endAngle);
    const y2 = center + radius * Math.sin(endAngle);

    const ix1 = center + innerRadius * Math.cos(endAngle);
    const iy1 = center + innerRadius * Math.sin(endAngle);
    const ix2 = center + innerRadius * Math.cos(startAngle);
    const iy2 = center + innerRadius * Math.sin(startAngle);

    const largeArc = angle > Math.PI ? 1 : 0;
    const path =
      fraction >= 0.999
        ? `M ${center - radius} ${center} A ${radius} ${radius} 0 1 0 ${center + radius} ${center} A ${radius} ${radius} 0 1 0 ${center - radius} ${center} M ${center - innerRadius} ${center} A ${innerRadius} ${innerRadius} 0 1 1 ${center + innerRadius} ${center} A ${innerRadius} ${innerRadius} 0 1 1 ${center - innerRadius} ${center}`
        : `M ${x1} ${y1} A ${radius} ${radius} 0 ${largeArc} 1 ${x2} ${y2} L ${ix1} ${iy1} A ${innerRadius} ${innerRadius} 0 ${largeArc} 0 ${ix2} ${iy2} Z`;

    const color = palette[i % palette.length];
    startAngle = endAngle;

    return { row: r, fraction, path, color, index: i };
  });

  return (
    <div className="flex flex-col sm:flex-row items-center justify-around gap-6">
      {/* Donut SVG */}
      <div className="relative flex-shrink-0">
        <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
          {slices.map((slice) => (
            <path
              key={slice.index}
              d={slice.path}
              fill={slice.color}
              className={`transition-all cursor-pointer ${
                hoveredIdx === slice.index ? "opacity-100 scale-105" : "opacity-85"
              }`}
              onMouseEnter={() => setHoveredIdx(slice.index)}
              onMouseLeave={() => setHoveredIdx(null)}
            >
              <title>{`${slice.row.method}: ${peso(slice.row.amountCents)} (${(
                slice.fraction * 100
              ).toFixed(1)}%)`}</title>
            </path>
          ))}
          <circle cx={center} cy={center} r={innerRadius - 4} fill="#ffffff" />
          <text
            x={center}
            y={center - 4}
            textAnchor="middle"
            className="fill-slate-500 text-[10px] font-medium"
          >
            Total Mix
          </text>
          <text
            x={center}
            y={center + 14}
            textAnchor="middle"
            className="fill-slate-900 text-xs font-bold tabular-nums"
          >
            {peso(total)}
          </text>
        </svg>
      </div>

      {/* Legend & Breakdown */}
      <div className="w-full space-y-2">
        {slices.map((s) => (
          <div
            key={s.index}
            className={`flex items-center justify-between gap-3 rounded-lg border p-2 text-xs transition-colors cursor-pointer ${
              hoveredIdx === s.index
                ? "border-blue-300 bg-blue-50/50"
                : "border-slate-100 bg-slate-50/50"
            }`}
            onMouseEnter={() => setHoveredIdx(s.index)}
            onMouseLeave={() => setHoveredIdx(null)}
          >
            <div className="flex items-center gap-2">
              <span
                className="h-3 w-3 rounded-sm flex-shrink-0"
                style={{ backgroundColor: s.color }}
              />
              <div>
                <p className="font-semibold text-slate-800">{s.row.method}</p>
                <p className="text-[10px] text-slate-500">{s.row.scope}</p>
              </div>
            </div>
            <div className="text-right tabular-nums">
              <p className="font-bold text-slate-950">{peso(s.row.amountCents)}</p>
              <p className="text-[10px] font-medium text-slate-600">
                {(s.fraction * 100).toFixed(1)}%
              </p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// --- 4. TOP PRODUCTS HORIZONTAL BAR CHART ---
interface TopProductsChartProps {
  products: {
    productId: string;
    historicalLabel: string;
    historicalBarcode: string | null;
    quantityX1000: number;
    lineRevenueCents: number;
    costCoverageComplete: boolean;
  }[];
}

export function TopProductsChart({ products }: TopProductsChartProps) {
  if (products.length === 0) {
    return (
      <div className="flex h-48 items-center justify-center text-sm text-slate-500">
        No completed product sales in this period.
      </div>
    );
  }

  const maxRevenue = Math.max(1, ...products.map((p) => p.lineRevenueCents));

  return (
    <div className="space-y-3">
      {products.map((p, idx) => {
        const pct = Math.max(3, (p.lineRevenueCents / maxRevenue) * 100);
        const qty = p.quantityX1000 / 1000;

        return (
          <div key={p.productId || idx} className="space-y-1">
            <div className="flex items-center justify-between gap-3 text-xs">
              <div className="flex items-center gap-2 overflow-hidden">
                <span className="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full bg-slate-100 text-[10px] font-bold text-slate-600">
                  #{idx + 1}
                </span>
                <span className="truncate font-medium text-slate-900" title={p.historicalLabel}>
                  {p.historicalLabel}
                </span>
              </div>
              <div className="flex items-center gap-3 tabular-nums">
                <span className="text-slate-500 text-[11px]">{qty.toLocaleString()} sold</span>
                <span className="font-bold text-slate-950">{peso(p.lineRevenueCents)}</span>
              </div>
            </div>
            <div className="h-3 w-full rounded bg-slate-100 overflow-hidden">
              <div
                className="h-full rounded bg-gradient-to-r from-blue-500 to-indigo-600 transition-all duration-300"
                style={{ width: `${pct}%` }}
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}

// --- 5. AR AGING DISTRIBUTION CHART ---
interface ArAgingChartProps {
  buckets: { key: string; label: string; outstandingCents: number }[];
  onSelectBucket?: (key: string) => void;
}

export function ArAgingDistributionChart({ buckets, onSelectBucket }: ArAgingChartProps) {
  const maxOutstanding = Math.max(1, ...buckets.map((b) => b.outstandingCents));

  const tierColors: Record<string, { bg: string; fill: string }> = {
    current: { bg: "bg-emerald-50 text-emerald-900", fill: "fill-emerald-500" },
    days30: { bg: "bg-sky-50 text-sky-900", fill: "fill-sky-500" },
    days60: { bg: "bg-amber-50 text-amber-900", fill: "fill-amber-500" },
    days90: { bg: "bg-orange-50 text-orange-900", fill: "fill-orange-500" },
    older: { bg: "bg-rose-50 text-rose-900", fill: "fill-rose-500" },
  };

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3">
        {buckets.map((b) => {
          const config = tierColors[b.key] || { bg: "bg-slate-50 text-slate-900", fill: "fill-slate-500" };
          const pct = Math.max(2, (b.outstandingCents / maxOutstanding) * 100);

          return (
            <button
              type="button"
              key={b.key}
              onClick={() => onSelectBucket?.(b.key)}
              className={`rounded-xl border border-slate-200 p-3 text-left transition-all hover:border-blue-400 hover:shadow-sm ${config.bg}`}
            >
              <p className="text-[11px] font-semibold uppercase tracking-wider text-slate-500">
                {b.label}
              </p>
              <p className="mt-1 text-base font-bold tabular-nums">
                {peso(b.outstandingCents)}
              </p>
              <div className="mt-2 h-1.5 w-full rounded bg-slate-200/60 overflow-hidden">
                <div
                  className="h-full rounded bg-slate-700 transition-all"
                  style={{ width: `${pct}%` }}
                />
              </div>
            </button>
          );
        })}
      </div>
    </div>
  );
}

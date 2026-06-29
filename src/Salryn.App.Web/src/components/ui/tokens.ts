// Salryn web design tokens (WEB-A3 foundation).
// Central place for shared Tailwind class fragments so primitives stay consistent.
// No business logic here. No backend capability implied.

export const surface = {
  shell: "bg-navy-900 text-white",
  rail: "bg-navy-800 text-slate-200",
  railActive: "bg-navy-700 text-white",
  workspace: "bg-slate-100",
  card: "bg-white border border-slate-200 shadow-card",
} as const;

export const text = {
  heading: "text-slate-900",
  body: "text-slate-700",
  muted: "text-slate-500",
  subtle: "text-slate-400",
} as const;

export const focusRing =
  "focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-offset-1";

export type Tone = "neutral" | "primary" | "success" | "warning" | "danger" | "info";

export const toneText: Record<Tone, string> = {
  neutral: "text-slate-600",
  primary: "text-navy-700",
  success: "text-emerald-700",
  warning: "text-amber-700",
  danger: "text-red-700",
  info: "text-sky-700",
};

export const tonePill: Record<Tone, string> = {
  neutral: "bg-slate-100 text-slate-700 ring-slate-200",
  primary: "bg-navy-700/10 text-navy-700 ring-navy-700/20",
  success: "bg-emerald-50 text-emerald-700 ring-emerald-200",
  warning: "bg-amber-50 text-amber-700 ring-amber-200",
  danger: "bg-red-50 text-red-700 ring-red-200",
  info: "bg-sky-50 text-sky-700 ring-sky-200",
};

export const toneAlert: Record<Tone, string> = {
  neutral: "bg-slate-50 border-slate-200 text-slate-700",
  primary: "bg-navy-700/5 border-navy-700/20 text-navy-700",
  success: "bg-emerald-50 border-emerald-200 text-emerald-800",
  warning: "bg-amber-50 border-amber-200 text-amber-800",
  danger: "bg-red-50 border-red-200 text-red-800",
  info: "bg-sky-50 border-sky-200 text-sky-800",
};

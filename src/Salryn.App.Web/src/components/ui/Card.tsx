import type { ReactNode } from "react";

type CardProps = {
  children: ReactNode;
  className?: string;
};

export function Card({ children, className = "" }: CardProps) {
  return (
    <div className={`rounded-2xl border border-slate-200 bg-white shadow-card ${className}`}>
      {children}
    </div>
  );
}

export function CardHeader({ children, className = "" }: CardProps) {
  return (
    <div className={`border-b border-slate-100 px-6 py-4 ${className}`}>{children}</div>
  );
}

export function CardTitle({ children, className = "" }: CardProps) {
  return <h3 className={`text-base font-semibold text-slate-900 ${className}`}>{children}</h3>;
}

export function CardBody({ children, className = "" }: CardProps) {
  return <div className={`px-6 py-5 ${className}`}>{children}</div>;
}

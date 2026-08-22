import type { ReactNode } from "react";

type CardProps = {
  children: ReactNode;
  className?: string;
};

export function Card({ children, className = "" }: CardProps) {
  return (
    <section className={`rounded-lg border border-slate-200 bg-white shadow-card ${className}`}>
      {children}
    </section>
  );
}

export function CardHeader({ children, className = "" }: CardProps) {
  return (
    <div className={`border-b border-slate-200 px-5 py-3.5 ${className}`}>{children}</div>
  );
}

export function CardTitle({ children, className = "" }: CardProps) {
  return <h3 className={`text-sm font-semibold text-slate-900 ${className}`}>{children}</h3>;
}

export function CardBody({ children, className = "" }: CardProps) {
  return <div className={`px-5 py-4 ${className}`}>{children}</div>;
}

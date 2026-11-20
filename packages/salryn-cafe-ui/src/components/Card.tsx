import type { ReactNode } from "react";

export function Card({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <section className={`salryn-card ${className}`.trim()}>{children}</section>;
}

export function CardHeader({
  title,
  subtitle,
  action,
}: {
  title: string;
  subtitle?: string;
  action?: ReactNode;
}) {
  return (
    <div className="salryn-card-header">
      <div>
        <h2 style={{ margin: 0, fontSize: "1.0625rem", fontWeight: 700, color: "var(--salryn-espresso)" }}>
          {title}
        </h2>
        {subtitle ? (
          <p style={{ margin: "2px 0 0", fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>{subtitle}</p>
        ) : null}
      </div>
      {action ? <div>{action}</div> : null}
    </div>
  );
}

export function CardBody({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <div className={`salryn-card-body ${className}`.trim()}>{children}</div>;
}

export function CardFooter({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <div className={`salryn-card-footer ${className}`.trim()}>{children}</div>;
}

export function MetricCard({
  label,
  value,
  detail,
}: {
  label: string;
  value: string;
  detail?: string;
}) {
  return (
    <Card>
      <CardBody>
        <p style={{ margin: 0, fontSize: "0.8125rem", fontWeight: 600, color: "var(--salryn-mocha)" }}>
          {label}
        </p>
        <p
          className="salryn-tabular"
          style={{
            margin: "6px 0 0",
            fontSize: "1.625rem",
            fontWeight: 800,
            color: "var(--salryn-espresso)",
            lineHeight: 1.2,
          }}
        >
          {value}
        </p>
        {detail ? (
          <p style={{ margin: "6px 0 0", fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>{detail}</p>
        ) : null}
      </CardBody>
    </Card>
  );
}

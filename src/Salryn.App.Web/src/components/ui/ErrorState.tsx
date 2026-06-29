import type { ReactNode } from "react";

type ErrorStateProps = {
  title?: string;
  message: string;
  action?: ReactNode;
};

export function ErrorState({ title = "Something went wrong", message, action }: ErrorStateProps) {
  return (
    <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-5 text-sm">
      <p className="font-semibold text-red-800">{title}</p>
      <p className="mt-1 text-red-700">{message}</p>
      {action ? <div className="mt-3">{action}</div> : null}
    </div>
  );
}

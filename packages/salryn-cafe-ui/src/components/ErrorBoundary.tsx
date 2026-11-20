import { Component, type ErrorInfo, type ReactNode } from "react";
import { Alert } from "./Feedback";
import { Button } from "./Button";

type ErrorBoundaryProps = {
  children: ReactNode;
  fallbackTitle?: string;
  onReset?: () => void;
};

type ErrorBoundaryState = {
  error: Error | null;
};

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(_error: Error, _info: ErrorInfo) {
    // Host applications may wire telemetry without crashing the workstation shell.
  }

  private handleReset = () => {
    this.setState({ error: null });
    this.props.onReset?.();
  };

  render() {
    if (this.state.error) {
      return (
        <div style={{ padding: 24, maxWidth: 560, margin: "24px auto" }}>
          <Alert
            tone="danger"
            title={this.props.fallbackTitle ?? "This Salryn Cafe view encountered an error"}
            description={this.state.error.message || "Unexpected rendering failure."}
          />
          <div style={{ marginTop: 14 }}>
            <Button variant="primary" onClick={this.handleReset}>
              Reload view
            </Button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}

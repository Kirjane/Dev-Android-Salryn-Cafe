import { useState, type FormEvent } from "react";
import { login } from "../api/session";
import { Alert, Button, FormField, PasswordInput, TextInput } from "../components/ui";

type LoginPageProps = {
  onLoggedIn: () => void;
};

export function LoginPage({ onLoggedIn }: LoginPageProps) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setIsSubmitting(true);
    setError(null);

    try {
      await login(username, password);
      // The raw password is never persisted past this request.
      setPassword("");
      onLoggedIn();
    } catch (submitError) {
      setError((submitError as Error).message || "Login failed.");
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-navy-900 px-4">
      <div className="w-full max-w-md">
        <div className="mb-6 text-center">
          <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-xl bg-navy-700 text-base font-bold text-white">
            SL
          </div>
          <p className="text-xs uppercase tracking-[0.3em] text-slate-400">Salryn</p>
        </div>

        <div className="rounded-2xl border border-slate-200 bg-white p-8 shadow-card">
          <h1 className="text-2xl font-semibold text-slate-900">Sign in to Salryn Ledger Console</h1>
          <p className="mt-2 text-sm text-slate-500">
            This local web console connects to the Salryn local API on this device.
          </p>

          <form className="mt-6 space-y-4" onSubmit={handleSubmit}>
            <FormField label="Username" htmlFor="login-username">
              <TextInput
                id="login-username"
                value={username}
                onChange={(event) => setUsername(event.target.value)}
                autoComplete="username"
                autoFocus
              />
            </FormField>

            <FormField label="Password" htmlFor="login-password">
              <PasswordInput
                id="login-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                autoComplete="current-password"
              />
            </FormField>

            {error ? <Alert tone="danger">{error}</Alert> : null}

            <Button type="submit" disabled={isSubmitting} className="w-full">
              {isSubmitting ? "Signing in..." : "Sign in"}
            </Button>
          </form>
        </div>

        <p className="mt-4 text-center text-xs text-slate-400">
          Local API · 127.0.0.1 · Authorized operators only
        </p>
      </div>
    </div>
  );
}

import { useState, type FormEvent } from "react";
import { Layers3, LockKeyhole } from "lucide-react";
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
          <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-md bg-white text-navy-900 shadow-lg">
            <Layers3 aria-hidden="true" size={26} />
          </div>
          <p className="text-sm font-bold text-white">Salryn POS</p>
          <p className="mt-1 text-xs text-slate-400">Store operator sign in</p>
        </div>

        <div className="rounded-lg border border-slate-200 bg-white p-8 shadow-xl">
          <div className="mb-4 flex h-9 w-9 items-center justify-center rounded-md bg-blue-50 text-blue-700">
            <LockKeyhole aria-hidden="true" size={19} />
          </div>
          <h1 className="text-xl font-bold text-slate-950">Sign in to Salryn POS</h1>
          <p className="mt-2 text-sm text-slate-500">Use your store operator account to continue.</p>

          <form className="mt-6 space-y-4" onSubmit={handleSubmit}>
            <FormField label="Username" htmlFor="login-username" required>
              <TextInput
                id="login-username"
                value={username}
                onChange={(event) => setUsername(event.target.value)}
                autoComplete="username"
                autoFocus
              />
            </FormField>

            <FormField label="Password" htmlFor="login-password" required>
              <PasswordInput
                id="login-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                autoComplete="current-password"
              />
            </FormField>

            {error ? <Alert tone="danger">{error}</Alert> : null}

            <Button type="submit" size="lg" disabled={isSubmitting} className="w-full">
              {isSubmitting ? "Signing in..." : "Sign in"}
            </Button>
          </form>
        </div>

        <p className="mt-4 text-center text-xs text-slate-400">Authorized operators only</p>
      </div>
    </div>
  );
}

import { useQueryClient } from "@tanstack/react-query";
import { DashboardPage, useAppSession } from "./pages/DashboardPage";
import { LoginPage } from "./pages/LoginPage";
import { Button } from "./components/ui";

function FullScreenCenter({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen items-center justify-center bg-navy-900 px-4">
      {children}
    </div>
  );
}

export default function App() {
  const queryClient = useQueryClient();
  const sessionQuery = useAppSession();

  if (sessionQuery.isLoading) {
    return (
      <FullScreenCenter>
        <p className="text-sm text-slate-300">Connecting to the Salryn local API...</p>
      </FullScreenCenter>
    );
  }

  if (sessionQuery.isError) {
    return (
      <FullScreenCenter>
        <div className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-8 shadow-card">
          <h1 className="text-xl font-semibold text-slate-900">Salryn local API unavailable</h1>
          <p className="mt-2 text-sm text-slate-500">
            The console could not reach the Salryn local API on this device. Make sure the
            local API is running, then try again.
          </p>
          <div className="mt-6">
            <Button onClick={() => void sessionQuery.refetch()}>Try again</Button>
          </div>
        </div>
      </FullScreenCenter>
    );
  }

  if (!sessionQuery.data?.isAuthenticated) {
    return (
      <LoginPage
        onLoggedIn={() => {
          void queryClient.invalidateQueries({ queryKey: ["session"] });
        }}
      />
    );
  }

  return (
    <DashboardPage
      session={sessionQuery.data}
      onLogout={() => {
        void queryClient.invalidateQueries({ queryKey: ["session"] });
      }}
    />
  );
}

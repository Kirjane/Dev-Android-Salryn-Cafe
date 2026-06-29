import { useQueryClient } from "@tanstack/react-query";
import { DashboardPage, useAppSession } from "./pages/DashboardPage";
import { LoginPage } from "./pages/LoginPage";

export default function App() {
  const queryClient = useQueryClient();
  const sessionQuery = useAppSession();

  if (sessionQuery.isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-100 text-sm text-slate-500">
        Checking session...
      </div>
    );
  }

  if (sessionQuery.isError || !sessionQuery.data?.isAuthenticated) {
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

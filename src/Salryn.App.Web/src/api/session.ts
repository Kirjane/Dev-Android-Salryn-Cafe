export type SessionUser = {
  userId: string;
  username: string;
  displayName: string;
  roleName: string;
  roleId: string;
  status: string;
  permissions: string[];
};

export type SessionState = {
  isAuthenticated: boolean;
  user: SessionUser | null;
  sessionStartedAt: string | null;
};

async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as { error?: string } | null;
    throw new Error(body?.error || `Request failed (${response.status})`);
  }

  return response.json() as Promise<T>;
}

export async function fetchSession(): Promise<SessionState> {
  const response = await fetch("/api/session", {
    credentials: "include",
  });

  return readJson<SessionState>(response);
}

export async function login(username: string, password: string): Promise<SessionState> {
  const response = await fetch("/api/auth/login", {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ username, password }),
  });

  return readJson<SessionState>(response);
}

export async function logout(): Promise<void> {
  const response = await fetch("/api/auth/logout", {
    method: "POST",
    credentials: "include",
  });

  if (!response.ok) {
    throw new Error(`Logout failed (${response.status})`);
  }
}

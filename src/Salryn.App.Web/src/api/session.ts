import { ApiError, readJson } from "./http";

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
    throw new ApiError(`Logout failed (${response.status})`, response.status);
  }
}

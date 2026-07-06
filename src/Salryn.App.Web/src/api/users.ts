import { readJson } from "./http";

export type UserListItem = {
  id: string;
  username: string;
  displayName: string;
  roleName: string;
  status: string;
  createdAt: string;
};

export type UserDetail = {
  id: string;
  username: string;
  displayName: string;
  roleName: string;
  status: string;
  createdAt: string;
  permissions: string[];
};

export type UserRole = {
  roleId: string;
  roleName: string;
  isActive: boolean;
};

export type CreateUserRequest = {
  username: string;
  displayName: string;
  temporaryPassword: string;
  confirmTemporaryPassword: string;
  roleId: string;
  isActive: boolean;
};

export async function fetchUsers(): Promise<UserListItem[]> {
  const response = await fetch("/api/users", { credentials: "include" });
  return readJson<UserListItem[]>(response);
}

export async function fetchUserRoles(): Promise<UserRole[]> {
  const response = await fetch("/api/users/roles", { credentials: "include" });
  return readJson<UserRole[]>(response);
}

export async function fetchUserDetail(userId: string): Promise<UserDetail> {
  const response = await fetch(`/api/users/${userId}`, { credentials: "include" });
  return readJson<UserDetail>(response);
}

export async function createUser(request: CreateUserRequest): Promise<UserListItem> {
  const response = await fetch("/api/users", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  return readJson<UserListItem>(response);
}

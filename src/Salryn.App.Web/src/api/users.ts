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

export async function fetchUsers(): Promise<UserListItem[]> {
  const response = await fetch("/api/users", { credentials: "include" });
  return readJson<UserListItem[]>(response);
}

export async function fetchUserDetail(userId: string): Promise<UserDetail> {
  const response = await fetch(`/api/users/${userId}`, { credentials: "include" });
  return readJson<UserDetail>(response);
}

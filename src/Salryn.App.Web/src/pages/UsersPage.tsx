import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchUserDetail, fetchUsers, type UserListItem } from "../api/users";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  DataTable,
  ErrorState,
  LoadingState,
  StatusPill,
} from "../components/ui";
import { formatDateTime } from "../utils/format";

export function UsersPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const usersQuery = useQuery({
    queryKey: ["users"],
    queryFn: fetchUsers,
  });

  const detailQuery = useQuery({
    queryKey: ["user-detail", selectedId],
    queryFn: () => fetchUserDetail(selectedId!),
    enabled: selectedId !== null,
  });

  const columns = [
    { key: "username", header: "Username", render: (row: UserListItem) => row.username },
    { key: "display", header: "Display name", render: (row: UserListItem) => row.displayName },
    { key: "role", header: "Role", render: (row: UserListItem) => row.roleName },
    {
      key: "status",
      header: "Status",
      render: (row: UserListItem) => (
        <StatusPill tone={row.status === "Active" ? "success" : "neutral"}>{row.status}</StatusPill>
      ),
    },
    {
      key: "created",
      header: "Created",
      render: (row: UserListItem) => formatDateTime(row.createdAt),
    },
  ];

  return (
    <div className="space-y-6">
      <CommandBar
        title="Users"
        subtitle="Read-only operator accounts from the local database."
        actions={
          <Button variant="secondary" onClick={() => void usersQuery.refetch()}>
            Refresh
          </Button>
        }
      />

      <Card>
        <CardBody className="space-y-4">
          {usersQuery.isLoading ? <LoadingState label="Loading users..." /> : null}
          {usersQuery.isError ? <ErrorState message={(usersQuery.error as Error).message} /> : null}
          {usersQuery.data ? (
            <DataTable
              columns={columns}
              rows={usersQuery.data}
              getRowKey={(row) => row.id}
              onRowClick={(row) => setSelectedId(row.id)}
              selectedRowKey={selectedId}
              emptyTitle="No users found"
              emptyDescription="No operator accounts are available."
            />
          ) : null}
        </CardBody>
      </Card>

      {selectedId && detailQuery.data ? (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle>{detailQuery.data.displayName}</CardTitle>
              <Button variant="secondary" size="sm" onClick={() => setSelectedId(null)}>
                Close
              </Button>
            </div>
          </CardHeader>
          <CardBody className="space-y-2 text-sm text-slate-600">
            <p>Username: {detailQuery.data.username}</p>
            <p>Role: {detailQuery.data.roleName}</p>
            <p>Status: {detailQuery.data.status}</p>
            <p>Created: {formatDateTime(detailQuery.data.createdAt)}</p>
            {detailQuery.data.permissions.length > 0 ? (
              <div className="flex flex-wrap gap-2 pt-2">
                {detailQuery.data.permissions.map((permission) => (
                  <StatusPill key={permission} tone="info">
                    {permission}
                  </StatusPill>
                ))}
              </div>
            ) : null}
          </CardBody>
        </Card>
      ) : null}
    </div>
  );
}

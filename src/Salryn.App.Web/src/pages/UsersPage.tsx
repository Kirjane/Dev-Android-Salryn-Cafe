import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createUser,
  fetchUserDetail,
  fetchUserRoles,
  fetchUsers,
  type CreateUserRequest,
  type UserListItem,
} from "../api/users";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  DataTable,
  ErrorState,
  FormField,
  LoadingState,
  PasswordInput,
  StatusPill,
  TextInput,
} from "../components/ui";
import { formatDateTime } from "../utils/format";

const emptyCreateForm: CreateUserRequest = {
  displayName: "",
  username: "",
  temporaryPassword: "",
  confirmTemporaryPassword: "",
  roleId: "",
  isActive: true,
};

export function UsersPage() {
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [createForm, setCreateForm] = useState<CreateUserRequest>(emptyCreateForm);
  const [formError, setFormError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const usersQuery = useQuery({
    queryKey: ["users"],
    queryFn: fetchUsers,
  });

  const rolesQuery = useQuery({
    queryKey: ["user-roles"],
    queryFn: fetchUserRoles,
  });

  const detailQuery = useQuery({
    queryKey: ["user-detail", selectedId],
    queryFn: () => fetchUserDetail(selectedId!),
    enabled: selectedId !== null,
  });

  const createMutation = useMutation({
    mutationFn: createUser,
    onSuccess: async (created) => {
      setCreateForm(emptyCreateForm);
      setFormError(null);
      setSuccessMessage(`Created ${created.displayName} as ${created.roleName}. Temporary password cleared.`);
      setSelectedId(created.id);
      await queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (error) => {
      setSuccessMessage(null);
      setFormError((error as Error).message);
    },
  });

  const createRoles = (rolesQuery.data ?? []).filter((role) =>
    role.isActive && ["Cashier", "Manager"].includes(role.roleName),
  );

  function updateCreateForm(patch: Partial<CreateUserRequest>) {
    setCreateForm((current) => ({ ...current, ...patch }));
  }

  function validateCreateForm(): string | null {
    if (!createForm.displayName.trim()) {
      return "Display name is required.";
    }

    if (!createForm.username.trim()) {
      return "Username is required.";
    }

    if (!createForm.roleId) {
      return "Role is required.";
    }

    if (!createForm.temporaryPassword.trim()) {
      return "Password is required.";
    }

    if (createForm.temporaryPassword.length < 8) {
      return "Password must be at least 8 characters.";
    }

    if (createForm.temporaryPassword !== createForm.confirmTemporaryPassword) {
      return "Password confirmation does not match.";
    }

    return null;
  }

  function handleCreateUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const validation = validateCreateForm();
    if (validation) {
      setSuccessMessage(null);
      setFormError(validation);
      return;
    }

    setFormError(null);
    setSuccessMessage(null);
    createMutation.mutate({
      ...createForm,
      displayName: createForm.displayName.trim(),
      username: createForm.username.trim(),
    });
  }

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
        subtitle="Create Manager and Cashier accounts and review their assigned access."
        actions={
          <Button variant="secondary" onClick={() => void usersQuery.refetch()}>
            Refresh
          </Button>
        }
      />

      <Alert tone="info" title="Role access guide">
        Admin manages store configuration and users. Manager handles catalog, inventory, sales, voids, and owner reports. Cashier is limited to Checkout and Sales & Receipts. An Auditor role is not available in this build.
      </Alert>

      <div className="grid gap-6 xl:grid-cols-[1.25fr_1fr]">
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>Operator Directory</CardTitle>
            </CardHeader>
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

        <div>
          <Card>
            <CardHeader>
              <CardTitle>Create user</CardTitle>
            </CardHeader>
            <CardBody>
              <form className="space-y-4" onSubmit={handleCreateUser}>
                {successMessage ? <Alert tone="success">{successMessage}</Alert> : null}
                {formError ? <Alert tone="danger">{formError}</Alert> : null}

                <div className="grid gap-4 md:grid-cols-2">
                  <FormField label="Display name" htmlFor="create-user-display-name">
                    <TextInput
                      id="create-user-display-name"
                      value={createForm.displayName}
                      onChange={(event) => updateCreateForm({ displayName: event.target.value })}
                      autoComplete="off"
                    />
                  </FormField>

                  <FormField label="Username" htmlFor="create-user-username">
                    <TextInput
                      id="create-user-username"
                      value={createForm.username}
                      onChange={(event) => updateCreateForm({ username: event.target.value })}
                      autoComplete="off"
                    />
                  </FormField>

                  <FormField label="Temporary password" htmlFor="create-user-temporary-password">
                    <PasswordInput
                      id="create-user-temporary-password"
                      value={createForm.temporaryPassword}
                      onChange={(event) => updateCreateForm({ temporaryPassword: event.target.value })}
                      autoComplete="new-password"
                    />
                  </FormField>

                  <FormField label="Confirm temporary password" htmlFor="create-user-confirm-password">
                    <PasswordInput
                      id="create-user-confirm-password"
                      value={createForm.confirmTemporaryPassword}
                      onChange={(event) => updateCreateForm({ confirmTemporaryPassword: event.target.value })}
                      autoComplete="new-password"
                    />
                  </FormField>

                  <FormField label="Role" htmlFor="create-user-role">
                    <select
                      id="create-user-role"
                      value={createForm.roleId}
                      onChange={(event) => updateCreateForm({ roleId: event.target.value })}
                      className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-900"
                    >
                      <option value="">Select role</option>
                      {createRoles.map((role) => (
                        <option key={role.roleId} value={role.roleId}>
                          {role.roleName}
                        </option>
                      ))}
                    </select>
                  </FormField>

                  <FormField label="Account status" htmlFor="create-user-active">
                    <label className="flex min-h-10 items-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700">
                      <input
                        id="create-user-active"
                        type="checkbox"
                        checked={createForm.isActive}
                        onChange={(event) => updateCreateForm({ isActive: event.target.checked })}
                        className="h-4 w-4 rounded border-slate-300"
                      />
                      Active/enabled
                    </label>
                  </FormField>
                </div>

                <div className="flex items-center justify-between gap-3 pt-2">
                  <p className="text-xs text-slate-500">
                    Cashier and Manager accounts can sign in to Remote POS only after terminal pairing.
                  </p>
                  <Button type="submit" disabled={createMutation.isPending || rolesQuery.isLoading}>
                    {createMutation.isPending ? "Creating..." : "Create user"}
                  </Button>
                </div>
              </form>
            </CardBody>
          </Card>
        </div>
      </div>
    </div>
  );
}

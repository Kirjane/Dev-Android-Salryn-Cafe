import { useState, type FormEvent } from "react";
import { Layers3, ShieldCheck } from "lucide-react";
import { createFirstAdmin, type BootstrapStatus } from "../api/bootstrap";
import { Alert, Button, FormField, PasswordInput, TextInput } from "../components/ui";

type BootstrapSetupPageProps = {
  status: BootstrapStatus;
  onCompleted: () => void;
};

export function BootstrapSetupPage({ status, onCompleted }: BootstrapSetupPageProps) {
  const [storeName, setStoreName] = useState("");
  const [businessName, setBusinessName] = useState("");
  const [address, setAddress] = useState("");
  const [contactNumber, setContactNumber] = useState("");
  const [tin, setTin] = useState("");
  const [receiptWidthMm, setReceiptWidthMm] = useState<58 | 80>(80);
  const [adminUsername, setAdminUsername] = useState("admin");
  const [adminDisplayName, setAdminDisplayName] = useState("");
  const [adminPassword, setAdminPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setSuccess(null);

    if (!storeName.trim()) {
      setError("Store name is required.");
      return;
    }

    if (!adminUsername.trim()) {
      setError("Admin username is required.");
      return;
    }

    if (!adminDisplayName.trim()) {
      setError("Admin display name is required.");
      return;
    }

    if (adminPassword.length < 8) {
      setError("Admin password must be at least 8 characters.");
      return;
    }

    if (adminPassword !== confirmPassword) {
      setError("Password confirmation does not match.");
      return;
    }

    setIsSubmitting(true);

    try {
      await createFirstAdmin({
        storeName,
        businessName: businessName.trim() || null,
        address: address.trim() || null,
        contactNumber: contactNumber.trim() || null,
        tin: tin.trim() || null,
        receiptWidthMm,
        adminUsername,
        adminDisplayName,
        adminPassword,
      });
      setAdminPassword("");
      setConfirmPassword("");
      setSuccess("First-run setup completed. Sign in with the admin account you created.");
      window.history.replaceState(null, "", "/");
      onCompleted();
    } catch (submitError) {
      setError((submitError as Error).message || "First-run setup failed.");
    } finally {
      setIsSubmitting(false);
    }
  }

  if (!status.firstRunRequired) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-navy-900 px-4">
        <div className="w-full max-w-md rounded-lg border border-slate-200 bg-white p-8 shadow-xl">
          <div className="mb-4 flex h-10 w-10 items-center justify-center rounded-md bg-emerald-50 text-emerald-700">
            <ShieldCheck aria-hidden="true" size={20} />
          </div>
          <h1 className="text-xl font-bold text-slate-950">First-run setup is already complete</h1>
          <p className="mt-2 text-sm text-slate-500">Return to login and sign in with an existing operator account.</p>
          <Button className="mt-6 w-full" onClick={onCompleted}>Go to login</Button>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-navy-900 px-4 py-10">
      <div className="mx-auto w-full max-w-3xl">
        <div className="mb-6 text-center">
          <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-md bg-white text-navy-900 shadow-lg">
            <Layers3 aria-hidden="true" size={26} />
          </div>
          <p className="text-sm font-bold text-white">Salryn POS</p>
          <p className="mt-1 text-xs text-slate-400">Store setup</p>
        </div>

        <div className="rounded-lg border border-slate-200 bg-white p-8 shadow-xl">
          <div className="mb-6">
            <h1 className="text-xl font-bold text-slate-950">Create the first store admin</h1>
            <p className="mt-2 text-sm text-slate-500">
              Add your store details and create the first administrator account.
            </p>
          </div>

          <form className="space-y-6" onSubmit={handleSubmit}>
            <section className="grid gap-4 md:grid-cols-2">
              <FormField label="Store name" htmlFor="bootstrap-store-name" required>
                <TextInput id="bootstrap-store-name" value={storeName} onChange={(event) => setStoreName(event.target.value)} autoFocus />
              </FormField>
              <FormField label="Business name" htmlFor="bootstrap-business-name">
                <TextInput id="bootstrap-business-name" value={businessName} onChange={(event) => setBusinessName(event.target.value)} />
              </FormField>
              <FormField label="Address" htmlFor="bootstrap-address">
                <TextInput id="bootstrap-address" value={address} onChange={(event) => setAddress(event.target.value)} />
              </FormField>
              <FormField label="Contact number" htmlFor="bootstrap-contact-number">
                <TextInput id="bootstrap-contact-number" value={contactNumber} onChange={(event) => setContactNumber(event.target.value)} />
              </FormField>
              <FormField label="TIN" htmlFor="bootstrap-tin">
                <TextInput id="bootstrap-tin" value={tin} onChange={(event) => setTin(event.target.value)} />
              </FormField>
              <FormField label="Receipt width" htmlFor="bootstrap-receipt-width" required>
                <select
                  id="bootstrap-receipt-width"
                  className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-900 outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-500/20"
                  value={receiptWidthMm}
                  onChange={(event) => setReceiptWidthMm(Number(event.target.value) === 58 ? 58 : 80)}
                >
                  <option value={80}>80 mm</option>
                  <option value={58}>58 mm</option>
                </select>
              </FormField>
            </section>

            <section className="grid gap-4 md:grid-cols-2">
              <FormField label="Admin username" htmlFor="bootstrap-admin-username" required>
                <TextInput id="bootstrap-admin-username" value={adminUsername} onChange={(event) => setAdminUsername(event.target.value)} autoComplete="username" />
              </FormField>
              <FormField label="Admin display name" htmlFor="bootstrap-admin-display-name" required>
                <TextInput id="bootstrap-admin-display-name" value={adminDisplayName} onChange={(event) => setAdminDisplayName(event.target.value)} />
              </FormField>
              <FormField label="Admin password" htmlFor="bootstrap-admin-password" required>
                <PasswordInput id="bootstrap-admin-password" value={adminPassword} onChange={(event) => setAdminPassword(event.target.value)} autoComplete="new-password" />
              </FormField>
              <FormField label="Confirm password" htmlFor="bootstrap-confirm-password" required>
                <PasswordInput id="bootstrap-confirm-password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} autoComplete="new-password" />
              </FormField>
            </section>

            {error ? <Alert tone="danger">{error}</Alert> : null}
            {success ? <Alert tone="success">{success}</Alert> : null}

            <Button type="submit" size="lg" disabled={isSubmitting} className="w-full">
              {isSubmitting ? "Completing setup..." : "Create admin and complete setup"}
            </Button>
          </form>
        </div>
      </div>
    </div>
  );
}

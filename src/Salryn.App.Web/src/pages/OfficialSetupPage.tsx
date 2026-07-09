import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchOfficialIntegrityStatus,
  fetchOfficialNumberingPreview,
  fetchOfficialReadiness,
  fetchOfficialSetup,
  saveOfficialBusiness,
  saveOfficialMachine,
  saveOfficialReceipt,
  saveOfficialSeries,
  validateOfficialNumbering,
} from "../api/officialSetup";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  FormField,
  LoadingState,
  StatusPill,
  TextInput,
} from "../components/ui";

export function OfficialSetupPage() {
  const queryClient = useQueryClient();
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const setupQuery = useQuery({ queryKey: ["official-setup"], queryFn: fetchOfficialSetup });
  const readinessQuery = useQuery({ queryKey: ["official-readiness"], queryFn: fetchOfficialReadiness });
  const numberingQuery = useQuery({
    queryKey: ["official-numbering-preview"],
    queryFn: fetchOfficialNumberingPreview,
  });
  const integrityQuery = useQuery({
    queryKey: ["official-integrity-status"],
    queryFn: fetchOfficialIntegrityStatus,
  });

  const [business, setBusiness] = useState({
    registeredTaxpayerName: "",
    businessName: "",
    registeredAddress: "",
    branchAddress: "",
    taxpayerIdentificationNumber: "",
    branchCode: "",
  });
  const [machine, setMachine] = useState({
    machineSerialNumber: "",
    machineIdentificationNumber: "",
    posTerminalNumber: "",
    accreditationNumber: "",
    permitToUseNumber: "",
    softwareVersion: "",
    softwareBuild: "",
  });
  const [receipt, setReceipt] = useState({
    documentTitle: "Invoice",
    documentKind: "OFFICIAL_INVOICE",
    footerLegalText: "",
    supplementaryDisclaimerText: "",
    reprintMarkerText: "REPRINT",
    trainingMarkerText: "TRAINING - NOT OFFICIAL",
    voidRefundMarkerText: "",
    invoiceOrTransitionMode: "",
    scPwdPlaceholderEnabled: false,
  });
  const [series, setSeries] = useState({
    documentKind: "OFFICIAL_INVOICE",
    prefix: "INV",
    year: new Date().getFullYear(),
    startNumber: 1,
    endNumber: "",
    paddingDigits: 6,
  });

  useEffect(() => {
    const data = setupQuery.data;
    if (!data) {
      return;
    }

    if (data.business) {
      setBusiness({
        registeredTaxpayerName: data.business.registeredTaxpayerName === "UNSET" ? "" : data.business.registeredTaxpayerName,
        businessName: data.business.businessName ?? "",
        registeredAddress: data.business.registeredAddress === "UNSET" ? "" : data.business.registeredAddress,
        branchAddress: data.business.branchAddress ?? "",
        taxpayerIdentificationNumber:
          data.business.taxpayerIdentificationNumber === "UNSET" ? "" : data.business.taxpayerIdentificationNumber,
        branchCode: data.business.branchCode === "00000" ? "" : data.business.branchCode,
      });
    }

    if (data.machine) {
      setMachine({
        machineSerialNumber: data.machine.machineSerialNumber === "UNSET" ? "" : data.machine.machineSerialNumber,
        machineIdentificationNumber: data.machine.machineIdentificationNumber ?? "",
        posTerminalNumber: data.machine.posTerminalNumber ?? "",
        accreditationNumber: data.machine.accreditationNumber ?? "",
        permitToUseNumber: data.machine.permitToUseNumber ?? "",
        softwareVersion: data.machine.softwareVersion ?? "",
        softwareBuild: data.machine.softwareBuild ?? "",
      });
    }

    if (data.receipt) {
      setReceipt({
        documentTitle: data.receipt.documentTitle,
        documentKind: data.receipt.documentKind,
        footerLegalText: data.receipt.footerLegalText ?? "",
        supplementaryDisclaimerText: data.receipt.supplementaryDisclaimerText ?? "",
        reprintMarkerText: data.receipt.reprintMarkerText ?? "REPRINT",
        trainingMarkerText: data.receipt.trainingMarkerText ?? "TRAINING - NOT OFFICIAL",
        voidRefundMarkerText: data.receipt.voidRefundMarkerText ?? "",
        invoiceOrTransitionMode: data.receipt.invoiceOrTransitionMode ?? "",
        scPwdPlaceholderEnabled: data.receipt.scPwdPlaceholderEnabled,
      });
    }

    if (data.series) {
      setSeries({
        documentKind: data.series.documentKind,
        prefix: data.series.prefix,
        year: data.series.year,
        startNumber: data.series.startNumber,
        endNumber: data.series.endNumber?.toString() ?? "",
        paddingDigits: data.series.paddingDigits,
      });
    }
  }, [setupQuery.data]);

  function onSaved(label: string) {
    setError(null);
    setMessage(`${label} saved. Official output is still not implemented.`);
    void queryClient.invalidateQueries({ queryKey: ["official-setup"] });
    void queryClient.invalidateQueries({ queryKey: ["official-readiness"] });
    void queryClient.invalidateQueries({ queryKey: ["official-numbering-preview"] });
  }

  function onFail(err: unknown) {
    setMessage(null);
    setError(err instanceof Error ? err.message : "Save failed.");
  }

  const businessMutation = useMutation({
    mutationFn: () =>
      saveOfficialBusiness({
        registeredTaxpayerName: business.registeredTaxpayerName,
        businessName: business.businessName || null,
        registeredAddress: business.registeredAddress,
        branchAddress: business.branchAddress || null,
        taxpayerIdentificationNumber: business.taxpayerIdentificationNumber,
        branchCode: business.branchCode,
        vatRegistrationMode: null,
        contactNote: null,
      }),
    onSuccess: () => onSaved("Business profile"),
    onError: onFail,
  });

  const machineMutation = useMutation({
    mutationFn: () =>
      saveOfficialMachine({
        machineSerialNumber: machine.machineSerialNumber,
        machineIdentificationNumber: machine.machineIdentificationNumber || null,
        posTerminalNumber: machine.posTerminalNumber || null,
        accreditationNumber: machine.accreditationNumber || null,
        permitToUseNumber: machine.permitToUseNumber || null,
        softwareVersion: machine.softwareVersion || null,
        softwareBuild: machine.softwareBuild || null,
      }),
    onSuccess: () => onSaved("Machine profile"),
    onError: onFail,
  });

  const receiptMutation = useMutation({
    mutationFn: () =>
      saveOfficialReceipt({
        documentTitle: receipt.documentTitle,
        documentKind: receipt.documentKind,
        footerLegalText: receipt.footerLegalText || null,
        supplementaryDisclaimerText: receipt.supplementaryDisclaimerText || null,
        reprintMarkerText: receipt.reprintMarkerText || null,
        trainingMarkerText: receipt.trainingMarkerText || null,
        voidRefundMarkerText: receipt.voidRefundMarkerText || null,
        invoiceOrTransitionMode: receipt.invoiceOrTransitionMode || null,
        scPwdPlaceholderEnabled: receipt.scPwdPlaceholderEnabled,
      }),
    onSuccess: () => onSaved("Receipt profile"),
    onError: onFail,
  });

  const seriesMutation = useMutation({
    mutationFn: () =>
      saveOfficialSeries({
        documentKind: series.documentKind,
        prefix: series.prefix,
        year: Number(series.year),
        startNumber: Number(series.startNumber),
        endNumber: series.endNumber.trim() ? Number(series.endNumber) : null,
        paddingDigits: Number(series.paddingDigits),
      }),
    onSuccess: () => onSaved("Document series"),
    onError: onFail,
  });

  const validateMutation = useMutation({
    mutationFn: validateOfficialNumbering,
    onSuccess: (data) => {
      setError(null);
      setMessage(data.summary);
      void queryClient.invalidateQueries({ queryKey: ["official-numbering-preview"] });
    },
    onError: onFail,
  });

  if (setupQuery.isLoading || readinessQuery.isLoading) {
    return <LoadingState label="Loading official setup…" />;
  }

  const readiness = readinessQuery.data;
  const numbering = numberingQuery.data;
  const integrity = integrityQuery.data;

  return (
    <div className="space-y-6">
      <CommandBar
        title="Official Setup"
        subtitle="Internal configuration for future official output. Not compliance. Not live official posting."
      />

      <Alert tone="warning" title="Not compliance-ready">
        Official output is not implemented. This setup does not make Salryn BIR-compliant, accredited, or certified.
        There is no official receipt generation or PDF action on this page. liveOfficialPostingAllowed remains false.
      </Alert>

      {message ? <Alert tone="success" title="Saved">{message}</Alert> : null}
      {error ? <Alert tone="danger" title="Error">{error}</Alert> : null}

      <Card>
        <CardHeader>
          <CardTitle>Readiness</CardTitle>
          {readiness ? <StatusPill tone="neutral">{readiness.status}</StatusPill> : null}
        </CardHeader>
        <CardBody className="space-y-2 text-sm text-slate-700">
          <p>{readiness?.summary}</p>
          <p>
            Live official posting allowed:{" "}
            <strong>{readiness?.liveOfficialPostingAllowed ? "yes" : "no"}</strong>
          </p>
          <p>Compliance mode: {readiness?.complianceMode ?? "Unknown"}</p>
          <div>
            <p className="font-medium">Blockers</p>
            <ul className="list-disc pl-5">
              {(readiness?.blockers ?? []).slice(0, 8).map((b) => (
                <li key={b.code}>{b.message}</li>
              ))}
            </ul>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Business / taxpayer</CardTitle>
        </CardHeader>
        <CardBody className="grid gap-3 md:grid-cols-2">
          <FormField label="Registered taxpayer name">
            <TextInput
              value={business.registeredTaxpayerName}
              onChange={(e) => setBusiness({ ...business, registeredTaxpayerName: e.target.value })}
            />
          </FormField>
          <FormField label="Business name / style">
            <TextInput
              value={business.businessName}
              onChange={(e) => setBusiness({ ...business, businessName: e.target.value })}
            />
          </FormField>
          <FormField label="Registered address">
            <TextInput
              value={business.registeredAddress}
              onChange={(e) => setBusiness({ ...business, registeredAddress: e.target.value })}
            />
          </FormField>
          <FormField label="Branch address">
            <TextInput
              value={business.branchAddress}
              onChange={(e) => setBusiness({ ...business, branchAddress: e.target.value })}
            />
          </FormField>
          <FormField label="TIN">
            <TextInput
              value={business.taxpayerIdentificationNumber}
              onChange={(e) => setBusiness({ ...business, taxpayerIdentificationNumber: e.target.value })}
            />
          </FormField>
          <FormField label="Branch code">
            <TextInput
              value={business.branchCode}
              onChange={(e) => setBusiness({ ...business, branchCode: e.target.value })}
            />
          </FormField>
          <div className="md:col-span-2">
            <Button onClick={() => businessMutation.mutate()} disabled={businessMutation.isPending}>
              Save business profile
            </Button>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Machine / POS</CardTitle>
        </CardHeader>
        <CardBody className="grid gap-3 md:grid-cols-2">
          <FormField label="Machine serial">
            <TextInput
              value={machine.machineSerialNumber}
              onChange={(e) => setMachine({ ...machine, machineSerialNumber: e.target.value })}
            />
          </FormField>
          <FormField label="MIN">
            <TextInput
              value={machine.machineIdentificationNumber}
              onChange={(e) => setMachine({ ...machine, machineIdentificationNumber: e.target.value })}
            />
          </FormField>
          <FormField label="Terminal number">
            <TextInput
              value={machine.posTerminalNumber}
              onChange={(e) => setMachine({ ...machine, posTerminalNumber: e.target.value })}
            />
          </FormField>
          <FormField label="Accreditation number (optional metadata)">
            <TextInput
              value={machine.accreditationNumber}
              onChange={(e) => setMachine({ ...machine, accreditationNumber: e.target.value })}
            />
          </FormField>
          <FormField label="PTU number (optional metadata)">
            <TextInput
              value={machine.permitToUseNumber}
              onChange={(e) => setMachine({ ...machine, permitToUseNumber: e.target.value })}
            />
          </FormField>
          <FormField label="Software version">
            <TextInput
              value={machine.softwareVersion}
              onChange={(e) => setMachine({ ...machine, softwareVersion: e.target.value })}
            />
          </FormField>
          <div className="md:col-span-2">
            <Button onClick={() => machineMutation.mutate()} disabled={machineMutation.isPending}>
              Save machine profile
            </Button>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Receipt profile</CardTitle>
        </CardHeader>
        <CardBody className="grid gap-3 md:grid-cols-2">
          <FormField label="Document title">
            <TextInput
              value={receipt.documentTitle}
              onChange={(e) => setReceipt({ ...receipt, documentTitle: e.target.value })}
            />
          </FormField>
          <FormField label="Document kind">
            <TextInput
              value={receipt.documentKind}
              onChange={(e) => setReceipt({ ...receipt, documentKind: e.target.value })}
            />
          </FormField>
          <FormField label="Footer legal text">
            <TextInput
              value={receipt.footerLegalText}
              onChange={(e) => setReceipt({ ...receipt, footerLegalText: e.target.value })}
            />
          </FormField>
          <FormField label="Training marker text">
            <TextInput
              value={receipt.trainingMarkerText}
              onChange={(e) => setReceipt({ ...receipt, trainingMarkerText: e.target.value })}
            />
          </FormField>
          <FormField label="Reprint marker text">
            <TextInput
              value={receipt.reprintMarkerText}
              onChange={(e) => setReceipt({ ...receipt, reprintMarkerText: e.target.value })}
            />
          </FormField>
          <div className="md:col-span-2">
            <Button onClick={() => receiptMutation.mutate()} disabled={receiptMutation.isPending}>
              Save receipt profile
            </Button>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Document series (setup only — not checkout allocation)</CardTitle>
        </CardHeader>
        <CardBody className="grid gap-3 md:grid-cols-2">
          <FormField label="Prefix">
            <TextInput value={series.prefix} onChange={(e) => setSeries({ ...series, prefix: e.target.value })} />
          </FormField>
          <FormField label="Year">
            <TextInput
              value={String(series.year)}
              onChange={(e) => setSeries({ ...series, year: Number(e.target.value) || series.year })}
            />
          </FormField>
          <FormField label="Start number">
            <TextInput
              value={String(series.startNumber)}
              onChange={(e) => setSeries({ ...series, startNumber: Number(e.target.value) || 1 })}
            />
          </FormField>
          <FormField label="Padding digits (>=6)">
            <TextInput
              value={String(series.paddingDigits)}
              onChange={(e) => setSeries({ ...series, paddingDigits: Number(e.target.value) || 6 })}
            />
          </FormField>
          <FormField label="End number (optional)">
            <TextInput value={series.endNumber} onChange={(e) => setSeries({ ...series, endNumber: e.target.value })} />
          </FormField>
          <div className="md:col-span-2">
            <Button onClick={() => seriesMutation.mutate()} disabled={seriesMutation.isPending}>
              Save document series
            </Button>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Official numbering preview</CardTitle>
          {numbering ? (
            <StatusPill tone={numbering.allocationAllowed ? "success" : "neutral"}>
              {numbering.mode}
            </StatusPill>
          ) : null}
        </CardHeader>
        <CardBody className="space-y-2 text-sm text-slate-700">
          <Alert tone="warning" title="Preview only">
            This preview does not issue an official number. Checkout is not connected to official numbering.
          </Alert>
          {numberingQuery.isLoading ? <p>Loading numbering preview…</p> : null}
          {numbering ? (
            <>
              <p>{numbering.summary}</p>
              <p>
                Current: <strong>{numbering.currentNumber}</strong> · Next:{" "}
                <strong>{numbering.formattedNextNumber || "(blocked)"}</strong>
              </p>
              <p>
                Live official posting: <strong>{numbering.liveOfficialPostingAllowed ? "yes" : "no"}</strong> ·
                Checkout uses allocator: <strong>{numbering.checkoutUsesAllocator ? "yes" : "no"}</strong> ·
                Sequence mutated by preview: <strong>{numbering.previewMutatesSequence ? "yes" : "no"}</strong>
              </p>
              {(numbering.blockers ?? []).length > 0 ? (
                <div>
                  <p className="font-medium">Preview blockers</p>
                  <ul className="list-disc pl-5">
                    {numbering.blockers.map((b) => (
                      <li key={b.code}>{b.message}</li>
                    ))}
                  </ul>
                </div>
              ) : null}
            </>
          ) : null}
          <div>
            <Button onClick={() => validateMutation.mutate()} disabled={validateMutation.isPending}>
              Validate series (no allocate)
            </Button>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Official integrity status</CardTitle>
          <StatusPill tone="neutral">Not live-bound</StatusPill>
        </CardHeader>
        <CardBody className="space-y-3 text-sm text-slate-700">
          <Alert tone="warning" title="No official posting">
            Integrity status is engineering visibility only. Electronic journal and void/refund are not live-bound to
            checkout. Official output is not implemented.
          </Alert>
          {integrityQuery.isLoading ? <p>Loading integrity status…</p> : null}
          {integrity ? (
            <>
              <p>{integrity.summary}</p>
              <p>
                Live official posting: <strong>{integrity.liveOfficialPostingAllowed ? "yes" : "no"}</strong> ·
                Checkout integrated: <strong>{integrity.checkoutIntegrated ? "yes" : "no"}</strong> ·
                Compliance claimed: <strong>{integrity.complianceClaimed ? "yes" : "no"}</strong>
              </p>
              <div>
                <p className="font-medium">Electronic journal</p>
                <p>
                  Skeleton: <strong>{integrity.journal.skeletonPresent ? "present" : "missing"}</strong> · Live-bound:{" "}
                  <strong>{integrity.journal.liveBound ? "yes" : "no"}</strong>
                </p>
                <p>{integrity.journal.summary}</p>
              </div>
              <div>
                <p className="font-medium">Void / refund integrity</p>
                <p>
                  Skeleton: <strong>{integrity.voidRefund.skeletonPresent ? "present" : "missing"}</strong> ·
                  Live integrated: <strong>{integrity.voidRefund.liveIntegrated ? "yes" : "no"}</strong>
                </p>
                <p>{integrity.voidRefund.summary}</p>
              </div>
            </>
          ) : null}
        </CardBody>
      </Card>
    </div>
  );
}

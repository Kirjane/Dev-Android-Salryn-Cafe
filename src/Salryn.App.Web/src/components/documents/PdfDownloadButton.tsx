import { useState } from "react";
import { Button, useNotifications } from "../ui";
import { downloadDocumentPdf } from "../../utils/documentPdf";
import { safeNotificationDescription } from "../../utils/notificationError";

interface Props {
  url: string;
  fileName: string;
  disabled?: boolean;
  label?: string;
  size?: "sm" | "md" | "lg";
}

/** Shared read-only button for downloading a server-rendered A4 PDF business document. */
export function PdfDownloadButton({ url, fileName, disabled = false, label = "Download A4 PDF", size = "md" }: Props) {
  const [busy, setBusy] = useState(false);
  const { notify } = useNotifications();

  const handleDownload = async () => {
    if (busy || disabled) return;
    setBusy(true);
    try {
      await downloadDocumentPdf(url, fileName);
    } catch (error) {
      notify.error("PDF download failed", safeNotificationDescription(error, "Unable to generate PDF document."));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Button
      size={size}
      variant="secondary"
      className="whitespace-nowrap"
      disabled={disabled || busy}
      onClick={() => void handleDownload()}
    >
      {busy ? "Generating PDF..." : label}
    </Button>
  );
}

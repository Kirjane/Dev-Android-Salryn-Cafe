/**
 * Downloads a read-only server-generated A4 PDF document using the active authenticated session.
 */
export async function downloadDocumentPdf(url: string, fallbackFileName: string): Promise<void> {
  const response = await fetch(url, {
    method: "GET",
    credentials: "include",
  });

  if (!response.ok) {
    let message = `Unable to download PDF document (HTTP ${response.status}).`;
    try {
      const payload = (await response.json()) as { error?: string; message?: string };
      if (payload?.error || payload?.message) {
        message = payload.error || payload.message || message;
      }
    } catch {
      // Ignore non-JSON error payloads and fall back to status text.
    }
    throw new Error(message);
  }

  const blob = await response.blob();
  const disposition = response.headers.get("content-disposition");
  const fileNameMatch = disposition?.match(/filename="?([^";]+)"?/i);
  const fileName = fileNameMatch?.[1] || fallbackFileName;

  const objectUrl = URL.createObjectURL(blob);
  try {
    const link = document.createElement("a");
    link.href = objectUrl;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}

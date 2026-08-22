const unsafeErrorText = /(?:stack trace|system\.|microsoft\.|sqlite|sqlstate|select\s|insert\s|update\s|delete\s|exception at|\.cs:\d+)/i;

export function safeNotificationDescription(error: unknown, fallback: string): string {
  const candidate = error instanceof Error ? error.message.trim() : "";
  if (!candidate || candidate.length > 240 || unsafeErrorText.test(candidate)) return fallback;
  return candidate;
}

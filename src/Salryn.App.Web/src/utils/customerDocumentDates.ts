/** Format a quotation expiry as a local calendar date for the API's DateOnly contract. */
export function localDateAfterDays(days: number, now: Date = new Date()): string {
  if (!Number.isInteger(days) || days < 0 || !Number.isFinite(now.getTime())) {
    throw new RangeError("Invalid quotation validity date.");
  }
  // Advance calendar days, not UTC milliseconds: neither DST nor the UTC offset
  // should shift the quotation's business date.
  const date = new Date(now.getFullYear(), now.getMonth(), now.getDate() + days);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

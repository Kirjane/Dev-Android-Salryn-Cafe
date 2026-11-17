/** A catalog refresh is advisory once Fast Add has committed its product/stock/audit. */
export async function refreshAfterCommittedFastAdd(
  refresh: () => Promise<unknown>,
): Promise<boolean> {
  try {
    await refresh();
    return true;
  } catch {
    return false;
  }
}

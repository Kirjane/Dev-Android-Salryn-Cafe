export function formatMoney(amount: number): string {
  return `PHP ${amount.toLocaleString("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}

export function formatDateTime(value: string): string {
  return new Date(value).toLocaleString();
}

export function formatQuantity(value: number): string {
  return value.toLocaleString("en-PH", { maximumFractionDigits: 3 });
}

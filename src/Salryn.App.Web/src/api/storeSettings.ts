import { readJson } from "./http";

export type StoreSettings = { id:string; storeName:string; businessName:string|null; address:string|null; contactNumber:string|null; tin:string|null; currencyLabel:string; receiptWidthMm:number; receiptHeader:string|null; receiptFooter:string|null; printerMode:"Disabled"|"WindowsPrinter"; windowsPrinterName:string|null };
export type StoreSettingsUpdate = Omit<StoreSettings,"id">;
export const fetchStoreSettings = () => fetch("/api/settings/store",{credentials:"include"}).then(readJson<StoreSettings>);
export async function saveStoreSettings(value:StoreSettingsUpdate){ const response=await fetch("/api/settings/store",{method:"PUT",headers:{"Content-Type":"application/json"},credentials:"include",body:JSON.stringify(value)}); if(!response.ok) await readJson(response); }

import { describe, expect, it, vi, afterEach } from "vitest";
import { reportRange, requireScopedReport, readScopedCafeReport, hasAuthoritativeProfit } from "./scopedReports";
import { hostRequest } from "../../api/hostClient";
vi.mock("../../api/hostClient", () => ({hostRequest: vi.fn()}));
const now = new Date("2026-11-24T16:10:00Z"); // 25 Nov, Manila
const range = reportRange("daily",now);
function fixture() {
  return {rangeStartUtc:range.fromUtc,rangeEndUtc:range.toUtc,generatedAtUtc:now.toISOString(),
    sales:{recognizedNetRevenueCents:900,completedSaleCount:1},
    profitability:{recognizedNetRevenueCents:900,historicalCogsCents:200,grossProfitCents:700,
      operationalExpenseCents:100,recognizedWasteCostCents:50,operationalContributionCents:550,
      authority:"Authoritative",authorityReasons:[]},
    costCoverage:{state:"Complete",costIncompleteSaleCount:0},syncCompleteness:{state:"Complete"},recipeCosts:[],
    cash:null,accountsReceivable:null};
}
afterEach(()=>vi.clearAllMocks());
describe("R3 live report contract",()=>{
  it("uses Manila midnight, Monday week and calendar month",()=>{
    expect(range).toEqual({fromUtc:"2026-11-24T16:00:00.000Z",toUtc:"2026-11-25T16:00:00.000Z"});
    expect(reportRange("weekly",now)).toEqual({fromUtc:"2026-11-22T16:00:00.000Z",toUtc:"2026-11-29T16:00:00.000Z"});
    expect(reportRange("monthly",now)).toEqual({fromUtc:"2026-10-31T16:00:00.000Z",toUtc:"2026-11-30T16:00:00.000Z"});
  });
  it("handles December rollover",()=>{
    expect(reportRange("monthly",new Date("2026-12-31T15:59:59Z")).toUtc).toBe("2026-12-31T16:00:00.000Z");
  });
  it("sends exact range and preserves Host amounts and redactions",async()=>{
    vi.mocked(hostRequest).mockResolvedValue({cafeManagement:fixture()});
    const result=await readScopedCafeReport("daily",now);
    expect(hostRequest).toHaveBeenCalledWith("/api/owner-reports/summary?"+new URLSearchParams(range).toString(),undefined,"GET");
    expect(result.profitability.grossProfitCents).toBe(700);
    expect(result.cash).toBeNull();
    expect(result.accountsReceivable).toBeNull();
    expect(hasAuthoritativeProfit(result)).toBe(true);
  });
  it("rejects missing or wrong range",()=>{
    expect(()=>requireScopedReport(null,range)).toThrow();
    expect(()=>requireScopedReport(fixture(),reportRange("weekly",now))).toThrow();
  });
  it("rejects invalid or inconsistent financial amounts",()=>{
    const v=fixture();v.profitability.recognizedNetRevenueCents=1;
    expect(()=>requireScopedReport(v,range)).toThrow();
    const bad=fixture();bad.profitability.historicalCogsCents=1.5;
    expect(()=>requireScopedReport(bad,range)).toThrow();
  });
  it("preserves provisional authority and never upgrades incomplete cost or sync",()=>{
    const v=fixture();v.profitability.authority="ProvisionalCostIncomplete";v.costCoverage.state="Incomplete";
    expect(hasAuthoritativeProfit(requireScopedReport(v,range))).toBe(false);
    const w=fixture();w.syncCompleteness.state="Unknown";
    expect(hasAuthoritativeProfit(requireScopedReport(w,range))).toBe(false);
  });
});

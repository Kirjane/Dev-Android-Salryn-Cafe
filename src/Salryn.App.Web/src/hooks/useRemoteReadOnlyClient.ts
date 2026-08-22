import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  fetchRemoteReadOnlyAuditVisibility,
  fetchRemoteReadOnlyBindPolicy,
  fetchRemoteReadOnlyDashboardSummary,
  fetchRemoteReadOnlyDiagnostics,
  fetchRemoteReadOnlyPreflight,
  fetchRemoteReadOnlyProducts,
  fetchRemoteReadOnlyStatus,
  fetchRemoteReadOnlyTerminalStatus,
  getRemoteReadOnlyShellState,
} from "../api/remoteReadOnlyClient";

export function useRemoteReadOnlyClient() {
  const [productSearchText, setProductSearchText] = useState("");
  const [productPage, setProductPage] = useState(1);
  const [productPageSize, setProductPageSize] = useState(10);
  const fallbackShellState = useMemo(() => getRemoteReadOnlyShellState(), []);

  const statusQuery = useQuery({
    queryKey: ["remote-readonly-status"],
    queryFn: fetchRemoteReadOnlyStatus,
    retry: false,
  });

  const bindPolicyQuery = useQuery({
    queryKey: ["remote-readonly-bind-policy"],
    queryFn: fetchRemoteReadOnlyBindPolicy,
    retry: false,
  });

  const diagnosticsQuery = useQuery({
    queryKey: ["remote-readonly-diagnostics"],
    queryFn: fetchRemoteReadOnlyDiagnostics,
    retry: false,
  });

  const preflightQuery = useQuery({
    queryKey: ["remote-readonly-preflight"],
    queryFn: fetchRemoteReadOnlyPreflight,
    retry: false,
  });

  const terminalStatusQuery = useQuery({
    queryKey: ["remote-readonly-terminal-status"],
    queryFn: fetchRemoteReadOnlyTerminalStatus,
    retry: false,
  });

  const dashboardQuery = useQuery({
    queryKey: ["remote-readonly-dashboard-summary"],
    queryFn: fetchRemoteReadOnlyDashboardSummary,
    retry: false,
  });

  const productQuery = useQuery({
    queryKey: ["remote-readonly-product-search", productSearchText, productPage, productPageSize],
    queryFn: () => fetchRemoteReadOnlyProducts(productSearchText, productPage, productPageSize),
    retry: false,
  });

  const auditQuery = useQuery({
    queryKey: ["remote-readonly-audit", 1, 10],
    queryFn: () => fetchRemoteReadOnlyAuditVisibility(1, 10),
    retry: false,
  });

  const shellState = statusQuery.data ?? fallbackShellState;

  return {
    shellState,
    statusQuery,
    bindPolicyQuery,
    diagnosticsQuery,
    preflightQuery,
    terminalStatusQuery,
    productSearchText,
    setProductSearchText,
    productPage,
    setProductPage,
    productPageSize,
    setProductPageSize,
    dashboardQuery,
    productQuery,
    auditQuery,
  };
}

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  fetchRemoteReadOnlyDashboardSummary,
  fetchRemoteReadOnlyProducts,
  getRemoteReadOnlyShellState,
} from "../api/remoteReadOnlyClient";

export function useRemoteReadOnlyClient() {
  const [productSearchText, setProductSearchText] = useState("");
  const shellState = useMemo(() => getRemoteReadOnlyShellState(), []);

  const dashboardQuery = useQuery({
    queryKey: ["remote-readonly-dashboard-summary"],
    queryFn: fetchRemoteReadOnlyDashboardSummary,
    retry: false,
  });

  const productQuery = useQuery({
    queryKey: ["remote-readonly-product-search", productSearchText],
    queryFn: () => fetchRemoteReadOnlyProducts(productSearchText),
    enabled: productSearchText.trim().length > 0,
    retry: false,
  });

  return {
    shellState,
    productSearchText,
    setProductSearchText,
    dashboardQuery,
    productQuery,
  };
}

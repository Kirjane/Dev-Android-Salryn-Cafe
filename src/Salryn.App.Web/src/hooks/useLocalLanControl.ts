import { useMutation, useQuery } from "@tanstack/react-query";
import {
  disableLocalLanControlTerminal,
  fetchLocalLanControlStatus,
  revokeLocalLanControlTerminal,
  setLocalLanControlRemoteTransport,
} from "../api/remoteReadOnlyClient";

/**
 * LAN-M15-C: operator-only local LAN control hook. This hook is intentionally
 * separate from the read-only remote client hook. It is used only from the POS host
 * localhost operator surface; every request is fail-closed server-side to the local
 * operator. Remote read-only clients receive 403 for these endpoints.
 */
export function useLocalLanControl() {
  const statusQuery = useQuery({
    queryKey: ["local-lan-control-status"],
    queryFn: fetchLocalLanControlStatus,
    retry: false,
  });

  const remoteTransportMutation = useMutation({
    mutationFn: ({ enable, bindAddress }: { enable: boolean; bindAddress: string | null }) =>
      setLocalLanControlRemoteTransport(enable, bindAddress),
    onSuccess: () => {
      void statusQuery.refetch();
    },
  });

  const disableTerminalMutation = useMutation({
    mutationFn: (terminalId: string) => disableLocalLanControlTerminal(terminalId),
    onSuccess: () => {
      void statusQuery.refetch();
    },
  });

  const revokeTerminalMutation = useMutation({
    mutationFn: (terminalId: string) => revokeLocalLanControlTerminal(terminalId),
    onSuccess: () => {
      void statusQuery.refetch();
    },
  });

  return {
    statusQuery,
    remoteTransportMutation,
    disableTerminalMutation,
    revokeTerminalMutation,
  };
}

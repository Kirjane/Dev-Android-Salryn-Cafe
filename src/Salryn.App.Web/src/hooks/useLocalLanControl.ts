import { useMutation, useQuery } from "@tanstack/react-query";
import {
  acceptRemotePosJoinRequest,
  disableLocalLanControlTerminal,
  fetchLocalLanControlRuntimeStatus,
  fetchLocalLanControlStatus,
  fetchRemotePosPairingHostStatus,
  rejectRemotePosJoinRequest,
  revokeLocalLanControlTerminal,
  rotateRemotePosJoinCode,
  setLocalLanControlRemotePos,
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

  const runtimeStatusQuery = useQuery({
    queryKey: ["local-lan-control-runtime-status"],
    queryFn: fetchLocalLanControlRuntimeStatus,
    retry: false,
    refetchInterval: 5000,
  });

  const pairingStatusQuery = useQuery({
    queryKey: ["local-lan-control-remote-pos-pairing"],
    queryFn: fetchRemotePosPairingHostStatus,
    retry: false,
    refetchInterval: 5000,
  });

  const remoteTransportMutation = useMutation({
    mutationFn: ({ enable, bindAddress }: { enable: boolean; bindAddress: string | null }) =>
      setLocalLanControlRemoteTransport(enable, bindAddress),
    onSuccess: () => {
      void statusQuery.refetch();
      void runtimeStatusQuery.refetch();
      void pairingStatusQuery.refetch();
    },
  });

  const remotePosRuntimeMutation = useMutation({
    mutationFn: ({ enable }: { enable: boolean }) => setLocalLanControlRemotePos(enable),
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

  const rotateJoinCodeMutation = useMutation({
    mutationFn: rotateRemotePosJoinCode,
    onSuccess: () => {
      void pairingStatusQuery.refetch();
    },
  });

  const acceptJoinRequestMutation = useMutation({
    mutationFn: (requestId: string) => acceptRemotePosJoinRequest(requestId),
    onSuccess: () => {
      void statusQuery.refetch();
      void pairingStatusQuery.refetch();
    },
  });

  const rejectJoinRequestMutation = useMutation({
    mutationFn: (requestId: string) => rejectRemotePosJoinRequest(requestId),
    onSuccess: () => {
      void pairingStatusQuery.refetch();
    },
  });

  return {
    statusQuery,
    runtimeStatusQuery,
    pairingStatusQuery,
    remoteTransportMutation,
    remotePosRuntimeMutation,
    disableTerminalMutation,
    revokeTerminalMutation,
    rotateJoinCodeMutation,
    acceptJoinRequestMutation,
    rejectJoinRequestMutation,
  };
}

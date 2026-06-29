import { useQuery } from "@tanstack/react-query";
import { fetchSession } from "../api/session";

export function useAppSession() {
  return useQuery({
    queryKey: ["session"],
    queryFn: fetchSession,
    retry: false,
  });
}

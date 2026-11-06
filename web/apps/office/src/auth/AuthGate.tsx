import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router";
import { Skeleton } from "@fleetops/ui";
import { authApi, type NextStep, type Session } from "./api";
import { SignIn } from "./SignIn";
import { SecondFactor } from "./SecondFactor";
import { Enrol } from "./Enrol";
import { PrivacyNotice } from "./PrivacyNotice";
import { Forgot } from "./Forgot";
import { Reset } from "./Reset";

export function useSession() {
  return useQuery<Session | null>({
    queryKey: ["session"],
    queryFn: async () => {
      try {
        return await authApi.session();
      } catch {
        return null;
      }
    },
    staleTime: 30_000,
    retry: false,
  });
}

export function AuthGate({ children }: { children: ReactNode }) {
  const location = useLocation();
  const navigate = useNavigate();
  const client = useQueryClient();
  const session = useSession();
  const [step, setStep] = useState<NextStep>("done");
  const [expired, setExpired] = useState(false);
  const [search] = useSearchParams();
  const requested = `${location.pathname}${location.search}`;
  useEffect(() => {
    const onExpired = () => {
      client.setQueryData(["session"], null);
      setExpired(true);
      setStep("done");
    };
    window.addEventListener("fleetops:auth-expired", onExpired);
    return () => window.removeEventListener("fleetops:auth-expired", onExpired);
  }, [client]);
  if (session.isLoading)
    return (
      <div className="auth-loading" aria-label="Loading">
        <Skeleton width="280px" />
        <Skeleton width="180px" />
        <Skeleton width="240px" />
      </div>
    );
  if (session.data) return <>{children}</>;
  if (search.get("forgot") === "1") return <Forgot />;
  if (search.get("reset") === "1") return <Reset />;
  const complete = async (next: NextStep) => {
    if (next === "done") {
      await client.invalidateQueries({ queryKey: ["session"] });
      setExpired(false);
      navigate(requested, { replace: true });
    } else setStep(next);
  };
  if (step === "second_factor") return <SecondFactor onNext={complete} />;
  if (step === "enrol_second_factor") return <Enrol onNext={complete} />;
  if (step === "privacy_notice") return <PrivacyNotice onNext={complete} />;
  return <SignIn onNext={complete} expired={expired} />;
}

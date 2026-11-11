"use client";

import {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  type ReactNode,
} from "react";
import { useRouter } from "next/navigation";
import { EmailNotConfirmedError } from "@/lib/errors";
import { useTranslations } from "@/lib/i18n/client";
import { isProtectedPath } from "@/lib/protected-routes";
import type { RegisterRequest, UserProfile } from "@/lib/types";

interface AuthContextValue {
  user: UserProfile | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (
    email: string,
    password: string,
    displayName: string,
    turnstileToken: string | null
  ) => Promise<void>;
  /** Signs out and goes to `to`; without one, a signed-in page goes to /notes and a public one stays. */
  logout: (to?: string) => Promise<void>;
  /** Ends every session of this account, as for a lost computer. */
  signOutEverywhere: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const t = useTranslations();
  const [user, setUser] = useState<UserProfile | null>(null);
  const [loading, setLoading] = useState(true);

  // Check auth status on mount
  useEffect(() => {
    let cancelled = false;
    async function checkAuth() {
      try {
        const res = await fetch("/api/auth/profile");
        if (res.ok) {
          const profile: UserProfile = await res.json();
          if (!cancelled) setUser(profile);
        }
      } catch {
        // Not authenticated — that's fine
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    checkAuth();
    return () => {
      cancelled = true;
    };
  }, []);

  const login = useCallback(
    async (email: string, password: string) => {
      const res = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });

      // A failed route handler may answer with a non-JSON body; fall back to our own message.
      const data = await res.json().catch(() => ({}));

      if (res.status === 403) {
        throw new EmailNotConfirmedError(data.error || t("emailConfirmation.notConfirmed"));
      }
      if (!res.ok) {
        throw new Error(data.error || t("auth.loginFailed"));
      }

      setUser({
        id: data.id,
        email: data.email,
        displayName: data.displayName,
        roles: data.roles,
      });
      setLoading(false);
    },
    [t]
  );

  const register = useCallback(
    async (email: string, password: string, displayName: string, turnstileToken: string | null) => {
      const body: RegisterRequest = { email, password, displayName, turnstileToken };
      const res = await fetch("/api/auth/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });

      // A failed route handler may answer with a non-JSON body; fall back to our own message.
      const data = await res.json().catch(() => ({}));

      if (!res.ok) {
        throw new Error(data.error || t("auth.registrationFailed"));
      }
      // No session yet: the user signs in after confirming the email.
    },
    [t]
  );

  const logout = useCallback(async (to?: string) => {
    try {
      await fetch("/api/auth/logout", { method: "POST" });
    } catch {
      // Clear local state even if backend call fails
    }
    setUser(null);
    // A signed-in page goes; a public one, as the privacy policy, stays, signed out.
    if (to) router.push(to);
    else if (isProtectedPath(window.location.pathname)) router.push("/notes");
    router.refresh();
  }, [router]);

  const signOutEverywhere = useCallback(async () => {
    const res = await fetch("/api/auth/sign-out-everywhere", { method: "POST" });
    if (!res.ok) {
      const data = await res.json().catch(() => ({}));
      throw new Error(data.error || t("errors.generic"));
    }
    setUser(null);
    if (isProtectedPath(window.location.pathname)) router.push("/login");
    router.refresh();
  }, [router, t]);

  return (
    <AuthContext.Provider value={{ user, loading, login, register, logout, signOutEverywhere }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return ctx;
}

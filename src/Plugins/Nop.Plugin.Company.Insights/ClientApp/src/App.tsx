import { useEffect, useRef, useState } from "react";
import { api } from "./api/client";
import { Workspace } from "./components/Workspace";
import { Toaster, DialogHost } from "./ui/Feedback";
import { useWorkspace } from "./store/workspace";
import type { WorkspaceSnapshot } from "./store/workspace";

/** Applies the resolved theme to the document root (light/dark/system). */
function useThemeEffect() {
  const theme = useWorkspace((s) => s.theme);
  useEffect(() => {
    const root = document.documentElement;
    if (theme === "system") root.removeAttribute("data-theme");
    else root.setAttribute("data-theme", theme);
  }, [theme]);
}

/**
 * Confirms the gated API is reachable (the P0 contract), loads the user's server-side workspace
 * when persistence is available, then renders the workspace and keeps it synced.
 */
export function App() {
  const [state, setState] = useState<"checking" | "ok" | "denied" | "error">("checking");
  const setCapabilities = useWorkspace((s) => s.setCapabilities);
  const applySnapshot = useWorkspace((s) => s.applySnapshot);
  const saveTimer = useRef<number | null>(null);

  useThemeEffect();

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const r = await api.ping();
        if (cancelled) return;
        if (!r.ok) {
          setState("denied");
          return;
        }
        if (r.capabilities) setCapabilities(r.capabilities);

        // Hydrate from the server copy when persistence is on and a saved workspace exists.
        if (r.capabilities?.persistence) {
          try {
            const remote = (await api.getWorkspace()) as Partial<WorkspaceSnapshot> | null;
            if (!cancelled && remote && Array.isArray(remote.tabs) && remote.tabs.length) {
              applySnapshot(remote);
            }
          } catch {
            /* fall back to local copy */
          }

          // Debounced push of durable changes to the server.
          const unsub = useWorkspace.subscribe(() => {
            if (saveTimer.current) window.clearTimeout(saveTimer.current);
            saveTimer.current = window.setTimeout(() => {
              const snap = useWorkspace.getState().snapshot();
              void api.saveWorkspace(snap).catch(() => {});
            }, 900);
          });
          if (cancelled) unsub();
        }

        setState("ok");
      } catch {
        if (!cancelled) setState("error");
      }
    })();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (state === "checking") {
    return <div className="ins-boot">Loading Insights…</div>;
  }
  if (state === "denied") {
    return (
      <div className="ins-boot err">
        You don't have access to Company Insights (missing the <code>AccessInsights</code>{" "}
        permission).
      </div>
    );
  }
  if (state === "error") {
    return (
      <div className="ins-boot err">
        Couldn't reach the Insights API. Check that you're signed in to the admin area.
      </div>
    );
  }

  return (
    <>
      <Workspace />
      <Toaster />
      <DialogHost />
    </>
  );
}

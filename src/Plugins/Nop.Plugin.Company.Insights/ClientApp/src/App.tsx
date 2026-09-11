import { useEffect, useState } from "react";
import { api } from "./api/client";
import { Workspace } from "./components/Workspace";

/**
 * Confirms the gated API is reachable (the P0 contract), then renders the workspace.
 * A 401/redirect here means the user lacks the AccessInsights permission.
 */
export function App() {
  const [state, setState] = useState<"checking" | "ok" | "denied" | "error">("checking");

  useEffect(() => {
    api
      .ping()
      .then((r) => setState(r.ok ? "ok" : "denied"))
      .catch(() => setState("error"));
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

  return <Workspace />;
}

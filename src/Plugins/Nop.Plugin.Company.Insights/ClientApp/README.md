# Company Insights — SPA (ClientApp)

React + TypeScript + Vite front-end for the `Nop.Plugin.Company.Insights` backoffice
analytics workspace. Talks to the plugin's admin JSON API under `/Admin/Insights`.

## Build / deploy model

The **built output** is committed at `../wwwroot/app/` (`main.js`, `main.css`) and ships
inside the plugin — the nopCommerce Docker image is .NET-only and does **not** run Node, so
the compiled bundle must be committed. `node_modules/` and Vite caches are gitignored.

**When you change anything under `src/`, rebuild and commit the output:**

```bash
cd ClientApp
npm install          # first time
npm run build        # emits ../wwwroot/app/main.js + main.css
npm run typecheck    # optional: strict TS check (vite build itself does not type-check)
```

Then rebuild the plugin so the assets are packaged:
`dotnet build src/Plugins/Nop.Plugin.Company.Insights/Nop.Plugin.Company.Insights.csproj`.

## Local dev with hot reload (optional)

`npm run dev` serves the SPA on http://localhost:5199. It calls the API at `/Admin/Insights`,
so run it behind a proxy to a live admin session (or use the committed build inside nopCommerce
for a true end-to-end check).

## Layout

- `src/store/workspace.ts` — Zustand store (tabs, widgets, grid layout), persisted to localStorage.
- `src/components/` — Workspace shell, TabBar, Canvas (react-grid-layout), WidgetFrame, widgets/, ChatPanel (P2 placeholder), AgentPicker, AddWidgetMenu.
- `src/charts/vegaSpec.ts` — builds Vega-Lite specs from report rows.
- `src/api/client.ts` + `src/hooks/useReport.ts` — gated report API access.

State is per-viewer localStorage for now; server-side persistence moves to the dedicated
Postgres + pgvector store in a later phase.

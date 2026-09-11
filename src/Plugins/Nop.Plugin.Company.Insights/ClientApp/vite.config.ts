import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The SPA is served by nopCommerce from the plugin output at /Plugins/Company.Insights/app/.
// Build output goes straight into the plugin's wwwroot/app so the .NET csproj ships it.
export default defineConfig({
  plugins: [react()],
  base: "/Plugins/Company.Insights/app/",
  build: {
    outDir: "../wwwroot/app",
    emptyOutDir: true,
    sourcemap: false,
    rollupOptions: {
      output: {
        // Fixed names so Areas/Admin/Views/Insights/Index.cshtml can reference them statically.
        entryFileNames: "main.js",
        chunkFileNames: "main-[name].js",
        assetFileNames: (info) =>
          info.name && info.name.endsWith(".css") ? "main.css" : "assets/[name][extname]",
      },
    },
  },
  server: {
    port: 5199,
  },
});

using System;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Environment-driven config for the P3 memory layer (separate Postgres + pgvector) and the
    /// CPU embedder. Defaults point at the provisioned dev services; only the PG password (from
    /// the Zalando secret) and the tenant id need injecting per deployment. If the password is
    /// absent, memory is disabled and the agent degrades gracefully to its P2 behavior.
    /// </summary>
    public class InsightsMemoryConfig
    {
        public string PgHost { get; }
        public string PgDatabase { get; }
        public string PgUser { get; }
        public string PgPassword { get; }
        public string EmbedderUrl { get; }
        public string EmbedderModel { get; }
        public int EmbeddingDim { get; }
        public string Tenant { get; }

        /// <summary>Bot token for scheduled report delivery (P4). Absent -> scheduling can't send.</summary>
        public string TelegramBotToken { get; }

        public bool Enabled => !string.IsNullOrWhiteSpace(PgPassword);
        public bool TelegramEnabled => !string.IsNullOrWhiteSpace(TelegramBotToken);

        public InsightsMemoryConfig()
        {
            PgHost = Env("INSIGHTS_PG_HOST", "postgres.mysnacks-dev.svc.cluster.local");
            PgDatabase = Env("INSIGHTS_PG_DB", "insights");
            PgUser = Env("INSIGHTS_PG_USER", "insights_user");
            PgPassword = Env("INSIGHTS_PG_PASSWORD", "");
            EmbedderUrl = Env("INSIGHTS_EMBEDDER_URL",
                "http://insights-embedder.mysnacks-dev.svc.cluster.local:11434/v1/embeddings");
            EmbedderModel = Env("INSIGHTS_EMBEDDER_MODEL", "bge-m3");
            EmbeddingDim = int.TryParse(Env("INSIGHTS_EMBEDDING_DIM", "1024"), out var d) ? d : 1024;
            Tenant = Env("INSIGHTS_TENANT", "default");
            TelegramBotToken = Env("INSIGHTS_TELEGRAM_BOT_TOKEN", "");
        }

        public string BuildConnectionString()
        {
            return $"Host={PgHost};Database={PgDatabase};Username={PgUser};Password={PgPassword};" +
                   "SSL Mode=Prefer;Trust Server Certificate=true;Timeout=10;Command Timeout=20;" +
                   "Maximum Pool Size=5;";
        }

        private static string Env(string name, string fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? fallback : v;
        }
    }
}

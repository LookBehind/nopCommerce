import { useWorkspace } from "../store/workspace";
import { useLlmStatus } from "../ui/llmStatus";

const STATUS_LABEL: Record<string, string> = {
  unknown: "Model status unknown",
  warming: "Model is warming up…",
  ready: "Model is ready",
  cold: "Model is asleep — first question will wake it",
};

/**
 * Primary interactive context selector. Shows the profiles the user's roles allow; for an admin
 * using a company-scoped profile it also shows a company picker (fail-closed until chosen).
 */
export function ProfilePicker() {
  const data = useWorkspace((s) => s.profilesData);
  const selectedProfileId = useWorkspace((s) => s.selectedProfileId);
  const selectedCompanyId = useWorkspace((s) => s.selectedCompanyId);
  const setProfile = useWorkspace((s) => s.setProfile);
  const setCompany = useWorkspace((s) => s.setCompany);
  const status = useLlmStatus((s) => s.status);

  if (!data || data.profiles.length === 0) return null;

  const active = data.profiles.find((p) => p.id === selectedProfileId) ?? data.profiles[0];
  const showCompanyPicker = active.companyScoped && active.needsCompany && data.companies.length > 0;

  return (
    <div className="ins-profile-picker">
      <label className="ins-agent-picker" title={`${active.description}\n${STATUS_LABEL[status]}`}>
        <span className={`ins-agent-dot ${status}`} />
        <select
          value={active.id}
          onChange={(e) => setProfile(e.target.value)}
          aria-label="Select profile"
          disabled={data.profiles.length <= 1}
        >
          {data.profiles.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
      </label>

      {showCompanyPicker && (
        <label className="ins-agent-picker" title="Company to view">
          <select
            value={selectedCompanyId ?? ""}
            onChange={(e) => setCompany(e.target.value ? Number(e.target.value) : null)}
            aria-label="Select company"
          >
            <option value="">— pick a company —</option>
            {data.companies.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
      )}
    </div>
  );
}

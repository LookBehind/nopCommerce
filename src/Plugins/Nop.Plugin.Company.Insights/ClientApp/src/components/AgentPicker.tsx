import { AGENTS, agentById } from "../agents";
import { useWorkspace } from "../store/workspace";
import { useLlmStatus } from "../ui/llmStatus";

const STATUS_LABEL: Record<string, string> = {
  unknown: "Model status unknown",
  warming: "Model is warming up…",
  ready: "Model is ready",
  cold: "Model is asleep — first question will wake it",
};

export function AgentPicker() {
  const selectedAgentId = useWorkspace((s) => s.selectedAgentId);
  const setAgent = useWorkspace((s) => s.setAgent);
  const agent = agentById(selectedAgentId);
  const status = useLlmStatus((s) => s.status);

  return (
    <label className="ins-agent-picker" title={`${agent.description}\n${STATUS_LABEL[status]}`}>
      <span className={`ins-agent-dot ${status}`} />
      <select value={selectedAgentId} onChange={(e) => setAgent(e.target.value)} aria-label="Select agent">
        {AGENTS.map((a) => (
          <option key={a.id} value={a.id}>
            {a.name}
          </option>
        ))}
      </select>
    </label>
  );
}

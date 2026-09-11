import { AGENTS, agentById } from "../agents";
import { useWorkspace } from "../store/workspace";

export function AgentPicker() {
  const selectedAgentId = useWorkspace((s) => s.selectedAgentId);
  const setAgent = useWorkspace((s) => s.setAgent);
  const agent = agentById(selectedAgentId);

  return (
    <label className="ins-agent-picker" title={agent.description}>
      <span className="ins-agent-dot" />
      <select value={selectedAgentId} onChange={(e) => setAgent(e.target.value)}>
        {AGENTS.map((a) => (
          <option key={a.id} value={a.id}>
            {a.name}
          </option>
        ))}
      </select>
    </label>
  );
}

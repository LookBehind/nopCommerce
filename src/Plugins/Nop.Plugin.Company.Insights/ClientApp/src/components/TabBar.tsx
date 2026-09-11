import { useWorkspace } from "../store/workspace";

export function TabBar() {
  const tabs = useWorkspace((s) => s.tabs);
  const activeTabId = useWorkspace((s) => s.activeTabId);
  const setActiveTab = useWorkspace((s) => s.setActiveTab);
  const addTab = useWorkspace((s) => s.addTab);
  const removeTab = useWorkspace((s) => s.removeTab);
  const renameTab = useWorkspace((s) => s.renameTab);

  return (
    <div className="ins-tabbar">
      {tabs.map((t) => (
        <div
          key={t.id}
          className={`ins-tab ${t.id === activeTabId ? "active" : ""}`}
          onClick={() => setActiveTab(t.id)}
          onDoubleClick={() => {
            const name = window.prompt("Rename tab", t.name);
            if (name && name.trim()) renameTab(t.id, name.trim());
          }}
          title="Double-click to rename"
        >
          <span className="ins-tab-name">{t.name}</span>
          {tabs.length > 1 && (
            <button
              className="ins-tab-close"
              title="Close tab"
              onClick={(e) => {
                e.stopPropagation();
                removeTab(t.id);
              }}
            >
              ✕
            </button>
          )}
        </div>
      ))}
      <button className="ins-tab-add" title="New tab" onClick={() => addTab()}>
        +
      </button>
    </div>
  );
}

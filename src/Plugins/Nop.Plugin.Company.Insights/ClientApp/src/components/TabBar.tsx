import { useWorkspace } from "../store/workspace";
import { promptDialog } from "../ui/feedback";

export function TabBar() {
  const tabs = useWorkspace((s) => s.tabs);
  const activeTabId = useWorkspace((s) => s.activeTabId);
  const setActiveTab = useWorkspace((s) => s.setActiveTab);
  const addTab = useWorkspace((s) => s.addTab);
  const removeTab = useWorkspace((s) => s.removeTab);
  const renameTab = useWorkspace((s) => s.renameTab);

  function rename(id: string, current: string) {
    void promptDialog({ title: "Rename tab", defaultValue: current, confirmLabel: "Rename" }).then((name) => {
      if (name && name.trim()) renameTab(id, name.trim());
    });
  }

  return (
    <div className="ins-tabbar">
      {tabs.map((t) => (
        <div
          key={t.id}
          className={`ins-tab ${t.id === activeTabId ? "active" : ""}`}
          onClick={() => setActiveTab(t.id)}
          onDoubleClick={() => rename(t.id, t.name)}
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

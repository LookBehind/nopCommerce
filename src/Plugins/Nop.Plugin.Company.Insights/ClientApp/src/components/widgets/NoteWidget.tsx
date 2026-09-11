interface Props {
  text: string;
  onChange: (text: string) => void;
}

export function NoteWidget({ text, onChange }: Props) {
  return (
    <textarea
      className="ins-note"
      value={text}
      placeholder="Write a note…"
      onChange={(e) => onChange(e.target.value)}
      // Don't let the grid start a drag when editing text.
      onMouseDown={(e) => e.stopPropagation()}
    />
  );
}

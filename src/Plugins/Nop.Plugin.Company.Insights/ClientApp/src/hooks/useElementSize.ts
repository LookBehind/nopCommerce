import { useEffect, useRef, useState } from "react";

/** Tracks an element's content-box size via ResizeObserver. */
export function useElementSize<T extends HTMLElement>() {
  const ref = useRef<T | null>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const ro = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const cr = entry.contentRect;
        setSize({ width: Math.round(cr.width), height: Math.round(cr.height) });
      }
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  return { ref, ...size };
}

import { useRef, useEffect } from 'react';
import ReactDOM from 'react-dom/client';

/**
 * Renders an extension-registered page component into a container div.
 * Used by App.tsx for dynamic extension routes.
 */
export default function ExtensionPage({ component: Component }: { component: React.ComponentType }) {
  const ref = useRef<HTMLDivElement>(null);
  const rootRef = useRef<ReactDOM.Root | null>(null);

  useEffect(() => {
    if (!ref.current) {
      return;
    }

    rootRef.current = ReactDOM.createRoot(ref.current);
    rootRef.current.render(<Component />);

    return () => {
      // Defer unmount to avoid React warnings about synchronous unmount during render
      const root = rootRef.current;
      if (root) {
        setTimeout(() => root.unmount(), 0);
      }
    };
  }, [Component]);

  return <div ref={ref} />;
}

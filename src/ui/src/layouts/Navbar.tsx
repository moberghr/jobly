import { Link } from 'react-router-dom';
import { Moon, Sun, LogOut, Menu } from 'lucide-react';
import { useTheme } from '@/hooks/useTheme';
import { useRealtimeStore } from '@/stores/realtime';
import { config } from '@/config';

interface Props {
  // Slot for the desktop top-level nav (hidden on mobile).
  desktopNav: React.ReactNode;
  // Called when the hamburger button is pressed (mobile only).
  onMenuClick: () => void;
}

export default function Navbar({ desktopNav, onMenuClick }: Props) {
  const { theme, toggle } = useTheme();
  const realtimeStatus = useRealtimeStore((s) => s.status);

  return (
    <header className="border-b bg-card">
      <div className="flex h-14 items-center px-4 lg:px-6 gap-2">
        <button
          type="button"
          onClick={onMenuClick}
          className="lg:hidden p-2 -ml-2 rounded-md hover:bg-accent text-muted-foreground"
          aria-label="Open navigation menu"
        >
          <Menu className="h-5 w-5" />
        </button>

        <Link to="/" className="text-lg font-bold lg:mr-8">
          Warp
        </Link>

        <div className="hidden lg:block">{desktopNav}</div>

        <div className="flex-1" />

        <RealtimeStatusIndicator status={realtimeStatus} />

        <button
          type="button"
          onClick={toggle}
          className="p-2 rounded-md hover:bg-accent text-muted-foreground"
          aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
        >
          {theme === 'dark' ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
        </button>

        {config.hasBuiltInLogin && (
          <button
            type="button"
            onClick={async () => {
              await fetch(`${config.apiPath}auth/logout`, { method: 'POST' });
              window.location.reload();
            }}
            className="p-2 rounded-md hover:bg-accent text-muted-foreground ml-1"
            aria-label="Log out"
            title="Logout"
          >
            <LogOut className="h-4 w-4" />
          </button>
        )}
      </div>
    </header>
  );
}

function RealtimeStatusIndicator({
  status,
}: {
  status: ReturnType<typeof useRealtimeStore.getState>['status'];
}) {
  // 'disabled' indicator is hidden in production: when the addon is not registered
  // we don't want to imply something is wrong — polling fallback is the supported
  // path. Visible in dev to surface "did the probe actually succeed" while iterating.
  if (status === 'disabled' && !import.meta.env.DEV) {
    return null;
  }
  if (status === 'idle' || status === 'probing') {
    return null;
  }

  const styles: Record<string, { dot: string; label: string; title: string }> = {
    connected: { dot: 'bg-green-500', label: 'Live', title: 'Realtime push connected' },
    connecting: {
      dot: 'bg-amber-500 animate-pulse',
      label: 'Connecting',
      title: 'Connecting realtime push…',
    },
    reconnecting: {
      dot: 'bg-amber-500 animate-pulse',
      label: 'Reconnecting',
      title: 'Reconnecting realtime push…',
    },
    disabled: {
      dot: 'bg-muted-foreground/40',
      label: 'Polling',
      title: 'Realtime push disabled; using polling fallback',
    },
  };
  const s = styles[status];
  if (!s) return null;

  return (
    <span
      className="hidden sm:flex items-center justify-end gap-1.5 min-w-28 px-2 py-1 mr-1 text-xs text-muted-foreground"
      title={s.title}
      aria-label={s.title}
    >
      <span className={`h-2 w-2 rounded-full ${s.dot}`} aria-hidden="true" />
      <span>{s.label}</span>
    </span>
  );
}

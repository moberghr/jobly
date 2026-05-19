import { useEffect, useState, type ReactNode } from 'react';
import { Bell, Menu } from 'lucide-react';
import { useRealtimeStore } from '@/stores/realtime';
import { LivePill } from '@/components/v2/LivePill';
import { cn } from '@/lib/utils';

function useUtcClock(): string {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000);

    return () => window.clearInterval(id);
  }, []);

  return now.toISOString().substring(11, 19);
}

interface Props {
  title: string;
  subtitle?: string;
  right?: ReactNode;
  onMenuClick?: () => void;
}

export default function WarpTopbar({ title, subtitle, right, onMenuClick }: Props) {
  const status = useRealtimeStore((s) => s.status);
  const utc = useUtcClock();

  const pillState: 'live' | 'idle' | 'disconnected' =
    status === 'connected' ? 'live' : status === 'disabled' ? 'idle' : 'disconnected';

  return (
    <div
      className={cn(
        'h-[60px] px-6 bg-background flex items-center gap-4 relative shrink-0',
        'after:absolute after:left-6 after:right-6 after:bottom-0 after:h-px',
        'after:bg-gradient-to-r after:from-transparent after:via-warp-green/30 after:to-transparent',
      )}
    >
      {onMenuClick && (
        <button
          type="button"
          onClick={onMenuClick}
          className="lg:hidden -ml-2 p-2 rounded-md text-text-dim hover:text-foreground hover:bg-panel-2"
          aria-label="Open navigation"
        >
          <Menu className="w-5 h-5" />
        </button>
      )}

      <div className="min-w-0 flex-1 lg:flex-initial">
        <div className="font-display text-[16px] font-semibold tracking-tight leading-none truncate">
          {title}
        </div>
        {subtitle && (
          <div className="hidden lg:block text-[11px] text-text-mute mt-1 truncate">
            {subtitle}
          </div>
        )}
      </div>

      <div className="ml-auto flex items-center gap-3.5 text-text-dim text-xs">
        {right}
        <LivePill state={pillState} detail={pillState === 'live' ? '1s' : undefined} />
        <span className="mono hidden lg:inline text-[11.5px] text-text-mute">UTC {utc}</span>
        <span className="hidden lg:inline w-px h-[18px] bg-border" />
        <button
          type="button"
          className="hidden lg:inline-flex p-1 rounded text-text-dim hover:text-foreground"
          aria-label="Notifications"
        >
          <Bell className="w-4 h-4" />
        </button>
        <div
          className="relative w-[30px] h-[30px] rounded-full p-[1.5px]"
          style={{ background: 'linear-gradient(135deg, #2dd4bf, var(--warp-purple, #a855f7))' }}
        >
          <div className="w-full h-full rounded-full bg-background flex items-center justify-center text-[11px] font-bold text-foreground">
            MR
          </div>
        </div>
      </div>
    </div>
  );
}

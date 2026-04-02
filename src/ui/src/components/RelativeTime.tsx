import { formatRelativeTime, formatDateTimeExact } from '@/utils/format';

export function RelativeTime({ date }: { date: string }) {
  return (
    <span>
      {formatDateTimeExact(date)} <span className="text-muted-foreground">({formatRelativeTime(date)})</span>
    </span>
  );
}

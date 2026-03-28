import { formatRelativeTime, formatDateTimeExact } from '@/utils/format';

export function RelativeTime({ date }: { date: string }) {
  return (
    <span title={formatDateTimeExact(date)} className="cursor-help">
      {formatRelativeTime(date)}
    </span>
  );
}

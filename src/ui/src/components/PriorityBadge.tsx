import { Badge } from '@/components/ui/badge';
import { Priority } from '@/types';
import { priorityName, priorityColor } from '@/utils/format';

export function PriorityBadge({ priority }: { priority: Priority }) {
  return (
    <Badge variant="outline" className={priorityColor(priority)}>
      {priorityName(priority)}
    </Badge>
  );
}

import { Badge } from '@/components/ui/badge';
import { State } from '@/types';
import { stateName, stateColor } from '@/utils/format';

export function StateBadge({ state }: { state: State }) {
  return (
    <Badge variant="outline" className={stateColor(state)}>
      {stateName(state)}
    </Badge>
  );
}

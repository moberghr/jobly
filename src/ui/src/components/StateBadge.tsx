import { Badge } from '@/components/ui/badge';
import { State, CancellationMode } from '@/types';
import { stateName, stateColor } from '@/utils/format';

export function StateBadge({ state, cancellationMode }: { state: State; cancellationMode?: CancellationMode }) {
  if (state === State.Processing && cancellationMode != null && cancellationMode !== CancellationMode.None) {
    return (
      <Badge variant="outline" className="bg-orange-100 text-orange-800 dark:bg-orange-900 dark:text-orange-200">
        Cancelling...
      </Badge>
    );
  }

  return (
    <Badge variant="outline" className={stateColor(state)}>
      {stateName(state)}
    </Badge>
  );
}

import { formatDistanceToNow, format } from 'date-fns';
import { State, Priority } from '@/types';

export function formatRelativeTime(dateString: string): string {
  return formatDistanceToNow(new Date(dateString), { addSuffix: true });
}

export function formatDateTime(dateString: string): string {
  return format(new Date(dateString), 'yyyy-MM-dd HH:mm:ss');
}

export function shortType(fullType: string): string {
  // "Namespace.ClassName, Assembly, ..." -> "ClassName"
  const parts = fullType.split(',')[0].split('.');
  return parts[parts.length - 1];
}

const stateNames: Record<number, string> = {
  [State.Enqueued]: 'Enqueued',
  [State.Awaiting]: 'Awaiting',
  [State.Processing]: 'Processing',
  [State.Completed]: 'Completed',
  [State.Failed]: 'Failed',
  [State.Deleted]: 'Deleted',
};

export function stateName(state: State): string {
  return stateNames[state] ?? 'Unknown';
}

export function stateColor(state: State): string {
  switch (state) {
    case State.Enqueued: return 'bg-blue-100 text-blue-800';
    case State.Awaiting: return 'bg-yellow-100 text-yellow-800';
    case State.Processing: return 'bg-purple-100 text-purple-800';
    case State.Completed: return 'bg-green-100 text-green-800';
    case State.Failed: return 'bg-red-100 text-red-800';
    case State.Deleted: return 'bg-gray-100 text-gray-800';
    default: return 'bg-gray-100 text-gray-800';
  }
}

const priorityNames: Record<number, string> = {
  [Priority.Urgent]: 'Urgent',
  [Priority.High]: 'High',
  [Priority.Normal]: 'Normal',
  [Priority.Low]: 'Low',
};

export function priorityName(priority: Priority): string {
  return priorityNames[priority] ?? 'Unknown';
}

export function priorityColor(priority: Priority): string {
  switch (priority) {
    case Priority.Urgent: return 'bg-red-100 text-red-800';
    case Priority.High: return 'bg-orange-100 text-orange-800';
    case Priority.Normal: return 'bg-gray-100 text-gray-800';
    case Priority.Low: return 'bg-blue-100 text-blue-800';
    default: return 'bg-gray-100 text-gray-800';
  }
}

export function shortId(id: string): string {
  return id.substring(0, 8);
}

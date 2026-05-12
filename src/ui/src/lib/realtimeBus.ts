import type { RealtimeEvent } from '@/api/realtime';

/**
 * In-process event bus that fans realtime events out to subscribed pages.
 *
 * Decoupled from the connection store (`@/stores/realtime`) so that subscribers
 * don't depend on store internals — they only care that an event happened, not
 * how the connection that delivered it is managed. The store imports `emit` to
 * fire synthetic events on reconnect drain; the SignalR `HubConnection.on`
 * binding is wired by the store in `bindBridge`.
 */

const listeners = new Map<RealtimeEvent, Set<() => void>>();

export function emit(event: RealtimeEvent) {
  const set = listeners.get(event);
  if (!set) {
    return;
  }
  for (const fn of set) {
    try {
      fn();
    } catch (err) {
      // Subscriber errors are isolated — a buggy refetch on one page must not
      // break event delivery to other pages. Log so a buggy refetch surfaces.
      console.warn('[realtime] subscriber threw for', event, err);
    }
  }
}

export function subscribeRealtime(event: RealtimeEvent, handler: () => void): () => void {
  let set = listeners.get(event);
  if (!set) {
    set = new Set();
    listeners.set(event, set);
  }
  set.add(handler);

  return () => {
    set?.delete(handler);
  };
}

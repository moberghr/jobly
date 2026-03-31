import { useLocation } from 'react-router-dom';

export function useRefreshKey(): number | undefined {
  const location = useLocation();
  return (location.state as Record<string, unknown>)?.refreshKey as number | undefined;
}

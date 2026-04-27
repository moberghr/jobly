import { useState } from 'react';

const PAGE_SIZES = [10, 20, 50, 100] as const;
const STORAGE_KEY = 'warp:pageSize';

export function usePersistedPageSize(): [number, (size: number) => void] {
  const [pageSize, setPageSize] = useState<number>(() => {
    const stored = localStorage.getItem(STORAGE_KEY);
    const parsed = stored ? parseInt(stored, 10) : NaN;
    return (PAGE_SIZES as readonly number[]).includes(parsed) ? parsed : 20;
  });

  const updatePageSize = (size: number) => {
    setPageSize(size);
    localStorage.setItem(STORAGE_KEY, String(size));
  };

  return [pageSize, updatePageSize];
}

export { PAGE_SIZES };

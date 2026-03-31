import { Button } from '@/components/ui/button';
import { ChevronLeft, ChevronRight } from 'lucide-react';

interface PaginationProps {
  page: number;
  pageCount: number;
  onPageChange: (page: number) => void;
  pageSize?: number;
  onPageSizeChange?: (size: number) => void;
}

export function Pagination({ page, pageCount, onPageChange, pageSize, onPageSizeChange }: PaginationProps) {
  if (pageCount <= 0 && !onPageSizeChange) return null;

  return (
    <div className="flex items-center gap-2 justify-center mt-4">
      <Button
        variant="outline"
        size="sm"
        onClick={() => onPageChange(page - 1)}
        disabled={page === 0}
      >
        <ChevronLeft className="h-4 w-4" />
      </Button>
      {pageCount > 0 && (
        <span className="text-sm text-muted-foreground">
          Page {page + 1} of {pageCount}
        </span>
      )}
      <Button
        variant="outline"
        size="sm"
        onClick={() => onPageChange(page + 1)}
        disabled={page >= pageCount - 1}
      >
        <ChevronRight className="h-4 w-4" />
      </Button>
      {onPageSizeChange && (
        <select
          value={pageSize}
          onChange={e => onPageSizeChange(Number(e.target.value))}
          className="ml-4 px-2 py-1 text-sm border rounded-md bg-background"
        >
          {[10, 20, 50, 100].map(size => (
            <option key={size} value={size}>{size} per page</option>
          ))}
        </select>
      )}
    </div>
  );
}

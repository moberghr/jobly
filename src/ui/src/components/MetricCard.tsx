import { Card, CardContent } from '@/components/ui/card';

interface MetricCardProps {
  label: string;
  value: number;
  icon?: React.ReactNode;
  color?: string;
}

export function MetricCard({ label, value, icon, color }: MetricCardProps) {
  return (
    <Card>
      <CardContent className="p-4">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm text-muted-foreground">{label}</p>
            <p className={`text-2xl font-bold ${color ?? ''}`}>{value.toLocaleString()}</p>
          </div>
          {icon && <div className="text-muted-foreground">{icon}</div>}
        </div>
      </CardContent>
    </Card>
  );
}

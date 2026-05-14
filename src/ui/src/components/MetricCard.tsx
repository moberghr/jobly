import { Link } from 'react-router-dom';
import { Card, CardContent } from '@/components/ui/card';
import { Sparkline } from '@/components/Sparkline';

interface MetricCardProps {
  label: string;
  value: number;
  icon?: React.ReactNode;
  color?: string;
  href?: string;
  sparkline?: number[];
  sparklineColor?: string;
  subtitle?: string;
  ariaLabel?: string;
}

export function MetricCard({
  label,
  value,
  icon,
  color,
  href,
  sparkline,
  sparklineColor,
  subtitle,
  ariaLabel,
}: MetricCardProps) {
  const content = (
    <Card className={href ? 'cursor-pointer hover:bg-accent/50 transition-colors' : ''}>
      <CardContent className="p-4">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm text-muted-foreground">{label}</p>
            <p className={`text-2xl font-bold ${color ?? ''}`}>{value.toLocaleString()}</p>
          </div>
          {icon && <div className="text-muted-foreground">{icon}</div>}
        </div>
        {subtitle && (
          <p className="text-xs text-muted-foreground mt-1">{subtitle}</p>
        )}
        {sparkline !== undefined && (
          <div className="mt-2">
            <Sparkline data={sparkline} color={sparklineColor} height={32} />
          </div>
        )}
      </CardContent>
    </Card>
  );

  if (href) {
    return (
      <Link to={href} aria-label={ariaLabel ?? `${label}: ${value}`} className="block">
        {content}
      </Link>
    );
  }
  return content;
}

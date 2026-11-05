import { Pipe, PipeTransform } from '@angular/core';

const money = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
  maximumFractionDigits: 0,
});
const decimal = new Intl.NumberFormat('en-US', {
  minimumFractionDigits: 0,
  maximumFractionDigits: 2,
});
const integer = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });

export const fmtMoney = (v: number | null | undefined) => (v == null ? '' : money.format(v));
export const fmtNumber = (v: number | null | undefined) => (v == null ? '' : decimal.format(v));
export const fmtInt = (v: number | null | undefined) => (v == null ? '' : integer.format(v));

export function fmtCompact(v: number): string {
  const abs = Math.abs(v);
  if (abs >= 1_000_000) return `$${(v / 1_000_000).toFixed(2)}M`;
  if (abs >= 10_000) return `$${(v / 1000).toFixed(1)}K`;
  return fmtMoney(v);
}

export function fmtDate(v: string | Date | null | undefined, withTime = false): string {
  if (!v) return '';
  const d = typeof v === 'string' ? new Date(v) : v;
  if (isNaN(d.getTime())) return '';
  const date = d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  return withTime
    ? `${date}, ${d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`
    : date;
}

/** "in 3 days", "2 days ago", "today". */
export function fmtRelative(v: string | Date, now = new Date()): string {
  const day = 86_400_000;
  const start = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const diff = Math.round((start(new Date(v)) - start(now)) / day);
  if (diff === 0) return 'today';
  if (diff === 1) return 'tomorrow';
  if (diff === -1) return 'yesterday';
  return diff > 0 ? `in ${diff} days` : `${-diff} days ago`;
}

/** "ColdCall" -> "Cold call", "FollowUp" -> "Follow up". */
export function humanize(v: string | null | undefined): string {
  if (!v) return '';
  if (v === 'LinkedIn') return v;
  const spaced = v.replace(/([a-z0-9])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}

export function isoDate(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

export function isoLocal(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${isoDate(d)}T${p(d.getHours())}:${p(d.getMinutes())}`;
}

export function initials(name: string | null | undefined): string {
  return (name ?? '?')
    .split(/\s+/)
    .filter(Boolean)
    .map((w) => w[0])
    .slice(0, 2)
    .join('')
    .toUpperCase();
}

/** Badge tone for every enum the API returns. */
export function toneFor(value: string | null | undefined): string {
  switch (value) {
    case 'Won':
    case 'Customer':
    case 'Converted':
    case 'Qualified':
    case 'true':
      return 'tone-ok';
    case 'Prospecting':
    case 'New':
    case 'Subscriber':
    case 'Low':
      return 'tone-brand';
    case 'Qualification':
    case 'Contacted':
    case 'Lead':
    case 'Normal':
      return 'tone-lilac';
    case 'Proposal':
    case 'Opportunity':
    case 'Meeting':
      return 'tone-info';
    case 'Negotiation':
    case 'High':
      return 'tone-warn';
    case 'Lost':
    case 'Unqualified':
    case 'Churned':
    case 'false':
      return 'tone-bad';
    case 'Call':
    case 'Email':
    case 'FollowUp':
      return 'tone-plum';
    default:
      return 'tone-gray';
  }
}

@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(v: number | null | undefined, compact = false): string {
    return compact && v != null ? fmtCompact(v) : fmtMoney(v);
  }
}

@Pipe({ name: 'human' })
export class HumanPipe implements PipeTransform {
  transform(v: string | null | undefined): string {
    return humanize(v);
  }
}

@Pipe({ name: 'dt' })
export class DatePipe2 implements PipeTransform {
  transform(v: string | Date | null | undefined, withTime = false): string {
    return fmtDate(v, withTime);
  }
}

@Pipe({ name: 'ago' })
export class RelativePipe implements PipeTransform {
  transform(v: string | Date | null | undefined): string {
    return v ? fmtRelative(v) : '';
  }
}

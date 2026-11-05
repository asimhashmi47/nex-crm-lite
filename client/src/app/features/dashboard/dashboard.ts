import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { ApexOptions } from 'ng-apexcharts';
import { Api } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import {
  HumanPipe,
  MoneyPipe,
  RelativePipe,
  fmtCompact,
  fmtInt,
  humanize,
  toneFor,
} from '../../core/format';
import { Dashboard } from '../../core/models';
import { CHART_FONT, Chart, LAVENDER, PALETTE, baseChart } from '../../shared/chart';
import { Avatar, PageHeader, Stat, change } from '../../shared/ui';

interface Tile {
  label: string;
  value: string;
  icon: string;
  tone: 'brand' | 'mint' | 'amber' | 'danger' | 'teal' | 'orchid';
  caption: string;
  delta?: number | null;
  route: string;
  query?: Record<string, string>;
}

@Component({
  selector: 'app-dashboard',
  imports: [
    RouterLink,
    LucideAngularModule,
    PageHeader,
    Stat,
    Chart,
    Avatar,
    MoneyPipe,
    HumanPipe,
    RelativePipe,
  ],
  templateUrl: './dashboard.html',
})
export class DashboardPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);

  readonly data = signal<Dashboard | null>(null);
  readonly tone = toneFor;

  readonly greeting = computed(() => {
    const hour = new Date().getHours();
    const part = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
    return `${part}, ${this.auth.user()?.fullName.split(' ')[0] ?? ''}`;
  });

  readonly tiles = computed<Tile[]>(() => {
    const k = this.data()?.kpis;
    if (!k) return [];
    const tiles: Tile[] = [
      {
        label: 'Open pipeline',
        value: fmtCompact(k.openPipeline),
        icon: 'kanban',
        tone: 'brand',
        caption: `${k.openDeals} open deals`,
        route: '/deals',
      },
      {
        label: 'Weighted forecast',
        value: fmtCompact(k.weightedForecast),
        icon: 'target',
        tone: 'orchid',
        caption: 'Amount × probability',
        route: '/deals',
        query: { view: 'list' },
      },
      {
        label: 'Won this month',
        value: fmtCompact(k.wonThisMonth),
        icon: 'trophy',
        tone: 'mint',
        delta: change(k.wonThisMonth, k.wonLastMonth),
        caption: `${k.dealsWonThisMonth} deals · last month ${fmtCompact(k.wonLastMonth)}`,
        route: '/deals',
        query: { view: 'list', stage: 'Won' },
      },
      {
        label: 'Win rate',
        value: `${k.winRate}%`,
        icon: 'percent',
        tone: 'teal',
        caption: 'Closed deals, last 90 days',
        route: '/deals',
        query: { view: 'list' },
      },
      {
        label: 'Average deal',
        value: fmtCompact(k.avgDealSize),
        icon: 'scale',
        tone: 'amber',
        caption: 'Won deals, last 90 days',
        route: '/deals',
        query: { view: 'list', stage: 'Won' },
      },
      {
        label: 'New leads',
        value: fmtInt(k.newLeadsThisMonth),
        icon: 'magnet',
        tone: 'brand',
        delta: change(k.newLeadsThisMonth, k.newLeadsLastMonth),
        caption: `This month · last month ${k.newLeadsLastMonth}`,
        route: '/leads',
      },
      {
        label: 'Overdue tasks',
        value: fmtInt(k.overdueTasks),
        icon: 'alarm-clock',
        tone: 'danger',
        caption: 'Past their due time',
        route: '/tasks',
        query: { due: 'overdue' },
      },
      {
        label: 'Due today',
        value: fmtInt(k.tasksDueToday),
        icon: 'calendar-check',
        tone: 'amber',
        caption: 'Calls, emails and meetings',
        route: '/tasks',
        query: { due: 'today' },
      },
    ];
    return tiles;
  });

  readonly trendChart = computed<ApexOptions | null>(() => {
    const t = this.data()?.trend;
    if (!t) return null;
    return {
      chart: { ...baseChart('line', 300), stacked: false },
      series: [
        { name: 'Won revenue', type: 'area', data: t.map((p) => p.won) },
        { name: 'New leads', type: 'column', data: t.map((p) => p.newLeads) },
      ],
      colors: [PALETTE[0], LAVENDER[2]],
      stroke: { width: [3, 0], curve: 'monotoneCubic' },
      fill: { type: 'solid', opacity: [0.14, 1] },
      plotOptions: { bar: { columnWidth: '42%', borderRadius: 4 } },
      dataLabels: { enabled: false },
      markers: { size: 0, hover: { size: 5 } },
      xaxis: {
        categories: t.map((p) => p.month),
        axisBorder: { show: false },
        axisTicks: { show: false },
        labels: { style: { colors: '#6f6879' } },
      },
      yaxis: [
        { labels: { formatter: (v: number) => fmtCompact(v), style: { colors: '#6f6879' } } },
        {
          opposite: true,
          labels: { formatter: (v: number) => fmtInt(v), style: { colors: '#6f6879' } },
        },
      ],
      grid: { borderColor: '#ece6f5', strokeDashArray: 4 },
      legend: {
        position: 'top',
        horizontalAlign: 'right',
        fontFamily: CHART_FONT,
        markers: { size: 5 },
      },
      tooltip: {
        shared: true,
        intersect: false,
        y: {
          formatter: (v: number | null, o?: { seriesIndex: number }) =>
            o?.seriesIndex === 0 ? fmtCompact(v ?? 0) : `${v ?? 0} leads`,
        },
      },
    };
  });

  readonly pipelineChart = computed<ApexOptions | null>(() => {
    const p = this.data()?.pipeline;
    if (!p) return null;
    return {
      chart: baseChart('bar', 250, (i) =>
        this.router.navigate(['/deals'], { queryParams: { view: 'list', stage: p[i].stage } }),
      ),
      series: [{ name: 'Amount', data: p.map((s) => s.amount) }],
      colors: ['#D0A2F7', '#B57FEA', '#9A5DDA', '#803FC6'],
      plotOptions: {
        bar: {
          horizontal: true,
          distributed: true,
          borderRadius: 6,
          barHeight: '62%',
          isFunnel: true,
        },
      },
      dataLabels: {
        enabled: true,
        // Dark ink on the two pale lavender bars, white on the deep ones.
        style: {
          fontFamily: CHART_FONT,
          fontWeight: 600,
          colors: ['#3a2556', '#3a2556', '#ffffff', '#ffffff'],
        },
        formatter: (v: string | number | number[], o?: { dataPointIndex: number }) => {
          const slice = p[o?.dataPointIndex ?? 0];
          return `${slice.stage}  ${fmtCompact(Number(v))} · ${slice.count}`;
        },
      },
      xaxis: { categories: p.map((s) => s.stage) },
      legend: { show: false },
      grid: { show: false },
      tooltip: { y: { formatter: (v: number | null) => fmtCompact(v ?? 0) } },
    };
  });

  readonly sourceChart = computed<ApexOptions | null>(() => {
    const s = this.data()?.sources;
    if (!s) return null;
    return {
      chart: baseChart('donut', 260, (i) =>
        this.router.navigate(['/leads'], { queryParams: { source: s[i].source } }),
      ),
      series: s.map((x) => x.leads),
      labels: s.map((x) => humanize(x.source)),
      colors: ['#803FC6', '#D0A2F7', '#2F9A86', '#E3A13B', '#C2417F', '#4C86A8', '#DCBFFF'],
      stroke: { width: 2, colors: ['#fff'] },
      dataLabels: { enabled: false },
      legend: { position: 'right', fontFamily: CHART_FONT, fontSize: '12px', markers: { size: 5 } },
      plotOptions: {
        pie: {
          donut: {
            size: '68%',
            labels: {
              show: true,
              total: { show: true, label: 'Leads', fontFamily: CHART_FONT, color: '#6f6879' },
            },
          },
        },
      },
    };
  });

  readonly topConversion = computed(() => {
    const s = this.data()?.sources ?? [];
    const best = [...s]
      .filter((x) => x.leads >= 10)
      .sort((a, b) => b.converted / b.leads - a.converted / a.leads)[0];
    return best
      ? `${humanize(best.source)} converts best at ${Math.round((best.converted / best.leads) * 100)}%`
      : '';
  });

  readonly maxWon = computed(() =>
    Math.max(1, ...(this.data()?.leaderboard ?? []).map((r) => r.won)),
  );

  constructor() {
    void this.api.get<Dashboard>('/api/dashboard').then((d) => this.data.set(d));
  }

  open(tile: Tile) {
    void this.router.navigate([tile.route], { queryParams: tile.query });
  }
}

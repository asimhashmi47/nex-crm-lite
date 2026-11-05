import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { filter } from 'rxjs';
import { AuthService, SILENT } from '../core/auth.service';
import { Alerts } from '../core/models';
import { SeenMap, browserStorage, markSeen, readSeen, unseen, writeSeen } from '../core/alerts';
import { fmtCompact, initials } from '../core/format';
import { Avatar } from '../shared/ui';
import { navFor } from './nav';

interface Alert {
  key: string;
  icon: string;
  tone: string;
  text: string;
  route: string;
  query?: Record<string, string>;
}
interface SearchHit {
  icon: string;
  label: string;
  route: string;
  query: Record<string, string>;
}

const SIDEBAR_KEY = 'nx:sidebar';
const ALERT_POLL_MS = 60_000;

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule, LucideAngularModule, Avatar],
  templateUrl: './shell.html',
  host: { '(document:click)': 'onDocClick($event)', '(document:keydown)': 'onKey($event)' },
})
export class Shell implements OnInit {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);

  readonly nav = navFor(this.auth.user()?.role);
  readonly year = new Date().getFullYear();
  readonly collapsed = signal(this.readCollapsed());
  readonly mobileOpen = signal(false);
  readonly userMenu = signal(false);
  readonly bellOpen = signal(false);
  readonly now = signal(new Date());
  readonly alerts = signal<Alert[]>([]);
  readonly pipeline = signal<number | null>(null);
  readonly searchText = signal('');
  readonly searchOpen = signal(false);

  readonly userInitials = computed(() => initials(this.auth.user()?.fullName));
  readonly pipelineLabel = computed(() =>
    this.pipeline() === null ? '' : fmtCompact(this.pipeline()!),
  );

  readonly searchHits = computed<SearchHit[]>(() => {
    const raw = this.searchText().trim();
    const q = raw.toLowerCase();
    if (!q) return [];

    const pages = this.nav
      .flatMap((g) => g.items)
      .filter((i) => i.label.toLowerCase().includes(q))
      .map((i) => ({ icon: i.icon, label: `Go to ${i.label}`, route: i.route, query: {} }));

    const records: SearchHit[] = [
      {
        icon: 'contact',
        label: `Contacts matching “${raw}”`,
        route: '/contacts',
        query: { search: raw },
      },
      {
        icon: 'building-2',
        label: `Companies matching “${raw}”`,
        route: '/companies',
        query: { search: raw },
      },
      { icon: 'magnet', label: `Leads matching “${raw}”`, route: '/leads', query: { search: raw } },
      {
        icon: 'handshake',
        label: `Deals matching “${raw}”`,
        route: '/deals',
        query: { view: 'list', search: raw },
      },
    ];
    return [...pages.slice(0, 3), ...records];
  });

  ngOnInit() {
    const clock = setInterval(() => this.now.set(new Date()), 1000);
    const poll = setInterval(() => this.loadAlerts(), ALERT_POLL_MS);
    this.destroyRef.onDestroy(() => {
      clearInterval(clock);
      clearInterval(poll);
    });
    this.loadAlerts();

    // The bell refreshes on its own timer; navigating no longer costs a request.
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => this.mobileOpen.set(false));
  }

  toggleCollapsed() {
    this.collapsed.update((v) => !v);
    try {
      localStorage.setItem(SIDEBAR_KEY, this.collapsed() ? '1' : '0');
    } catch {
      /* storage blocked */
    }
  }

  loadAlerts() {
    this.http
      .get<Alerts>('/api/dashboard/alerts', { context: new HttpContext().set(SILENT, true) })
      .subscribe({
        next: (a) => {
          const alerts: Alert[] = [];
          if (a.overdueTasks)
            alerts.push({
              key: 'overdue',
              icon: 'alarm-clock',
              tone: 'tone-bad',
              text: `${a.overdueTasks} tasks are overdue`,
              route: '/tasks',
              query: { due: 'overdue' },
            });
          if (a.tasksDueToday)
            alerts.push({
              key: 'today',
              icon: 'calendar-check',
              tone: 'tone-warn',
              text: `${a.tasksDueToday} tasks due today`,
              route: '/tasks',
              query: { due: 'today' },
            });
          if (a.closingSoon)
            alerts.push({
              key: 'closing',
              icon: 'handshake',
              tone: 'tone-brand',
              text: `${a.closingSoon} big deals close in the next 30 days`,
              route: '/deals',
            });
          if (a.newLeadsThisMonth)
            alerts.push({
              key: 'leads',
              icon: 'magnet',
              tone: 'tone-info',
              text: `${a.newLeadsThisMonth} new leads this month`,
              route: '/leads',
              query: { status: 'New' },
            });
          this.alerts.set(alerts);
          this.pipeline.set(a.openPipeline);
        },
      });
  }

  /** What this user has already seen in the bell, with the numbers they saw. Kept across reloads. */
  private readonly seen = signal<SeenMap>(
    readSeen(browserStorage(), this.auth.user()?.username ?? ''),
  );
  /** The badge counts only alerts not yet seen with their current numbers. */
  readonly unseenAlerts = computed(() => unseen(this.alerts(), this.seen()));
  /** Alerts that were new when the bell was opened, marked with a dot in the list. */
  readonly freshKeys = signal<ReadonlySet<string>>(new Set());

  toggleBell() {
    const open = !this.bellOpen();
    this.bellOpen.set(open);
    if (!open) return;
    // Opening the bell is reading it: everything on screen now counts as seen.
    this.freshKeys.set(new Set(this.unseenAlerts().map((a) => a.key)));
    const seen = markSeen(this.alerts());
    this.seen.set(seen);
    writeSeen(browserStorage(), this.auth.user()?.username ?? '', seen);
  }

  go(route: string, query?: Record<string, string>) {
    this.searchOpen.set(false);
    this.bellOpen.set(false);
    this.searchText.set('');
    void this.router.navigate([route], { queryParams: query });
  }

  submitSearch() {
    const first = this.searchHits()[0];
    if (first) this.go(first.route, first.query);
  }

  onDocClick(e: Event) {
    const target = e.target as HTMLElement;
    if (!target.closest('[data-pop="user"]')) this.userMenu.set(false);
    if (!target.closest('[data-pop="bell"]')) this.bellOpen.set(false);
    if (!target.closest('[data-pop="search"]')) this.searchOpen.set(false);
  }

  onKey(e: KeyboardEvent) {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      document.getElementById('global-search')?.focus();
    }
  }

  logout() {
    void this.auth.signOut();
  }

  private readCollapsed() {
    try {
      return localStorage.getItem(SIDEBAR_KEY) === '1';
    } catch {
      return false;
    }
  }
}

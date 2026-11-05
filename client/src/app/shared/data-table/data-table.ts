import { HttpClient } from '@angular/common/http';
import {
  Component,
  Directive,
  ElementRef,
  TemplateRef,
  booleanAttribute,
  computed,
  contentChildren,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { Subscription } from 'rxjs';
import { Api } from '../../core/api';
import { FilterRule, PagedResult, TableQuery } from '../../core/models';
import { fmtDate, fmtInt, fmtMoney, fmtNumber, humanize, toneFor } from '../../core/format';
import { ToastService } from '../../core/toast.service';

export type ColumnType =
  'text' | 'number' | 'money' | 'percent' | 'date' | 'datetime' | 'enum' | 'bool';

export interface ColumnDef<T = any> {
  key: string;
  label: string;
  type?: ColumnType;
  /** Enum choices for the header filter and advanced filter. */
  options?: string[];
  sortable?: boolean;
  filterable?: boolean;
  /** Hidden by default; the user can switch it on from the column menu. */
  hidden?: boolean;
  /** Show a total for this numeric column across the whole filtered result. */
  sum?: boolean;
  /** Render enum values as tinted badges. */
  badge?: boolean;
  width?: string;
  /** Custom text for a cell. */
  format?: (row: T) => string;
  /** Extra classes for the cell. */
  cellClass?: string;
}

/** <ng-template dtCell="status" let-row> ... </ng-template> overrides the cell for a column. */
@Directive({ selector: 'ng-template[dtCell]' })
export class DtCell {
  readonly key = input.required<string>({ alias: 'dtCell' });
  readonly tpl = inject(TemplateRef);
}

/** <ng-template dtActions let-row> renders the trailing per-row actions. */
@Directive({ selector: 'ng-template[dtActions]' })
export class DtActions {
  readonly tpl = inject(TemplateRef);
}

/** <ng-template dtToolbar> adds buttons to the right of the toolbar (e.g. "New dish"). */
@Directive({ selector: 'ng-template[dtToolbar]' })
export class DtToolbar {
  readonly tpl = inject(TemplateRef);
}

interface OperatorDef {
  value: string;
  label: string;
  needsValue: 0 | 1 | 2;
}

const OPS: Record<string, OperatorDef[]> = {
  text: [
    { value: 'contains', label: 'contains', needsValue: 1 },
    { value: 'notContains', label: 'does not contain', needsValue: 1 },
    { value: 'equals', label: 'is', needsValue: 1 },
    { value: 'notEquals', label: 'is not', needsValue: 1 },
    { value: 'startsWith', label: 'starts with', needsValue: 1 },
    { value: 'endsWith', label: 'ends with', needsValue: 1 },
    { value: 'isEmpty', label: 'is empty', needsValue: 0 },
    { value: 'isNotEmpty', label: 'is not empty', needsValue: 0 },
  ],
  number: [
    { value: 'equals', label: '=', needsValue: 1 },
    { value: 'notEquals', label: '≠', needsValue: 1 },
    { value: 'gt', label: '>', needsValue: 1 },
    { value: 'gte', label: '≥', needsValue: 1 },
    { value: 'lt', label: '<', needsValue: 1 },
    { value: 'lte', label: '≤', needsValue: 1 },
    { value: 'between', label: 'between', needsValue: 2 },
    { value: 'isEmpty', label: 'is empty', needsValue: 0 },
  ],
  date: [
    { value: 'on', label: 'on', needsValue: 1 },
    { value: 'before', label: 'before', needsValue: 1 },
    { value: 'after', label: 'after', needsValue: 1 },
    { value: 'between', label: 'between', needsValue: 2 },
    { value: 'isEmpty', label: 'is empty', needsValue: 0 },
    { value: 'isNotEmpty', label: 'is not empty', needsValue: 0 },
  ],
  enum: [
    { value: 'equals', label: 'is', needsValue: 1 },
    { value: 'notEquals', label: 'is not', needsValue: 1 },
  ],
  bool: [
    { value: 'isTrue', label: 'is yes', needsValue: 0 },
    { value: 'isFalse', label: 'is no', needsValue: 0 },
  ],
};

const kind = (t: ColumnType | undefined) =>
  t === 'number' || t === 'money' || t === 'percent'
    ? 'number'
    : t === 'date' || t === 'datetime'
      ? 'date'
      : t === 'enum'
        ? 'enum'
        : t === 'bool'
          ? 'bool'
          : 'text';

@Component({
  selector: 'app-data-table',
  imports: [FormsModule, NgTemplateOutlet, LucideAngularModule],
  templateUrl: './data-table.html',
  host: {
    class: 'block',
    '(document:click)': 'onDocClick($event)',
    '(document:keydown.escape)': 'closeMenus()',
  },
})
export class DataTable<T extends Record<string, any> = any> {
  private http = inject(HttpClient);
  private api = inject(Api);
  private toast = inject(ToastService);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Unique id; column visibility and page size are remembered per table. */
  readonly tableId = input.required<string>();
  readonly title = input<string>('');
  /** Base URL: POSTs to `${endpoint}/query` and `${endpoint}/export`. */
  readonly endpoint = input.required<string>();
  readonly columns = input.required<ColumnDef<T>[]>();
  /** Extra URL query parameters (report date ranges). */
  readonly params = input<Record<string, string>>({});
  /** Header filters applied on load, e.g. from dashboard drill-down query params. */
  readonly initialFilters = input<Record<string, string>>({});
  /** Global search applied on load (from the topbar search). */
  readonly initialSearch = input<string>('');
  readonly defaultSort = input<string | null>(null);
  readonly defaultSortDesc = input(true);
  readonly pageSizeDefault = input(25);
  readonly exportName = input<string>('');
  /** Hides the Download menu for lists the API does not export. */
  readonly exportable = input(true, { transform: booleanAttribute });
  /** Rows to tint, e.g. overdue tasks. */
  readonly rowClass = input<(row: T) => string>(() => '');
  readonly clickableRows = input(false, { transform: booleanAttribute });
  readonly rowClick = output<T>();

  readonly cellTpls = contentChildren(DtCell);
  readonly actionsTpl = contentChildren(DtActions);
  readonly toolbarTpl = contentChildren(DtToolbar);

  // State
  readonly rows = signal<T[]>([]);
  readonly total = signal(0);
  readonly sums = signal<Record<string, number> | null>(null);
  readonly loading = signal(true);
  readonly firstLoad = signal(true);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly search = signal('');
  readonly sortBy = signal<string | null>(null);
  readonly sortDesc = signal(true);
  readonly colFilters = signal<Record<string, string | undefined>>({});
  readonly rules = signal<FilterRule[]>([]);
  readonly logic = signal<'and' | 'or'>('and');
  readonly hiddenCols = signal<Set<string>>(new Set());
  readonly showFilterRow = signal(true);
  readonly exporting = signal<string | null>(null);

  // Menus
  readonly menu = signal<'columns' | 'export' | 'filters' | null>(null);
  draftRules: FilterRule[] = [];
  draftLogic: 'and' | 'or' = 'and';

  readonly pageSizes = [10, 25, 50, 100, 200];
  readonly visibleColumns = computed(() =>
    this.columns().filter((c) => !this.hiddenCols().has(c.key)),
  );
  readonly filterableColumns = computed(() => this.columns().filter((c) => c.filterable !== false));
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  readonly rangeFrom = computed(() =>
    this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1,
  );
  readonly rangeTo = computed(() => Math.min(this.total(), this.page() * this.pageSize()));
  readonly activeFilterCount = computed(
    () =>
      Object.values(this.colFilters()).filter((v) => v !== '' && v != null).length +
      this.rules().length,
  );
  readonly hasSums = computed(() => this.visibleColumns().some((c) => c.sum) && !!this.sums());
  readonly skeletonRows = Array.from({ length: 8 }, (_, i) => i);
  readonly pages = computed(() => {
    const count = this.pageCount(),
      cur = this.page();
    const set = new Set([1, count, cur - 1, cur, cur + 1].filter((p) => p >= 1 && p <= count));
    const sorted = [...set].sort((a, b) => a - b);
    const out: (number | '…')[] = [];
    sorted.forEach((p, i) => {
      if (i > 0 && p - sorted[i - 1] > 1) out.push('…');
      out.push(p);
    });
    return out;
  });

  private sub?: Subscription;
  private searchTimer?: ReturnType<typeof setTimeout>;
  private filterTimer?: ReturnType<typeof setTimeout>;
  private initialized = false;

  constructor() {
    // Initialise from inputs once, then reload whenever query-shaping state changes.
    effect(() => {
      const id = this.tableId();
      if (this.initialized) return;
      this.initialized = true;
      untracked(() => {
        const saved = this.readPrefs(id);
        this.pageSize.set(saved?.pageSize ?? this.pageSizeDefault());
        const hidden =
          saved?.hidden ??
          this.columns()
            .filter((c) => c.hidden)
            .map((c) => c.key);
        this.hiddenCols.set(new Set(hidden));
        this.sortBy.set(this.defaultSort());
        this.sortDesc.set(this.defaultSortDesc());
        this.colFilters.set({ ...this.initialFilters() } as Record<string, string | undefined>);
        this.search.set(this.initialSearch() ?? '');
      });
    });

    effect(() => {
      const q = this.buildQuery();
      const url = `${this.endpoint()}/query`;
      const params = this.params();
      untracked(() => this.fetch(url, q, params));
    });
  }

  /** Re-runs the current query (after a create, edit or delete). */
  reload() {
    this.fetch(`${this.endpoint()}/query`, this.buildQuery(), this.params());
  }

  private buildQuery(): TableQuery {
    const filters = Object.fromEntries(
      Object.entries(this.colFilters()).filter(([, v]) => v !== '' && v != null),
    ) as Record<string, string>;
    return {
      page: this.page(),
      pageSize: this.pageSize(),
      search: this.search() || null,
      sortBy: this.sortBy(),
      sortDesc: this.sortDesc(),
      columnFilters: filters,
      filters: this.rules(),
      logic: this.logic(),
      sumFields: this.columns()
        .filter((c) => c.sum)
        .map((c) => c.key),
    };
  }

  private fetch(url: string, q: TableQuery, params: Record<string, string>) {
    this.sub?.unsubscribe();
    this.loading.set(true);
    this.sub = this.http.post<PagedResult<T>>(url, q, { params }).subscribe({
      next: (r) => {
        this.rows.set(r.items);
        this.total.set(r.total);
        this.sums.set(r.sums ?? null);
        if (r.page !== this.page()) this.page.set(r.page);
        this.loading.set(false);
        this.firstLoad.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.firstLoad.set(false);
      },
    });
  }

  // Toolbar

  onSearch(value: string) {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.search.set(value.trim());
      this.page.set(1);
    }, 300);
  }

  setColFilter(key: string, value: string, debounce = true) {
    clearTimeout(this.filterTimer);
    const apply = () => {
      this.colFilters.update((f) => ({ ...f, [key]: value }));
      this.page.set(1);
    };
    if (debounce) this.filterTimer = setTimeout(apply, 350);
    else apply();
  }

  setDateRange(key: string, part: 0 | 1, value: string) {
    const [a, b] = (this.colFilters()[key] ?? '').split('..');
    const next = part === 0 ? [value, b ?? ''] : [a ?? '', value];
    const combined = next[0] || next[1] ? `${next[0] || ''}..${next[1] || ''}` : '';
    this.setColFilter(key, next[0] && !next[1] ? `${next[0]}..` : combined, false);
  }

  dateRangePart(key: string, part: 0 | 1): string {
    return (this.colFilters()[key] ?? '').split('..')[part] ?? '';
  }

  clearAll() {
    this.colFilters.set({});
    this.rules.set([]);
    this.search.set('');
    const box = this.host.nativeElement.querySelector(
      'input[data-search]',
    ) as HTMLInputElement | null;
    if (box) box.value = '';
    this.host.nativeElement
      .querySelectorAll<HTMLInputElement>('[data-colfilter]')
      .forEach((i) => (i.value = ''));
    this.page.set(1);
  }

  sort(col: ColumnDef<T>) {
    if (col.sortable === false) return;
    if (this.sortBy() === col.key) {
      if (this.sortDesc()) this.sortDesc.set(false);
      else {
        this.sortBy.set(this.defaultSort());
        this.sortDesc.set(this.defaultSortDesc());
      }
    } else {
      this.sortBy.set(col.key);
      this.sortDesc.set(true);
    }
    this.page.set(1);
  }

  goTo(p: number) {
    if (p >= 1 && p <= this.pageCount() && p !== this.page()) this.page.set(p);
  }

  setPageSize(size: number) {
    this.pageSize.set(+size);
    this.page.set(1);
    this.savePrefs();
  }

  toggleColumn(key: string) {
    this.hiddenCols.update((s) => {
      const n = new Set(s);
      if (n.has(key)) n.delete(key);
      else if (this.visibleColumns().length > 1) n.add(key);
      return n;
    });
    this.savePrefs();
  }

  // Advanced filter builder

  openFilters() {
    this.draftRules = this.rules().map((r) => ({ ...r }));
    if (this.draftRules.length === 0) this.addRule();
    this.draftLogic = this.logic();
    this.menu.set(this.menu() === 'filters' ? null : 'filters');
  }

  addRule() {
    const col = this.filterableColumns()[0];
    this.draftRules = [
      ...this.draftRules,
      { field: col.key, operator: OPS[kind(col.type)][0].value, value: '', value2: '' },
    ];
  }

  removeRule(i: number) {
    this.draftRules = this.draftRules.filter((_, idx) => idx !== i);
  }

  onRuleField(rule: FilterRule) {
    rule.operator = this.opsFor(rule.field)[0].value;
    rule.value = '';
    rule.value2 = '';
  }

  applyRules() {
    const valid = this.draftRules.filter((r) => {
      const op = this.opsFor(r.field).find((o) => o.value === r.operator);
      return op && (op.needsValue === 0 || (r.value ?? '') !== '');
    });
    this.rules.set(valid.map((r) => ({ ...r })));
    this.logic.set(this.draftLogic);
    this.page.set(1);
    this.menu.set(null);
  }

  clearRules() {
    this.draftRules = [];
    this.rules.set([]);
    this.page.set(1);
    this.menu.set(null);
  }

  removeAppliedRule(i: number) {
    this.rules.update((r) => r.filter((_, idx) => idx !== i));
    this.page.set(1);
  }

  opsFor(field: string): OperatorDef[] {
    return OPS[kind(this.col(field)?.type)];
  }
  needsValue(rule: FilterRule): number {
    return this.opsFor(rule.field).find((o) => o.value === rule.operator)?.needsValue ?? 1;
  }
  col(key: string) {
    return this.columns().find((c) => c.key === key);
  }
  kindOf(c: ColumnDef<T> | undefined) {
    return kind(c?.type);
  }
  inputTypeFor(field: string) {
    const k = kind(this.col(field)?.type);
    return k === 'number' ? 'number' : k === 'date' ? 'date' : 'text';
  }

  describeRule(r: FilterRule): string {
    const c = this.col(r.field);
    const op = this.opsFor(r.field).find((o) => o.value === r.operator);
    const val =
      op?.needsValue === 2
        ? `${r.value} and ${r.value2}`
        : op?.needsValue === 1
          ? humanize(r.value ?? '')
          : '';
    return `${c?.label ?? r.field} ${op?.label ?? r.operator} ${val}`.trim();
  }

  describeColFilter(key: string, value: string): string {
    const c = this.col(key);
    return `${c?.label ?? key}: ${value.split(',').map(humanize).join(', ').replace('..', ' → ')}`;
  }

  colFilterEntries() {
    return Object.entries(this.colFilters()).filter(([, v]) => v !== '' && v != null) as [
      string,
      string,
    ][];
  }
  clearColFilter(key: string) {
    this.colFilters.update((f) => {
      const n = { ...f };
      delete n[key];
      return n;
    });
    const el = this.host.nativeElement.querySelector(
      `[data-colfilter="${key}"]`,
    ) as HTMLInputElement | null;
    if (el) el.value = '';
    this.page.set(1);
  }

  // Export

  async export(format: 'xlsx' | 'csv') {
    this.menu.set(null);
    this.exporting.set(format);
    try {
      const q = { ...this.buildQuery(), page: 1, pageSize: 100000 };
      const qs = new URLSearchParams(this.params()).toString();
      await this.api.download(`${this.endpoint()}/export${qs ? '?' + qs : ''}`, {
        format,
        query: q,
        fileName: this.exportName() || this.tableId(),
        title: this.title() || undefined,
        columns: this.visibleColumns().map((c) => ({ field: c.key, header: c.label })),
      });
      this.toast.success(
        `${this.total().toLocaleString()} rows exported to ${format === 'xlsx' ? 'Excel' : 'CSV'}.`,
        'Download ready',
      );
    } catch {
      /* interceptor already toasted */
    } finally {
      this.exporting.set(null);
    }
  }

  // Cells

  tplFor(key: string): TemplateRef<unknown> | null {
    return this.cellTpls().find((t) => t.key() === key)?.tpl ?? null;
  }

  value(row: T, c: ColumnDef<T>): string {
    if (c.format) return c.format(row);
    const v = row[c.key];
    if (v === null || v === undefined || v === '') return '—';
    switch (c.type) {
      case 'money':
        return fmtMoney(v);
      case 'number':
        return Number.isInteger(v) ? fmtInt(v) : fmtNumber(v);
      case 'percent':
        return `${fmtNumber(v)}%`;
      case 'date':
        return fmtDate(v, false);
      case 'datetime':
        return fmtDate(v, true);
      case 'bool':
        return v ? 'Yes' : 'No';
      case 'enum':
        return humanize(String(v));
      default:
        return String(v);
    }
  }

  sumValue(c: ColumnDef<T>): string {
    const v = this.sums()?.[c.key];
    if (v === undefined || v === null) return '';
    return c.type === 'money' ? fmtMoney(v) : fmtNumber(v);
  }

  tone(v: unknown) {
    return toneFor(String(v));
  }
  isNumeric(c: ColumnDef<T>) {
    return kind(c.type) === 'number';
  }
  humanize = humanize;

  // Menus

  toggleMenu(m: 'columns' | 'export', ev: Event) {
    ev.stopPropagation();
    this.menu.set(this.menu() === m ? null : m);
  }
  closeMenus() {
    this.menu.set(null);
  }
  onDocClick(ev: Event) {
    const t = ev.target as HTMLElement;
    if (this.menu() && !t.closest('[data-menu]')) this.menu.set(null);
  }

  private readPrefs(id: string): { pageSize?: number; hidden?: string[] } | null {
    try {
      return JSON.parse(localStorage.getItem(`nx-dt:${id}`) ?? 'null');
    } catch {
      return null;
    }
  }

  private savePrefs() {
    try {
      localStorage.setItem(
        `nx-dt:${this.tableId()}`,
        JSON.stringify({ pageSize: this.pageSize(), hidden: [...this.hiddenCols()] }),
      );
    } catch {
      /* storage unavailable */
    }
  }
}

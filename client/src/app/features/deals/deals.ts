import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { MoneyPipe, RelativePipe, fmtCompact, isoDate, toneFor } from '../../core/format';
import { BoardCard, BoardColumn, DEAL_STAGES, Deal, DealStage } from '../../core/models';
import { DealApi, DealForm } from '../../core/record-apis';
import { ColumnDef, DataTable, DtActions, DtCell } from '../../shared/data-table/data-table';
import { LookupSelect } from '../../shared/lookup-select';
import { RecordPage } from '../../shared/record-page';
import { Avatar, Drawer, FieldError, PageHeader } from '../../shared/ui';

type View = 'board' | 'list';

const DEFAULT_PROBABILITY: Record<DealStage, number> = {
  Prospecting: 10,
  Qualification: 25,
  Proposal: 50,
  Negotiation: 75,
  Won: 100,
  Lost: 0,
};
const STAGE_ACCENT: Record<DealStage, string> = {
  Prospecting: '#DCBFFF',
  Qualification: '#D0A2F7',
  Proposal: '#B57FEA',
  Negotiation: '#803FC6',
  Won: 'oklch(0.6 0.13 165)',
  Lost: 'oklch(0.5 0.17 12)',
};

@Component({
  selector: 'app-deals',
  imports: [
    ReactiveFormsModule,
    FormsModule,
    LucideAngularModule,
    PageHeader,
    DataTable,
    DtCell,
    DtActions,
    Drawer,
    FieldError,
    Avatar,
    LookupSelect,
    MoneyPipe,
    RelativePipe,
  ],
  templateUrl: './deals.html',
})
export class DealsPage extends RecordPage<Deal, DealForm> {
  protected readonly records = inject(DealApi);
  protected readonly noun = 'Deal';
  private readonly router = inject(Router);

  readonly stages = DEAL_STAGES;
  readonly accent = STAGE_ACCENT;
  readonly tone = toneFor;
  readonly view = signal<View>(
    this.route.snapshot.queryParamMap.get('view') === 'list' ? 'list' : 'board',
  );
  readonly board = signal<BoardColumn[]>([]);
  readonly boardOwner = signal<number | null>(null);
  readonly dragging = signal<BoardCard | null>(null);
  readonly dropTarget = signal<DealStage | null>(null);
  readonly flash = signal<number | null>(null);
  readonly initialFilters = this.filtersFromUrl('stage');

  readonly openTotal = computed(() =>
    this.board()
      .filter((c) => c.stage !== 'Won' && c.stage !== 'Lost')
      .reduce((sum, c) => sum + c.total, 0),
  );
  readonly openCount = computed(() =>
    this.board()
      .filter((c) => c.stage !== 'Won' && c.stage !== 'Lost')
      .reduce((sum, c) => sum + c.count, 0),
  );
  readonly searchCompanies = (term: string) => this.lookups.companies(term);
  readonly searchContacts = (term: string) =>
    this.lookups.contacts(term, this.form.controls.companyId.value);

  readonly form = inject(FormBuilder).group({
    title: ['', [Validators.required, Validators.maxLength(150)]],
    amount: [10000, [Validators.required, Validators.min(1)]],
    stage: ['Prospecting' as DealStage, Validators.required],
    probability: [10, [Validators.required, Validators.min(0), Validators.max(100)]],
    expectedCloseDate: ['', Validators.required],
    companyId: [null as number | null, Validators.required],
    contactId: [null as number | null],
    ownerId: [0, [Validators.required, Validators.min(1)]],
  });

  readonly columns: ColumnDef<Deal>[] = [
    { key: 'title', label: 'Deal', width: '280px' },
    { key: 'company', label: 'Company' },
    { key: 'stage', label: 'Stage', type: 'enum', options: [...DEAL_STAGES], badge: true },
    { key: 'amount', label: 'Amount', type: 'money', sum: true },
    { key: 'probability', label: 'Prob.', type: 'percent', hidden: true },
    { key: 'weighted', label: 'Weighted', type: 'money', sum: true },
    { key: 'expectedCloseDate', label: 'Expected close', type: 'date' },
    { key: 'closedAt', label: 'Closed', type: 'date', hidden: true },
    { key: 'contact', label: 'Contact', hidden: true },
    { key: 'owner', label: 'Owner' },
    { key: 'createdAt', label: 'Created', type: 'date' },
  ];

  constructor() {
    super();
    void this.loadBoard();
    // A new stage suggests its usual probability; the rep can still adjust it.
    this.form.controls.stage.valueChanges.subscribe((stage) => {
      if (stage)
        this.form.controls.probability.setValue(DEFAULT_PROBABILITY[stage], { emitEvent: false });
    });
  }

  setView(view: View) {
    this.view.set(view);
    void this.router.navigate([], {
      queryParams: { view: view === 'list' ? 'list' : null, stage: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    if (view === 'board') void this.loadBoard();
  }

  async loadBoard() {
    this.board.set(await this.records.board(this.boardOwner()));
  }

  filterOwner(ownerId: number | null) {
    this.boardOwner.set(ownerId);
    void this.loadBoard();
  }

  compact(v: number) {
    return fmtCompact(v);
  }

  // Drag and drop between columns

  onDragStart(card: BoardCard, e: DragEvent) {
    this.dragging.set(card);
    e.dataTransfer?.setData('text/plain', String(card.id));
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
  }

  onDragOver(stage: DealStage, e: DragEvent) {
    if (!this.dragging()) return;
    e.preventDefault();
    this.dropTarget.set(stage);
  }

  onDragEnd() {
    this.dragging.set(null);
    this.dropTarget.set(null);
  }

  async onDrop(stage: DealStage, e: DragEvent) {
    e.preventDefault();
    const card = this.dragging();
    this.onDragEnd();
    if (!card) return;

    const from = this.board().find((c) => c.cards.some((x) => x.id === card.id));
    if (!from || from.stage === stage) return;

    const before = this.board();
    this.board.set(this.moveCard(before, card, from.stage, stage));
    this.flash.set(card.id);
    try {
      await this.records.move(card.id, stage);
      this.toast.success(
        this.moveMessage(card, stage),
        stage === 'Won' ? 'Deal won' : stage === 'Lost' ? 'Deal lost' : `Moved to ${stage}`,
      );
    } catch {
      this.board.set(before);
    }
  }

  /** Immutable move so the board re-renders instantly; the server confirms it a moment later. */
  private moveCard(
    columns: BoardColumn[],
    card: BoardCard,
    from: DealStage,
    to: DealStage,
  ): BoardColumn[] {
    const moved: BoardCard = {
      ...card,
      probability: DEFAULT_PROBABILITY[to],
      overdue: to !== 'Won' && to !== 'Lost' && card.overdue,
    };
    return columns.map((col) => {
      if (col.stage === from)
        return {
          ...col,
          count: col.count - 1,
          total: col.total - card.amount,
          cards: col.cards.filter((c) => c.id !== card.id),
        };
      if (col.stage === to)
        return {
          ...col,
          count: col.count + 1,
          total: col.total + card.amount,
          cards: [moved, ...col.cards],
        };
      return col;
    });
  }

  private moveMessage(card: BoardCard, stage: DealStage) {
    if (stage === 'Won') return `${card.title} closed for ${fmtCompact(card.amount)}.`;
    if (stage === 'Lost') return `${card.title} marked as lost.`;
    return `${card.title} is now at ${DEFAULT_PROBABILITY[stage]}% probability.`;
  }

  // Drawer

  async editCard(card: BoardCard) {
    this.edit(await this.records.get(card.id));
  }

  newInStage(stage: DealStage) {
    this.openNew();
    this.form.patchValue({ stage, probability: DEFAULT_PROBABILITY[stage] });
  }

  protected override afterChange() {
    super.afterChange();
    if (this.view() === 'board') void this.loadBoard();
  }

  protected blank(): Partial<DealForm> {
    const close = new Date();
    close.setDate(close.getDate() + 30);
    return {
      amount: 10000,
      stage: 'Prospecting',
      probability: 10,
      expectedCloseDate: isoDate(close),
      companyId: null,
      contactId: null,
    };
  }

  protected toForm(d: Deal): DealForm {
    return {
      title: d.title,
      amount: d.amount,
      stage: d.stage,
      probability: d.probability,
      expectedCloseDate: d.expectedCloseDate.slice(0, 10),
      companyId: d.companyId,
      contactId: d.contactId,
      ownerId: d.ownerId,
    };
  }

  protected describe(d: Deal) {
    return d.title;
  }
}

import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { HumanPipe, RelativePipe, isoDate, isoLocal } from '../../core/format';
import { CrmTask, TASK_PRIORITIES, TASK_TYPES, TaskPriority, TaskType } from '../../core/models';
import { TaskApi, TaskForm } from '../../core/record-apis';
import {
  ColumnDef,
  DataTable,
  DtActions,
  DtCell,
  DtToolbar,
} from '../../shared/data-table/data-table';
import { LookupSelect } from '../../shared/lookup-select';
import { RecordPage } from '../../shared/record-page';
import { Drawer, FieldError, PageHeader } from '../../shared/ui';

type View = 'open' | 'overdue' | 'today' | 'done' | 'all';

const TYPE_ICONS: Record<string, string> = {
  Call: 'phone',
  Email: 'mail',
  Meeting: 'users',
  FollowUp: 'repeat',
};

/** Each tab is a starting set of column filters; the user can refine them in the grid. */
const VIEW_FILTERS: Record<View, () => Record<string, string>> = {
  open: () => ({ isDone: 'false' }),
  today: () => ({ dueAt: isoDate(new Date()), isDone: 'false' }),
  overdue: () => ({ overdue: 'true' }),
  done: () => ({ isDone: 'true' }),
  all: () => ({}),
};

@Component({
  selector: 'app-tasks',
  imports: [
    ReactiveFormsModule,
    LucideAngularModule,
    PageHeader,
    DataTable,
    DtCell,
    DtActions,
    DtToolbar,
    Drawer,
    FieldError,
    LookupSelect,
    HumanPipe,
    RelativePipe,
  ],
  templateUrl: './tasks.html',
})
export class TasksPage extends RecordPage<CrmTask, TaskForm> {
  protected readonly records = inject(TaskApi);
  protected readonly noun = 'Task';
  private readonly router = inject(Router);

  readonly types = TASK_TYPES;
  readonly priorities = TASK_PRIORITIES;
  readonly typeIcon = TYPE_ICONS;
  readonly searchContacts = (term: string) => this.lookups.contacts(term);
  readonly searchDeals = (term: string) => this.lookups.deals(term);

  readonly views: { id: View; label: string; icon: string }[] = [
    { id: 'open', label: 'Open', icon: 'circle-dashed' },
    { id: 'today', label: 'Due today', icon: 'calendar-check' },
    { id: 'overdue', label: 'Overdue', icon: 'alarm-clock' },
    { id: 'done', label: 'Done', icon: 'circle-check' },
    { id: 'all', label: 'All', icon: 'list' },
  ];

  readonly view = signal<View>(this.viewFromUrl());
  readonly viewFilters = computed(() => VIEW_FILTERS[this.view()]());

  readonly form = inject(FormBuilder).group({
    subject: ['', [Validators.required, Validators.maxLength(150)]],
    type: ['Call' as TaskType, Validators.required],
    priority: ['Normal' as TaskPriority, Validators.required],
    dueAt: ['', Validators.required],
    notes: ['' as string | null, Validators.maxLength(1000)],
    contactId: [null as number | null],
    dealId: [null as number | null],
    ownerId: [0, [Validators.required, Validators.min(1)]],
  });

  readonly columns: ColumnDef<CrmTask>[] = [
    { key: 'isDone', label: 'Done', type: 'bool', width: '64px' },
    { key: 'subject', label: 'Task', width: '300px' },
    { key: 'type', label: 'Type', type: 'enum', options: [...TASK_TYPES] },
    {
      key: 'priority',
      label: 'Priority',
      type: 'enum',
      options: [...TASK_PRIORITIES],
      badge: true,
    },
    { key: 'dueAt', label: 'Due', type: 'datetime' },
    { key: 'overdue', label: 'Overdue', type: 'bool', hidden: true },
    { key: 'contact', label: 'Contact' },
    { key: 'deal', label: 'Deal', hidden: true },
    { key: 'owner', label: 'Owner' },
    { key: 'completedAt', label: 'Completed', type: 'datetime', hidden: true },
  ];

  readonly rowClass = (t: CrmTask) => (t.overdue ? 'bg-bad-soft/50' : t.isDone ? 'opacity-70' : '');

  setView(view: View) {
    this.view.set(view);
    void this.router.navigate([], {
      queryParams: { due: view === 'open' ? null : view },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  async toggle(task: CrmTask) {
    try {
      const updated = await this.records.setDone(task.id, !task.isDone);
      if (updated.isDone) this.toast.success(`“${task.subject}” ticked off.`, 'Nice work');
      this.afterChange();
    } catch {
      /* the interceptor already explained why */
    }
  }

  protected blank(): Partial<TaskForm> {
    const due = new Date();
    due.setDate(due.getDate() + 1);
    due.setHours(10, 0, 0, 0);
    return {
      type: 'Call',
      priority: 'Normal',
      dueAt: isoLocal(due),
      notes: '',
      contactId: null,
      dealId: null,
    };
  }

  protected toForm(t: CrmTask): TaskForm {
    return {
      subject: t.subject,
      type: t.type,
      priority: t.priority,
      dueAt: isoLocal(new Date(t.dueAt)),
      notes: t.notes,
      contactId: t.contactId,
      dealId: t.dealId,
      ownerId: t.ownerId,
    };
  }

  protected describe(t: CrmTask) {
    return `“${t.subject}”`;
  }

  private viewFromUrl(): View {
    const due = this.route.snapshot.queryParamMap.get('due') as View | null;
    return due && ['overdue', 'today', 'done', 'all'].includes(due) ? due : 'open';
  }
}

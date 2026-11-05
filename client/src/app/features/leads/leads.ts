import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { HumanPipe, fmtMoney } from '../../core/format';
import { LEAD_SOURCES, LEAD_STATUSES, Lead, LeadSource, LeadStatus } from '../../core/models';
import { LeadApi, LeadForm } from '../../core/record-apis';
import {
  ColumnDef,
  DataTable,
  DtActions,
  DtCell,
  DtToolbar,
} from '../../shared/data-table/data-table';
import { RecordPage } from '../../shared/record-page';
import { Avatar, Drawer, FieldError, PageHeader } from '../../shared/ui';

@Component({
  selector: 'app-leads',
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
    Avatar,
    HumanPipe,
  ],
  templateUrl: './leads.html',
})
export class LeadsPage extends RecordPage<Lead, LeadForm> {
  protected readonly records = inject(LeadApi);
  protected readonly noun = 'Lead';
  private readonly router = inject(Router);

  readonly sources = LEAD_SOURCES;
  readonly editableStatuses = LEAD_STATUSES.filter((s) => s !== 'Converted');
  readonly initialFilters = this.filtersFromUrl('status', 'source');

  readonly form = inject(FormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    companyName: ['', [Validators.required, Validators.maxLength(120)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(160)]],
    phone: ['' as string | null, [Validators.maxLength(30), Validators.pattern(/^[0-9+\-() ]*$/)]],
    source: ['Website' as LeadSource, Validators.required],
    status: ['New' as LeadStatus, Validators.required],
    score: [50, [Validators.required, Validators.min(0), Validators.max(100)]],
    estimatedValue: [5000, [Validators.required, Validators.min(0)]],
    ownerId: [0, [Validators.required, Validators.min(1)]],
  });

  readonly columns: ColumnDef<Lead>[] = [
    { key: 'name', label: 'Lead', width: '220px' },
    { key: 'companyName', label: 'Company' },
    { key: 'email', label: 'Email', hidden: true },
    { key: 'source', label: 'Source', type: 'enum', options: [...LEAD_SOURCES] },
    { key: 'status', label: 'Status', type: 'enum', options: [...LEAD_STATUSES], badge: true },
    { key: 'score', label: 'Score', type: 'number', width: '120px' },
    { key: 'estimatedValue', label: 'Est. value', type: 'money', sum: true },
    { key: 'owner', label: 'Owner' },
    { key: 'createdAt', label: 'Created', type: 'date' },
  ];

  protected blank(): Partial<LeadForm> {
    return { source: 'Website', status: 'New', score: 50, estimatedValue: 5000, phone: '' };
  }

  protected toForm(l: Lead): LeadForm {
    return {
      name: l.name,
      companyName: l.companyName,
      email: l.email,
      phone: l.phone,
      source: l.source,
      status: l.status,
      score: l.score,
      estimatedValue: l.estimatedValue,
      ownerId: l.ownerId,
    };
  }

  protected describe(l: Lead) {
    return l.name;
  }

  scoreTone(score: number) {
    return score >= 70
      ? 'var(--color-ok)'
      : score >= 40
        ? 'var(--color-lilac-300)'
        : 'var(--color-line-strong)';
  }

  async convert(lead: Lead) {
    const ok = await this.confirm.ask(
      'Convert to a deal?',
      `${lead.name} becomes a contact at ${lead.companyName}, and a ${fmtMoney(lead.estimatedValue)} deal opens in Prospecting.`,
      'Convert lead',
    );
    if (!ok) return;

    try {
      const result = await this.records.convert(lead.id);
      this.toast.success(`${result.dealTitle} is now on the board.`, 'Lead converted');
      this.afterChange();
    } catch {
      /* the interceptor already explained why */
    }
  }

  openDeal(lead: Lead) {
    void this.router.navigate(['/deals'], {
      queryParams: { view: 'list', search: lead.companyName },
    });
  }
}

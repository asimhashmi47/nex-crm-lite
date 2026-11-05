import { Component } from '@angular/core';
import { ColumnDef, DataTable, DtCell } from '../../shared/data-table/data-table';
import { PageHeader } from '../../shared/ui';

interface AuditRow {
  id: number;
  at: string;
  username: string;
  action: string;
  entityType: string;
  entityId: string | null;
  summary: string | null;
  ipAddress: string | null;
}

const ACTION_TONES: Record<string, string> = {
  Archived: 'tone-bad',
  Exported: 'tone-warn',
  SignInFailed: 'tone-bad',
  Converted: 'tone-ok',
  StageChanged: 'tone-info',
  Created: 'tone-brand',
};

/** Read-only history of sensitive actions. Admins only; the API enforces the same rule. */
@Component({
  selector: 'app-audit',
  imports: [PageHeader, DataTable, DtCell],
  template: `
    <app-page-header
      title="Audit log"
      icon="scroll-text"
      subtitle="Who signed in, created, changed, reassigned, converted, exported or deleted what, and when."
    />

    <app-data-table
      tableId="audit"
      title="Every recorded action"
      endpoint="/api/audit"
      [columns]="columns"
      defaultSort="at"
      [exportable]="false"
    >
      <ng-template dtCell="action" let-row>
        <span class="badge {{ tone(row.action) }}">{{ row.action }}</span>
      </ng-template>
    </app-data-table>
  `,
})
export class AuditPage {
  readonly columns: ColumnDef<AuditRow>[] = [
    { key: 'at', label: 'When', type: 'datetime', width: '170px' },
    { key: 'username', label: 'User' },
    { key: 'action', label: 'Action' },
    { key: 'entityType', label: 'Record' },
    { key: 'entityId', label: 'Id' },
    { key: 'summary', label: 'Details' },
    { key: 'ipAddress', label: 'Address', hidden: true },
  ];

  tone(action: string) {
    return ACTION_TONES[action.split('.')[1]] ?? 'tone-gray';
  }
}

import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { HumanPipe } from '../../core/format';
import { CONTACT_STAGES, Contact, ContactStage } from '../../core/models';
import { ContactApi, ContactForm } from '../../core/record-apis';
import {
  ColumnDef,
  DataTable,
  DtActions,
  DtCell,
  DtToolbar,
} from '../../shared/data-table/data-table';
import { LookupSelect } from '../../shared/lookup-select';
import { RecordPage } from '../../shared/record-page';
import { Avatar, Drawer, FieldError, PageHeader } from '../../shared/ui';

@Component({
  selector: 'app-contacts',
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
    LookupSelect,
    HumanPipe,
  ],
  templateUrl: './contacts.html',
})
export class ContactsPage extends RecordPage<Contact, ContactForm> {
  protected readonly records = inject(ContactApi);
  protected readonly noun = 'Contact';
  readonly stages = CONTACT_STAGES;
  readonly searchCompanies = (term: string) => this.lookups.companies(term);

  readonly form = inject(FormBuilder).group({
    firstName: ['', [Validators.required, Validators.maxLength(60)]],
    lastName: ['', [Validators.required, Validators.maxLength(60)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(160)]],
    phone: ['' as string | null, [Validators.maxLength(30), Validators.pattern(/^[0-9+\-() ]*$/)]],
    jobTitle: ['' as string | null, Validators.maxLength(100)],
    stage: ['Lead' as ContactStage, Validators.required],
    companyId: [null as number | null],
    ownerId: [0, [Validators.required, Validators.min(1)]],
  });

  readonly columns: ColumnDef<Contact>[] = [
    { key: 'name', label: 'Contact', width: '230px' },
    { key: 'email', label: 'Email' },
    { key: 'phone', label: 'Phone', hidden: true },
    { key: 'jobTitle', label: 'Job title' },
    { key: 'company', label: 'Company' },
    { key: 'stage', label: 'Stage', type: 'enum', options: [...CONTACT_STAGES], badge: true },
    { key: 'openTasks', label: 'Open tasks', type: 'number', sum: true },
    { key: 'owner', label: 'Owner' },
    { key: 'lastContactedAt', label: 'Last contacted', type: 'date' },
    { key: 'createdAt', label: 'Added', type: 'date', hidden: true },
  ];

  protected blank(): Partial<ContactForm> {
    return { stage: 'Lead', companyId: null, phone: '', jobTitle: '' };
  }

  protected toForm(c: Contact): ContactForm {
    return {
      firstName: c.firstName,
      lastName: c.lastName,
      email: c.email,
      phone: c.phone,
      jobTitle: c.jobTitle,
      stage: c.stage,
      companyId: c.companyId,
      ownerId: c.ownerId,
    };
  }

  protected describe(c: Contact) {
    return c.name ?? `${c.firstName} ${c.lastName}`;
  }
}

import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { Company } from '../../core/models';
import { CompanyApi, CompanyForm } from '../../core/record-apis';
import {
  ColumnDef,
  DataTable,
  DtActions,
  DtCell,
  DtToolbar,
} from '../../shared/data-table/data-table';
import { RecordPage } from '../../shared/record-page';
import { Avatar, Drawer, FieldError, PageHeader } from '../../shared/ui';

const INDUSTRIES = [
  'Software',
  'Logistics',
  'Healthcare',
  'Retail',
  'Manufacturing',
  'Real estate',
  'Education',
  'Finance',
  'Hospitality',
  'Energy',
  'Unassigned',
];

@Component({
  selector: 'app-companies',
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
  ],
  templateUrl: './companies.html',
})
export class CompaniesPage extends RecordPage<Company, CompanyForm> {
  protected readonly records = inject(CompanyApi);
  protected readonly noun = 'Company';
  readonly industries = INDUSTRIES;

  readonly form = inject(FormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    industry: ['Software', Validators.required],
    website: ['' as string | null, Validators.maxLength(200)],
    phone: ['' as string | null, [Validators.maxLength(30), Validators.pattern(/^[0-9+\-() ]*$/)]],
    city: ['', [Validators.required, Validators.maxLength(80)]],
    country: ['', [Validators.required, Validators.maxLength(80)]],
    employees: [10, [Validators.required, Validators.min(0)]],
    annualRevenue: [0, [Validators.required, Validators.min(0)]],
    ownerId: [0, [Validators.required, Validators.min(1)]],
  });

  readonly columns: ColumnDef<Company>[] = [
    { key: 'name', label: 'Company', width: '240px' },
    { key: 'industry', label: 'Industry', type: 'enum', options: INDUSTRIES },
    { key: 'city', label: 'City' },
    { key: 'country', label: 'Country', hidden: true },
    { key: 'employees', label: 'Employees', type: 'number' },
    { key: 'contacts', label: 'Contacts', type: 'number', sum: true },
    { key: 'openDeals', label: 'Open deals', type: 'number', sum: true },
    { key: 'wonRevenue', label: 'Won revenue', type: 'money', sum: true },
    { key: 'annualRevenue', label: 'Annual revenue', type: 'money', hidden: true },
    { key: 'owner', label: 'Owner' },
    { key: 'website', label: 'Website', hidden: true },
    { key: 'createdAt', label: 'Added', type: 'date' },
  ];

  protected blank(): Partial<CompanyForm> {
    return { industry: 'Software', employees: 10, annualRevenue: 0, website: '', phone: '' };
  }

  protected toForm(c: Company): CompanyForm {
    return {
      name: c.name,
      industry: c.industry,
      website: c.website,
      phone: c.phone,
      city: c.city,
      country: c.country,
      employees: c.employees,
      annualRevenue: c.annualRevenue,
      ownerId: c.ownerId,
    };
  }

  protected describe(c: Company) {
    return c.name;
  }
}

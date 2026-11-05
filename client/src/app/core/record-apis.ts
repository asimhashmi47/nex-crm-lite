import { Injectable, inject } from '@angular/core';
import { Api, RecordApi } from './api';
import {
  BoardColumn,
  Company,
  Contact,
  ContactStage,
  ConvertLeadResult,
  CrmTask,
  Deal,
  DealStage,
  Lead,
  LeadSource,
  LeadStatus,
  Option,
  TaskPriority,
  TaskType,
} from './models';

export interface CompanyForm {
  name: string;
  industry: string;
  website: string | null;
  phone: string | null;
  city: string;
  country: string;
  employees: number;
  annualRevenue: number;
  ownerId: number;
}
export interface ContactForm {
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;
  jobTitle: string | null;
  stage: ContactStage;
  companyId: number | null;
  ownerId: number;
}
export interface LeadForm {
  name: string;
  companyName: string;
  email: string;
  phone: string | null;
  source: LeadSource;
  status: LeadStatus;
  score: number;
  estimatedValue: number;
  ownerId: number;
}
export interface DealForm {
  title: string;
  amount: number;
  stage: DealStage;
  probability: number;
  expectedCloseDate: string;
  companyId: number | null;
  contactId: number | null;
  ownerId: number;
}
export interface TaskForm {
  subject: string;
  type: TaskType;
  priority: TaskPriority;
  dueAt: string;
  notes: string | null;
  contactId: number | null;
  dealId: number | null;
  ownerId: number;
}

@Injectable({ providedIn: 'root' })
export class CompanyApi extends RecordApi<Company, CompanyForm> {
  readonly endpoint = '/api/companies';
}

@Injectable({ providedIn: 'root' })
export class ContactApi extends RecordApi<Contact, ContactForm> {
  readonly endpoint = '/api/contacts';
}

@Injectable({ providedIn: 'root' })
export class LeadApi extends RecordApi<Lead, LeadForm> {
  readonly endpoint = '/api/leads';

  convert(id: number) {
    return this.api.post<ConvertLeadResult>(`${this.endpoint}/${id}/convert`);
  }
}

@Injectable({ providedIn: 'root' })
export class DealApi extends RecordApi<Deal, DealForm> {
  readonly endpoint = '/api/deals';

  board(ownerId?: number | null) {
    return this.api.get<BoardColumn[]>(`${this.endpoint}/board`, ownerId ? { ownerId } : undefined);
  }
  move(id: number, stage: DealStage) {
    return this.api.patch<Deal>(`${this.endpoint}/${id}/stage`, { stage });
  }
}

@Injectable({ providedIn: 'root' })
export class TaskApi extends RecordApi<CrmTask, TaskForm> {
  readonly endpoint = '/api/tasks';

  setDone(id: number, done: boolean) {
    return this.api.patch<CrmTask>(`${this.endpoint}/${id}/done`, { done });
  }
}

/** Id/label lists for the pickers in the drawers. */
@Injectable({ providedIn: 'root' })
export class LookupApi {
  private readonly api = inject(Api);
  private readonly endpoint = '/api/lookups';

  owners() {
    return this.api.get<Option[]>(`${this.endpoint}/owners`);
  }
  companies(term = '') {
    return this.api.get<Option[]>(`${this.endpoint}/companies`, { term });
  }
  contacts(term = '', companyId?: number | null) {
    return this.api.get<Option[]>(
      `${this.endpoint}/contacts`,
      companyId ? { term, companyId } : { term },
    );
  }
  deals(term = '') {
    return this.api.get<Option[]>(`${this.endpoint}/deals`, { term });
  }
}

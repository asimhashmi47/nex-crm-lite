export interface FilterRule {
  field: string;
  operator: string;
  value?: string | null;
  value2?: string | null;
}

export interface TableQuery {
  page: number;
  pageSize: number;
  search?: string | null;
  sortBy?: string | null;
  sortDesc?: boolean;
  columnFilters?: Record<string, string>;
  filters?: FilterRule[];
  logic?: 'and' | 'or';
  sumFields?: string[];
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  sums?: Record<string, number> | null;
}

export interface AuthUser {
  id: number;
  username: string;
  fullName: string;
  email: string;
  role: string;
  canDelete: boolean;
}
export interface TokenResponse {
  accessToken: string;
  expiresAt: string;
  user: AuthUser;
}
export interface DemoAccount {
  username: string;
  role: string;
  description: string;
}
export interface AuthConfig {
  demoMode: boolean;
  demoAccounts: DemoAccount[];
}

export interface Option {
  id: number;
  label: string;
  hint?: string | null;
}

export const LEAD_SOURCES = [
  'Website',
  'Referral',
  'LinkedIn',
  'ColdCall',
  'Event',
  'Partner',
  'Ads',
] as const;
export const LEAD_STATUSES = ['New', 'Contacted', 'Qualified', 'Unqualified', 'Converted'] as const;
export const DEAL_STAGES = [
  'Prospecting',
  'Qualification',
  'Proposal',
  'Negotiation',
  'Won',
  'Lost',
] as const;
export const OPEN_STAGES = DEAL_STAGES.slice(0, 4);
export const CONTACT_STAGES = ['Subscriber', 'Lead', 'Opportunity', 'Customer', 'Churned'] as const;
export const TASK_TYPES = ['Call', 'Email', 'Meeting', 'FollowUp'] as const;
export const TASK_PRIORITIES = ['Low', 'Normal', 'High'] as const;

export type LeadSource = (typeof LEAD_SOURCES)[number];
export type LeadStatus = (typeof LEAD_STATUSES)[number];
export type DealStage = (typeof DEAL_STAGES)[number];
export type ContactStage = (typeof CONTACT_STAGES)[number];
export type TaskType = (typeof TASK_TYPES)[number];
export type TaskPriority = (typeof TASK_PRIORITIES)[number];

export interface Company {
  id: number;
  name: string;
  industry: string;
  website: string | null;
  phone: string | null;
  city: string;
  country: string;
  employees: number;
  annualRevenue: number;
  ownerId: number;
  owner: string;
  contacts: number;
  openDeals: number;
  wonRevenue: number;
  createdAt: string;
}

export interface Contact {
  id: number;
  name: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;
  jobTitle: string | null;
  stage: ContactStage;
  companyId: number | null;
  company: string | null;
  ownerId: number;
  owner: string;
  openTasks: number;
  lastContactedAt: string | null;
  createdAt: string;
}

export interface Lead {
  id: number;
  name: string;
  companyName: string;
  email: string;
  phone: string | null;
  source: LeadSource;
  status: LeadStatus;
  score: number;
  estimatedValue: number;
  ownerId: number;
  owner: string;
  convertedDealId: number | null;
  createdAt: string;
}

export interface Deal {
  id: number;
  title: string;
  amount: number;
  stage: DealStage;
  probability: number;
  weighted: number;
  expectedCloseDate: string;
  closedAt: string | null;
  companyId: number;
  company: string;
  contactId: number | null;
  contact: string | null;
  ownerId: number;
  owner: string;
  createdAt: string;
}

export interface CrmTask {
  id: number;
  subject: string;
  type: TaskType;
  priority: TaskPriority;
  dueAt: string;
  isDone: boolean;
  overdue: boolean;
  completedAt: string | null;
  notes: string | null;
  contactId: number | null;
  contact: string | null;
  dealId: number | null;
  deal: string | null;
  ownerId: number;
  owner: string;
}

export interface BoardCard {
  id: number;
  title: string;
  amount: number;
  probability: number;
  expectedCloseDate: string;
  company: string;
  owner: string;
  overdue: boolean;
}
export interface BoardColumn {
  stage: DealStage;
  count: number;
  total: number;
  cards: BoardCard[];
}
export interface ConvertLeadResult {
  dealId: number;
  companyId: number;
  contactId: number;
  dealTitle: string;
}

export interface Kpis {
  openPipeline: number;
  openDeals: number;
  weightedForecast: number;
  wonThisMonth: number;
  wonLastMonth: number;
  dealsWonThisMonth: number;
  winRate: number;
  avgDealSize: number;
  newLeadsThisMonth: number;
  newLeadsLastMonth: number;
  overdueTasks: number;
  tasksDueToday: number;
}

export interface Dashboard {
  kpis: Kpis;
  trend: { month: string; won: number; newLeads: number }[];
  pipeline: { stage: DealStage; count: number; amount: number }[];
  sources: { source: LeadSource; leads: number; converted: number }[];
  leaderboard: { ownerId: number; owner: string; won: number; deals: number; pipeline: number }[];
  dueTasks: {
    id: number;
    subject: string;
    type: TaskType;
    priority: TaskPriority;
    dueAt: string;
    overdue: boolean;
    contact: string | null;
  }[];
  closingSoon: {
    id: number;
    title: string;
    company: string;
    amount: number;
    stage: DealStage;
    expectedCloseDate: string;
  }[];
}

/** The bell's summary (GET /api/dashboard/alerts). */
export interface Alerts {
  overdueTasks: number;
  tasksDueToday: number;
  closingSoon: number;
  newLeadsThisMonth: number;
  openPipeline: number;
}

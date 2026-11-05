import { Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/guards';
import { Shell } from './layout/shell';

const title = (page: string) => `${page} · NexCRM Lite`;

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login').then((m) => m.Login),
    title: title('Sign in'),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.DashboardPage),
        title: title('Dashboard'),
      },
      {
        path: 'deals',
        loadComponent: () => import('./features/deals/deals').then((m) => m.DealsPage),
        title: title('Pipeline'),
      },
      {
        path: 'leads',
        loadComponent: () => import('./features/leads/leads').then((m) => m.LeadsPage),
        title: title('Leads'),
      },
      {
        path: 'contacts',
        loadComponent: () => import('./features/contacts/contacts').then((m) => m.ContactsPage),
        title: title('Contacts'),
      },
      {
        path: 'companies',
        loadComponent: () => import('./features/companies/companies').then((m) => m.CompaniesPage),
        title: title('Companies'),
      },
      {
        path: 'tasks',
        loadComponent: () => import('./features/tasks/tasks').then((m) => m.TasksPage),
        title: title('Tasks'),
      },
      {
        path: 'audit',
        canActivate: [roleGuard],
        data: { roles: ['Admin'] },
        loadComponent: () => import('./features/audit/audit').then((m) => m.AuditPage),
        title: title('Audit log'),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];

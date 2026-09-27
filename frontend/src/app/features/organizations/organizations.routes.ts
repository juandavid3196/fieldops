import { Routes } from '@angular/router';

export default [
  {
    path: '',
    pathMatch: 'full',
    title: 'Create your organization · FieldOps',
    loadComponent: () =>
      import('./pages/register-company/register-company').then((m) => m.RegisterCompany),
  },
] satisfies Routes;

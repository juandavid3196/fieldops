import { Routes } from '@angular/router';

export default [
  {
    path: '',
    title: 'Overview · FieldOps',
    loadComponent: () => import('./pages/overview/overview').then((m) => m.Overview),
  },
] satisfies Routes;

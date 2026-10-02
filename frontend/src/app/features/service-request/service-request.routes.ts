import { Routes } from '@angular/router';

export default [
  {
    // Public on purpose: no guard and no authenticated shell; an existing session is ignored.
    path: '',
    title: 'Request a service',
    loadComponent: () =>
      import('./pages/service-request/service-request').then((m) => m.ServiceRequest),
  },
] satisfies Routes;

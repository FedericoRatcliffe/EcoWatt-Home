import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
    title: 'EcoWatt Casa',
  },
  {
    path: 'dispositivo/:id',
    loadComponent: () => import('./features/device-detail/device-detail').then((m) => m.DeviceDetail),
    title: 'Dispositivo | EcoWatt Casa',
  },
  {
    path: 'config',
    loadComponent: () => import('./features/settings/settings').then((m) => m.Settings),
    title: 'Configuracion | EcoWatt Casa',
  },
  { path: '**', redirectTo: '' },
];

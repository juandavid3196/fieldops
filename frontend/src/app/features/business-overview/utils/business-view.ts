import { BusinessAlert } from '../models/business-overview.model';

export const EMPTY_VALUE = '—';
export const BELOW_TARGET_TEXT = 'Below 40% target';
export const ATTENTION_LIMIT = 3;
export const SERVICE_LIMIT = 5;

/** BR-10: card and table money in whole currency units with the symbol. */
export function money(value: number, currency: string): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency,
    maximumFractionDigits: 0,
  }).format(value);
}

/** BR-10: margins with one decimal, "—" when not computable. */
export function percentOne(value: number | null): string {
  return value === null ? EMPTY_VALUE : `${value.toFixed(1)}%`;
}

/** BR-10: conversion, utilization and on-time as integers. */
export function percentWhole(value: number | null): string {
  return value === null ? EMPTY_VALUE : `${Math.round(value)}%`;
}

/** BR-10: whole hours, `round(minutes / 60)`. */
export function hours(minutes: number): string {
  return `${Math.round(minutes / 60)}h`;
}

export function ratingText(value: number | null): string {
  return value === null ? EMPTY_VALUE : value.toFixed(1);
}

/** BR-09: below target when the margin is known and under the server constant. */
export function belowTarget(margin: number | null, target: number): boolean {
  return margin !== null && margin < target;
}

export interface Delta {
  readonly direction: 'up' | 'down' | 'flat';
  readonly text: string;
}

/** BR-11: relative change vs the previous period; hidden (null) when previous is 0. */
export function delta(current: number, previous: number): Delta | null {
  if (previous === 0) {
    return null;
  }
  const value = Math.round(((current - previous) / Math.abs(previous)) * 1000) / 10;
  if (value === 0) {
    return { direction: 'flat', text: 'No change' };
  }
  return value > 0
    ? { direction: 'up', text: `+${value.toFixed(1)}%` }
    : { direction: 'down', text: `−${Math.abs(value).toFixed(1)}%` };
}

/** BR-17: overtime change vs the previous period; shown only when previous overtime > 0. */
export function overtimeChange(current: number, previous: number): Delta | null {
  if (previous <= 0) {
    return null;
  }
  const value = Math.round(((current - previous) / previous) * 100);
  if (value === 0) {
    return { direction: 'flat', text: 'No change' };
  }
  return value > 0
    ? { direction: 'up', text: `${value}%` }
    : { direction: 'down', text: `${Math.abs(value)}%` };
}

export type AttentionLink =
  | { readonly kind: 'invoices' }
  | { readonly kind: 'branch'; readonly branchId: string }
  | { readonly kind: 'catalog' }
  | { readonly kind: 'team' };

export interface AttentionItem {
  readonly key: string;
  readonly severity: 'critical' | 'warning';
  readonly title: string;
  readonly detail: string;
  readonly link: AttentionLink;
}

/** BR-18: copy and target of each alert; the server owns order and eligibility. */
export function attentionItem(
  alert: BusinessAlert,
  index: number,
  currency: string,
  target: number,
): AttentionItem {
  const targetText = `${percentOne(alert.marginPercent)} vs ${target.toFixed(1)}% target`;
  const key = `${alert.type}-${alert.branchId ?? alert.categoryId ?? index}`;
  switch (alert.type) {
    case 'overdue_invoices': {
      const count = alert.count ?? 0;
      return {
        key,
        severity: alert.severity,
        title: `${count} overdue invoice${count === 1 ? '' : 's'}`,
        detail: `Totaling ${money(alert.amount ?? 0, currency)}`,
        link: { kind: 'invoices' },
      };
    }
    case 'branch_margin':
      return {
        key,
        severity: alert.severity,
        title: `${alert.name ?? 'Branch'} estimated margin below target`,
        detail: targetText,
        link: { kind: 'branch', branchId: alert.branchId ?? '' },
      };
    case 'service_margin':
      return {
        key,
        severity: alert.severity,
        title: `${alert.name ?? 'Service'} estimated margin below target`,
        detail: targetText,
        link: { kind: 'catalog' },
      };
    case 'overtime':
      return {
        key,
        severity: alert.severity,
        title: `Scheduled overtime up ${Math.round(alert.changePercent ?? 0)}%`,
        detail: `${hours(alert.overtimeMinutes ?? 0)} vs ${hours(alert.previousOvertimeMinutes ?? 0)} in previous period`,
        link: { kind: 'team' },
      };
  }
}

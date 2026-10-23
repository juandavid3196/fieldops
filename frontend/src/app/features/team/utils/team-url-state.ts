import { ParamMap } from '@angular/router';

import {
  JOB_HISTORY_PAGE_SIZES,
  JOB_HISTORY_PERIODS,
  JOB_HISTORY_SORTS,
  JOB_HISTORY_STATUSES,
  JobHistoryPeriod,
  JobHistoryQuery,
  JobHistorySort,
  JobHistoryStatus,
} from '../models/team-job-history.model';
import { AccountLinkFilter, StatusFilter, TeamPeriod, TeamSort } from '../models/team.model';

export type UrlParams = Record<string, string | null>;

const SEARCH_MAX = 100;
const STATUSES: readonly StatusFilter[] = [
  'all_active',
  'available',
  'on_job',
  'break',
  'time_off',
  'off',
  'inactive',
  'suspended',
];
const ACCOUNTS: readonly AccountLinkFilter[] = ['all', 'linked', 'not_linked'];
const SORTS: readonly TeamSort[] = ['name_asc', 'name_desc'];
const PERIODS: readonly TeamPeriod[] = ['today', 'week'];

function oneOf<T extends string>(value: string | null, allowed: readonly T[], fallback: T): T {
  return allowed.find((candidate) => candidate === value) ?? fallback;
}

function pageOf(value: string | null): number {
  const page = value !== null && /^\d{1,6}$/.test(value) ? Number(value) : 1;
  return page >= 1 ? page : 1;
}

function searchOf(value: string | null): string {
  return (value ?? '').trim().slice(0, SEARCH_MAX);
}

/** `team-technician-management` list state kept in the URL query (BR-02 of the profile spec). */
export interface TeamUrlState {
  readonly q: string;
  readonly branch: string | null;
  readonly skill: string | null;
  readonly status: StatusFilter;
  readonly account: AccountLinkFilter;
  readonly sort: TeamSort;
  readonly period: TeamPeriod;
  readonly page: number;
}

export function parseTeamUrl(params: ParamMap): TeamUrlState {
  const text = (name: string): string | null => {
    const value = params.get(name)?.trim();
    return value ? value : null;
  };
  return {
    q: searchOf(params.get('q')),
    branch: text('branch'),
    skill: text('skill'),
    status: oneOf(params.get('status'), STATUSES, 'all_active'),
    account: oneOf(params.get('account'), ACCOUNTS, 'all'),
    sort: oneOf(params.get('sort'), SORTS, 'name_asc'),
    period: oneOf(params.get('period'), PERIODS, 'today'),
    page: pageOf(params.get('page')),
  };
}

/** Canonical query: defaults are absent (`null` removes the parameter when merging). */
export function teamUrlParams(state: TeamUrlState): UrlParams {
  return {
    q: state.q === '' ? null : state.q,
    branch: state.branch,
    skill: state.skill,
    status: state.status === 'all_active' ? null : state.status,
    account: state.account === 'all' ? null : state.account,
    sort: state.sort === 'name_asc' ? null : state.sort,
    period: state.period === 'today' ? null : state.period,
    page: state.page === 1 ? null : String(state.page),
  };
}

export function sameTeamUrlState(a: TeamUrlState, b: TeamUrlState): boolean {
  return (
    a.q === b.q &&
    a.branch === b.branch &&
    a.skill === b.skill &&
    a.status === b.status &&
    a.account === b.account &&
    a.sort === b.sort &&
    a.period === b.period &&
    a.page === b.page
  );
}

/** True when the URL holds a value that differs from the canonical one (it must be replaced). */
export function needsCanonicalUrl(params: ParamMap, canonical: UrlParams): boolean {
  return Object.entries(canonical).some(([name, value]) => (params.get(name) ?? null) !== value);
}

/** Job history state kept in the URL query (BR-19). */
export function parseJobHistoryUrl(params: ParamMap): JobHistoryQuery {
  const size = Number(params.get('pageSize'));
  return {
    search: searchOf(params.get('q')),
    status: oneOf<JobHistoryStatus>(params.get('status'), JOB_HISTORY_STATUSES, 'all'),
    period: oneOf<JobHistoryPeriod>(params.get('period'), JOB_HISTORY_PERIODS, '90d'),
    sort: oneOf<JobHistorySort>(params.get('sort'), JOB_HISTORY_SORTS, 'newest'),
    page: pageOf(params.get('page')),
    pageSize: JOB_HISTORY_PAGE_SIZES.includes(size) ? size : 10,
  };
}

export function jobHistoryUrlParams(query: JobHistoryQuery): UrlParams {
  return {
    q: query.search === '' ? null : query.search,
    status: query.status === 'all' ? null : query.status,
    period: query.period === '90d' ? null : query.period,
    sort: query.sort === 'newest' ? null : query.sort,
    page: query.page === 1 ? null : String(query.page),
    pageSize: query.pageSize === 10 ? null : String(query.pageSize),
  };
}

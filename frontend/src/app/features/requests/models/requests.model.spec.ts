import {
  RequestActionId,
  RequestStatus,
  canEditRequest,
  footerActions,
  menuActions,
} from './requests.model';

const ids = (actions: readonly { id: RequestActionId }[]): RequestActionId[] =>
  actions.map((action) => action.id);

const COMMON: RequestActionId[] = ['assign', 'change-priority', 'set-branch', 'log-response'];
const REVIEW_FOOTER: RequestActionId[] = [
  'schedule-assessment',
  'request-information',
  'mark-ready',
];

type Row = [RequestStatus, RequestActionId[], RequestActionId | null, RequestActionId[]];

describe('request panel actions (BR-03)', () => {
  it.each<Row>([
    ['new', REVIEW_FOOTER, 'schedule-assessment', [...COMMON, 'start-review', 'cancel-request']],
    ['needs_review', REVIEW_FOOTER, 'schedule-assessment', [...COMMON, 'cancel-request']],
    [
      'assessment_scheduled',
      ['complete-assessment', 'request-information', 'reschedule'],
      'complete-assessment',
      [...COMMON, 'cancel-assessment', 'cancel-request'],
    ],
    [
      'ready_for_quote',
      ['create-quote', 'request-information'],
      'create-quote',
      [...COMMON, 'move-back', 'cancel-request'],
    ],
    ['quoted', [], null, []],
    ['converted', [], null, []],
    ['cancelled', [], null, []],
  ])(
    '%s: managers get the footer/menu of BR-03; read roles get no actions and no inputs (AC-05, AC-18, AC-19)',
    (status, footer, primary, menu) => {
      const actions = footerActions(status, true);

      expect(ids(actions)).toEqual(footer);
      expect(actions.find((action) => action.primary)?.id ?? null).toBe(primary);
      expect(ids(menuActions(status, true))).toEqual(menu);
      expect(canEditRequest(status, true)).toBe(footer.length > 0);

      expect(footerActions(status, false)).toEqual([]);
      expect(menuActions(status, false)).toEqual([]);
      expect(canEditRequest(status, false)).toBe(false);
    },
  );
});

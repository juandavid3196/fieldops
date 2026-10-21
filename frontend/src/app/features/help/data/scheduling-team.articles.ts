import { HelpSection } from '../models/help.model';

import { localizedList, t } from '../../../core/i18n/translate';
/** Scheduling & team articles. Content describes only behavior FieldOps implements (BR-10). */
export const SCHEDULING_TEAM_SECTIONS: readonly HelpSection[] = localizedList(() => [
  {
    label: t('help.schedulingTeamArticles.scheduling'),
    articles: [
      {
        slug: 'schedule-a-visit',
        title: t('help.schedulingTeamArticles.scheduleAVisit'),
        description: t('help.schedulingTeamArticles.setTheDateTime'),
        icon: 'pi-calendar',
        introduction: t('help.schedulingTeamArticles.useTheDispatchCalendar'),
        steps: [
          { text: t('help.schedulingTeamArticles.selectScheduleInThe') },
          {
            text: t('help.schedulingTeamArticles.inTheUnscheduledPanel'),
          },
          {
            text: t('help.schedulingTeamArticles.inTheAssignWork'),
          },
          {
            text: t('help.schedulingTeamArticles.timesUseTheBranch'),
          },
          { text: t('help.schedulingTeamArticles.selectSchedule') },
        ],
        whatHappensNext: t('help.schedulingTeamArticles.theVisitMovesFrom'),
      },
      {
        slug: 'assign-a-technician',
        title: t('help.schedulingTeamArticles.assignATechnician'),
        description: t('help.schedulingTeamArticles.matchTheVisitWith'),
        icon: 'pi-users',
        introduction: t('help.schedulingTeamArticles.youChooseTheTechnicians'),
        callout: t('help.schedulingTeamArticles.requiredSkillsOfThe'),
        steps: [
          { text: t('help.schedulingTeamArticles.openTheVisitFrom') },
          {
            text: t('help.schedulingTeamArticles.inAssignToTechnicians'),
          },
          {
            text: t('help.schedulingTeamArticles.reviewWorkloadAndRoute'),
          },
          {
            text: t('help.schedulingTeamArticles.chooseWhetherToEmail'),
          },
          { text: t('help.schedulingTeamArticles.selectAssignOrAssign') },
        ],
        whatHappensNext: t('help.schedulingTeamArticles.theVisitAppearsOn'),
      },
      {
        slug: 'reschedule-or-cancel-a-visit',
        title: t('help.schedulingTeamArticles.rescheduleOrCancelA'),
        description: t('help.schedulingTeamArticles.updateThePlanWhile'),
        icon: 'pi-clock',
        introduction: t('help.schedulingTeamArticles.youCanChangeThe'),
        callout: t('help.schedulingTeamArticles.theScheduleDoesNot'),
        steps: [
          { text: t('help.schedulingTeamArticles.selectScheduleAndFind') },
          { text: t('help.schedulingTeamArticles.selectTheVisitTo') },
          {
            text: t('help.schedulingTeamArticles.changeTheDateStart'),
          },
          { text: t('help.schedulingTeamArticles.selectUpdateOrUpdate') },
        ],
        whatHappensNext: t('help.schedulingTeamArticles.theCalendarShowsThe'),
      },
    ],
  },
  {
    label: t('help.schedulingTeamArticles.fieldWork'),
    articles: [
      {
        slug: 'update-a-visit-status',
        title: t('help.schedulingTeamArticles.updateAVisitStatus'),
        description: t('help.schedulingTeamArticles.moveAVisitFrom'),
        icon: 'pi-file-edit',
        introduction: t('help.schedulingTeamArticles.techniciansMoveAVisit'),
        steps: [
          {
            text: t('help.schedulingTeamArticles.thePrimaryTechnicianOpens'),
          },
          { text: t('help.schedulingTeamArticles.selectStartTravelThe') },
          { text: t('help.schedulingTeamArticles.onArrivalSelectIve') },
          { text: t('help.schedulingTeamArticles.selectStartJobThe') },
          {
            text: t('help.schedulingTeamArticles.usePauseWhenWork'),
          },
          { text: t('help.schedulingTeamArticles.whenTheWorkIs') },
        ],
        whatHappensNext: t('help.schedulingTeamArticles.officeUsersSeeThe'),
      },
      {
        slug: 'track-time-on-a-visit',
        title: t('help.schedulingTeamArticles.trackTimeOnA'),
        description: t('help.schedulingTeamArticles.startPauseAndFinish'),
        icon: 'pi-stopwatch',
        introduction: t('help.schedulingTeamArticles.fieldopsMeasuresTheWork'),
        callout: t('help.schedulingTeamArticles.manualTimeEntriesAre'),
        steps: [
          { text: t('help.schedulingTeamArticles.theTechnicianSelectsStart') },
          {
            text: t('help.schedulingTeamArticles.theBannerShowsIn'),
          },
          { text: t('help.schedulingTeamArticles.aPausedVisitShows') },
          {
            text: t('help.schedulingTeamArticles.selectReviewCompleteThe'),
          },
        ],
        whatHappensNext: t('help.schedulingTeamArticles.theWorkTimeIs'),
      },
      {
        slug: 'use-the-schedule-calendar',
        title: t('help.schedulingTeamArticles.useTheScheduleCalendar'),
        description: t('help.schedulingTeamArticles.reviewTeamWorkloadBy'),
        icon: 'pi-calendar',
        introduction: t('help.schedulingTeamArticles.theDispatchCalendarShows'),
        callout: t('help.schedulingTeamArticles.mapViewIsNot'),
        steps: [
          { text: t('help.schedulingTeamArticles.selectScheduleInThe2') },
          { text: t('help.schedulingTeamArticles.useDayOrWeek') },
          {
            text: t('help.schedulingTeamArticles.useFiltersToNarrow'),
          },
          {
            text: t('help.schedulingTeamArticles.useTheLegendTo'),
          },
          { text: t('help.schedulingTeamArticles.selectAVisitTo') },
        ],
        whatHappensNext: t('help.schedulingTeamArticles.anyChangeYouSave'),
      },
    ],
  },
]);

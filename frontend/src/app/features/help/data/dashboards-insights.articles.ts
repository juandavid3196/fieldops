import { HelpSection } from '../models/help.model';

import { localizedList, t } from '../../../core/i18n/translate';
/** Dashboards and insights articles. Content describes only behavior FieldOps implements (BR-10). */
export const DASHBOARDS_INSIGHTS_SECTIONS: readonly HelpSection[] = localizedList(() => [
  {
    label: t('help.dashboardsInsightsArticles.dashboards'),
    articles: [
      {
        slug: 'read-the-operations-dashboard',
        title: t('help.dashboardsInsightsArticles.readTheOperationsDashboard'),
        description: t('help.dashboardsInsightsArticles.monitorTodaysVisitsAnd'),
        icon: 'pi-th-large',
        introduction: t('help.dashboardsInsightsArticles.theOperationsDashboardShows'),
        callout: t('help.dashboardsInsightsArticles.ownerOperationsManagerAnd'),
        steps: [
          {
            text: t('help.dashboardsInsightsArticles.chooseABranchAnd'),
          },
          {
            text: t('help.dashboardsInsightsArticles.readTheKeyFigures'),
          },
          { text: t('help.dashboardsInsightsArticles.reviewTheVisitList') },
          { text: t('help.dashboardsInsightsArticles.checkTechnicianAvailabilityTo') },
          {
            text: t('help.dashboardsInsightsArticles.scrollToEstimatedVs'),
          },
          {
            text: t('help.dashboardsInsightsArticles.selectOpenDispatchBoard'),
          },
        ],
        whatHappensNext: t('help.dashboardsInsightsArticles.theLinksInEach'),
      },
      {
        slug: 'read-the-business-overview',
        title: t('help.dashboardsInsightsArticles.readTheBusinessOverview'),
        description: t('help.dashboardsInsightsArticles.followRevenuePipelineAnd'),
        icon: 'pi-chart-line',
        introduction: t('help.dashboardsInsightsArticles.theBusinessOverviewSummarizes'),
        callout: t('help.dashboardsInsightsArticles.theBusinessViewIs'),
        steps: [
          {
            text: t('help.dashboardsInsightsArticles.chooseABranchAnd2'),
          },
          {
            text: t('help.dashboardsInsightsArticles.turnOnCompareTo'),
          },
          { text: t('help.dashboardsInsightsArticles.readTheKeyFigures2') },
          {
            text: t('help.dashboardsInsightsArticles.reviewSalesPipelineBranch'),
          },
          { text: t('help.dashboardsInsightsArticles.checkOwnerAttentionFor') },
          { text: t('help.dashboardsInsightsArticles.selectExportReportTo') },
        ],
        whatHappensNext: t('help.dashboardsInsightsArticles.profitAndProfitabilityFigures'),
      },
      {
        slug: 'use-the-overview-page',
        title: t('help.dashboardsInsightsArticles.useTheOverviewPage'),
        description: t('help.dashboardsInsightsArticles.knowWhatYouSee'),
        icon: 'pi-home',
        introduction: t('help.dashboardsInsightsArticles.rolesWithoutADashboard'),
        callout: t('help.dashboardsInsightsArticles.accountingAndViewerRoles'),
        steps: [
          { text: t('help.dashboardsInsightsArticles.signInToOpen') },
          { text: t('help.dashboardsInsightsArticles.readYourOrganizationAnd') },
          {
            text: t('help.dashboardsInsightsArticles.useTheSidebarTo'),
          },
        ],
        whatHappensNext: t('help.dashboardsInsightsArticles.overviewHasNoCharts'),
      },
    ],
  },
  {
    label: t('help.dashboardsInsightsArticles.reportsActivity'),
    articles: [
      {
        slug: 'use-the-reports-workspace',
        title: t('help.dashboardsInsightsArticles.useTheReportsWorkspace'),
        description: t('help.dashboardsInsightsArticles.filterCompareAndExport'),
        icon: 'pi-chart-bar',
        introduction: t('help.dashboardsInsightsArticles.reportsLetsYouPick'),
        callout: t('help.dashboardsInsightsArticles.ownerOperationsManagerDispatcher'),
        steps: [
          {
            text: t('help.dashboardsInsightsArticles.openReportsAndChoose'),
          },
          {
            text: t('help.dashboardsInsightsArticles.setTheDateRange'),
          },
          {
            text: t('help.dashboardsInsightsArticles.useTheFiltersUnder'),
          },
          {
            text: t('help.dashboardsInsightsArticles.readTheSummaryFigures'),
          },
          { text: t('help.dashboardsInsightsArticles.selectTheStarTo') },
          {
            text: t('help.dashboardsInsightsArticles.selectExportCsvOr'),
          },
        ],
        whatHappensNext: t('help.dashboardsInsightsArticles.invoiceAgingReflectsToday'),
      },
      {
        slug: 'review-the-audit-log',
        title: t('help.dashboardsInsightsArticles.reviewTheAuditLog'),
        description: t('help.dashboardsInsightsArticles.traceWhoChangedWhat'),
        icon: 'pi-history',
        introduction: t('help.dashboardsInsightsArticles.theAuditLogRecords'),
        callout: t('help.dashboardsInsightsArticles.theAuditLogIs'),
        steps: [
          { text: t('help.dashboardsInsightsArticles.selectAuditLogIn') },
          {
            text: t('help.dashboardsInsightsArticles.readTheSummaryCards'),
          },
          {
            text: t('help.dashboardsInsightsArticles.searchByUserEntity'),
          },
          { text: t('help.dashboardsInsightsArticles.sortByTimestampOr') },
          {
            text: t('help.dashboardsInsightsArticles.selectViewOnA'),
          },
          { text: t('help.dashboardsInsightsArticles.selectExportCsvTo') },
        ],
        whatHappensNext: t('help.dashboardsInsightsArticles.inTheEventDetails'),
      },
      {
        slug: 'follow-notifications',
        title: t('help.dashboardsInsightsArticles.followNotifications'),
        description: t('help.dashboardsInsightsArticles.stayOnTopOf'),
        icon: 'pi-bell',
        introduction: t('help.dashboardsInsightsArticles.notificationsTellsYouAbout'),
        callout: t('help.dashboardsInsightsArticles.notificationsAreAvailableTo'),
        steps: [
          {
            text: t('help.dashboardsInsightsArticles.selectTheBellIcon'),
          },
          {
            text: t('help.dashboardsInsightsArticles.chooseAllNotificationsOr'),
          },
          {
            text: t('help.dashboardsInsightsArticles.selectANotificationTo'),
          },
          { text: t('help.dashboardsInsightsArticles.useTheRowMenu') },
          { text: t('help.dashboardsInsightsArticles.selectMarkAllAs') },
        ],
        whatHappensNext: t('help.dashboardsInsightsArticles.theBellShowsThe'),
      },
    ],
  },
]);

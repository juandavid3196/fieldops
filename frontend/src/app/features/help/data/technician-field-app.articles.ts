import { HelpSection } from '../models/help.model';

import { localizedList, t } from '../../../core/i18n/translate';
/** Technician field app articles. Content describes only behavior FieldOps implements (BR-10). */
export const TECHNICIAN_FIELD_APP_SECTIONS: readonly HelpSection[] = localizedList(() => [
  {
    label: t('help.technicianFieldAppArticles.todaysJobs'),
    articles: [
      {
        slug: 'use-todays-jobs',
        title: t('help.technicianFieldAppArticles.useTodaysJobs'),
        description: t('help.technicianFieldAppArticles.reviewYourRouteAnd'),
        icon: 'pi-briefcase',
        introduction: t('help.technicianFieldAppArticles.todaysJobsIsThe'),
        callout: t('help.technicianFieldAppArticles.theBottomBarAlso'),
        steps: [
          {
            text: t('help.technicianFieldAppArticles.readTheSummaryAt'),
          },
          {
            text: t('help.technicianFieldAppArticles.inTheNextCard'),
          },
          { text: t('help.technicianFieldAppArticles.selectCallToPhone') },
          { text: t('help.technicianFieldAppArticles.selectStartTravelWhen') },
          { text: t('help.technicianFieldAppArticles.selectViewJobDetails') },
          { text: t('help.technicianFieldAppArticles.useTheRefreshIcon') },
        ],
        whatHappensNext: t('help.technicianFieldAppArticles.afterStartTravelThe'),
      },
      {
        slug: 'open-a-job-and-read-its-details',
        title: t('help.technicianFieldAppArticles.openAJobAnd'),
        description: t('help.technicianFieldAppArticles.checkAccessScopeAnd'),
        icon: 'pi-file',
        introduction: t('help.technicianFieldAppArticles.theJobPageGathers'),
        callout: t('help.technicianFieldAppArticles.onlyThePrimaryTechnician'),
        steps: [
          {
            text: t('help.technicianFieldAppArticles.checkTheProgressSteps'),
          },
          {
            text: t('help.technicianFieldAppArticles.reviewTheJobHeader'),
          },
          {
            text: t('help.technicianFieldAppArticles.readAccessDetailsFor'),
          },
          {
            text: t('help.technicianFieldAppArticles.useTheOverviewTab'),
          },
          {
            text: t('help.technicianFieldAppArticles.useTheTasksMaterials'),
          },
        ],
        whatHappensNext: t('help.technicianFieldAppArticles.tasksMaterialsAndPhotos'),
      },
      {
        slug: 'use-the-technician-menu',
        title: t('help.technicianFieldAppArticles.useTheTechnicianMenu'),
        description: t('help.technicianFieldAppArticles.reachYourProfileAccount'),
        icon: 'pi-user',
        introduction: t('help.technicianFieldAppArticles.theTechnicianMenuIs'),
        callout: t('help.technicianFieldAppArticles.thereIsNoPage'),
        steps: [
          { text: t('help.technicianFieldAppArticles.selectMoreInThe') },
          { text: t('help.technicianFieldAppArticles.selectMyProfileTo') },
          { text: t('help.technicianFieldAppArticles.selectAccountSettingsTo') },
          { text: t('help.technicianFieldAppArticles.selectHelpSupportTo') },
          {
            text: t('help.technicianFieldAppArticles.selectSignOutWhen'),
          },
        ],
        whatHappensNext: t('help.technicianFieldAppArticles.selectTodayInThe'),
      },
    ],
  },
  {
    label: t('help.technicianFieldAppArticles.onSite'),
    articles: [
      {
        slug: 'travel-arrive-and-start-a-job',
        title: t('help.technicianFieldAppArticles.travelArriveAndStart'),
        description: t('help.technicianFieldAppArticles.moveAVisitFrom'),
        icon: 'pi-map-marker',
        introduction: t('help.technicianFieldAppArticles.threeButtonsMoveA'),
        callout: t('help.technicianFieldAppArticles.startJobStaysDisabled'),
        steps: [
          {
            text: t('help.technicianFieldAppArticles.selectStartTravelOn'),
          },
          {
            text: t('help.technicianFieldAppArticles.whenYouReachThe'),
          },
          {
            text: t('help.technicianFieldAppArticles.selectStartJobTo'),
          },
        ],
        whatHappensNext: t('help.technicianFieldAppArticles.theJobPageSwitches'),
      },
      {
        slug: 'complete-tasks-and-add-materials',
        title: t('help.technicianFieldAppArticles.completeTasksAndAdd'),
        description: t('help.technicianFieldAppArticles.trackTheChecklistAnd'),
        icon: 'pi-check-square',
        introduction: t('help.technicianFieldAppArticles.whileTheJobIs'),
        callout: t('help.technicianFieldAppArticles.onlyThePrimaryTechnician2'),
        steps: [
          {
            text: t('help.technicianFieldAppArticles.tickEachTaskIn'),
          },
          { text: t('help.technicianFieldAppArticles.selectTheCommentIcon') },
          { text: t('help.technicianFieldAppArticles.selectAddTaskTo') },
          {
            text: t('help.technicianFieldAppArticles.inMaterialsUsedCompare'),
          },
          {
            text: t('help.technicianFieldAppArticles.selectAddMaterialThen'),
          },
        ],
        whatHappensNext: t('help.technicianFieldAppArticles.thePageShowsWhether'),
      },
      {
        slug: 'add-photos-and-notes',
        title: t('help.technicianFieldAppArticles.addPhotosAndNotes'),
        description: t('help.technicianFieldAppArticles.recordBeforeAndAfter'),
        icon: 'pi-camera',
        introduction: t('help.technicianFieldAppArticles.photosAndNotesDocument'),
        callout: t('help.technicianFieldAppArticles.photosMustBeJpeg'),
        steps: [
          { text: t('help.technicianFieldAppArticles.inPhotosEvidenceSelect') },
          {
            text: t('help.technicianFieldAppArticles.chooseBeforeOrAfter'),
          },
          { text: t('help.technicianFieldAppArticles.selectAPhotoTo') },
          {
            text: t('help.technicianFieldAppArticles.writeInTechnicianNotes'),
          },
          {
            text: t('help.technicianFieldAppArticles.notesSaveAutomaticallyWhen'),
          },
        ],
        whatHappensNext: t('help.technicianFieldAppArticles.whenBothBeforeAnd'),
      },
      {
        slug: 'pause-resume-or-report-an-issue',
        title: t('help.technicianFieldAppArticles.pauseResumeOrReport'),
        description: t('help.technicianFieldAppArticles.handleBreaksAndProblems'),
        icon: 'pi-pause',
        introduction: t('help.technicianFieldAppArticles.usePauseForA'),
        callout: t('help.technicianFieldAppArticles.issueReportingIsComing'),
        steps: [
          {
            text: t('help.technicianFieldAppArticles.selectPauseInThe'),
          },
          { text: t('help.technicianFieldAppArticles.selectResumeToContinue') },
          {
            text: t('help.technicianFieldAppArticles.selectReportIssueIn'),
          },
          { text: t('help.technicianFieldAppArticles.selectCallOfficeTo') },
        ],
        whatHappensNext: t('help.technicianFieldAppArticles.theTimeSummaryShows'),
      },
    ],
  },
  {
    label: t('help.technicianFieldAppArticles.completeTheJob'),
    articles: [
      {
        slug: 'review-and-complete-a-job',
        title: t('help.technicianFieldAppArticles.reviewAndCompleteA'),
        description: t('help.technicianFieldAppArticles.confirmTheWorkAnd'),
        icon: 'pi-check-circle',
        introduction: t('help.technicianFieldAppArticles.reviewCompleteConfirmsThe'),
        callout: t('help.technicianFieldAppArticles.reviewCompleteStaysDisabled'),
        steps: [
          { text: t('help.technicianFieldAppArticles.selectReviewCompleteOn') },
          {
            text: t('help.technicianFieldAppArticles.checkTheCompletionChecklist'),
          },
          { text: t('help.technicianFieldAppArticles.whenThePageSays') },
          {
            text: t('help.technicianFieldAppArticles.chooseAMethodSignature'),
          },
          {
            text: t('help.technicianFieldAppArticles.fillTheFieldsShown'),
          },
          { text: t('help.technicianFieldAppArticles.selectCompleteJob') },
        ],
        whatHappensNext: t('help.technicianFieldAppArticles.aSignatureConfirmsService'),
      },
    ],
  },
]);

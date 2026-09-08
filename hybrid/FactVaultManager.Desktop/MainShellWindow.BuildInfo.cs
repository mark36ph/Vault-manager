using System;
using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    public const int CurrentBuildNumber = 216;

    private static readonly bool BuildInfoUiRegistered = RegisterBuildInfoUi();
    private bool _deferredShellInitializationScheduled;

    private static bool RegisterBuildInfoUi()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindowBuildInfo_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void MainShellWindowBuildInfo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainShellWindow window)
        {
            using var perf = PerformanceDiagnostics.Measure("Startup.Loaded");
            window.Title = $"Factburst Quiz Manager • Build {CurrentBuildNumber}";

            using (PerformanceDiagnostics.Measure("Startup.Loaded.SettingsWorkflow"))
                window.InitializeSettingsWorkflow();
            using (PerformanceDiagnostics.Measure("Startup.Loaded.ApiConnections"))
            {
                window.InitializeApiConnectionsSettings();
                window.InitializeApiConnectionsWebsite();
            }
            using (PerformanceDiagnostics.Measure("Startup.Loaded.QuizCleanup"))
                window.InitializeQuizOnlyCleanup();
            using (PerformanceDiagnostics.Measure("Startup.Loaded.InstagramPromo"))
                window.InitializeInstagramPromoApprovalUi();
            using (PerformanceDiagnostics.Measure("Startup.Loaded.PerformanceDiagnosticsUi"))
                window.InitializePerformanceDiagnosticsUi();

            PerformanceDiagnostics.ClearStartupProfileRequest();

            if (!window._deferredShellInitializationScheduled)
            {
                window._deferredShellInitializationScheduled = true;
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(window.InitializeDeferredShellFeatures));
            }
        }
    }

    private void QueueDeferredShellPhase(Action phase)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                using var perf = PerformanceDiagnostics.Measure($"Startup.DeferredStep.{phase.Method.Name}");
                phase();
            }));
    }

    private void InitializeDeferredShellFeatures()
    {
        QueueDeferredShellPhase(InitializeDeferredQuizPhase);
    }

    private void InitializeDeferredQuizPhase()
    {
        QueueDeferredShellPhase(FinalizeApiConnectionsYouTubeButton);
        QueueDeferredShellPhase(InitializeFinalVideoLabelSync);
        QueueDeferredShellPhase(InitializeQuizBatchButtonSync);
        QueueDeferredShellPhase(InitializeQuizYouTubePackagingMenuSync);
        QueueDeferredShellPhase(InitializeYouTubeUploadPackageUi);
        QueueDeferredShellPhase(InitializeUploadManagerYouTubeStatusSync);
        QueueDeferredShellPhase(InitializeUnifiedPublicationStateUi);
        QueueDeferredShellPhase(InitializeScheduledPromoPublishingBatchForApp);
        QueueDeferredShellPhase(InitializeScheduledRelatedVideoGuideForApp);
        QueueDeferredShellPhase(InitializeScheduledWebsitePublishingLayoutSafeForApp);
        QueueDeferredShellPhase(InitializeYouTubeGrowthAnalyticsUiReliably);
        QueueDeferredShellPhase(InitializeYouTubeGrowthRecommendationGuard);
        QueueDeferredShellPhase(InitializeYouTubeFirstCommentAutopilot);
        QueueDeferredShellPhase(InitializeDeferredAutopilotPhase);
    }

    private void InitializeDeferredAutopilotPhase()
    {
        QueueDeferredShellPhase(InitializeAutopilotFirstUi);
        QueueDeferredShellPhase(InitializeAutopilotMasterUi);
        QueueDeferredShellPhase(InitializeAutopilotNeedsYouTaskQueue);
        QueueDeferredShellPhase(InitializeAutopilotNeedsYouAlignedQueue);
        QueueDeferredShellPhase(InitializeAutopilotGuidedNeedsYou);
        QueueDeferredShellPhase(InitializeDeferredWebsitePhase);
    }

    private void InitializeDeferredWebsitePhase()
    {
        QueueDeferredShellPhase(InitializeWebsiteManagerPage);
        QueueDeferredShellPhase(InitializeWebsiteYouTubeScheduleSync);
        QueueDeferredShellPhase(InitializeWebsiteVisibilityControls);
        QueueDeferredShellPhase(InitializeWebsiteUsersPage);
        QueueDeferredShellPhase(InitializeWebsiteAnalyticsPage);
        QueueDeferredShellPhase(InitializeWebsiteUserProvisioningControls);
        QueueDeferredShellPhase(InitializeWebsiteUsersFriendsPanel);
        QueueDeferredShellPhase(InitializeWebsiteMaintenancePlacement);
        QueueDeferredShellPhase(InitializeWebsiteAdministrationEnhancements);
        QueueDeferredShellPhase(InitializeWebsiteNavigationDivider);
        QueueDeferredShellPhase(InitializeWebsiteAdsSettings);
        QueueDeferredShellPhase(InitializeWebsiteSettingsShortcut);
        QueueDeferredShellPhase(InitializeWebsiteCommentModerationNavigation);
        QueueDeferredShellPhase(InitializeWebsiteSeoAuditPage);
        QueueDeferredShellPhase(InitializeWebsiteSeoAutoFixButton);
        QueueDeferredShellPhase(InitializeLogoQuizPromoArtworkRepair);
        QueueDeferredShellPhase(InitializeDeferredHistoryAndMaintenancePhase);
    }

    private void InitializeDeferredHistoryAndMaintenancePhase()
    {
        QueueDeferredShellPhase(InitializeAutopilotNeedsYouCountSync);
        QueueDeferredShellPhase(InitializeInstagramPromoFollowup);
        QueueDeferredShellPhase(InitializeAutopilotNeedsYouVisualStability);
        QueueDeferredShellPhase(InitializeAutopilotShellActivationFix);
        // Autopilot schedule supervision is opt-in. Do not initialize its timer during startup.
        // The master switch initializes it only after the user explicitly turns Autopilot on.
        QueueDeferredShellPhase(InitializeQuizHistoryBulkArchiveUi);
        QueueDeferredShellPhase(InitializeQuizHistoryGroupedBulkArchiveUi);
        QueueDeferredShellPhase(InitializeQuizHistoryUiCleanup);
        QueueDeferredShellPhase(InitializeQuizContentLifecycleUi);
        QueueDeferredShellPhase(InitializeLibraryPublicationStatusUi);
        QueueDeferredShellPhase(InitializeLibraryPlatformStatusFix);
        QueueDeferredShellPhase(InitializeLibraryPlatformSymbolFix);
        QueueDeferredShellPhase(InitializeStartupSafeUiCleanup);
        QueueDeferredShellPhase(InitializeCreateAdvancedUiCleanup);
        QueueDeferredShellPhase(InitializeDatabaseBackupAndRecovery);
        // Build the History page only after the shell and sidebar are responsive.
        QueueDeferredShellPhase(InitializeQuizHistoryPage);
    }
}

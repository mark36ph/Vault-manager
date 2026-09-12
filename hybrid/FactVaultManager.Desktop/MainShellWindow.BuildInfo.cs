using System;
using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    // Version/build values are owned by version.json; this compatibility property
    // keeps existing UI and feature code using CurrentBuildNumber without duplicating data.
    public static int CurrentBuildNumber => AppVersion.BuildNumber;

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
        QueueDeferredShellPhase(InitializeUploadManagerPage);
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
        // Keep only desktop operations that are still needed for publishing.
        // Website administration, moderation, SEO, analytics, ads and maintenance now live on the website.
        QueueDeferredShellPhase(InitializeWebsiteYouTubeScheduleSync);
        QueueDeferredShellPhase(InitializeWebsiteVisibilityControls);
        QueueDeferredShellPhase(InitializeLogoQuizPromoArtworkRepair);
        QueueDeferredShellPhase(InitializeDeferredHistoryAndMaintenancePhase);
    }

    private void InitializeDeferredHistoryAndMaintenancePhase()
    {
        QueueDeferredShellPhase(InitializeAutopilotNeedsYouCountSync);
        QueueDeferredShellPhase(InitializeInstagramPromoFollowup);
        QueueDeferredShellPhase(InitializeAutopilotNeedsYouVisualStability);
        QueueDeferredShellPhase(InitializeAutopilotShellActivationFix);
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
        QueueDeferredShellPhase(InitializeQuizHistoryPage);
    }
}

using System;
using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    public const int CurrentBuildNumber = 180;

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
            window.InitializeSettingsWorkflow();
            window.InitializeApiConnectionsSettings();
            window.InitializeApiConnectionsWebsite();
            window.InitializeQuizOnlyCleanup();
            window.InitializeInstagramPromoApprovalUi();

            if (!window._deferredShellInitializationScheduled)
            {
                window._deferredShellInitializationScheduled = true;
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(window.InitializeDeferredShellFeatures));
            }
        }
    }

    private void QueueDeferredShellPhase(Action phase)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            phase);
    }

    private void InitializeDeferredShellFeatures()
    {
        using var perf = PerformanceDiagnostics.Measure("Startup.DeferredShellFeatures");
        InitializeDeferredQuizPhase();
        QueueDeferredShellPhase(InitializeDeferredAutopilotPhase);
    }

    private void InitializeDeferredQuizPhase()
    {
        using var perf = PerformanceDiagnostics.Measure("Startup.DeferredQuizPhase");
        FinalizeApiConnectionsYouTubeButton();
        InitializeFinalVideoLabelSync();
        InitializeQuizBatchButtonSync();
        InitializeQuizYouTubePackagingMenuSync();
        InitializeYouTubeUploadPackageUi();
        InitializeUploadManagerYouTubeStatusSync();
        InitializeUnifiedPublicationStateUi();
        InitializeScheduledPromoPublishingBatchForApp();
        InitializeScheduledRelatedVideoGuideForApp();
        InitializeScheduledWebsitePublishingLayoutSafeForApp();
        InitializeYouTubeGrowthAnalyticsUiReliably();
        InitializeYouTubeGrowthRecommendationGuard();
        InitializeYouTubeFirstCommentAutopilot();
    }

    private void InitializeDeferredAutopilotPhase()
    {
        using var perf = PerformanceDiagnostics.Measure("Startup.DeferredAutopilotPhase");
        InitializeFullAutopilot();
        InitializeAutopilotFirstUi();
        InitializeAutopilotMasterUi();
        InitializeAutopilotNeedsYouTaskQueue();
        InitializeAutopilotNeedsYouAlignedQueue();
        InitializeAutopilotGuidedNeedsYou();
        QueueDeferredShellPhase(InitializeDeferredWebsitePhase);
    }

    private void InitializeDeferredWebsitePhase()
    {
        using var perf = PerformanceDiagnostics.Measure("Startup.DeferredWebsitePhase");
        InitializeWebsiteManagerPage();
        InitializeWebsiteYouTubeScheduleSync();
        InitializeWebsiteVisibilityControls();
        InitializeWebsiteUsersPage();
        InitializeWebsiteAnalyticsPage();
        InitializeWebsiteUserProvisioningControls();
        InitializeWebsiteUsersFriendsPanel();
        InitializeWebsiteMaintenancePlacement();
        InitializeWebsiteAdministrationEnhancements();
        InitializeWebsiteNavigationDivider();
        InitializeWebsiteAdsSettings();
        InitializeWebsiteSettingsShortcut();
        InitializeWebsiteCommentModerationNavigation();
        InitializeWebsiteSeoAuditPage();
        InitializeWebsiteSeoAutoFixButton();
        InitializeLogoQuizPromoArtworkRepair();
        QueueDeferredShellPhase(InitializeDeferredHistoryAndMaintenancePhase);
    }

    private void InitializeDeferredHistoryAndMaintenancePhase()
    {
        using var perf = PerformanceDiagnostics.Measure("Startup.DeferredHistoryAndMaintenancePhase");
        InitializeAutopilotNeedsYouCountSync();
        InitializeInstagramPromoFollowup();
        InitializeAutopilotNeedsYouVisualStability();
        InitializeAutopilotShellActivationFix();
        InitializeAutopilotScheduleTarget();
        InitializeQuizHistoryBulkArchiveUi();
        InitializeQuizHistoryGroupedBulkArchiveUi();
        InitializeQuizHistoryUiCleanup();
        InitializeQuizContentLifecycleUi();
        InitializeLibraryPublicationStatusUi();
        InitializeLibraryPlatformStatusFix();
        InitializeLibraryPlatformSymbolFix();
        InitializeStartupSafeUiCleanup();
        InitializeCreateAdvancedUiCleanup();
        InitializeDatabaseBackupAndRecovery();
    }
}

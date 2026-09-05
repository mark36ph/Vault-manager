using System;
using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    public const int CurrentBuildNumber = 173;

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
        if (sender is not MainShellWindow window || window._deferredShellInitializationScheduled)
            return;

        window._deferredShellInitializationScheduled = true;
        window.Title = $"Factburst Quiz Manager • Build {CurrentBuildNumber}";
        window.InitializeSettingsWorkflow();
        window.InitializeApiConnectionsSettings();
        window.InitializeApiConnectionsWebsite();
        window.InitializeQuizOnlyCleanup();
        window.InitializeInstagramPromoApprovalUi();
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(window.InitializeDeferredShellFeatures));
    }

    private void InitializeDeferredShellFeatures()
    {
        InitializeDeferredQuizPhase();
        QueueDeferredShellPhase(InitializeDeferredAutopilotPhase);
    }

    private void InitializeDeferredQuizPhase()
    {
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
        InitializeWebsiteNavigation();
        InitializeWebsiteSettings();
        InitializeWebsitePages();
        InitializeWebsiteContent();
        InitializeWebsiteSeo();
        InitializeWebsiteAnalytics();
        InitializeWebsiteUsers();
        InitializeWebsiteComments();
        InitializeWebsiteAdvanced();
        QueueDeferredShellPhase(InitializeDeferredHistoryAndMaintenancePhase);
    }

    private void InitializeDeferredHistoryAndMaintenancePhase()
    {
        InitializeAutopilotNeedsYouCountSync();
        InitializeInstagramPromoFollowup();
        InitializeAutopilotNeedsYouVisualStability();
        InitializeAutopilotShellActivationFix();
        InitializeAutopilotScheduleTarget();
        InitializeQuizHistory();
        InitializeQuizLifecycle();
        InitializeLibraryStatusFixes();
        InitializeStartupSafeCleanup();
        InitializeCreateAdvancedCleanup();
        InitializeDatabaseBackupRecovery();
    }

    private void QueueDeferredShellPhase(Action phase)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                phase();
            }));
    }
}

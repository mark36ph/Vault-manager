using System;
using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    public const int CurrentBuildNumber = 166;

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
            window.Title = $"Factburst Quiz Manager • Build {CurrentBuildNumber}";
            window.InitializeSettingsWorkflow();
            window.InitializeApiConnectionsSettings();
            window.InitializeApiConnectionsWebsite();
            window.InitializeQuizOnlyCleanup();

            if (!window._deferredShellInitializationScheduled)
            {
                window._deferredShellInitializationScheduled = true;
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(window.InitializeDeferredShellFeatures));
            }
        }
    }

    private void InitializeDeferredShellFeatures()
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
        InitializeFullAutopilot();
        InitializeAutopilotFirstUi();
        InitializeAutopilotMasterUi();
        InitializeAutopilotNeedsYouTaskQueue();
        InitializeAutopilotNeedsYouAlignedQueue();
        InitializeAutopilotGuidedNeedsYou();
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

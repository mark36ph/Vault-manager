using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    public const int CurrentBuildNumber = 145;

    private static readonly bool BuildInfoUiRegistered = RegisterBuildInfoUi();

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
            window.FinalizeApiConnectionsYouTubeButton();
            window.InitializeFinalVideoLabelSync();
            window.InitializeQuizBatchButtonSync();
            window.InitializeQuizYouTubePackagingMenuSync();
            window.InitializeYouTubeUploadPackageUi();
            window.InitializeUploadManagerYouTubeStatusSync();
            window.InitializeUnifiedPublicationStateUi();
            window.InitializeScheduledPromoPublishingBatchForApp();
            window.InitializeScheduledRelatedVideoGuideForApp();
            window.InitializeScheduledWebsitePublishingLayoutSafeForApp();
            window.InitializeYouTubeGrowthAnalyticsUiReliably();
            window.InitializeYouTubeGrowthRecommendationGuard();
            window.InitializeYouTubeFirstCommentAutopilot();
            window.InitializeFullAutopilot();
            window.InitializeAutopilotFirstUi();
            window.InitializeAutopilotMasterUi();
            window.InitializeAutopilotNeedsYouTaskQueue();
            window.InitializeAutopilotNeedsYouAlignedQueue();
            window.InitializeAutopilotGuidedNeedsYou();
            window.InitializeWebsiteManagerPage();
            window.InitializeWebsiteYouTubeScheduleSync();
            window.InitializeWebsiteVisibilityControls();
            window.InitializeWebsiteUsersPage();
            window.InitializeWebsiteAnalyticsPage();
            window.InitializeWebsiteUserProvisioningControls();
            window.InitializeWebsiteUsersFriendsPanel();
            window.InitializeWebsiteMaintenancePlacement();
            window.InitializeWebsiteAdministrationEnhancements();
            window.InitializeWebsiteNavigationDivider();
            window.InitializeWebsiteAdsSettings();
            window.InitializeWebsiteSettingsShortcut();
            window.InitializeWebsiteCommentModerationNavigation();
            window.InitializeWebsiteSeoAuditPage();
            window.InitializeWebsiteSeoAutoFixButton();
            window.InitializeLogoQuizPromoArtworkRepair();
            window.InitializeAutopilotNeedsYouCountSync();
            window.InitializeInstagramPromoFollowup();
            window.InitializeAutopilotNeedsYouVisualStability();
            window.InitializeAutopilotShellActivationFix();
            window.InitializeAutopilotScheduleTarget();
            window.InitializeQuizHistoryBulkArchiveUi();
            window.InitializeQuizHistoryGroupedBulkArchiveUi();
            window.InitializeQuizHistoryUiCleanup();
            window.InitializeQuizContentLifecycleUi();
            window.InitializeLibraryPublicationStatusUi();
            window.InitializeLibraryPlatformStatusFix();
            window.InitializeLibraryPlatformSymbolFix();
            window.InitializeStartupSafeUiCleanup();
            window.InitializeCreateAdvancedUiCleanup();
            window.InitializeDatabaseBackupAndRecovery();
        }
    }
}

using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    public const int CurrentBuildNumber = 85;

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
            window.Title = $"FactVaultManager • Build {CurrentBuildNumber}";
            window.InitializeFinalVideoLabelSync();
            window.InitializeQuizBatchButtonSync();
            window.InitializeQuizYouTubePackagingMenuSync();
            window.InitializeYouTubeUploadPackageUi();
            window.InitializeUploadManagerYouTubeStatusSync();
            window.InitializeScheduledPromoPublishingBatchForApp();
            window.InitializeScheduledRelatedVideoGuideForApp();
            window.InitializeScheduledWebsitePublishingLayoutSafeForApp();
            window.InitializeYouTubeGrowthAnalyticsUiReliably();
            window.InitializeYouTubeGrowthRecommendationGuard();
            window.InitializeYouTubeFirstCommentAutopilot();
            window.InitializeFullAutopilot();
            window.InitializeAutopilotFirstUi();
            window.InitializeAutopilotNeedsYouTaskQueue();
            window.InitializeAutopilotNeedsYouAlignedQueue();
            window.InitializeWebsiteManagerPage();
            window.InitializeWebsiteVisibilityControls();
            window.InitializeWebsiteUsersPage();
            window.InitializeWebsiteUserProvisioningControls();
            window.InitializeWebsiteUsersFriendsPanel();
            window.InitializeWebsiteMaintenancePlacement();
            window.InitializeWebsiteAdministrationEnhancements();
            window.InitializeWebsiteNavigationDivider();
            window.InitializeWebsiteAdsSettings();
            window.InitializeWebsiteSettingsShortcut();
            window.InitializeWebsiteCommentModerationNavigation();
            window.InitializeLogoQuizPromoArtworkRepair();
            window.InitializeAutopilotNeedsYouCountSync();
            window.InitializeAutopilotShellActivationFix();
            window.InitializeAutopilotRecoverySupervisor();
            window.InitializeAutopilotScheduleTarget();
            window.InitializeQuizHistoryManualArchiveUi();
            window.InitializeQuizHistoryArchiveMatcherUi();
        }
    }
}

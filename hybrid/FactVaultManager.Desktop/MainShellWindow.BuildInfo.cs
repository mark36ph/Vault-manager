using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    public const int CurrentBuildNumber = 43;

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
        }
    }
}

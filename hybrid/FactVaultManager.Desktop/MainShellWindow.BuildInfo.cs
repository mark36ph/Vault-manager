namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const int CurrentBuildNumber = 150;

    private void InitializeCurrentBuild()
    {
        InitializeSettingsWorkflow();
        InitializeApiConnectionsSettings();
        InitializeApiConnectionsWebsite();
        InitializeApiConnectionsYouTubeButton();
        InitializeSettingsUiPolish();
        InitializeQuizWorkflow();
        InitializeQuizQuestionBankPage();
        InitializeQuizQuestionBankBulkActions();
        InitializeQuizQuestionImport();
        InitializeQuizHistory();
        InitializeQuizNotes();
        InitializeQuizLibrary();
        InitializeQuizArchiveDiagnosticsUi();
        InitializeQuizProjectIdentity();
        InitializeQuizAnalytics();
        InitializeAutopilot();
        InitializeWebsiteSettingsAdministration();
        InitializeWebsiteReportedQuestions();
        InitializeWebsiteUsers();
        InitializeWebsiteCategoryManagement();
        InitializeWebsiteRewards();
        InitializeWebsiteInviteManagement();
        InitializeYouTubeUpload();
        InitializePublicationStatus();
        InitializeInstagramManager();
        InitializeFacebookManager();
        InitializeYouTubeManager();
        InitializeCreateAdvancedUiCleanup();
        InitializeDailyUiCleanup();
        InitializeQuizHistoryUiCleanup();
        InitializeQuizOnlyCleanup();
        InitializeStartupUiCleanupHotfix();
        InitializeQuizQuestionBankUiCleanup();
        InitializeQuizAnalyticsUiCleanup();
        InitializeAutopilotShellActivationFix();
        InitializeAutopilotNeedsYou();
        InitializeAutopilotNeedsYouUiCleanup();
        InitializeInstagramPageCleanup();
        InitializeUploadManagerUiCleanup();
        InitializeYouTubeManagerUiCleanup();
        InitializeFacebookManagerUiCleanup();
        InitializeWebsiteSettingsUiCleanup();
        InitializeWebsiteManagementUiCleanup();
        InitializeQuizHistoryPageCleanup();
        InitializeQuizLibraryPageCleanup();
        InitializeQuizQuestionBankPageCleanup();
        InitializeQuizWorkflowPageCleanup();
        InitializeQuizAnalyticsPageCleanup();
        InitializeSettingsPageCleanup();
        InitializeMainShellUiCleanup();
    }
}

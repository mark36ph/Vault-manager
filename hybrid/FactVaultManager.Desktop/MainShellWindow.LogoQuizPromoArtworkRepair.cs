using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _logoQuizPromoArtworkRepairInitialized;
    private bool _logoQuizPromoArtworkRepairBusy;
    private DispatcherTimer? _logoQuizPromoArtworkRepairTimer;
    private readonly Dictionary<string, long> _logoQuizPromoArtworkRepairSeen =
        new(StringComparer.OrdinalIgnoreCase);

    public void InitializeLogoQuizPromoArtworkRepair()
    {
        if (_logoQuizPromoArtworkRepairInitialized) return;
        _logoQuizPromoArtworkRepairInitialized = true;

        _logoQuizPromoArtworkRepairTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _logoQuizPromoArtworkRepairTimer.Tick += async (_, _) =>
            await RepairLogoQuizPromoArtworkAsync();
        _logoQuizPromoArtworkRepairTimer.Start();

        Closed += (_, _) => _logoQuizPromoArtworkRepairTimer?.Stop();
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => _ = RepairLogoQuizPromoArtworkAsync()));
    }

    private async Task RepairLogoQuizPromoArtworkAsync()
    {
        if (_logoQuizPromoArtworkRepairBusy) return;
        _logoQuizPromoArtworkRepairBusy = true;
        try
        {
            var projectsFolder = (_data.LoadSettings().ProjectsFolder ?? "").Trim();
            if (projectsFolder.Length == 0) return;

            var quizzesFolder = Path.Combine(Path.GetFullPath(projectsFolder), "Quizzes");
            if (!Directory.Exists(quizzesFolder)) return;

            string[] quizFiles;
            try
            {
                quizFiles = Directory.GetFiles(
                    quizzesFolder,
                    "quiz.json",
                    SearchOption.AllDirectories);
            }
            catch
            {
                return;
            }

            foreach (var quizPath in quizFiles)
            {
                long stamp;
                try { stamp = File.GetLastWriteTimeUtc(quizPath).Ticks; }
                catch { continue; }

                if (_logoQuizPromoArtworkRepairSeen.TryGetValue(quizPath, out var seen) && seen == stamp)
                    continue;
                _logoQuizPromoArtworkRepairSeen[quizPath] = stamp;

                try
                {
                    var projectFolder = Path.GetDirectoryName(quizPath);
                    if (string.IsNullOrWhiteSpace(projectFolder)) continue;
                    var repaired = await LogoQuizProjectArtworkRepair.RepairAsync(projectFolder);
                    if (repaired > 0 && File.Exists(quizPath))
                        _logoQuizPromoArtworkRepairSeen[quizPath] = File.GetLastWriteTimeUtc(quizPath).Ticks;
                }
                catch
                {
                    // Artwork repair is best-effort and must never interrupt production.
                }
            }
        }
        finally
        {
            _logoQuizPromoArtworkRepairBusy = false;
        }
    }
}

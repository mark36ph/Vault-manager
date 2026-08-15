using System.Diagnostics;
using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _nativeResolveExportWired;

    private void WireNativeResolveExportButton()
    {
        if (_nativeResolveExportWired || _productionExportButton is null)
            return;

        _productionExportButton.Click -= EmbeddedExportResolve_Click;
        _productionExportButton.Click += EmbeddedNativeResolveExport_Click;
        _nativeResolveExportWired = true;
    }

    private async void EmbeddedNativeResolveExport_Click(object sender, RoutedEventArgs e)
    {
        if (_embeddedProductionRunning || EmbeddedSelectedProject is not { } project)
            return;

        _productionExportButton.IsEnabled = false;
        _productionCurrentStageText.Text = "Creating Resolve export...";
        AppendEmbeddedProductionLog("Creating native C# Resolve export...");

        try
        {
            var timeline = new NativeProjectTimelineStore(project.Folder).Load();
            var desktopProject = _data.GetProjects().FirstOrDefault(item => item.Id == project.Id);
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = desktopProject?.Title ?? project.Title,
                ["description"] = desktopProject?.Description ?? "",
                ["pinned_comment"] = desktopProject?.PinnedComment ?? "",
                ["script"] = desktopProject?.Script ?? "",
                ["sources"] = desktopProject?.Sources ?? "",
            };
            var onscreenText = desktopProject?.OnScreenText ?? "";

            var ffmpeg = new NativeFfmpegTimelineService
            {
                Progress = (_, progress, message) =>
                {
                    _productionProgressBar.Value = Math.Clamp(progress * 100, 0, 100);
                    _productionPercentText.Text = $"{Math.Round(_productionProgressBar.Value):0}%";
                    _productionCurrentStageText.Text = message;
                    AppendEmbeddedProductionLog(message);
                },
            };

            var exportTimeline = await ffmpeg.PrepareResolveTimelineAsync(
                timeline,
                project.Folder,
                onscreenText);

            _productionCurrentStageText.Text = "Packaging Resolve export...";
            _productionProgressBar.Value = 70;
            _productionPercentText.Text = "70%";

            var result = await Task.Run(() =>
                new NativeResolveFreeExportService().Export(
                    exportTimeline,
                    project.Folder,
                    metadata));

            _productionProgressBar.Value = 100;
            _productionPercentText.Text = "100%";
            _productionCurrentStageText.Text = "Resolve export ready";
            if (_productionStageRows.TryGetValue("resolve", out var resolveRow))
            {
                resolveRow.Icon.Text = "✓";
                resolveRow.Icon.Foreground = ProductionReadyBrush();
                resolveRow.Detail.Text = "Ready";
            }

            AppendEmbeddedProductionLog($"Resolve FCPXML created: {result.FcpXml.Path}");
            AppendEmbeddedProductionLog($"Validated media files: {result.ValidatedMedia.Count}");

            if (Directory.Exists(result.Package.PackageFolder))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{result.Package.PackageFolder}\"")
                {
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception error)
        {
            _productionCurrentStageText.Text = "Resolve export failed";
            if (_productionStageRows.TryGetValue("resolve", out var resolveRow))
            {
                resolveRow.Icon.Text = "✗";
                resolveRow.Detail.Text = "Failed";
            }
            AppendEmbeddedProductionLog($"Resolve export failed: {error.Message}");
        }
        finally
        {
            _productionExportButton.IsEnabled = !_embeddedProductionRunning && EmbeddedSelectedProject?.TimelineExists == true;
        }
    }
}

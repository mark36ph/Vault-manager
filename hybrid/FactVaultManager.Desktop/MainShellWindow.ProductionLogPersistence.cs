using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private int _productionLogObservedLength;
    private string? _activeProductionLogPath;
    private ProductionLogFileStore? _productionLogFileStore;
    private bool _productionLogPersistenceBusy;
    private bool _productionLogPersistenceFailed;

    static MainShellWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            TextBox.TextChangedEvent,
            new TextChangedEventHandler(ProductionLogTextChangedClassHandler));
    }

    private static void ProductionLogTextChangedClassHandler(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;
        if (Window.GetWindow(textBox) is not MainShellWindow window)
            return;
        if (!ReferenceEquals(textBox, window._productionLogTextBox))
            return;

        window.PersistProductionLogChange();
    }

    private void PersistProductionLogChange()
    {
        if (_productionLogPersistenceBusy || _productionLogPersistenceFailed || _productionLogTextBox is null)
            return;

        _productionLogPersistenceBusy = true;
        try
        {
            var text = _productionLogTextBox.Text ?? "";

            if (!_embeddedProductionRunning)
            {
                if (!string.IsNullOrWhiteSpace(_activeProductionLogPath) && _productionLogFileStore is not null)
                {
                    _productionLogFileStore.Finish(_activeProductionLogPath);
                    _activeProductionLogPath = null;
                }

                _productionLogObservedLength = text.Length;
                return;
            }

            if (_activeProductionLogPath is null)
            {
                var project = EmbeddedSelectedProject;
                _productionLogFileStore ??= new ProductionLogFileStore(_data.RuntimeRoot);
                _activeProductionLogPath = _productionLogFileStore.Start(
                    project?.Id ?? 0,
                    project?.Title ?? _productionTopicTextBox?.Text ?? "production");
            }

            if (text.Length < _productionLogObservedLength)
            {
                _productionLogFileStore!.Append(
                    _activeProductionLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [On-screen production log cleared]{Environment.NewLine}");
                _productionLogObservedLength = 0;
            }

            if (text.Length > _productionLogObservedLength)
            {
                _productionLogFileStore!.Append(
                    _activeProductionLogPath,
                    text[_productionLogObservedLength..]);
                _productionLogObservedLength = text.Length;
            }
        }
        catch (Exception error)
        {
            _productionLogPersistenceFailed = true;
            _activeProductionLogPath = null;
            Debug.WriteLine($"Production log auto-save failed: {error}");
        }
        finally
        {
            _productionLogPersistenceBusy = false;
        }
    }
}

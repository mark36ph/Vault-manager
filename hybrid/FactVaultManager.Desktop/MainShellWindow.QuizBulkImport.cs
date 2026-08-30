using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizBulkImportHookRegistered = RegisterQuizBulkImportHook();
    private bool _quizBulkImporterConfigured;
    private int _quizBulkImporterConfigureAttempts;

    private static bool RegisterQuizBulkImportHook()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizBulkImportWindow_Loaded));
        return true;
    }

    private static void QuizBulkImportWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainShellWindow window)
        {
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(window.ConfigureQuizBulkImporter));
        }
    }

    private void ConfigureQuizBulkImporter()
    {
        if (_quizBulkImporterConfigured)
            return;
        if (_quizImportTextBox is null || Content is not DependencyObject root)
        {
            RetryQuizBulkImporterConfiguration();
            return;
        }

        var buttons = FindVisualChildren<Button>(root).ToArray();
        var importButton = buttons.FirstOrDefault(button =>
            string.Equals(button.Content?.ToString(), "Import pasted JSON", StringComparison.Ordinal));
        var loadButton = buttons.FirstOrDefault(button =>
            string.Equals(button.Content?.ToString(), "Load downloaded JSON file", StringComparison.Ordinal));
        if (importButton is null || loadButton is null)
        {
            RetryQuizBulkImporterConfiguration();
            return;
        }

        _quizBulkImporterConfigured = true;

        importButton.Click -= ImportQuizJson_Click;
        importButton.Click += ImportQuizJsonWithPreview_Click;
        importButton.Content = "Preview and import JSON";
        importButton.ToolTip = "Preview valid questions, duplicates, category mappings, and skipped entries before importing";

        loadButton.Click -= LoadQuizJsonFile_Click;
        loadButton.Click += LoadBulkQuizJsonFile_Click;
        loadButton.Content = "Load JSON file";
        loadButton.ToolTip = "Load Factburst/ChatGPT JSON or an Open Trivia DB JSON file";

        var importTab = FindVisualChildren<TabItem>(root)
            .FirstOrDefault(tab => string.Equals(tab.Header?.ToString(), "Import from ChatGPT", StringComparison.Ordinal));
        if (importTab is not null)
            importTab.Header = "Import Questions";

        var instructions = FindVisualChildren<TextBlock>(root)
            .FirstOrDefault(text => text.Text.Contains(
                "Download the quiz-questions.json file ChatGPT creates",
                StringComparison.Ordinal));
        if (instructions is not null)
        {
            instructions.Text =
                "Load a Factburst/ChatGPT JSON file or an Open Trivia DB JSON dump. OpenTDB categories are mapped automatically, HTML text is cleaned, answers are shuffled, and duplicate questions are skipped. A preview is shown before anything is imported.";
        }
    }

    private void RetryQuizBulkImporterConfiguration()
    {
        if (_quizBulkImporterConfigureAttempts++ >= 40)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ConfigureQuizBulkImporter));
    }

    private void ImportQuizJsonWithPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_quizImportTextBox is null)
            return;

        try
        {
            var preview = _data.PreviewQuizQuestionImport(_quizImportTextBox.Text, "JSON import");
            var sourceName = preview.IsOpenTriviaDb ? "Open Trivia DB" : "Factburst JSON";
            var previewMessage =
                $"Source: {sourceName}\n\n" +
                $"Detected: {preview.Detected:N0}\n" +
                $"Valid four-answer questions: {preview.Valid:N0}\n" +
                $"Ready to import: {preview.Ready:N0}\n" +
                $"Duplicates skipped: {preview.Duplicates:N0}\n" +
                $"Unsupported / invalid skipped: {preview.Invalid:N0}\n" +
                $"Category mappings: {preview.CategoryMappings:N0}";

            if (preview.Ready == 0)
            {
                MessageBox.Show(
                    this,
                    previewMessage + "\n\nThere are no new questions to import.",
                    "Quiz Import Preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirmation = MessageBox.Show(
                this,
                previewMessage + "\n\nImport the ready questions?",
                "Quiz Import Preview",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);
            if (confirmation != MessageBoxResult.Yes)
                return;

            var result = _data.ImportQuizQuestionsBulk(_quizImportTextBox.Text, "JSON import");
            _quizImportTextBox.Clear();
            RefreshQuizBank();

            if (_quizPageStatusText is not null)
            {
                _quizPageStatusText.Text =
                    $"Imported {result.Inserted:N0} • skipped {result.Duplicates:N0} duplicates • {result.Invalid:N0} invalid";
            }

            MessageBox.Show(
                this,
                $"Detected: {result.Detected:N0}\n" +
                $"Valid: {result.Valid:N0}\n" +
                $"Imported: {result.Inserted:N0}\n" +
                $"Duplicates skipped: {result.Duplicates:N0}\n" +
                $"Unsupported / invalid skipped: {result.Invalid:N0}\n" +
                $"Category mappings: {result.CategoryMappings:N0}",
                "Quiz Import Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Quiz Import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadBulkQuizJsonFile_Click(object sender, RoutedEventArgs e)
    {
        if (_quizImportTextBox is null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Import quiz questions",
            Filter = "JSON files (*.json)|*.json|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > QuizQuestionBulkImportParser.MaximumImportCharacters * 4L)
                throw new InvalidDataException("Quiz import file is too large. Import a smaller file.");

            var json = File.ReadAllText(dialog.FileName);
            if (json.Length > QuizQuestionBulkImportParser.MaximumImportCharacters)
                throw new InvalidDataException("Quiz import file is too large. Import a smaller file.");

            _quizImportTextBox.Text = json;
            if (_quizPageStatusText is not null)
            {
                _quizPageStatusText.Text =
                    $"Loaded {Path.GetFileName(dialog.FileName)} — click Preview and import JSON";
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Load Quiz JSON", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

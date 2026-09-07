using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const int DailyQuizQueueTarget = 21;
    private static readonly TimeSpan DailyQuizQueueHorizon = TimeSpan.FromDays(DailyQuizQueueTarget - 1);
    private readonly Random _dailyQuizRandom = new();
    private bool _dailyQuizAutopilotRunning;
    private System.Windows.Threading.DispatcherTimer? _dailyQuizAutopilotTimer;

    private sealed record DailyQuizScheduleItem(
        string DayKey,
        int QuizId,
        string Slug,
        string Title,
        string Category,
        string Status,
        string? PublishAt);

    private sealed record DailyQuizScheduleResponse(
        string From,
        string To,
        IReadOnlyList<DailyQuizScheduleItem>? Schedule);

    public void InitializeDailyQuizAutopilotForApp()
    {
        if (_dailyQuizAutopilotTimer is not null) return;

        _dailyQuizAutopilotTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _dailyQuizAutopilotTimer.Tick += async (_, _) => await RunDailyQuizQueueCycleAsync();
        _dailyQuizAutopilotTimer.Start();

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                await RunDailyQuizQueueCycleAsync();
            }));
    }

    private async Task RunDailyQuizQueueCycleAsync()
    {
        if (_dailyQuizAutopilotRunning || _quizBatchAutomationRunning || _quizBatchRenderRunning || _quizAutopilotFinishing)
            return;

        await MaintainDailyQuizQueueAsync();
    }

    private async Task<string?> MaintainDailyQuizQueueAsync()
    {
        if (_dailyQuizAutopilotRunning) return null;
        _dailyQuizAutopilotRunning = true;

        try
        {
            var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (!tracker.IsConfigured) return "Daily Quiz queue waiting for Link Tracker";

            var now = DateTimeOffset.UtcNow;
            var from = now.UtcDateTime.ToString("yyyy-MM-dd");
            var to = now.Add(DailyQuizQueueHorizon).UtcDateTime.ToString("yyyy-MM-dd");
            var schedule = await FetchDailyQuizScheduleAsync(tracker.BaseUrl, tracker.ApiKey, from, to);
            var validByDay = schedule
                .Where(IsUsableDailySchedule)
                .GroupBy(item => item.DayKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var missingDay = Enumerable.Range(0, DailyQuizQueueTarget)
                .Select(offset => now.AddDays(offset).UtcDateTime.ToString("yyyy-MM-dd"))
                .FirstOrDefault(day => !validByDay.ContainsKey(day));

            if (missingDay is null)
                return $"Daily Quiz queue: {validByDay.Count:N0}/{DailyQuizQueueTarget:N0} scheduled";

            var category = await Task.Run(() => ChooseDailyQuizCategory(schedule));
            if (string.IsNullOrWhiteSpace(category))
                return "Daily Quiz queue waiting for an enabled category";

            var originalCategory = _quizCategoryComboBox?.SelectedItem;
            var originalTitle = _quizTitleTextBox?.Text ?? "";
            var originalQuestionCount = _quizQuestionCountTextBox?.Text ?? "";
            var originalSeconds = _quizSecondsPerQuestionTextBox?.Text ?? "";
            var originalMode = _quizModeComboBox?.SelectedItem;
            var originalFormat = _quizFormatComboBox?.SelectedIndex ?? 0;

            try
            {
                SelectQuizBatchCategory(category);
                if (_quizModeComboBox is not null)
                    _quizModeComboBox.SelectedItem = QuizBuilderModePresets.Full;
                if (_quizQuestionCountTextBox is not null)
                    _quizQuestionCountTextBox.Text = QuizBuilderModePresets.Full.QuestionCount.ToString();
                if (_quizSecondsPerQuestionTextBox is not null)
                    _quizSecondsPerQuestionTextBox.Text = QuizBuilderModePresets.Full.SecondsPerQuestion.ToString();
                if (_quizFormatComboBox is not null)
                    _quizFormatComboBox.SelectedIndex = QuizBuilderModePresets.Full.Vertical ? 1 : 0;
                if (_quizTitleTextBox is not null)
                    _quizTitleTextBox.Text = $"Daily Quiz • {missingDay} • {category}";

                SetScheduledReadinessStatus($"Daily Quiz Autopilot: creating {category} for {missingDay}...");
                var result = await RenderOneBatchQuizAsync(1, 1, stage => SetScheduledReadinessStatus(stage));
                var history = await Task.Run(() => _data.GetQuizHistory(2_000)
                    .FirstOrDefault(item => item.ProjectFolder == result.ProjectFolder ||
                                            string.Equals(item.UploadTitleDisplay, result.YouTubeTitle, StringComparison.Ordinal)));
                if (history is null)
                    throw new InvalidOperationException("The new Daily Quiz was rendered, but its history record could not be found.");

                var questionImagePaths = await Task.Run(() => _data.GetQuizQuestions(limit: 10_000)
                    .Where(question => question.Id > 0 && !string.IsNullOrWhiteSpace(question.ImagePath))
                    .GroupBy(question => question.Id)
                    .ToDictionary(group => group.Key, group => group.First().ImagePath));

                using var website = new FactburstWebsitePublishingClient();
                var websitePayload = FactburstWebsiteQuizBuilder.Build(
                    history,
                    DateTimeOffset.Parse($"{missingDay}T00:00:00.000Z"),
                    questionImagePaths);
                websitePayload = websitePayload with
                {
                    PublishAt = $"{missingDay}T00:00:00.000Z",
                    Status = "published",
                    Title = $"Daily Quiz • {missingDay} • {category}",
                };
                await website.PublishQuizAsync(tracker.BaseUrl, tracker.ApiKey, websitePayload);
                await SetDailyQuizAsync(tracker.BaseUrl, tracker.ApiKey, missingDay, websitePayload.Slug);

                var scheduledTotal = validByDay.Count + 1;
                SetScheduledReadinessStatus(
                    $"Daily Quiz Autopilot: scheduled {category} for {missingDay} • queue {scheduledTotal}/{DailyQuizQueueTarget}");
                return $"Daily Quiz added: {category} on {missingDay} • queue {scheduledTotal}/{DailyQuizQueueTarget}";
            }
            finally
            {
                if (_quizCategoryComboBox is not null)
                    _quizCategoryComboBox.SelectedItem = originalCategory;
                if (_quizTitleTextBox is not null)
                    _quizTitleTextBox.Text = originalTitle;
                if (_quizQuestionCountTextBox is not null)
                    _quizQuestionCountTextBox.Text = originalQuestionCount;
                if (_quizSecondsPerQuestionTextBox is not null)
                    _quizSecondsPerQuestionTextBox.Text = originalSeconds;
                if (_quizModeComboBox is not null)
                    _quizModeComboBox.SelectedItem = originalMode;
                if (_quizFormatComboBox is not null)
                    _quizFormatComboBox.SelectedIndex = originalFormat;
            }
        }
        catch (Exception error)
        {
            SetScheduledReadinessStatus("Daily Quiz Autopilot: retry pending • " + error.Message);
            return "Daily Quiz retry pending";
        }
        finally
        {
            _dailyQuizAutopilotRunning = false;
        }
    }

    private string ChooseDailyQuizCategory(IReadOnlyList<DailyQuizScheduleItem> schedule)
    {
        var available = _data.GetQuizCategorySummaries()
            .Where(summary => summary.EnabledCount > 0)
            .Select(summary => summary.Category.Trim())
            .Where(category => category.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (available.Count == 0) return "";

        var scheduledCategories = schedule
            .Where(IsUsableDailySchedule)
            .Select(item => item.Category.Trim())
            .Where(category => category.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = available
            .Where(category => !scheduledCategories.Contains(category))
            .ToList();

        if (candidates.Count == 0)
        {
            var recent = schedule
                .Where(IsUsableDailySchedule)
                .OrderByDescending(item => item.DayKey, StringComparer.Ordinal)
                .Select(item => item.Category.Trim())
                .Where(category => category.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            candidates = available.Where(category => !recent.Contains(category)).ToList();
            if (candidates.Count == 0) candidates = available;
        }

        return candidates[_dailyQuizRandom.Next(candidates.Count)];
    }

    private static bool IsUsableDailySchedule(DailyQuizScheduleItem item)
    {
        if (!string.Equals(item.Status, "published", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(item.Slug) || string.IsNullOrWhiteSpace(item.Category)) return false;
        if (!DateTimeOffset.TryParse(item.PublishAt, out var publishAt)) return false;
        return publishAt <= DateTimeOffset.Parse($"{item.DayKey}T00:00:00.000Z");
    }

    private static async Task<IReadOnlyList<DailyQuizScheduleItem>> FetchDailyQuizScheduleAsync(
        string baseUrl,
        string apiKey,
        string from,
        string to)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var normalized = NormalizeDailyApiBaseUrl(baseUrl);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{normalized}/api/site/daily?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Daily schedule request returned HTTP {(int)response.StatusCode}: {body}");
        var parsed = JsonSerializer.Deserialize<DailyQuizScheduleResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return parsed?.Schedule ?? Array.Empty<DailyQuizScheduleItem>();
    }

    private static async Task SetDailyQuizAsync(
        string baseUrl,
        string apiKey,
        string dayKey,
        string slug)
    {
        var quizzesUrl = NormalizeDailyApiBaseUrl(baseUrl) + "/api/site/quizzes";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var findRequest = new HttpRequestMessage(HttpMethod.Get, quizzesUrl);
        findRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using var findResponse = await client.SendAsync(findRequest);
        var findBody = await findResponse.Content.ReadAsStringAsync();
        if (!findResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Website quiz lookup returned HTTP {(int)findResponse.StatusCode}: {findBody}");
        using var document = JsonDocument.Parse(findBody);
        var quiz = document.RootElement.TryGetProperty("quizzes", out var quizzes)
            ? quizzes.EnumerateArray().FirstOrDefault(item =>
                string.Equals(item.TryGetProperty("slug", out var slugElement) ? slugElement.GetString() : "", slug, StringComparison.OrdinalIgnoreCase))
            : default;
        if (quiz.ValueKind == JsonValueKind.Undefined || !quiz.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var quizId))
            throw new InvalidOperationException("The new Daily Quiz was published to the website but could not be found for scheduling.");

        using var request = new HttpRequestMessage(HttpMethod.Post, NormalizeDailyApiBaseUrl(baseUrl) + "/api/site/daily");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { day_key = dayKey, quiz_id = quizId }),
            Encoding.UTF8,
            "application/json");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Daily quiz scheduling returned HTTP {(int)response.StatusCode}: {body}");
    }

    private static string NormalizeDailyApiBaseUrl(string value)
    {
        var normalized = (value ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The Link Tracker base URL must be a complete HTTPS address.", nameof(value));
        }
        return uri.AbsoluteUri.TrimEnd('/');
    }
}

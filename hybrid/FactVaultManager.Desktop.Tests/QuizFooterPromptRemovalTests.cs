using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizFooterPromptRemovalTests
{
    [Fact]
    public void Footer_OmitsLegacyChoicePrompt_ButKeepsRevealAnswer()
    {
        Exception? testError = null;
        var thread = new Thread(() =>
        {
            try
            {
                var question = new QuizQuestion(
                    991001,
                    "Which gas do plants take in during photosynthesis?",
                    "Carbon dioxide",
                    "Oxygen",
                    "Nitrogen",
                    "Helium",
                    0,
                    "Plants use carbon dioxide during photosynthesis.",
                    "Science",
                    "Easy",
                    "Test",
                    0);
                var options = new QuizVideoBuildOptions("General Knowledge Quiz");
                var method = typeof(QuizThemedCardRenderer).GetMethod(
                    "BuildFooter",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(method);

                var normal = Assert.IsType<Grid>(method.Invoke(
                    null,
                    [question, options, false, false, false, null]));
                Assert.DoesNotContain(
                    normal.Children.OfType<TextBlock>(),
                    text => string.Equals(text.Text, "Choose A, B, C or D", StringComparison.Ordinal));

                var reveal = Assert.IsType<Grid>(method.Invoke(
                    null,
                    [question, options, true, false, false, null]));
                Assert.Contains(
                    reveal.Children.OfType<TextBlock>(),
                    text => string.Equals(text.Text, "A. Carbon dioxide", StringComparison.Ordinal));
            }
            catch (Exception error)
            {
                testError = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (testError is not null)
            ExceptionDispatchInfo.Capture(testError).Throw();
    }
}

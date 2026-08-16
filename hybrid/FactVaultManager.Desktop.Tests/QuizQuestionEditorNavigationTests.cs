namespace FactVaultManager.Desktop.Tests;

public sealed class QuizQuestionEditorNavigationTests
{
    [Fact]
    public void FindNeighborId_FollowsVisibleDisplayOrder()
    {
        var order = new[] { 12, 4, 27, 9 };

        Assert.Equal(27, QuizQuestionEditorNavigation.FindNeighborId(order, 4, 1));
        Assert.Equal(4, QuizQuestionEditorNavigation.FindNeighborId(order, 27, -1));
    }

    [Fact]
    public void FindNeighborId_DoesNotWrapAtEnds()
    {
        var order = new[] { 12, 4, 27 };

        Assert.Null(QuizQuestionEditorNavigation.FindNeighborId(order, 12, -1));
        Assert.Null(QuizQuestionEditorNavigation.FindNeighborId(order, 27, 1));
    }

    [Fact]
    public void FindNeighborId_ReturnsNullWhenQuestionIsNotVisible()
    {
        Assert.Null(QuizQuestionEditorNavigation.FindNeighborId(new[] { 1, 2, 3 }, 99, 1));
    }

    [Fact]
    public void FindNeighborId_RejectsInvalidDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuizQuestionEditorNavigation.FindNeighborId(new[] { 1, 2, 3 }, 2, 0));
    }
}

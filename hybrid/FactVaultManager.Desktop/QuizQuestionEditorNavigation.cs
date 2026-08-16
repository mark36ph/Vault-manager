namespace FactVaultManager.Desktop;

public static class QuizQuestionEditorNavigation
{
    public static int? FindNeighborId(IReadOnlyList<int> displayOrder, int currentId, int direction)
    {
        ArgumentNullException.ThrowIfNull(displayOrder);
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction), "Direction must be -1 or 1.");

        var index = -1;
        for (var position = 0; position < displayOrder.Count; position++)
        {
            if (displayOrder[position] == currentId)
            {
                index = position;
                break;
            }
        }

        if (index < 0)
            return null;

        var neighborIndex = index + direction;
        return neighborIndex >= 0 && neighborIndex < displayOrder.Count
            ? displayOrder[neighborIndex]
            : null;
    }
}

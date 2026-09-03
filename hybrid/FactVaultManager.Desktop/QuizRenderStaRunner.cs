namespace FactVaultManager.Desktop;

internal static class QuizRenderStaRunner
{
    public static Task<T> RunAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return Task.FromResult(work());

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "FactVaultManager Quiz Render STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static Task RunAsync(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            work();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work();
                completion.SetResult(null);
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "FactVaultManager Quiz Render STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}

using Android.Gms.Tasks;

namespace HomeTracker.Device.Vision;

/// <summary>Bridges Google Play services tasks (used by ML Kit) to .NET async/await.</summary>
internal static class GmsTaskExtensions
{
    public static System.Threading.Tasks.Task<Java.Lang.Object?> AsTask(this Android.Gms.Tasks.Task task)
    {
        var listener = new Listener();
        task.AddOnSuccessListener(listener);
        task.AddOnFailureListener(listener);
        return listener.Completion.Task;
    }

    private sealed class Listener : Java.Lang.Object, IOnSuccessListener, IOnFailureListener
    {
        public TaskCompletionSource<Java.Lang.Object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnSuccess(Java.Lang.Object? result) => Completion.TrySetResult(result);

        public void OnFailure(Java.Lang.Exception e) => Completion.TrySetException(e);
    }
}

using Android.App;
using Android.App.Job;
using Android.Content;
using Android.Util;
using AndroidApp = Android.App.Application;

namespace HomeTracker.Device.BackgroundWork;

/// <summary>
/// Runs a piece of work about every 15 minutes while the phone has internet, even when the app is closed
/// and after a restart. Uses Android's built-in JobScheduler, so Android decides the exact moment
/// (it may wait longer in battery saving mode).
/// </summary>
public static class BackgroundWork
{
    private const int JobId = 4101;

    private static Func<CancellationToken, Task>? _work;

    internal static Func<CancellationToken, Task> Work =>
        _work ?? throw new InvalidOperationException("BackgroundWork.Initialize was not called.");

    /// <summary>Call at app start-up (it also runs when Android starts the app for the job).</summary>
    public static void Initialize(Func<CancellationToken, Task> work) => _work = work;

    /// <summary>Starts the periodic job, or stops it when <paramref name="enabled"/> is false. Safe to call often.</summary>
    public static void SetEnabled(bool enabled)
    {
        var context = AndroidApp.Context;
        var scheduler = (JobScheduler)context.GetSystemService(Context.JobSchedulerService)!;
        if (!enabled)
        {
            scheduler.Cancel(JobId);
            return;
        }

        if (scheduler.GetPendingJob(JobId) is not null)
        {
            return;
        }

        var job = new JobInfo.Builder(JobId, new ComponentName(context, Java.Lang.Class.FromType(typeof(BackgroundWorkService))))
            .SetRequiredNetworkType(NetworkType.Any)!
            .SetPeriodic(15 * 60 * 1000L)!
            // Kept after a restart (needs RECEIVE_BOOT_COMPLETED).
            .SetPersisted(true)!
            .Build()!;
        var result = scheduler.Schedule(job);
        Log.Info("HomeTracker", $"Background work scheduled: {(result == JobScheduler.ResultSuccess ? "ok" : "failed")}");
    }
}

/// <summary>Android calls this when the periodic job is due.</summary>
[Service(Name = "hometracker.backgroundwork.BackgroundWorkService", Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
public sealed class BackgroundWorkService : JobService
{
    private CancellationTokenSource? _cancel;

    public override bool OnStartJob(JobParameters? @params)
    {
        _cancel = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var token = _cancel.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await BackgroundWork.Work(token);
            }
            catch (Exception ex)
            {
                Log.Error("HomeTracker", $"Background work failed: {ex}");
            }
            finally
            {
                JobFinished(@params, false);
            }
        });

        return true; // Still working on another thread.
    }

    public override bool OnStopJob(JobParameters? @params)
    {
        _cancel?.Cancel();
        return true; // Try again later.
    }
}

using System.Collections.Concurrent;
using Android.App;
using Android.Content;

namespace CoinFlow.App.Backup;

/// <summary>
/// Başka bir ekranı (dosya seçici, izin ayarı) açıp dönüşünü bekler.
/// <c>MainActivity.OnActivityResult</c> sonucu buraya iletir.
/// </summary>
public static class ActivityResults
{
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<(Result ResultCode, Intent? Data)>>
        Pending = new();
    private static int _nextRequestCode = 7300;

    public static Task<(Result ResultCode, Intent? Data)> StartAsync(Intent intent)
    {
        var activity = Platform.CurrentActivity ??
                       throw new InvalidOperationException("Ekran açılamadı.");
        var requestCode = Interlocked.Increment(ref _nextRequestCode);
        var pending = new TaskCompletionSource<(Result, Intent?)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[requestCode] = pending;
        try
        {
            activity.StartActivityForResult(intent, requestCode);
        }
        catch
        {
            Pending.TryRemove(requestCode, out _);
            throw;
        }

        return pending.Task;
    }

    public static void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (Pending.TryRemove(requestCode, out var pending))
        {
            pending.TrySetResult((resultCode, data));
        }
    }
}

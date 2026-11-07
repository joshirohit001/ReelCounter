namespace ReelCounter.Services;

/// <summary>Shared in-process state. The accessibility service and the Blazor UI run in the same process.</summary>
public static class ReelStats
{
    public const int Limit = 500;                                  // warn at this many reels...
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30); // ...inside this window

    static readonly Queue<DateTime> _recent = new();
    static readonly object _lock = new();

    public static event Action? Changed;

    static string TodayKey() => "count_" + DateTime.Now.ToString("yyyyMMdd");

    public static int Today => Preferences.Default.Get(TodayKey(), 0);

    public static int InWindow
    {
        get { lock (_lock) { Trim(); return _recent.Count; } }
    }

    static void Trim()
    {
        var cutoff = DateTime.UtcNow - Window;
        while (_recent.Count > 0 && _recent.Peek() < cutoff) _recent.Dequeue();
    }

    /// <summary>Registers one reel. Returns the number of reels in the last 30 minutes.</summary>
    public static int Add()
    {
        int n;
        lock (_lock)
        {
            _recent.Enqueue(DateTime.UtcNow);
            Trim();
            n = _recent.Count;
        }
        Preferences.Default.Set(TodayKey(), Today + 1);
        Changed?.Invoke();
        return n;
    }

    public static void Reset()
    {
        lock (_lock) _recent.Clear();
        Preferences.Default.Set(TodayKey(), 0);
        Changed?.Invoke();
    }
}

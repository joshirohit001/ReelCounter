using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using ReelCounter.Services;
using Color = Android.Graphics.Color;

namespace ReelCounter;

/// <summary>
/// Watches Instagram / YouTube / Facebook. Every vertical reel swipe counts as 1 reel.
/// A small pill is drawn at the top of the screen, beside the front-camera cut-out.
/// </summary>
[Service(Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true, Label = "Reel Counter")]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/accessibility_service_config")]
public class ReelAccessibilityService : AccessibilityService
{
    static readonly HashSet<string> Targets = new()
    {
        "com.instagram.android",
        "com.google.android.youtube",
        "com.facebook.katana",
    };

    IWindowManager? _wm;
    TextView? _pill;
    GradientDrawable? _bg;
    WindowManagerLayoutParams? _lp;
    bool _shown;
    long _lastCount;
    readonly Handler _ui = new(Looper.MainLooper!);
    readonly System.Action _onStatsChanged;

    public ReelAccessibilityService() => _onStatsChanged = () => _ui.Post(Refresh);

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        BuildPill();
        ReelStats.Changed += _onStatsChanged;
    }

    public override void OnInterrupt() { }

    public override void OnDestroy()
    {
        ReelStats.Changed -= _onStatsChanged;
        Hide();
        base.OnDestroy();
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        var pkg = e?.PackageName;
        if (e is null || pkg is null) return;

        // App switched: show the pill only while a target app is in front.
        if (e.EventType == EventTypes.WindowStateChanged)
        {
            if (Targets.Contains(pkg)) { Refresh(); Show(); }
            else if (pkg != PackageName && pkg != "com.android.systemui") Hide();
            return;
        }

        if (e.EventType != EventTypes.ViewScrolled || !Targets.Contains(pkg)) return;
        if (!IsReelPager(e)) return;

        // A swipe fires several scroll events; debounce to count once.
        var now = SystemClock.ElapsedRealtime();
        if (now - _lastCount < 700) return;
        _lastCount = now;

        int inWindow = ReelStats.Add();
        Show();
        Refresh();

        if (inWindow >= ReelStats.Limit && inWindow % 50 == 0) Warn(inWindow);
    }

    // The reels screen is a full-screen vertical pager. View ids can change with app updates,
    // so we match on id keywords and fall back to the ViewPager class name.
    static bool IsReelPager(AccessibilityEvent e)
    {
        var id = e.Source?.ViewIdResourceName ?? "";
        if (id.Contains("clips", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("reel", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("shorts", StringComparison.OrdinalIgnoreCase)) return true;

        var cls = e.ClassName ?? "";
        return cls.Contains("ViewPager", StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Overlay ----------

    int Dp(int v) => (int)(v * Resources!.DisplayMetrics!.Density);

    void BuildPill()
    {
        _wm = GetSystemService(WindowService)!.JavaCast<IWindowManager>();

        _bg = new GradientDrawable();
        _bg.SetCornerRadius(Dp(16));

        _pill = new TextView(this) { Text = "Reels: 0", TextSize = 13, Gravity = GravityFlags.Center };
        _pill.SetTextColor(Color.White);
        _pill.SetTypeface(null, TypefaceStyle.Bold);
        _pill.SetPadding(Dp(12), Dp(4), Dp(12), Dp(4));
        _pill.Background = _bg;

        _lp = new WindowManagerLayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent,
            WindowManagerTypes.AccessibilityOverlay,   // no extra "draw over apps" permission needed
            WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchable |
            WindowManagerFlags.LayoutInScreen | WindowManagerFlags.LayoutNoLimits,
            Format.Translucent)
        {
            Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal,
            X = Dp(64),   // shift right of the camera hole; tweak for your phone
            Y = Dp(6),
        };
        if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            _lp.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;

        SetPill("Reels: 0", false);
    }

    void SetPill(string text, bool alert)
    {
        if (_pill is null || _bg is null) return;
        _pill.Text = text;
        _bg.SetColor(alert ? Color.Argb(235, 200, 30, 30) : Color.Argb(220, 25, 25, 25));
    }

    void Refresh()
    {
        int n = ReelStats.InWindow;
        SetPill($"Reels: {n}", n >= ReelStats.Limit);
    }

    void Show()
    {
        if (_shown || _wm is null || _pill is null || _lp is null) return;
        try { _wm.AddView(_pill, _lp); _shown = true; } catch { /* ignore */ }
    }

    void Hide()
    {
        if (!_shown || _wm is null || _pill is null) return;
        try { _wm.RemoveView(_pill); } catch { /* ignore */ }
        _shown = false;
    }

    void Warn(int n)
    {
        SetPill($"⚠ {n} reels in 30 min! Stop scrolling", true);
        Toast.MakeText(this,
            $"You've watched {n} reels in 30 minutes. Put the phone down and take a break!",
            ToastLength.Long)?.Show();

        try
        {
            if (GetSystemService(VibratorService) is Vibrator v && Build.VERSION.SdkInt >= BuildVersionCodes.O)
                v.Vibrate(VibrationEffect.CreateOneShot(500, VibrationEffect.DefaultAmplitude));
        }
        catch { /* ignore */ }

        _ui.PostDelayed(Refresh, 6000); // back to the short counter, stays red while over the limit
    }
}

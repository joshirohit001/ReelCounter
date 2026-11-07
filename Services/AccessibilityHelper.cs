using Android.Content;
using Android.Provider;

namespace ReelCounter.Services;

public static class AccessibilityHelper
{
    public static bool IsServiceEnabled()
    {
        var ctx = Android.App.Application.Context;
        var enabled = Settings.Secure.GetString(ctx.ContentResolver, Settings.Secure.EnabledAccessibilityServices) ?? "";
        return enabled.Contains(ctx.PackageName + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static void OpenAccessibilitySettings()
    {
        var intent = new Intent(Settings.ActionAccessibilitySettings);
        intent.AddFlags(ActivityFlags.NewTask);
        Android.App.Application.Context.StartActivity(intent);
    }

    public static void OpenAppInfo()
    {
        var ctx = Android.App.Application.Context;
        var intent = new Intent(Settings.ActionApplicationDetailsSettings,
            Android.Net.Uri.Parse("package:" + ctx.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        ctx.StartActivity(intent);
    }
}

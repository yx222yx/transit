using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Suiyi.Windows;

internal static class AppIdentity
{
    internal static string Version => typeof(AppIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "Alpha";
    internal static string Title => "随译 · Windows · " + Version;
    internal static ImageSource WindowIcon => BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));

    internal static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute))!.Stream;
        using var source = new System.Drawing.Icon(stream, 32, 32);
        return (System.Drawing.Icon)source.Clone();
    }
}

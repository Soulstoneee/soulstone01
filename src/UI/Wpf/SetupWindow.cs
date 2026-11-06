using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RestMode.Util;

namespace RestMode.UI.Wpf;

/// <summary>
/// The installer screen (what RestMode-Setup.exe shows): installs RestMode for
/// this user - Start menu entries, optional desktop shortcut, optional start
/// with Windows, and an Apps-list entry whose Uninstall restores everything.
/// </summary>
internal sealed class SetupWindow : AppWindow
{
    public SetupWindow() : base("Setup", 640, 460)
    {
        var p = new StackPanel { Margin = new Thickness(48, 10, 48, 24) };
        p.Children.Add(Heading("Install RestMode", 24));
        p.Children.Add(new FrameworkElement { Height = 10 });
        p.Children.Add(Text("RestMode is installed for you only, in your user profile – no admin rights needed."));
        p.Children.Add(Secondary(Installer.InstallDir));
        p.Children.Add(new FrameworkElement { Height = 18 });
        var desktop = new System.Windows.Controls.CheckBox { Content = "Desktop shortcut \"RestMode – Rest now\"", IsChecked = true, Margin = new Thickness(0, 0, 0, 10) };
        var startup = new System.Windows.Controls.CheckBox { Content = "Start RestMode with Windows", IsChecked = true };
        p.Children.Add(desktop);
        p.Children.Add(startup);
        p.Children.Add(new FrameworkElement { Height = 16 });
        p.Children.Add(Secondary("Start menu entries \"RestMode\" and \"RestMode – Rest now\" are always added. Remove it later from Windows Settings › Apps › RestMode › Uninstall – that puts every setting back."));
        var status = Warn("");
        var install = Flat("Install", (_, _) => { }, "PrimaryButton");
        install.FontSize = 15;
        install.FontWeight = FontWeights.SemiBold;
        install.Padding = new Thickness(36, 9, 36, 9);
        install.HorizontalAlignment = HorizontalAlignment.Left;
        install.Margin = new Thickness(0, 22, 0, 0);
        install.Click += (_, _) =>
        {
            install.IsEnabled = false;
            string? err = Installer.Install(desktop.IsChecked == true, startup.IsChecked == true);
            if (err != null)
            {
                status.Text = "Install failed: " + err;
                install.IsEnabled = true;
                return;
            }
            try { Process.Start(new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Error("Starting the installed copy failed", ex); }
            Close();
        };
        p.Children.Add(install);
        p.Children.Add(status);
        Body.Content = p;
        Loaded += (_, _) => install.Focus();
    }
}

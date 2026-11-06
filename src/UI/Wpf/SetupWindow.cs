using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RestMode.Util;

namespace RestMode.UI.Wpf;

/// <summary>
/// The installer screen (what RestMode-Setup.exe shows): installs RestMode for
/// this user - Start menu entries, optional desktop shortcut, optional start
/// with Windows, and an Apps-list entry whose Uninstall restores everything.
/// </summary>
internal sealed class SetupWindow : OrnateWindow
{
    public SetupWindow() : base("RestMode · Setup", 860, 640)
    {
        var p = new StackPanel { Margin = new Thickness(70, 0, 70, 20) };
        p.Children.Add(Heading("Install RestMode", 56));
        p.Children.Add(Label("RestMode will be installed for you only, in your user profile - no admin rights needed:"));
        p.Children.Add(Hint(Installer.InstallDir));
        var desktop = new System.Windows.Controls.CheckBox { Content = "Desktop shortcut \"RestMode – Rest now\"", IsChecked = true };
        var startup = new System.Windows.Controls.CheckBox { Content = "Start RestMode with Windows", IsChecked = true };
        p.Children.Add(desktop);
        p.Children.Add(startup);
        p.Children.Add(Hint("Start menu entries \"RestMode\" and \"RestMode – Rest now\" are always added. To remove RestMode later: Windows Settings › Apps › RestMode › Uninstall - it puts every setting back."));
        var status = Hint("");
        var install = new OrnateButton("Install", 44, 150) { Margin = new Thickness(0, 24, 0, 0) };
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

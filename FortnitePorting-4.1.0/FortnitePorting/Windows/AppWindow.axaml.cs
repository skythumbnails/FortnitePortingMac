using System;
using Avalonia;
using Avalonia.Controls;
using FortnitePorting.Controls.Navigation.Sidebar;
using FortnitePorting.Framework;
using FortnitePorting.Services;
using AppWindowModel = FortnitePorting.WindowModels.AppWindowModel;

namespace FortnitePorting.Windows;

public partial class AppWindow : WindowBase<AppWindowModel>
{
    public AppWindow() : base(initializeWindowModel: false)
    {
        InitializeComponent();
        DataContext = WindowModel;

        Navigation.App.Initialize(Sidebar, ContentFrame);

        KeyDownEvent.AddClassHandler<TopLevel>((sender, args) => BlackHole.HandleKey(args.Key), handledEventsToo: true);

        WindowModel.SupaBase.LevelUp += (sender, level) =>
        {
            TaskService.RunDispatcher(async () => await LevelUpOverlay.ShowLevelUp(level));
        };

        // Hide the custom Windows-style minimize/maximize/close buttons on macOS — the native
        // traffic-light controls are enabled by WindowBase.OnInitialized. Also push the sidebar
        // header down so the FP logo / "Fortnite Porting" title clear the native traffic lights
        // that sit in the top-left of the extended title bar.
        if (OperatingSystem.IsMacOS())
        {
            WindowsChromeButtons.IsVisible = false;
            SidebarHeader.Margin = new Thickness(0, 28, 0, 0);
        }
    }

    private void OnSidebarItemSelected(object? sender, SidebarItemSelectedArgs args)
    {
        if (!AppSettings.Installation.FinishedSetup) return;
        
        Navigation.App.Open(args.Tag);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        App.Lifetime.Shutdown();
    }
}
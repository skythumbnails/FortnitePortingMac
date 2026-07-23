using System;
using System.IO;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FluentAvalonia.Styling;
using FortnitePorting.Shared.Extensions;

namespace FortnitePorting.Application;

public partial class FortnitePortingApp : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        BindingPlugins.DataValidators.RemoveAll(validator => validator is DataAnnotationsValidationPlugin);

        if (Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault() is { } fluentTheme)
        {
            fluentTheme.CustomAccentColor = Color.Parse("#953bf8");
        }
        
        AppServices.Initialize();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            App.InitializeDesktop(desktop);
        }

        // Discord sign-in on macOS: the OAuth redirect comes back as a fortniteporting:// URL open,
        // which macOS delivers as an Apple Event to the RUNNING app — never argv, so the Windows
        // single-instance pipe in Program.cs can't see it. Avalonia surfaces those as protocol
        // activations; route them into the same handler. (Requires CFBundleURLTypes in Info.plist.)
        if (OperatingSystem.IsMacOS() && TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, activatedArgs) =>
            {
                if (activatedArgs is ProtocolActivatedEventArgs protocol)
                    App.HandleUrlScheme(protocol.Uri.ToString());
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
    
}
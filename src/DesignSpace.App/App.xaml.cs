using DesignSpace.Workbench.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.App;

public sealed partial class App : Application
{
    private Window? _window;
    private WorkbenchView? _workbench;
    private BrowserDiagnostics? _diagnostics;
    public App(){InitializeComponent();UnhandledException+=(_,e)=>Console.Error.WriteLine("[DesignSpace] "+e.Exception);}
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
#if __WASM__
        // Imports are copies, never live filesystem handles. Use the same upload
        // interaction in all browsers, including those with File System Access.
        Uno.WinRTFeatureConfiguration.Storage.Pickers.WasmConfiguration = Uno.WasmPickerConfiguration.DownloadUpload;
#endif
        _window=new Window{Title="DesignSpace"};
        _window.Content=new TextBlock{Text="Starting DesignSpace…",Margin=new Thickness(28),FontSize=20};_window.Activate();
        try
        {
            await ApplicationFonts.InitializeAsync();BrowserKeyboardInput.Initialize();
            _workbench=new WorkbenchView(new ImageAssetPlatform());_workbench.EnableAdvancedTools();_window.Content=_workbench;
            _diagnostics=new BrowserDiagnostics(_workbench);await _workbench.InitializeAsync();
            Console.WriteLine("[DesignSpace] Workbench ready");
            _window.Closed+=(_,_)=>{_diagnostics?.Dispose();_workbench.DisposeAdvancedTools();_workbench.Dispose();};
        }
        catch(Exception exception)
        {
            Console.Error.WriteLine("[DesignSpace] Startup failed: "+exception);
            _window.Content=new ScrollViewer{Content=new TextBlock{Text="DesignSpace could not start.\n\n"+exception,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(24),IsTextSelectionEnabled=true}};
        }
    }
}

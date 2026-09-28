using Uno.UI.Hosting;
namespace DesignSpace.App;
public static class Program
{
    public static async Task Main(string[] args)
    {
        var host=UnoPlatformHostBuilder.Create().App(()=>new App()).UseWebAssembly().Build();
        await host.RunAsync();
    }
}

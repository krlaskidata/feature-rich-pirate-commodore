using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PiratBotCSharp
{    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                var builder = Host.CreateDefaultBuilder(args)
                    .ConfigureServices((hostContext, services) =>
                    {
                        services.AddSingleton<Bot>();
                    })
                    .UseConsoleLifetime();

                var host = builder.Build();

                var bot = host.Services.GetRequiredService<Bot>();
                await bot.InitializeAsync();

                await host.RunAsync();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💀 FATAL: Startup failed: {ex.Message}");
                return 1;
            }
        }
    }
}

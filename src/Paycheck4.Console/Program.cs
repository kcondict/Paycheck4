using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Paycheck4.Console.Configuration;
using Paycheck4.Core;
using Paycheck4.Core.Protocol;
using Paycheck4.Core.Usb;
using Serilog;
using Serilog.Settings.Configuration;

namespace Paycheck4.Console
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Get the application's base directory for log paths
            var baseDir = AppContext.BaseDirectory;
            var logsDir = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(logsDir); // Ensure logs directory exists

            // Load configuration first
            var configuration = new ConfigurationBuilder()
                .SetBasePath(baseDir)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();

            // Set up logging with explicit configuration for single-file deployment
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "paycheck4.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .Enrich.FromLogContext()
                .CreateLogger();

            try
            {
                await CreateHostBuilder(args).Build().RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    // Configuration
                    services.Configure<PrinterConfig>(
                        hostContext.Configuration.GetSection("Printer"));

                    // Core services  
                    services.AddHostedService<PrinterEmulatorService>();
                    services.AddSingleton<IPrinterEmulator>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<PrinterEmulator>>();
                        var usbLogger = sp.GetRequiredService<ILogger<UsbGadgetManager>>();
                        var protocolLogger = sp.GetRequiredService<ILogger<TclProtocol>>();
                        return new PrinterEmulator(logger, usbLogger, protocolLogger);
                    });
                })
                .UseSerilog();
    }
}
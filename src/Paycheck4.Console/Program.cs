using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
		private static IPrinterEmulator? _printerEmulator;
		private static readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

		public static async Task Main(string[] args)
		{
			// Get the application's base directory for log paths
			var baseDir = AppDomain.CurrentDomain.BaseDirectory;
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

			// Set up logging with explicit configuration
			Log.Logger = new LoggerConfiguration()
				.ReadFrom.Configuration(configuration)
				.WriteTo.Console(
					outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
				.WriteTo.File(
					path: Path.Combine(baseDir, "logs", "paycheck4.log"),
					rollingInterval: RollingInterval.Day,
					outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
				.Enrich.FromLogContext()
				.CreateLogger();

			try
			{
				Log.Information("Starting Paycheck4 Printer Emulator");

				// Set up dependency injection
				var services = new ServiceCollection();
				
				// Add logging
				services.AddLogging(builder =>
				{
					builder.AddSerilog(dispose: true);
				});

				// Configure services
				services.Configure<PrinterConfig>(configuration.GetSection("Printer"));

				// Build service provider
				var serviceProvider = services.BuildServiceProvider();

				// Create printer emulator
				var logger = serviceProvider.GetRequiredService<ILogger<PrinterEmulator>>();
				var usbLogger = serviceProvider.GetRequiredService<ILogger<UsbGadgetManager>>();
				var protocolLogger = serviceProvider.GetRequiredService<ILogger<TclProtocol>>();

				// Get protocol configuration values
				var statusReportingInterval = configuration.GetValue<int>("Protocol:StatusReportingInterval", 5000);
				var printStartDelayInterval = configuration.GetValue<int>("Protocol:PrintStartDelayInterval", 3000);
				var validationDelayInterval = configuration.GetValue<int>("Protocol:ValidationDelayInterval", 18000);
				var busyStateChangeInterval = configuration.GetValue<int>("Protocol:BusyStateChangeInterval", 20000);
				var tofStateChangeInterval = configuration.GetValue<int>("Protocol:TOFStateChangeInterval", 4000);
				var paperInChuteSetInterval = configuration.GetValue<int>("Protocol:PaperInChuteSetInterval", 2000);
				var paperInChuteClearInterval = configuration.GetValue<int>("Protocol:PaperInChuteClearInterval", 3000);

				_printerEmulator = new PrinterEmulator(
					logger,
					usbLogger,
					protocolLogger,
					statusReportingInterval,
					printStartDelayInterval,
					validationDelayInterval,
					busyStateChangeInterval,
					tofStateChangeInterval,
					paperInChuteSetInterval,
					paperInChuteClearInterval);

				// Set up console cancellation
				System.Console.CancelKeyPress += OnCancelKeyPress;

				// Initialize and start the emulator
				_printerEmulator.Initialize();
				_printerEmulator.Start();

				Log.Information("Printer emulator started. Press Ctrl+C to stop.");

				// Wait for cancellation
				await Task.Delay(Timeout.Infinite, _cancellationTokenSource.Token);
			}
			catch (OperationCanceledException)
			{
				Log.Information("Application shutting down...");
			}
			catch (Exception ex)
			{
				Log.Fatal(ex, "Application terminated unexpectedly");
			}
			finally
			{
				// Clean up
				if (_printerEmulator != null)
				{
					try
					{
						_printerEmulator.Stop();
						_printerEmulator.Dispose();
					}
					catch (Exception ex)
					{
						Log.Error(ex, "Error during cleanup");
					}
				}

				Log.CloseAndFlush();
			}
		}

		private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
		{
			e.Cancel = true; // Prevent immediate termination
			Log.Information("Shutdown requested...");
			_cancellationTokenSource.Cancel();
		}
	}
}
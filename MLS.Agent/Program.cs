﻿using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using static Pocket.Logger<MLS.Agent.Program>;
using Pocket;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MLS.Agent.Tools;
using Pocket.For.ApplicationInsights;
using Recipes;
using Serilog.Sinks.RollingFileAlternate;
using WorkspaceServer.Servers.Roslyn;
using SerilogLoggerConfiguration = Serilog.LoggerConfiguration;

namespace MLS.Agent
{
    public class Program
    {
        public static X509Certificate2 ParseKey(string base64EncodedKey)
        {
            var bytes = Convert.FromBase64String(base64EncodedKey);
            return new X509Certificate2(bytes);
        }

        private static readonly Assembly[] assembliesEmittingPocketLoggerLogs = {
            typeof(Startup).Assembly,
            typeof(Dotnet).Assembly,
            typeof(RoslynWorkspaceServer).Assembly
        };

        private static void StartLogging(CompositeDisposable disposables, CommandLineOptions options)
        {
            if (options.IsProduction)
            {
                var applicationVersion = AssemblyVersionSensor.Version().AssemblyInformationalVersion;
                var websiteSiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "UNKNOWN-AGENT";

                disposables.Add(
                    LogEvents.Enrich(a =>
                    {
                        a(("applicationVersion", applicationVersion));
                        a(("websiteSiteName", websiteSiteName));
                        a(("id", options.Id));
                    }));
            }

            var shouldLogToFile = options.LogToFile;

#if DEBUG
            shouldLogToFile = true;
#endif 

            if (shouldLogToFile)
            {
                var log = new SerilogLoggerConfiguration()
                          .WriteTo
                          .RollingFileAlternate("./logs", outputTemplate: "{Message}{NewLine}")
                          .CreateLogger();

                var subscription = LogEvents.Subscribe(
                    e => log.Information(e.ToLogString()),
                    assembliesEmittingPocketLoggerLogs);

                disposables.Add(subscription);
                disposables.Add(log);
            }

            disposables.Add(
                LogEvents.Subscribe(e => Console.WriteLine(e.ToLogString()),
                                    assembliesEmittingPocketLoggerLogs));

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                Log.Warning($"{nameof(TaskScheduler.UnobservedTaskException)}", args.Exception);
                args.SetObserved();
            };

            if (options.ApplicationInsightsKey != null)
            {
                var telemetryClient = new TelemetryClient(new TelemetryConfiguration(options.ApplicationInsightsKey))
                {
                    InstrumentationKey = options.ApplicationInsightsKey
                };
                disposables.Add(telemetryClient.SubscribeToPocketLogger(assembliesEmittingPocketLoggerLogs));
            }

            Log.Event("AgentStarting");
        }

        public static IWebHost ConstructWebHost(CommandLineOptions options)
        {
            var disposables = new CompositeDisposable();
            StartLogging(disposables, options);

            if (options.Key is null)
            {
                Log.Info("No Key Provided");
            }
            else
            {
                Log.Info("Received Key: {key}", options.Key);
            }
          
            var webHost = new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(c =>
                {
                    if (options.ApplicationInsightsKey != null)
                    {
                        c.AddApplicationInsightsTelemetry(options.ApplicationInsightsKey);
                    }
                    c.AddSingleton(options);
                    c.AddSingleton(new AgentOptions(options.IsLanguageService));
                })
                .UseEnvironment(options.IsProduction
                                      ? EnvironmentName.Production
                                      : EnvironmentName.Development)
                .UseStartup<Startup>()
                .Build();

            return webHost;
        }

        public static int Main(string[] args)
        {
            var options = CommandLineOptions.Parse(args);
            if (options.HelpRequested)
            {
                Console.WriteLine(options.HelpText);
                return 0;
            }

            if (!options.WasSuccess)
            {
                Console.WriteLine(options.HelpText);
                return 1;
            }

            ConstructWebHost(options).Run();
            return 0;
        }
    }
}
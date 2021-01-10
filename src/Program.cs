//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Main program for SIP/Web server application. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Serilog;
using Serilog.Extensions.Logging;

namespace devcall
{
    public class Program
    {
        // This configuration instnaces is made advailable early solely for the logging configuration.
        // It can be removed if the Serilog logger is configured programatically only.
        public static IConfiguration Configuration { get; } = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(Configuration)
            //.MinimumLevel.Debug()
            //.MinimumLevel.Override("Microsoft", LogEventLevel.Debug)
            //.MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning) // Set this to Information to see SQL queries.
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

            Log.Logger.Information($"Starting devcall server version {GetVersion()}...");

            var factory = new SerilogLoggerFactory(Log.Logger);
            SIPSorcery.LogFactory.Set(factory);

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                //.ConfigureLogging(logging =>
                //  logging.AddAzureWebAppDiagnostics()
                //)
                .ConfigureAppConfiguration((context, config) =>
                {
                    if (context.HostingEnvironment.IsProduction())
                    {
                        var builtConfig = config.Build();
                        var secretClient = new SecretClient(new Uri($"https://{builtConfig["KeyVaultName"]}.vault.azure.net/"),
                                                                 new DefaultAzureCredential());
                        config.AddAzureKeyVault(secretClient, new KeyVaultSecretManager());
                    }
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    var keyVaultCertName = Configuration["KeyVaultHttpsCertificateName"];

                    if (!string.IsNullOrWhiteSpace(keyVaultCertName))
                    {
                        webBuilder.ConfigureKestrel(serverOptions =>
                        {
                            serverOptions.ConfigureHttpsDefaults(listenOptions =>
                            {
                                // For loading certificates from Azure Key Vault see:
                                // https://github.com/Azure/azure-sdk-for-net/tree/master/sdk/keyvault/Azure.Security.KeyVault.Certificates#retrieve-a-certificate
                                var client = new CertificateClient(vaultUri: new Uri($"https://{Configuration["KeyVaultName"]}.vault.azure.net/"),
                                        credential: new DefaultAzureCredential());

                                var certResponse = client.GetCertificate(keyVaultCertName);
                                if (certResponse != null && certResponse.Value != null)
                                {
                                    X509Certificate2 cert = new X509Certificate2(certResponse.Value.Cer);
                                    Log.Logger.Information($"Certificate successfully loaded from Azure Key Vault, Common Name {cert.FriendlyName}.");
                                    listenOptions.ServerCertificate = cert;
                                }
                            });
                        });
                    }

                    webBuilder.UseStartup<Startup>();
                });

        public static string GetVersion() => $"v{Assembly.GetExecutingAssembly().GetName().Version}";
    }
}

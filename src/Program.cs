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
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using Serilog;
using Serilog.Extensions.Logging;

namespace devcall
{
    /// <summary>
    /// Convenience class to hold the keys that are used to get configuration settings from
    /// the appSettings files and elsewhere.
    /// </summary>
    public static class ConfigKeys
    {
        /// <summary>
        /// The Azure key vault to load secrets and certificates from.
        /// </summary>
        public const string KEY_VAULT_NAME = "KeyVaultName";

        /// <summary>
        /// The name of the certificate in the Azure Key Vault to use for the HTTPS
        /// end point.
        /// </summary>
        public const string KEY_VAULT_HTTPS_CERTIFICATE_NAME = "KeyVaultHttpsCertificateName";
    }

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
                        var secretClient = new SecretClient(new Uri($"https://{builtConfig[ConfigKeys.KEY_VAULT_NAME]}.vault.azure.net/"),
                                                                 new DefaultAzureCredential());
                        config.AddAzureKeyVault(secretClient, new KeyVaultSecretManager());
                    }
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    ConfigureHttps(webBuilder);
                    webBuilder.ConfigureKestrel(options => options.UseSystemd());
                    webBuilder.UseStartup<Startup>();
                });

        public static string GetVersion() => $"v{Assembly.GetExecutingAssembly().GetName().Version}";

        /// <summary>
        /// Configures the Kestrel HTTPS end point. The mechanism is:
        /// - Get the port from appSettings to allow for easy changing,
        /// - Get the certificate from the Azure Key Vault to allow for centralisation and sharing
        ///   amongst mutliple virtual machines.
        /// </summary>
        private static void ConfigureHttps(IWebHostBuilder webBuilder)
        {
            var keyVaultName = Configuration[ConfigKeys.KEY_VAULT_NAME];
            var keyVaultCertName = Configuration[ConfigKeys.KEY_VAULT_HTTPS_CERTIFICATE_NAME];
            Uri vaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");

            var cred = new DefaultAzureCredential();

            if (!string.IsNullOrWhiteSpace(keyVaultCertName))
            {
                webBuilder.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.ConfigureHttpsDefaults(listenOptions =>
                    {
                        var client = new CertificateClient(vaultUri: vaultUri, credential: cred);

                        var certResponse = client.GetCertificate(keyVaultCertName);
                        if (certResponse != null && certResponse.Value != null)
                        {
                            string secretName = certResponse.Value.SecretId.Segments[2].TrimEnd('/');
                            SecretClient secretClient = new SecretClient(vaultUri, cred);
                            KeyVaultSecret secret = secretClient.GetSecret(secretName);

                            byte[] pfx = Convert.FromBase64String(secret.Value);
                            X509Certificate2 cert = new X509Certificate2(pfx);

                            Log.Logger.Information($"Certificate successfully loaded from Azure Key Vault, Common Name {cert.Subject}, has private key {cert.HasPrivateKey}.");
                            listenOptions.ServerCertificate = cert;
                        }
                    });
                });
            }
            else
            {
                webBuilder.ConfigureKestrel(serverOptions =>
                {
                    // TODO: Disable HTPS end point.
                    Log.Logger.Warning($"HTTP end point disabled as no {ConfigKeys.KEY_VAULT_HTTPS_CERTIFICATE_NAME} configuration setting was available.");
                });
            }
        }
    }
}

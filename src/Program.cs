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

namespace signalrtc
{
    public class Program
    {
        /// <summary>
        /// Optional TLS certificate that will be used for web and SIP connections.
        /// </summary>
        public static X509Certificate2 TlsCertificate { get; private set; }

        // This configuration instances is made available early solely for the logging configuration.
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

            Log.Logger.Information($"Starting signalrtc server version {GetVersion()}...");

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

                    var keyVaultCertName = Configuration[ConfigKeys.KEY_VAULT_HTTPS_CERTIFICATE_NAME];
                    if (!string.IsNullOrEmpty(keyVaultCertName))
                    {
                        TlsCertificate = GetCertificateFromKeyVault(keyVaultCertName);
                        Log.Logger.Information($"Certificate successfully loaded from {ConfigKeys.KEY_VAULT_NAME} Azure Key Vault," +
                            $" Common Name {TlsCertificate.Subject}, has private key {TlsCertificate.HasPrivateKey}.");
                    }
                    else
                    {
                        var certificatePath = Configuration[ConfigKeys.HTTPS_CERTIFICATE_PATH];
                        if (!string.IsNullOrEmpty(certificatePath) && File.Exists(certificatePath))
                        {
                            TlsCertificate = new X509Certificate2(certificatePath);
                            Log.Logger.Debug($"Successfully loaded TLS certificate from {certificatePath}, Common Name {TlsCertificate.Subject}," +
                            $" has private key {TlsCertificate.HasPrivateKey}.");
                        }
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
        ///   amongst multiple virtual machines.
        /// </summary>
        private static void ConfigureHttps(IWebHostBuilder webBuilder)
        {
            webBuilder.ConfigureKestrel(serverOptions =>
            {
                if (TlsCertificate != null)
                {
                    serverOptions.ConfigureHttpsDefaults(listenOptions =>
                    {
                        listenOptions.ServerCertificate = TlsCertificate;
                    });
                }
            });
        }

        private static X509Certificate2 GetCertificateFromKeyVault(string keyVaultCertName)
        {
            var keyVaultName = Configuration[ConfigKeys.KEY_VAULT_NAME];
            Uri vaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
            var cred = new DefaultAzureCredential();

            var client = new CertificateClient(vaultUri: vaultUri, credential: cred);

            var certResponse = client.GetCertificate(keyVaultCertName);
            if (certResponse != null && certResponse.Value != null)
            {
                string secretName = certResponse.Value.SecretId.Segments[2].TrimEnd('/');
                SecretClient secretClient = new SecretClient(vaultUri, cred);
                KeyVaultSecret secret = secretClient.GetSecret(secretName);

                byte[] pfx = Convert.FromBase64String(secret.Value);
                return new X509Certificate2(pfx);
            }
            else
            {
                return null;
            }
        }
    }
}

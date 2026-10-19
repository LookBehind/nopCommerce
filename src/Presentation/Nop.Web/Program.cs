using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nop.Services.Configuration;

namespace Nop.Web
{
    public static class Program
    {
        /// <summary>
        /// SHA-256 (hex, uppercase) of the JWT signing key that was committed to this public
        /// repository. The key itself is never referenced here; the guard below refuses to
        /// start if the effective Jwt:Key still matches this compromised value.
        /// </summary>
        private const string CompromisedJwtKeySha256 =
            "5F6341D9AD15D4B11D8CD0F39CBDD84E7D2C2B9DC66AC0EB8922B654B1F11E00";

        /// <returns>A task that represents the asynchronous operation</returns>
        public static async Task Main(string[] args)
        {
            IConfiguration configuration = null;

            //initialize the host
            using var host = Host.CreateDefaultBuilder(args)
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureWebHostDefaults(webBuilder => webBuilder
                    .ConfigureAppConfiguration(config =>
                    {
                        config
                            .AddJsonFile(NopConfigurationDefaults.AppSettingsFilePath, true, true)
                            .AddEnvironmentVariables();

                        configuration = config.Build();
                    })
                    .UseStartup<Startup>())
                .Build();

            EnsureJwtSigningKeyIsSafe(host.Services.GetRequiredService<ILoggerFactory>(), configuration);

            //start the program, a task will be completed when the host starts
            await host.StartAsync();

            //a task will be completed when shutdown is triggered
            await host.WaitForShutdownAsync();
        }

        /// <summary>
        /// Startup guard: the JWT signing key must come from configuration that overrides
        /// appsettings.json (e.g. the Jwt__Key environment variable backed by a Kubernetes
        /// Secret). Fail fast instead of running with a missing or compromised key.
        /// </summary>
        private static void EnsureJwtSigningKeyIsSafe(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            var logger = loggerFactory.CreateLogger(typeof(Program).FullName);
            var key = configuration["Jwt:Key"];

            if (string.IsNullOrWhiteSpace(key))
            {
                logger.LogCritical(
                    "FATAL: Jwt:Key is not configured. Set the Jwt__Key environment variable " +
                    "(e.g. from the mysnacks-jwt Kubernetes Secret) before starting the application.");
                throw new InvalidOperationException(
                    "Jwt:Key is missing. The JWT signing key must be provided via configuration (Jwt__Key).");
            }

            if (string.Equals(Sha256HexUpper(key), CompromisedJwtKeySha256, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogCritical(
                    "FATAL: Jwt:Key matches the signing key that was committed to the public repository " +
                    "and is compromised. Generate a new 64+ character key and provide it via Jwt__Key.");
                throw new InvalidOperationException(
                    "Jwt:Key matches the compromised key previously committed to this repository. Rotate it.");
            }
        }

        private static string Sha256HexUpper(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash);
        }
    }
}

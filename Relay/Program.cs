using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Relay
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static readonly string SettingsOverrideFile = Environment.GetEnvironmentVariable("NOSTR_USER_OVERRIDE")??"user-override.json";

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder => builder.AddEnvironmentVariables("NOSTR_")
                    .AddJsonFile(SettingsOverrideFile, true, true))
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
    }
}
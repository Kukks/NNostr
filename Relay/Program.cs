using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Relay
{
    public partial class Program
    {
        public static void Main(string[] args)
        {
            if (!File.Exists(SettingsOverrideFile))
                File.WriteAllText(SettingsOverrideFile, "{}");
            CreateHostBuilder(args).Build().Run();
        }

        public static readonly string SettingsOverrideFile = Environment.GetEnvironmentVariable("NNOSTR_USER_OVERRIDE")??"user-override.json";

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder => builder.AddEnvironmentVariables("NOSTR_")
                    .AddJsonFile(SettingsOverrideFile, true, true))
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
    }
}
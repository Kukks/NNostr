using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Relay;

public class Nip11Middleware : IMiddleware
{
    private readonly IOptionsMonitor<RelayOptions> _options;

    public Nip11Middleware(IOptionsMonitor<RelayOptions> options)
    {
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (_options.CurrentValue.EnableNip11 && !context.WebSockets.IsWebSocketRequest &&
            context.Request.Headers.Accept.Any(s =>
                s.Equals("application/nostr+json", StringComparison.InvariantCultureIgnoreCase)))
        {
            List<int> nips = new() { 12 };

            if (_options.CurrentValue.EnableNip09)
            {
                nips.Add(9);
            }

            if (_options.CurrentValue.EnableNip11)
            {
                nips.Add(11);
            }
            if (_options.CurrentValue.EnableNip16)
            {
                nips.Add(16);
            }
            if (_options.CurrentValue.EnableNip33)
            {
                nips.Add(33);
            }
            if (_options.CurrentValue.EnableNip20)
            {
                nips.Add(20);
            }
            if (_options.CurrentValue.Nip13Difficulty is not 0)
            {
                nips.Add(13);
            }
            if (_options.CurrentValue.Nip22ForwardLimit is not null || _options.CurrentValue.Nip22BackwardLimit is not null  )
            {
                nips.Add(22);
            }

            
            var response = new Nip11Response()
            {
                Contact = _options.CurrentValue.RelayAlternativeContact,
                Name = _options.CurrentValue.RelayName,
                Description = _options.CurrentValue.RelayDescription,
                PubKey = _options.CurrentValue.AdminPublicKey,
                SupportedNips = nips.ToArray(),
                Software = "https://github.com/Kukks/NNostr",
                Version = typeof(Startup).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()
                    ?.Version
            };
            context.Response.Clear();
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
                
            await  context.Response.WriteAsync(JsonSerializer.Serialize(response));
            return;
        }

        await next.Invoke(context);
    }

    public class Nip11Response
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)][JsonPropertyName("name")] public string? Name { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)][JsonPropertyName("description")] public string? Description { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)][JsonPropertyName("pubkey")] public string? PubKey { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)][JsonPropertyName("contact")] public string? Contact { get; set; }
        [JsonPropertyName("supported_nips")] public int[] SupportedNips { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)][JsonPropertyName("software")] public string Software { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)][JsonPropertyName("version")] public string Version { get; set; }
    }
}
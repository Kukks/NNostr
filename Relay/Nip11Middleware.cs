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
    private readonly IOptions<RelayOptions> _options;

    public Nip11Middleware(IOptions<RelayOptions> options)
    {
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (_options.Value.EnableNip11 && !context.WebSockets.IsWebSocketRequest &&
            context.Request.Headers.Accept.Any(s =>
                s.Equals("application/nostr+json", StringComparison.InvariantCultureIgnoreCase)))
        {
            List<int> nips = new() { 12 };

            if (_options.Value.EnableNip09)
            {
                nips.Add(9);
            }

            if (_options.Value.EnableNip11)
            {
                nips.Add(11);
            }
            if (_options.Value.EnableNip16)
            {
                nips.Add(16);
            }
            if (_options.Value.EnableNip33)
            {
                nips.Add(33);
            }
            if (_options.Value.EnableNip20)
            {
                nips.Add(20);
            }
            if (_options.Value.Nip13Difficulty is not 0)
            {
                nips.Add(13);
            }
            if (_options.Value.Nip22ForwardLimit is not null || _options.Value.Nip22BackwardLimit is not null  )
            {
                nips.Add(22);
            }

            var response = new Nip11Response()
            {
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
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("pubkey")] public string PubKey { get; set; }
        [JsonPropertyName("contact")] public string Contact { get; set; }
        [JsonPropertyName("supported_nips")] public int[] SupportedNips { get; set; }
        [JsonPropertyName("software")] public string Software { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
    }
}
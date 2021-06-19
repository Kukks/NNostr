using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Relay
{
    public class WebsocketMiddleware : IMiddleware
    {
        private readonly WebSocketHandler _webSocketHandler;

        public WebsocketMiddleware(WebSocketHandler webSocketHandler)
        {
            _webSocketHandler = webSocketHandler;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (context.Request.Path.StartsWithSegments(new PathString("/ws")) && context.WebSockets.IsWebSocketRequest)
            {
                var socket = await context.WebSockets.AcceptWebSocketAsync();
                await _webSocketHandler.OnConnected(socket);

                await Receive(socket, async (result, buffer) =>
                {
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        await _webSocketHandler.ReceiveAsync(socket, result, buffer);
                        return;
                    }

                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocketHandler.OnDisconnected(socket);
                        return;
                    }
                });

            }
            else
            {
                
                await next.Invoke(context);
            }

        }

        private async Task Receive(WebSocket socket, Action<WebSocketReceiveResult, string> handleMessage)
        {
            while (socket.State == WebSocketState.Open)
            {
                var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
                string message = null;
                WebSocketReceiveResult result = null;
                try
                {
                    await using (var ms = new MemoryStream())
                    {
                        do
                        {
                            result = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                            ms.Write(buffer.Array, buffer.Offset, result.Count);
                        } while (!result.EndOfMessage);

                        ms.Seek(0, SeekOrigin.Begin);

                        using (var reader = new StreamReader(ms, Encoding.UTF8))
                        {
                            message = await reader.ReadToEndAsync().ConfigureAwait(false);
                        }
                    }

                    handleMessage(result, message);
                }
                catch (WebSocketException e)
                {
                    if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        socket.Abort();
                    }
                }
            }

            await _webSocketHandler.OnDisconnected(socket);
        }
    }
}
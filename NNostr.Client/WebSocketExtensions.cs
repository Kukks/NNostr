using System.Net.WebSockets;
using System.Text;

namespace NNostr.Client
{
    public static class WebSocketExtensions
    {
        public static async Task SendMessageAsync(this WebSocket socket, string message, CancellationToken cancellationToken)
        {
            if (socket.State != WebSocketState.Open)
                return;
            var buffer = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(new Memory<byte>(buffer), WebSocketMessageType.Text, true, cancellationToken);
        }
    }
}
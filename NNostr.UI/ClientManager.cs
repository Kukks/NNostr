using NNostr.Client;
using NNostr.UI;

public class ClientManager : IHostedService
{
    public ClientManager()
    {
    }

    public Task StartAsync(CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public Task StopAsync(CancellationToken token)
    {
        throw new NotImplementedException();
    }


    public class NostrListener : IHostedService
    {
        private readonly NostrClient _nostrClient;

        public NostrListener(NostrClient nostrClient)
        {
            _nostrClient = nostrClient;
        }

        public void Dispose()
        {
            _nostrClient.Dispose();
        }

        public async Task StartAsync(CancellationToken token)
        {
            _nostrClient.NoticeReceived += NoticeReceived;
            _nostrClient.MessageReceived += NoticeReceived;
            await _nostrClient.Connect(token);
        }

        private void NoticeReceived(object? sender, string e)
        {
            
        }
        

        public async Task StopAsync(CancellationToken token)
        {
            await _nostrClient.Disconnect();
        }
        
    }

    public class MessageStore
    {
        
    }
}


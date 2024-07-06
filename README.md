# NNostr
A Nostr Relay and Client written in C#

# Client

## Installation

```dotnet add package NNostr.Client```

## Usage

1.) Initiate a new client
```csharp
var client = new NostrClient("wss://your-relay-url.com");
```
2.) Connect to the relay
```csharp
_ = client.ConnectAsync();
```
3.) Wait for the connection to be established
```csharp
await client.WaitUntilConnected();
```
4.) Listen in for events
```csharp
void OnClientOnEventsReceived(object sender, (string subscriptionId, NostrEvent[] events) args)
    {
        if (args.subscriptionId == "my-subscription-id")
        {
            foreach (var nostrEvent in args.events)
            {
                Console.WriteLine(nostrEvent.Content);
            }
        }
    }
client.EventsReceived += OnClientOnEventsReceived;
```

5.) Subscribe to events
```csharp
//subscribe to all kind 1 events under the subscription id of "my-subscription-id"
client.CreateSubscription("my-subscription-id", new []
        {
            new NostrSubscriptionFilter()
            {
                Kinds = new []{1}
            }
})
```
6.) Send events
```csharp
// create a private key:
var key = ECPrivKey.Create(RandomUtils.GetBytes(32));
// or else load a private key from a hex string
key = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742721d66e40e321ca50b682c27f7422190c14a187525e69e604836a"));
// or load from an nsec string
key = "nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5".FromNIP19Nsec();

// create a new event
var newEvent = new NostrEvent()
{
    Kind = 1,
    Content = "Hello World!"
};
// sign the event
await newEvent.ComputeIdAndSignAsync(key);
// send the event
await client.SendEventsAndWaitUntilReceived(new[] {evt}, CancellationToken.None);
```



# Relay
* Install BTCPay Server through docker deployment https://docs.btcpayserver.org/Docker/#full-installation-for-technical-users
* run the following ssh commands
```bash
cd btcpayserver-docker
BTCPAYGEN_ADDITIONAL_FRAGMENTS="$BTCPAYGEN_ADDITIONAL_FRAGMENTS;opt-add-nostr-relay"
. ./btcpay-setup.sh -i
```
* your relay will be available at your btcpay url `/nostr`


Alternatively, you can configure a docker compose with the following service definition (it is missing nginx, ssl, postgres)
```yml
services:
  nnostr-relay:
    restart: unless-stopped
    image: kukks/nnostr-relay:v0.0.10-1
    container_name: nnostr-relay
    environment:
      NOSTR_CONNECTIONSTRINGS__RelayDatabase: User ID=postgres;Host=postgres;Port=5432;Database=nnostr
      ASPNETCORE_URLS: "http://0.0.0.0:80"
    links:
      - postgres
      - btcpayserver
    volumes:
      - "nnostr_datadir:/datadir"
volumes:
  nnostr_datadir:
```

Alternatively, you can go to `/Relay`, configure `appsettings.json` and `dotnet run` ( you need dotnet runtime installed)

## Configure the relay
The relay is now running, great! DO NOT SHARE THE URL JUST YET! As there is no UI, one must first connect to the relay using a nostr client, and wait for a Notice event from the relay, that states that the relay is not yet configured and a temporary nostr private key has been created that you can use to configure. Simply login using that key, send a DM to yourself with the message `/admin config` to get the confougration options, and then send it back with `/admin update {CONFIG}`. Please note that the admin key (`AdminKey`) MUST be hex format, not `nsec`.

The BTCPay Server integration uses the BTCPay Server Greenfield API. You can generate a new API key by going to `Account => API Keys`, and the only permissions needed are `CanViewInvoices` and `CanCreateInvoice`.The mandatory ones for payments are `BTCPayServerUri`, `BTCPayServerApiKey`, and `BTCPayServerStoreId`. 

Optionally, you can configure BTCPayServer to send a webhook (`Store-Settings->Webhooks`) to `your relay url.com/nostr/btcpay/webhook` for the specific event of `An invoice has been settled`. You are highly recommended to also configure a secret and then to configure it in the relay with `BTCPayServerWebhookSecret` configuration to reduce spam attacks.
### Payment options
The relay allows you to charge a fee per event if wanted using the `EventCost` configuration. This is always in sats. You may also configure the relay to make the cost of an event dynamic, to charge based on the size of the event (1byte = eventcost) by setting `EventCostPerByte` to true.

You can charge pubkeys to be whitelisted on the server by setting `PubKeyCost` to the number of sats to charge.

### Commands
As an admin (the key saved in `AdminKey`), you can DM yourself with the commands `/admin update {config}`, `/admin factory-reset`, `/admin config`

Any user can send a DM to the admin's public key, with the commands `/topup` and `/balance`. 



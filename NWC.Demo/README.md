# 🚀 NWC (Nostr Wallet Connect) Demo

Diese Demo zeigt die Verwendung der NIP-47 Implementierung in NNostr für Nostr Wallet Connect.

## 🎯 Was ist NWC?

Nostr Wallet Connect (NIP-47) ermöglicht es Nostr-Apps, sicher über verschlüsselte Nachrichten mit Lightning Wallets zu interagieren. Es bietet:

- 🔐 End-to-End verschlüsselte Kommunikation
- 🔑 Einzigartige ephemere Schlüssel für jede Verbindung
- ⚡ Standard Lightning Wallet Operationen
- 🌐 Dezentralisiert über Nostr Relays

## 🏗️ Demo Modi

### 1. Mock Wallet Server Demo
Startet einen simulierten Wallet Server, der NWC-Requests beantwortet.

**Features:**
- Generiert Wallet Keys und Connection URI
- Antwortet auf `get_info`, `get_balance`, `make_invoice` etc.
- Zeigt eingehende Requests in der Konsole

### 2. Client Demo
Demonstriert, wie sich ein Client mit einem Wallet verbindet.

**Features:**
- Parst NWC Connection URIs
- Sendet `get_info` und `get_balance` Requests
- Zeigt Wallet-Antworten an

### 3. Full Round-trip Demo
Führt Wallet Server und Client in einem Prozess aus.

## ⚙️ Verwendung

```bash
# Demo starten
cd NWC.Demo/NWC.Demo
dotnet run

# Option auswählen (1-3)
```

## 🔧 Implementierungsdetails

### Wallet Server Setup

```csharp
// Wallet Keys generieren
var walletKey = ECPrivKey.Create(RandomUtils.GetBytes(32));
var walletPubKey = walletKey.CreateXOnlyPubKey();

// Unterstützte Commands definieren
var supportedCommands = new[] {
    "get_info", "get_balance", "make_invoice", "pay_invoice"
};

// Wallet Server erstellen
var walletServer = new NIP47.NostrWalletConnectServer(
    nostrClient,
    walletKey,
    supportedCommands,
    HandleWalletRequest
);

await walletServer.StartAsync(CancellationToken.None);
```

### Client Verbindung

```csharp
// Connection URI parsen
var (walletPubKey, clientSecret, relays, lud16) = NIP47.ParseUri(connectionUri);

// Request senden
var response = await client.SendNIP47Request<GetInfoResponse>(
    walletPubKey,
    clientSecret,
    new GetInfoRequest(),
    CancellationToken.None
);
```

### Request Handler

```csharp
static async Task<Nip47Response> HandleWalletRequest(
    ECXOnlyPubKey clientPubKey,
    Nip47Request request,
    CancellationToken cancellationToken)
{
    return request.Method switch
    {
        "get_info" => CreateSuccessResponse(new GetInfoResponse { ... }),
        "get_balance" => CreateSuccessResponse(new GetBalanceResponse { ... }),
        _ => CreateErrorResponse("NOT_IMPLEMENTED")
    };
}
```

## 📋 Unterstützte NIP-47 Commands

- ✅ `get_info` - Wallet Informationen abrufen
- ✅ `get_balance` - Aktuelles Guthaben anzeigen
- ✅ `make_invoice` - Lightning Invoice erstellen
- ✅ `pay_invoice` - Lightning Invoice bezahlen
- ✅ `pay_keysend` - Keysend Payment senden
- ✅ `list_transactions` - Transaktionshistorie abrufen
- ✅ `lookup_invoice` - Spezifische Invoice nachschlagen

## 🔗 Connection URI Format

```
nostr+walletconnect://{wallet_pubkey}?relay={relay_url}&secret={client_secret}&lud16={optional_lud16}
```

**Beispiel:**
```
nostr+walletconnect://a1b2c3d4...?relay=wss://relay.damus.io&secret=x1y2z3a4...
```

## 🛠️ Technische Details

### Event Types (NIP-47)
- **Info Event** (kind 13194): Wallet Capabilities
- **Request Event** (kind 23194): Client → Wallet Commands
- **Response Event** (kind 23195): Wallet → Client Antworten
- **Notification Event** (kind 23196): Wallet Updates

### Verschlüsselung
- Verwendet NIP-04 für Backward Compatibility
- End-to-End Verschlüsselung mit client/wallet keys
- Sichere Übertragung über öffentliche Nostr Relays

### Error Handling
Standardisierte Error Codes:
- `NOT_IMPLEMENTED` - Command nicht unterstützt
- `INSUFFICIENT_BALANCE` - Nicht genug Guthaben
- `RATE_LIMITED` - Zu viele Requests
- `UNAUTHORIZED` - Fehlende Berechtigung
- `INTERNAL` - Server Fehler

## 🚀 Nächste Schritte

1. **Multi-Payment Commands** hinzufügen (`multi_pay_invoice`, `multi_pay_keysend`)
2. **NIP-44 Verschlüsselung** implementieren
3. **Notification Events** für Live-Updates
4. **Real Lightning Integration** mit LND/CLN
5. **Web Interface** für bessere UX

## 📚 Links

- [NIP-47 Specification (Local)](../docs/nips/47.md) - Always current via git submodule
- [NIP-47 Specification (Online)](https://github.com/nostr-protocol/nips/blob/master/47.md)
- [All NIPs (Local)](../docs/nips/) - Complete Nostr protocol specifications
- [NNostr Client Documentation](../README.md)
- [Nostr Protocol](https://nostr.com)
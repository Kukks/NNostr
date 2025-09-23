using NBitcoin.Secp256k1;
using NNostr.Client;
using NNostr.Client.Protocols;
using System.Text.Json;

namespace NWC.Demo;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 NWC (Nostr Wallet Connect) Demo");
        Console.WriteLine("===================================\n");

        Console.WriteLine("Choose demo mode:");
        Console.WriteLine("1. Mock Wallet Server Demo");
        Console.WriteLine("2. Client Demo (connect to wallet)");
        Console.WriteLine("3. Full Round-trip Demo");
        Console.WriteLine("4. Real NWC Demo (with real wallet)");
        Console.WriteLine("\nEnter choice (1-4): ");

        var choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                await RunWalletServerDemo();
                break;
            case "2":
                await RunClientDemo();
                break;
            case "3":
                await RunFullDemo();
                break;
            case "4":
                await RunRealNWCDemo();
                break;
            default:
                Console.WriteLine("Invalid choice. Running real NWC demo...");
                await RunRealNWCDemo();
                break;
        }
    }

    static async Task RunWalletServerDemo()
    {
        Console.WriteLine("\n🏦 Starting Mock Wallet Server Demo...\n");

        // Create relay connection
        var relay = new Uri("wss://relay.damus.io");
        var client = new NostrClient(relay);

        // Generate wallet keys
        var walletKey = ECPrivKey.Create(new byte[32].Select(x => (byte)Random.Shared.Next(256)).ToArray());
        var walletPubKey = walletKey.CreateXOnlyPubKey();

        Console.WriteLine($"💰 Wallet PubKey: {walletPubKey.ToHex()}");
        Console.WriteLine($"🔗 Relay: {relay}");

        // Define supported commands
        var supportedCommands = new[]
        {
            "get_info",
            "get_balance",
            "make_invoice",
            "pay_invoice",
            "list_transactions"
        };

        // Create wallet server
        var walletServer = new NIP47.NostrWalletConnectServer(
            client,
            walletKey,
            supportedCommands,
            HandleWalletRequest
        );

        try
        {
            await client.Connect();
            await client.WaitUntilConnected();
            Console.WriteLine("✅ Connected to relay");

            await walletServer.StartAsync(CancellationToken.None);
            Console.WriteLine("🟢 Wallet server started and listening for requests...");
            Console.WriteLine("\n📱 Connection URI:");

            // Generate connection URI for clients
            var connectionSecret = ECPrivKey.Create(new byte[32].Select(x => (byte)Random.Shared.Next(256)).ToArray());
            var connectionUri = NIP47.CreateUri(walletPubKey, connectionSecret, relay);
            Console.WriteLine($"📋 {connectionUri}");

            Console.WriteLine("\nPress any key to stop the wallet server...");
            Console.ReadKey();
        }
        finally
        {
            await walletServer.StopAsync(CancellationToken.None);
            await walletServer.DisposeAsync();
            await client.Disconnect();
            Console.WriteLine("🔴 Wallet server stopped");
        }
    }

    static async Task RunClientDemo()
    {
        Console.WriteLine("\n📱 Starting Client Demo...\n");

        Console.WriteLine("Enter NWC connection string (nostr+walletconnect://...):");
        var connectionString = Console.ReadLine();

        if (string.IsNullOrEmpty(connectionString) || !connectionString.StartsWith("nostr+walletconnect://"))
        {
            Console.WriteLine("❌ Invalid connection string. Using demo connection...");
            // Use a demo connection string
            var demoWalletKey = ECPrivKey.Create(new byte[32].Select(x => (byte)Random.Shared.Next(256)).ToArray());
            var demoSecret = ECPrivKey.Create(new byte[32].Select(x => (byte)Random.Shared.Next(256)).ToArray());
            var demoRelay = new Uri("wss://relay.damus.io");
            connectionString = NIP47.CreateUri(demoWalletKey.CreateXOnlyPubKey(), demoSecret, demoRelay).ToString();
        }

        var connectionUri = new Uri(connectionString);
        var (walletPubKey, clientSecret, relays, lud16) = NIP47.ParseUri(connectionUri);

        Console.WriteLine($"💰 Wallet: {walletPubKey.ToHex()}");
        Console.WriteLine($"🔗 Relay: {relays[0]}");

        var client = new NostrClient(relays[0]);

        try
        {
            await client.Connect();
            await client.WaitUntilConnected();
            Console.WriteLine("✅ Connected to relay");

            // Test get_info
            Console.WriteLine("\n🔍 Testing get_info...");
            try
            {
                var infoResponse = await client.SendNIP47Request<NIP47.GetInfoResponse>(
                    walletPubKey,
                    clientSecret,
                    new NIP47.GetInfoRequest(),
                    CancellationToken.None
                );

                Console.WriteLine($"📊 Wallet Info:");
                Console.WriteLine($"   Alias: {infoResponse.Alias}");
                Console.WriteLine($"   Network: {infoResponse.Network}");
                Console.WriteLine($"   Methods: {string.Join(", ", infoResponse.Methods)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ get_info failed: {ex.Message}");
            }

            // Test get_balance
            Console.WriteLine("\n💰 Testing get_balance...");
            try
            {
                var balanceResponse = await client.SendNIP47Request<NIP47.GetBalanceResponse>(
                    walletPubKey,
                    clientSecret,
                    new NIP47.NIP47Request("get_balance"),
                    CancellationToken.None
                );

                Console.WriteLine($"💸 Balance: {balanceResponse.BalanceMsats} msats");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ get_balance failed: {ex.Message}");
            }

        }
        finally
        {
            await client.Disconnect();
            Console.WriteLine("🔴 Client disconnected");
        }
    }

    static async Task RunFullDemo()
    {
        Console.WriteLine("\n🔄 Starting Full Round-trip Demo...\n");
        Console.WriteLine("This demo runs both wallet server and client in the same process.\n");

        // We'll run the wallet server and client in parallel
        var walletTask = Task.Run(async () =>
        {
            await Task.Delay(1000); // Give some time for setup
            await RunMockWalletInBackground();
        });

        var clientTask = Task.Run(async () =>
        {
            await Task.Delay(3000); // Wait for wallet to start
            await RunMockClientInBackground();
        });

        await Task.WhenAll(walletTask, clientTask);
        Console.WriteLine("\n✅ Full demo completed!");
    }

    static async Task RunMockWalletInBackground()
    {
        // Similar to RunWalletServerDemo but simplified for background operation
        var relay = new Uri("wss://relay.damus.io");
        var client = new NostrClient(relay);
        var walletKey = ECPrivKey.Create(new byte[32].Select(x => (byte)Random.Shared.Next(256)).ToArray());

        var walletServer = new NIP47.NostrWalletConnectServer(
            client,
            walletKey,
            new[] { "get_info", "get_balance" },
            HandleWalletRequest
        );

        try
        {
            await client.Connect();
            await client.WaitUntilConnected();
            await walletServer.StartAsync(CancellationToken.None);

            // Keep running for demo
            await Task.Delay(30000); // 30 seconds
        }
        finally
        {
            await walletServer.StopAsync(CancellationToken.None);
            await walletServer.DisposeAsync();
            await client.Disconnect();
        }
    }

    static async Task RunMockClientInBackground()
    {
        // Mock client that connects to the wallet
        Console.WriteLine("🤖 Mock client connecting...");
        await Task.Delay(2000);
        Console.WriteLine("✅ Mock client operations completed");
    }

    // Mock wallet request handler
    static async Task<NIP47.Nip47Response> HandleWalletRequest(
        ECXOnlyPubKey clientPubKey,
        NIP47.Nip47Request request,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"📨 Received request: {request.Method} from {clientPubKey.ToHex()[..8]}...");

        // Mock responses based on method
        return request.Method switch
        {
            "get_info" => CreateSuccessResponse(new NIP47.GetInfoResponse
            {
                Alias = "Mock NWC Wallet",
                Color = "#FF6B6B",
                Pubkey = "mock_wallet_pubkey",
                Network = "regtest",
                BlockHeight = 850000,
                BlockHash = "mock_block_hash",
                Methods = new[] { "get_info", "get_balance", "make_invoice", "pay_invoice" }
            }),

            "get_balance" => CreateSuccessResponse(new NIP47.GetBalanceResponse
            {
                BalanceMsats = 100000000 // 100k sats
            }),

            "make_invoice" => CreateSuccessResponse(new
            {
                invoice = "lnbc1000n1...", // Mock invoice
                payment_hash = "mock_payment_hash"
            }),

            _ => new NIP47.Nip47Response
            {
                ResultType = NIP47.ErrorCodes.NotImplemented,
                Error = new NIP47.Nip47Response.Nip47ResponseError
                {
                    Code = NIP47.ErrorCodes.NotImplemented,
                    Message = $"Method {request.Method} not implemented in mock wallet"
                }
            }
        };
    }

    static NIP47.Nip47Response CreateSuccessResponse(object result)
    {
        var resultJson = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            JsonSerializer.Serialize(result)
        );

        return new NIP47.Nip47Response
        {
            ResultType = "success",
            Result = resultJson
        };
    }

    static async Task RunRealNWCDemo()
    {
        Console.WriteLine("\n⚡ Real NWC Demo - Connect to Real Lightning Wallet\n");
        Console.WriteLine("This demo connects to a real Lightning wallet using NWC.");
        Console.WriteLine("You'll need a real NWC connection string from a compatible wallet.\n");

        Console.WriteLine("📋 Enter your NWC connection string:");
        Console.WriteLine("(Format: nostr+walletconnect://pubkey?relay=...&secret=...)");
        Console.Write("> ");

        var connectionString = Console.ReadLine();

        if (string.IsNullOrEmpty(connectionString) || !connectionString.StartsWith("nostr+walletconnect://"))
        {
            Console.WriteLine("❌ Invalid NWC connection string format!");
            Console.WriteLine("Expected format: nostr+walletconnect://pubkey?relay=...&secret=...");
            return;
        }

        try
        {
            // Parse the connection URI
            var connectionUri = new Uri(connectionString);
            var (walletPubKey, clientSecret, relays, lud16) = NIP47.ParseUri(connectionUri);

            Console.WriteLine($"\n📊 Connection Details:");
            Console.WriteLine($"   Wallet PubKey: {walletPubKey.ToHex()}");
            Console.WriteLine($"   Primary Relay: {relays[0]}");
            if (!string.IsNullOrEmpty(lud16))
                Console.WriteLine($"   LUD16: {lud16}");
            Console.WriteLine($"   Additional Relays: {relays.Length - 1}");

            // Connect to relay
            var client = new NostrClient(relays[0]);
            Console.WriteLine($"\n🔌 Connecting to relay {relays[0]}...");

            await client.Connect();
            await client.WaitUntilConnected();
            Console.WriteLine("✅ Connected to relay successfully!");

            // Test wallet capabilities
            await TestRealWalletCapabilities(client, walletPubKey, clientSecret);

            // Interactive menu for wallet operations
            await RealWalletInteractiveMenu(client, walletPubKey, clientSecret);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error connecting to wallet: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    static async Task TestRealWalletCapabilities(INostrClient client, ECXOnlyPubKey walletPubKey, ECPrivKey clientSecret)
    {
        Console.WriteLine("\n🔍 Testing wallet capabilities...\n");

        // Test 1: Get wallet info
        Console.WriteLine("1️⃣ Testing get_info...");
        try
        {
            var infoResponse = await client.SendNIP47Request<NIP47.GetInfoResponse>(
                walletPubKey,
                clientSecret,
                new NIP47.GetInfoRequest(),
                CancellationToken.None
            );

            Console.WriteLine($"   ✅ Wallet Info Retrieved:");
            Console.WriteLine($"      Alias: {infoResponse.Alias}");
            Console.WriteLine($"      Network: {infoResponse.Network}");
            Console.WriteLine($"      Node PubKey: {infoResponse.Pubkey}");
            Console.WriteLine($"      Block Height: {infoResponse.BlockHeight}");
            Console.WriteLine($"      Supported Methods: {string.Join(", ", infoResponse.Methods)}");
            if (infoResponse.Notifications != null)
                Console.WriteLine($"      Notifications: {string.Join(", ", infoResponse.Notifications)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ get_info failed: {ex.Message}");
        }

        // Test 2: Get balance
        Console.WriteLine("\n2️⃣ Testing get_balance...");
        try
        {
            var balanceResponse = await client.SendNIP47Request<NIP47.GetBalanceResponse>(
                walletPubKey,
                clientSecret,
                new NIP47.NIP47Request("get_balance"),
                CancellationToken.None
            );

            var balanceSats = balanceResponse.BalanceMsats / 1000;
            Console.WriteLine($"   ✅ Current Balance: {balanceResponse.BalanceMsats:N0} msats ({balanceSats:N0} sats)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ get_balance failed: {ex.Message}");
        }

        // Test 3: List recent transactions
        Console.WriteLine("\n3️⃣ Testing list_transactions...");
        try
        {
            var txRequest = new NIP47.ListTransactionsRequest
            {
                Limit = 5, // Last 5 transactions
                Type = null // Both incoming and outgoing
            };

            var txResponse = await client.SendNIP47Request<NIP47.ListTransactionsResponse>(
                walletPubKey,
                clientSecret,
                txRequest,
                CancellationToken.None
            );

            Console.WriteLine($"   ✅ Found {txResponse.Transactions.Length} recent transactions:");
            foreach (var tx in txResponse.Transactions.Take(3)) // Show first 3
            {
                var amountSats = tx.AmountMsats / 1000;
                var feeSats = tx.FeesPaidMsats / 1000;
                var createdAt = DateTimeOffset.FromUnixTimeSeconds(tx.CreatedAt);
                Console.WriteLine($"      • {tx.Type} - {amountSats:N0} sats (fee: {feeSats} sats) - {createdAt:yyyy-MM-dd HH:mm}");
                if (!string.IsNullOrEmpty(tx.Description))
                    Console.WriteLine($"        Description: {tx.Description}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ list_transactions failed: {ex.Message}");
        }
    }

    static async Task RealWalletInteractiveMenu(INostrClient client, ECXOnlyPubKey walletPubKey, ECPrivKey clientSecret)
    {
        while (true)
        {
            Console.WriteLine("\n🎛️  Interactive Wallet Operations");
            Console.WriteLine("=================================");
            Console.WriteLine("1. 📊 Get wallet info");
            Console.WriteLine("2. 💰 Check balance");
            Console.WriteLine("3. 🧾 Create invoice");
            Console.WriteLine("4. 📜 List transactions");
            Console.WriteLine("5. 🔍 Lookup invoice");
            Console.WriteLine("6. ⚡ Pay invoice (ENHANCED)");
            Console.WriteLine("7. 🚪 Exit");
            Console.Write("\nEnter choice (1-7): ");

            var choice = Console.ReadLine();

            try
            {
                switch (choice)
                {
                    case "1":
                        await GetWalletInfo(client, walletPubKey, clientSecret);
                        break;
                    case "2":
                        await GetBalance(client, walletPubKey, clientSecret);
                        break;
                    case "3":
                        await CreateInvoice(client, walletPubKey, clientSecret);
                        break;
                    case "4":
                        await ListTransactions(client, walletPubKey, clientSecret);
                        break;
                    case "5":
                        await LookupInvoice(client, walletPubKey, clientSecret);
                        break;
                    case "6":
                        await PayInvoiceEnhanced(client, walletPubKey, clientSecret);
                        break;
                    case "7":
                        Console.WriteLine("👋 Goodbye!");
                        return;
                    default:
                        Console.WriteLine("❌ Invalid choice. Please try again.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Operation failed: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }
    }

    static async Task GetWalletInfo(INostrClient client, ECXOnlyPubKey walletPubKey, ECPrivKey clientSecret)
    {
        Console.WriteLine("\n📊 Fetching wallet information...");

        var response = await client.SendNIP47Request<NIP47.GetInfoResponse>(
            walletPubKey,
            clientSecret,
            new NIP47.GetInfoRequest(),
            CancellationToken.None
        );

        Console.WriteLine($"✅ Wallet Info:");
        Console.WriteLine($"   Alias: {response.Alias}");
        Console.WriteLine($"   Color: {response.Color}");
        Console.WriteLine($"   Network: {response.Network}");
        Console.WriteLine($"   Node PubKey: {response.Pubkey}");
        Console.WriteLine($"   Block Height: {response.BlockHeight:N0}");
        Console.WriteLine($"   Block Hash: {response.BlockHash}");
        Console.WriteLine($"   Supported Methods: {string.Join(", ", response.Methods)}");
    }

    static async Task GetBalance(INostrClient client, ECXOnlyPubKey walletPubKey, ECPrivKey clientSecret)
    {
        Console.WriteLine("\n💰 Fetching balance...");

        var response = await client.SendNIP47Request<NIP47.GetBalanceResponse>(
            walletPubKey,
            clientSecret,
            new NIP47.NIP47Request("get_balance"),
            CancellationToken.None
        );

        Console.WriteLine($"✅ Current Balance: {LightningHelper.FormatAmount(response.BalanceMsats)}");
    }

    static async Task CreateInvoice(INostrClient client, ECXOnlyPubKey walletPubKey, ECPrivKey clientSecret)
    {
        Console.WriteLine("\n🧾 Creating Lightning Invoice");

        Console.Write("Enter amount in sats: ");
        if (!long.TryParse(Console.ReadLine(), out var amountSats) || amountSats <= 0)
        {
            Console.WriteLine("❌ Invalid amount");
            return;
        }

        Console.Write("Enter description (optional): ");
        var description = Console.ReadLine();

        Console.Write("Enter expiry in seconds (default 3600): ");
        var expiryInput = Console.ReadLine();
        long? expiry = null;
        if (!string.IsNullOrEmpty(expiryInput) && long.TryParse(expiryInput, out var expirySeconds))
        {
            expiry = expirySeconds;
        }

        var request = new NIP47.MakeInvoiceRequest
        {
            AmountMsats = amountSats * 1000,
            Description = string.IsNullOrEmpty(description) ? null : description,
            ExpirySeconds = expiry
        };

        Console.WriteLine($"\n📤 Creating invoice for {amountSats:N0} sats...");

        var response = await client.SendNIP47Request(
            walletPubKey,
            clientSecret,
            request,
            CancellationToken.None
        );

        if (response.Error != null)
        {
            Console.WriteLine($"❌ Failed to create invoice: {response.Error.Message}");
            return;
        }

        var invoiceData = response.Result?.Deserialize<dynamic>();
        Console.WriteLine($"✅ Invoice created successfully!");
        Console.WriteLine($"   Invoice: {invoiceData?.GetProperty("invoice")}");
        Console.WriteLine($"   Payment Hash: {invoiceData?.GetProperty("payment_hash")}");
    }

    static async Task ListTransactions(INostrClient client, ECXOnlyPubKey walletPubKey, ECPrivKey clientSecret)
    {
        Console.WriteLine("\n📜 Listing recent transactions");

        Console.Write("Number of transactions (default 10): ");
        var limitInput = Console.ReadLine();
        var limit = 10;
        if (!string.IsNullOrEmpty(limitInput) && int.TryParse(limitInput, out var parsedLimit))
        {
            limit = parsedLimit;
        }

        var request = new NIP47.ListTransactionsRequest
        {
            Limit = limit
        };

        var response = await client.SendNIP47Request<NIP47.ListTransactionsResponse>(
            walletPubKey,
            clientSecret,
            request,
            CancellationToken.None
        );

        Console.WriteLine($"\n✅ Found {response.Transactions.Length} transactions:");
        Console.WriteLine("📊 Recent Transactions:");
        Console.WriteLine("=" + new string('=', 80));

        foreach (var tx in response.Transactions)
        {
            var amountSats = tx.AmountMsats / 1000;
            var feeSats = tx.FeesPaidMsats / 1000;
            var createdAt = DateTimeOffset.FromUnixTimeSeconds(tx.CreatedAt);
            var typeIcon = tx.Type == "incoming" ? "📈" : "📉";

            Console.WriteLine($"{typeIcon} {tx.Type.ToUpper()} - {amountSats:N0} sats");
            Console.WriteLine($"   Created: {createdAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"   Fee: {feeSats:N0} sats");
            Console.WriteLine($"   Hash: {tx.PaymentHash}");

            if (!string.IsNullOrEmpty(tx.Description))
                Console.WriteLine($"   Description: {tx.Description}");

            if (!string.IsNullOrEmpty(tx.Preimage))
                Console.WriteLine($"   Preimage: {tx.Preimage}");

            Console.WriteLine();
        }
    }

    static async Task LookupInvoice(INostrClient client, ECXOnlyPubKey walletPubKey, ECPrivKey clientSecret)
    {
        Console.WriteLine("\n🔍 Looking up invoice");

        Console.WriteLine("Lookup by:");
        Console.WriteLine("1. Payment hash");
        Console.WriteLine("2. Invoice string");
        Console.Write("Choice (1-2): ");

        var choice = Console.ReadLine();

        var request = new NIP47.LookupInvoiceRequest();

        if (choice == "1")
        {
            Console.Write("Enter payment hash: ");
            request.PaymentHash = Console.ReadLine();
        }
        else if (choice == "2")
        {
            Console.Write("Enter invoice string: ");
            request.Invoice = Console.ReadLine();
        }
        else
        {
            Console.WriteLine("❌ Invalid choice");
            return;
        }

        var response = await client.SendNIP47Request<NIP47.Nip47Transaction>(
            walletPubKey,
            clientSecret,
            request,
            CancellationToken.None
        );

        Console.WriteLine($"✅ Invoice found:");
        Console.WriteLine($"   Type: {response.Type}");
        Console.WriteLine($"   Amount: {response.AmountMsats / 1000:N0} sats");
        Console.WriteLine($"   Created: {DateTimeOffset.FromUnixTimeSeconds(response.CreatedAt):yyyy-MM-dd HH:mm:ss}");

        if (!string.IsNullOrEmpty(response.Description))
            Console.WriteLine($"   Description: {response.Description}");

        if (!string.IsNullOrEmpty(response.Preimage))
            Console.WriteLine($"   Preimage: {response.Preimage}");
    }

    // ENHANCED PayInvoice function with Lightning validation and better error handling
    static async Task PayInvoiceEnhanced(INostrClient client, ECXOnlyPubKey walletPubKey, ECPrivKey clientSecret)
    {
        Console.WriteLine("\n⚡ Enhanced Pay Lightning Invoice");
        Console.WriteLine("=====================================");
        Console.WriteLine("⚠️  WARNING: This will send REAL money!");
        Console.WriteLine("⚠️  Double-check the invoice before proceeding!\n");

        Console.Write("Enter Lightning invoice to pay: ");
        var invoice = Console.ReadLine();

        if (string.IsNullOrEmpty(invoice))
        {
            Console.WriteLine("❌ No invoice provided");
            return;
        }

        // Step 1: Validate invoice format
        Console.WriteLine("\n🔍 Step 1: Validating invoice format...");
        if (!LightningHelper.IsValidLightningInvoice(invoice))
        {
            Console.WriteLine("❌ Invalid Lightning invoice format!");
            Console.WriteLine("   Lightning invoices should start with 'lnbc', 'lntb', or 'lnbcrt'");
            Console.WriteLine("   and be at least 100 characters long.");
            return;
        }
        Console.WriteLine("✅ Invoice format is valid");

        // Step 2: Parse invoice information
        Console.WriteLine("\n📊 Step 2: Parsing invoice information...");
        var (network, amountMsats, description) = LightningHelper.ParseInvoiceInfo(invoice);

        Console.WriteLine($"   Network: {network}");
        if (amountMsats.HasValue)
            Console.WriteLine($"   Amount: {LightningHelper.FormatAmount(amountMsats.Value)}");
        else
            Console.WriteLine("   Amount: Not specified in invoice (tip/donation)");
        Console.WriteLine($"   Description: {description}");

        // Step 3: Show invoice preview and confirm
        Console.WriteLine($"\n📋 Step 3: Invoice Preview");
        Console.WriteLine($"   Invoice: {invoice[..Math.Min(50, invoice.Length)]}...");
        Console.WriteLine($"   Full length: {invoice.Length} characters");

        // Step 4: Final confirmation
        Console.WriteLine("\n⚠️  FINAL CONFIRMATION");
        Console.WriteLine("Type 'PAY NOW' to proceed with the payment:");
        var confirmation = Console.ReadLine();

        if (confirmation != "PAY NOW")
        {
            Console.WriteLine("❌ Payment cancelled - confirmation not received");
            return;
        }

        // Step 5: Check wallet balance first
        Console.WriteLine("\n💰 Step 5: Checking wallet balance...");
        try
        {
            var balanceResponse = await client.SendNIP47Request<NIP47.GetBalanceResponse>(
                walletPubKey,
                clientSecret,
                new NIP47.NIP47Request("get_balance"),
                CancellationToken.None
            );

            Console.WriteLine($"   Current balance: {LightningHelper.FormatAmount(balanceResponse.BalanceMsats)}");

            if (amountMsats.HasValue && balanceResponse.BalanceMsats < amountMsats.Value)
            {
                Console.WriteLine("❌ Insufficient balance for this payment!");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Could not check balance: {ex.Message}");
            Console.WriteLine("Proceeding anyway...");
        }

        // Step 6: Send payment
        Console.WriteLine("\n💸 Step 6: Processing payment...");
        Console.WriteLine("   This may take a few seconds...");

        var request = new NIP47.PayInvoiceRequest
        {
            Invoice = invoice
        };

        try
        {
            var response = await client.SendNIP47Request(
                walletPubKey,
                clientSecret,
                request,
                CancellationToken.None
            );

            if (response.Error != null)
            {
                Console.WriteLine($"\n❌ Payment failed!");
                Console.WriteLine($"   Error Code: {response.Error.Code}");
                Console.WriteLine($"   Error Message: {response.Error.Message}");
                Console.WriteLine($"   Error Description: {LightningHelper.GetPaymentErrorDescription(response.Error.Code)}");

                // Provide specific guidance based on error
                switch (response.Error.Code)
                {
                    case NIP47.ErrorCodes.InsufficientBalance:
                        Console.WriteLine("\n💡 Suggestion: Check your wallet balance (option 2)");
                        break;
                    case NIP47.ErrorCodes.Unauthorized:
                        Console.WriteLine("\n💡 Suggestion: Verify wallet permissions support payments");
                        break;
                    case NIP47.ErrorCodes.NotImplemented:
                        Console.WriteLine("\n💡 Suggestion: This wallet may not support pay_invoice");
                        break;
                    case NIP47.ErrorCodes.Restricted:
                        Console.WriteLine("\n💡 Suggestion: Payment may be restricted by wallet policies");
                        break;
                    default:
                        Console.WriteLine("\n💡 Suggestion: Verify invoice and wallet connection");
                        break;
                }
                return;
            }

            // Success! Parse response
            var paymentResult = response.Result?.Deserialize<NIP47.PayInvoiceResponse>();
            if (paymentResult != null)
            {
                Console.WriteLine($"\n🎉 Payment successful!");
                Console.WriteLine($"   ✅ Preimage: {paymentResult.Preimage}");
                if (paymentResult.FeesPaid.HasValue)
                    Console.WriteLine($"   💸 Fees paid: {paymentResult.FeesPaid.Value:N0} msats");

                if (amountMsats.HasValue)
                    Console.WriteLine($"   💰 Amount sent: {LightningHelper.FormatAmount(amountMsats.Value)}");

                Console.WriteLine($"\n📝 Payment proof saved:");
                Console.WriteLine($"   You can use the preimage as proof of payment");
            }
            else
            {
                Console.WriteLine($"\n✅ Payment appears successful!");
                Console.WriteLine($"   Raw response: {response.Result}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Payment failed with exception:");
            Console.WriteLine($"   Exception: {ex.Message}");
            Console.WriteLine($"   Type: {ex.GetType().Name}");

            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner Exception: {ex.InnerException.Message}");
            }

            Console.WriteLine("\n🔧 Debugging information:");
            Console.WriteLine($"   Invoice length: {invoice.Length}");
            Console.WriteLine($"   Invoice prefix: {invoice[..Math.Min(20, invoice.Length)]}");
            Console.WriteLine("   Suggestion: Verify wallet supports pay_invoice (check get_info)");
        }
    }
}

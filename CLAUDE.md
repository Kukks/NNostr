# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

NNostr is a Nostr relay and client implementation written in C#. The project consists of three main components:

1. **NNostr.Client** - A .NET library for connecting to Nostr relays (multi-target: net6.0, net7.0, net8.0, netstandard2.1)
2. **Relay** - An ASP.NET Core web application that implements a Nostr relay server (net8.0)
3. **NNostr.Tests** - Test suite using xUnit (net8.0)

## Solution Structure

- `NNostr.Client/` - Client library with core Nostr functionality
- `Relay/` - Relay server implementation with Entity Framework and PostgreSQL
- `NNostr.Tests/` - Unit and integration tests
- `NNostr.UI/` - UI components (not currently active in solution)

## Build and Test Commands

```bash
# Build the entire solution
dotnet build

# Run tests
dotnet test

# Build specific projects
dotnet build NNostr.Client/
dotnet build Relay/

# Run the relay server
cd Relay && dotnet run

# Create NuGet package for client
dotnet pack NNostr.Client/
```

## Key Architecture Components

### Client Library (NNostr.Client)
- `NostrClient` - Main client class for connecting to relays via WebSocket
- `NostrEvent` and `BaseNostrEvent<T>` - Core event model with JSON serialization
- `NostrEventTag` - Event tagging system
- `NostrSubscriptionFilter` - Filtering for event subscriptions
- `Protocols/` - NIP implementations (NIP04, NIP05, NIP17, NIP19, NIP28, NIP44, NIP47, NIP59, NIP67)
- `Crypto/` - Cryptographic utilities including AES encryption and Bech32 encoding

### Relay Server (Relay)
- `Program.cs` and `Startup.cs` - ASP.NET Core application setup
- `WebSocketHandler` and `WebsocketMiddleware` - WebSocket connection management
- `MessageHandlers/` - Nostr protocol message processors (Event, Request, Close)
- `Data/RelayDbContext` - Entity Framework database context for PostgreSQL
- `NostrEventService` - Core business logic for event processing
- `BTCPayServerService` - Payment integration for paid relay features
- `AdminChatBot` - Administrative command interface via DMs

### Database Schema
- Uses Entity Framework Core with PostgreSQL
- Migrations located in `Relay/Migrations/`
- Key entities: `RelayNostrEvent`, `Balance`, `BalanceTransaction`

## Configuration

### Relay Configuration
- `appsettings.json` and `appsettings.Development.json` in Relay/
- Environment variables prefixed with `NOSTR_`
- User overrides via `user-override.json` (configurable via `NNOSTR_USER_OVERRIDE`)
- Admin configuration via DM commands to admin pubkey

### Client Configuration
- Multi-target framework support with conditional package references
- Crypto dependencies vary by target framework (HKDF.Standard for netstandard2.1)

## Development Notes

- The client supports connection pooling via `NostrClientPool`
- WebSocket connections use channels for message processing
- JSON serialization uses custom converters for Nostr-specific formats
- The relay supports BTCPay Server integration for payment processing
- Event validation and signature verification is handled by the client library
- Both client and relay implement the core Nostr protocol with various NIP extensions

## Testing

- Uses xUnit with Shouldly assertions
- Integration tests include ASP.NET Core TestHost
- Test coverage includes both client and relay functionality

## 📚 Documentation Structure

- `docs/nips/` - Official Nostr Protocol Specifications (git submodule)
  - `docs/nips/47.md` - NIP-47 (Nostr Wallet Connect) specification
  - `docs/nips/01.md` - NIP-01 (Basic protocol flow)
  - `docs/nips/04.md` - NIP-04 (Encrypted Direct Messages)
  - `docs/nips/44.md` - NIP-44 (Versioned Encryption)
  - And all other current NIPs...

## 📚 Useful References

- [Local NIP-47 Specification](docs/nips/47.md) - Always up-to-date via submodule
- [Official NIPs Repository](https://github.com/nostr-protocol/nips)
- [Nostr Protocol](https://nostr.com)

## Git Submodules

This repository includes the official NIPs repository as a submodule. To initialize and update:

```bash
# Initialize submodules after cloning
git submodule update --init --recursive

# Update to latest NIPs
git submodule update --remote docs/nips
```
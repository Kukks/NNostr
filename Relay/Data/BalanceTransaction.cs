using System;
using NNostr.Client;

namespace Relay.Data;

public class BalanceTransaction
{
    public string Id { get; set; }  
    public string BalanceId { get; set; }
    public string? BalanceTopupId { get; set; }
    public string? EventId { get; set; }
    public long Value { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public RelayNostrEvent? Event { get; set; }
    public BalanceTopup? Topup { get; set; }
    public Balance Balance { get; set; }
}
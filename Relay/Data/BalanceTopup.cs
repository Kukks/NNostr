using System.Collections.Generic;

namespace Relay.Data;

public class BalanceTopup
{
        
    public string Id { get; set; }
    public string BalanceId { get; set; }
    public TopupStatus Status { get; set; }

    public List<BalanceTransaction> BalanceTransactions { get; set; }

    public Balance Balance { get; set; }
    public enum TopupStatus
    {
        Pending,
        Complete,
        Expired,
            
    }
}
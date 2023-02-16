using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Relay.Data;

public class Balance
{
    [Key]
    public string PublicKey { get; set; }
    public long CurrentBalance { get; set; }
    public List<BalanceTransaction> BalanceTransactions { get; set; }
}
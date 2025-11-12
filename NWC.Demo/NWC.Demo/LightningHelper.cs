using System.Text.RegularExpressions;

namespace NWC.Demo;

public static class LightningHelper
{
    private static readonly Regex InvoiceRegex = new Regex(
        @"^(lnbc|lntb|lnbcrt|lntbrt)([0-9]+[munp]?)?1[02-9ac-hj-np-z]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static bool IsValidLightningInvoice(string invoice)
    {
        if (string.IsNullOrEmpty(invoice))
            return false;

        // Basic format validation
        if (!InvoiceRegex.IsMatch(invoice))
            return false;

        // Additional length check (Lightning invoices are typically 200+ characters)
        if (invoice.Length < 100)
            return false;

        return true;
    }

    public static (string network, long? amountMsats, string description) ParseInvoiceInfo(string invoice)
    {
        if (!IsValidLightningInvoice(invoice))
            return ("unknown", null, "Invalid invoice");

        try
        {
            // Extract network from prefix
            var network = invoice.ToLower() switch
            {
                var inv when inv.StartsWith("lnbc") => "mainnet",
                var inv when inv.StartsWith("lntb") => "testnet",
                var inv when inv.StartsWith("lnbcrt") => "regtest",
                var inv when inv.StartsWith("lntbrt") => "testnet-regtest",
                _ => "unknown"
            };

            // Try to extract amount (simplified parsing)
            long? amountMsats = null;
            var amountMatch = Regex.Match(invoice, @"^ln[a-z]+([0-9]+)([munp]?)");
            if (amountMatch.Success && amountMatch.Groups.Count >= 2)
            {
                if (long.TryParse(amountMatch.Groups[1].Value, out var baseAmount))
                {
                    var multiplier = amountMatch.Groups[2].Value switch
                    {
                        "m" => 100_000L,      // milli-bitcoin
                        "u" => 100L,          // micro-bitcoin
                        "n" => 0.1m,          // nano-bitcoin
                        "p" => 0.0001m,       // pico-bitcoin
                        _ => 100_000_000L     // default to bitcoin
                    };

                    if (multiplier >= 1)
                        amountMsats = baseAmount * (long)multiplier;
                    else
                        amountMsats = (long)(baseAmount * multiplier);
                }
            }

            return (network, amountMsats, "Lightning Invoice");
        }
        catch
        {
            return ("unknown", null, "Parsing failed");
        }
    }

    public static string FormatAmount(long amountMsats)
    {
        var sats = amountMsats / 1000;
        var btc = amountMsats / 100_000_000_000.0;

        return $"{amountMsats:N0} msats ({sats:N0} sats, {btc:F8} BTC)";
    }

    public static class PaymentErrors
    {
        public const string InvalidInvoice = "INVALID_INVOICE";
        public const string InvoiceExpired = "INVOICE_EXPIRED";
        public const string InvoiceAlreadyPaid = "INVOICE_ALREADY_PAID";
        public const string RouteNotFound = "ROUTE_NOT_FOUND";
        public const string InsufficientBalance = "INSUFFICIENT_BALANCE";
        public const string PaymentTimeout = "PAYMENT_TIMEOUT";
        public const string PaymentFailed = "PAYMENT_FAILED";
    }

    public static string GetPaymentErrorDescription(string errorCode) => errorCode switch
    {
        PaymentErrors.InvalidInvoice => "The invoice format is invalid or corrupted",
        PaymentErrors.InvoiceExpired => "The invoice has expired and can no longer be paid",
        PaymentErrors.InvoiceAlreadyPaid => "This invoice has already been paid",
        PaymentErrors.RouteNotFound => "No route found to the destination",
        PaymentErrors.InsufficientBalance => "Insufficient balance to complete payment",
        PaymentErrors.PaymentTimeout => "Payment timed out",
        PaymentErrors.PaymentFailed => "Payment failed for unknown reasons",
        _ => $"Unknown error: {errorCode}"
    };
}
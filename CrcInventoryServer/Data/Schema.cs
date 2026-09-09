namespace CrcInventory.Server;

/// <summary>Table names and column layouts. Matches the desktop app databases.</summary>
internal static class Schema
{
    public const string LiveFileName = "crc_inventory.db";
    public const string ArchiveFileName = "old_inventory.db";
    public const string RolesFileName = "admins.json";
    public const string CertificateFileName = "crc-server.pfx";

    public const string PurchaseSales = "purchase_sales";
    public const string Sales = "sales";
    public const string Customers = "customers";
    public const string Vendors = "vendors";
    public const string ItemCodes = "item_codes";
    public const string Invoices = "invoices";
    public const string BankTransactions = "bank_transactions";
    public const string Debits = "debits";
    public const string Credits = "credits";
    public const string PendingChanges = "pending_changes";
    public const string RecordStatus = "Record Status";
    public const string RecordLive = "Live";
    public const string AdminGroup = "Admin";
    public const string ItGroup = "IT";

    /// <summary>Admin group: Settings and user management only; every inventory table is denied.</summary>
    public const string AdminGroupAccessJson =
        "{\"purchases\":false,\"sales\":false,\"invoices\":false,\"customers\":false,\"vendors\":false,\"items\":false,\"banking\":false,\"debits\":false,\"credits\":false,\"reports\":false}";

    public static readonly string[] All =
    {
        PurchaseSales, Sales, Customers, Vendors, ItemCodes,
        Invoices, BankTransactions, Debits, Credits, PendingChanges
    };

    public static readonly HashSet<string> MasterTables = new(StringComparer.OrdinalIgnoreCase)
    {
        Customers, Vendors, ItemCodes
    };

    public static readonly string[] ProcessTables =
    {
        PurchaseSales, Sales, Invoices, BankTransactions, Debits, Credits
    };

    public static bool IsKnownTable(string table) =>
        All.Any(name => name.Equals(table, StringComparison.OrdinalIgnoreCase));

    public static bool IsProcessTable(string table) =>
        ProcessTables.Any(name => name.Equals(table, StringComparison.OrdinalIgnoreCase));

    public static string[] Headers(string table)
    {
        string header = table.ToLowerInvariant() switch
        {
            PurchaseSales =>
                "PO #,Vendor Code,Vendor,Location,Item Code,Description,COO,Pack Size,CS,Volume,Volume Received,Price Paid / LB,Overhead / LB,Freight / LB,Forwarder / LB,Other / LB,Total Cost / LB,Total Cost,Agreement Date,Expected Ship Date,Vendor Terms,Vendor Due Date,Ship Date,Arrival Date,Forwarder,Logistics,Status,Record Status",
            Sales =>
                "PO #,SO #,Customer Code,Customer,Customer Terms,Item Code,Lot #,Description,COO,Pack Size,CS,Volume,Sell Price / LB,Amount,Ship Date,Due Date,Invoice #,Paid,Status,Record Status",
            Customers =>
                "Code,Name,Company,Established,Terms,Credit Limit,Contact Name,Address,Email,Phone,Current Balance,Notes,Description,Routing Number,Account Number,Record Status",
            Vendors =>
                "Code,Name,Company,Type,Terms,Amount,Phone,Contact Name,Current Balance,Notes,Description,Finalized,Routing Number,Account Number,Record Status",
            ItemCodes =>
                "Code,Description,COO,Farmed / Wild,Fresh / Frozen,Proc Country,Species,Scientific Name,Record Status",
            Invoices =>
                "Invoice #,Type,SO #,PO #,Customer Code,Customer,Vendor Code,Vendor,Ship Date,Due Date,Amount,Paid,Outstanding,Status,Payment Date,Payment Method,PDF Created,Invoice Date,Terms,Ship Via,Sales Rep,Sold To,Ship To,Discount,Freight,Tax,Tax Mode,Lines Json,Record Status",
            BankTransactions =>
                "Date,Amount,Method,Reference,Invoice #,SO #,Customer Code,Notes,Record Status",
            Debits =>
                "Debit #,Date Submitted,Vendor Code,Vendor,PO #,Date Received,Date of Issue,Item Code,Description,Reason,LBS Received,Price / LB,Value,LBS Claimed,Claim Value,Claim %,Sales Rep,Vendor Approved,Notes,Record Status",
            Credits =>
                "Credit #,Date Submitted,Customer Code,Customer,Invoice #,Date Received,Date of Issue,Item Code,Description,Reason,LBS Received,Price / LB,Value,LBS Claimed,Claim Value,Claim %,Contact,Approved,Notes,Record Status",
            PendingChanges =>
                "Table,Action,Summary,Match Json,Before Json,After Json,Requested By,Requested At,Status,Reviewed By,Reviewed At",
            _ => ""
        };

        return header.Split(',')
            .Select(h => h.Trim())
            .Where(h => h.Length > 0)
            .ToArray();
    }

    public static bool IsProcessComplete(string table, Dictionary<string, string> values)
    {
        if (table.Equals(PurchaseSales, StringComparison.OrdinalIgnoreCase))
        {
            return IsClosedStatus(Lookup(values, "Status")) ||
                   HasText(values, "Ship Date") ||
                   HasText(values, "Arrival Date") ||
                   HasPositiveNumber(values, "Volume Received");
        }

        if (table.Equals(Sales, StringComparison.OrdinalIgnoreCase))
        {
            return HasText(values, "Invoice #") ||
                   IsClosedStatus(Lookup(values, "Status")) ||
                   HasPositiveNumber(values, "Paid");
        }

        if (table.Equals(Invoices, StringComparison.OrdinalIgnoreCase))
        {
            return IsClosedStatus(Lookup(values, "Status")) ||
                   HasText(values, "Payment Date") ||
                   PaidCoversAmount(values);
        }

        if (table.Equals(BankTransactions, StringComparison.OrdinalIgnoreCase))
            return HasText(values, "Date") || HasText(values, "Amount");

        if (table.Equals(Debits, StringComparison.OrdinalIgnoreCase))
            return IsApproved(Lookup(values, "Vendor Approved"));

        if (table.Equals(Credits, StringComparison.OrdinalIgnoreCase))
            return IsApproved(Lookup(values, "Approved"));

        return !IsProcessTable(table);
    }

    public static string CellValue(Dictionary<string, string> values, string name)
    {
        string value = Lookup(values, name);
        if (name.Equals(RecordStatus, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(value))
            return RecordLive;
        return value;
    }

    public static string Lookup(Dictionary<string, string> values, string name)
    {
        foreach (var pair in values)
        {
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                return pair.Value ?? "";
        }

        if (name.Equals("Customer PO", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in values)
            {
                if (pair.Key.Equals("PO #", StringComparison.OrdinalIgnoreCase))
                    return pair.Value ?? "";
            }
        }

        if (name.Equals("PO #", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in values)
            {
                if (pair.Key.Equals("Lot #", StringComparison.OrdinalIgnoreCase))
                    return pair.Value ?? "";
            }
        }

        return "";
    }

    private static bool HasText(Dictionary<string, string> values, string column) =>
        Lookup(values, column).Trim().Length > 0;

    private static bool HasPositiveNumber(Dictionary<string, string> values, string column)
    {
        string raw = Lookup(values, column).Trim().Replace("$", "").Replace(",", "");
        return decimal.TryParse(raw, out var amount) && amount > 0;
    }

    private static bool PaidCoversAmount(Dictionary<string, string> values)
    {
        if (HasPositiveNumber(values, "Paid") &&
            decimal.TryParse(Lookup(values, "Amount").Trim().Replace("$", "").Replace(",", ""), out var amount) &&
            decimal.TryParse(Lookup(values, "Paid").Trim().Replace("$", "").Replace(",", ""), out var paid) &&
            amount > 0 && paid >= amount)
            return true;

        string outstanding = Lookup(values, "Outstanding").Trim().Replace("$", "").Replace(",", "");
        return HasPositiveNumber(values, "Paid") &&
               decimal.TryParse(outstanding, out var left) &&
               left <= 0;
    }

    private static bool IsClosedStatus(string status)
    {
        string value = (status ?? "").Trim();
        return value.Equals("paid", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("closed", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("complete", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("finished", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("settled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApproved(string value)
    {
        string trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0)
            return false;

        return trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("approved", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("x", StringComparison.OrdinalIgnoreCase);
    }
}

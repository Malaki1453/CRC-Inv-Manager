namespace CrcInventory.Server;

/// <summary>Table names and column layouts. Matches the desktop app databases.</summary>
internal static class Schema
{
    /// <summary>Live SQLite file name (crc_inventory.db).</summary>
    public const string LiveFileName = "crc_inventory.db";
    /// <summary>Archive SQLite file name (old_inventory.db).</summary>
    public const string ArchiveFileName = "old_inventory.db";
    /// <summary>On-disk admin/IT username list.</summary>
    public const string RolesFileName = "admins.json";
    /// <summary>Self-signed TLS certificate PFX in the data folder.</summary>
    public const string CertificateFileName = "crc-server.pfx";

    /// <summary>Purchase-to-sales tracker table.</summary>
    public const string PurchaseSales = "purchase_sales";
    /// <summary>Sales-order table.</summary>
    public const string Sales = "sales";
    /// <summary>Customer master table.</summary>
    public const string Customers = "customers";
    /// <summary>Vendor master table.</summary>
    public const string Vendors = "vendors";
    /// <summary>Item-code master table.</summary>
    public const string ItemCodes = "item_codes";
    /// <summary>Invoice table.</summary>
    public const string Invoices = "invoices";
    /// <summary>Bank-transaction table.</summary>
    public const string BankTransactions = "bank_transactions";
    /// <summary>Debit-claim table.</summary>
    public const string Debits = "debits";
    /// <summary>Credit-claim table.</summary>
    public const string Credits = "credits";
    /// <summary>Pending-change review queue.</summary>
    public const string PendingChanges = "pending_changes";
    /// <summary>Column that marks a row Live vs archived status text.</summary>
    public const string RecordStatus = "Record Status";
    /// <summary>Default Record Status value for live rows.</summary>
    public const string RecordLive = "Live";
    /// <summary>Built-in access group for administrators.</summary>
    public const string AdminGroup = "Admin";
    /// <summary>Built-in access group for IT.</summary>
    public const string ItGroup = "IT";

    /// <summary>Admin group: Settings and user management only; every inventory table is denied.</summary>
    public const string AdminGroupAccessJson =
        "{\"purchases\":false,\"sales\":false,\"invoices\":false,\"customers\":false,\"vendors\":false,\"items\":false,\"banking\":false,\"debits\":false,\"credits\":false,\"reports\":false}";

    /// <summary>Every inventory table created in the live database.</summary>
    public static readonly string[] All =
    {
        PurchaseSales, Sales, Customers, Vendors, ItemCodes,
        Invoices, BankTransactions, Debits, Credits, PendingChanges
    };

    /// <summary>Master tables that are not archived by term (customers, vendors, items).</summary>
    public static readonly HashSet<string> MasterTables = new(StringComparer.OrdinalIgnoreCase)
    {
        Customers, Vendors, ItemCodes
    };

    /// <summary>Process tables whose completed rows move to the archive database.</summary>
    public static readonly string[] ProcessTables =
    {
        PurchaseSales, Sales, Invoices, BankTransactions, Debits, Credits
    };

    /// <summary>True when <paramref name="table"/> is one of the hosted inventory tables.</summary>
    public static bool IsKnownTable(string table) =>
        All.Any(name => name.Equals(table, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="table"/> is archived by <c>ArchiveCompleted</c>.</summary>
    public static bool IsProcessTable(string table) =>
        ProcessTables.Any(name => name.Equals(table, StringComparison.OrdinalIgnoreCase));

    /// <summary>Canonical header list for <paramref name="table"/>, matching the desktop spreadsheet columns.</summary>
    public static string[] Headers(string table)
    {
        string header = table.ToLowerInvariant() switch
        {
            // Purchase tracker columns through Record Status.
            PurchaseSales =>
                "PO #,Vendor Code,Vendor,Location,Item Code,Description,COO,Pack Size,CS,Volume,Price Paid / LB,Overhead / LB,Freight / LB,Freight Company,Forwarder / LB,Other / LB,Total Cost / LB,Total Cost,Agreement Date,Expected Ship Date,Vendor Terms,Vendor Due Date,Ship Date,Arrival Date,Forwarder,Logistics,Status,Record Status",
            // Sales order columns through Record Status.
            Sales =>
                "PO #,SO #,Customer Code,Customer,Customer Terms,Item Code,Description,COO,Pack Size,CS,Volume,Sell Price / LB,Amount,Ship Date,Due Date,Invoice #,Paid,Status,Freight Company,Record Status",
            // Customer master, including sealed routing/account numbers.
            Customers =>
                "Code,Name,Company,Established,Terms,Credit Limit,Contact Name,Address,Email,Phone,Current Balance,Notes,Description,Routing Number,Account Number,Record Status",
            // Vendor master, including sealed routing/account numbers.
            Vendors =>
                "Code,Name,Company,Type,Terms,Amount,Phone,Contact Name,Current Balance,Notes,Description,Finalized,Routing Number,Account Number,Record Status",
            // Item catalog columns.
            ItemCodes =>
                "Code,Description,COO,Farmed / Wild,Fresh / Frozen,Proc Country,Species,Scientific Name,Record Status",
            // Invoice columns including the JSON line blob.
            Invoices =>
                "Invoice #,Type,SO #,PO #,Customer Code,Customer,Vendor Code,Vendor,Ship Date,Due Date,Amount,Paid,Outstanding,Status,Payment Date,Payment Method,Invoice Date,Terms,Ship Via,Sales Rep,Sold To,Ship To,Discount,Freight,Freight Company,Tax,Tax Mode,Lines Json,Record Status",
            // Bank feed / payment rows.
            BankTransactions =>
                "Date,Amount,Method,Reference,Invoice #,SO #,Customer Code,Notes,Record Status",
            // Vendor debit claims.
            Debits =>
                "Debit #,Date Submitted,Vendor Code,Vendor,PO #,Date Received,Date of Issue,Item Code,Description,Reason,LBS Received,Price / LB,Value,LBS Claimed,Claim Value,Claim %,Sales Rep,Vendor Approved,Notes,Record Status",
            // Customer credit claims.
            Credits =>
                "Credit #,Date Submitted,Customer Code,Customer,Invoice #,Date Received,Date of Issue,Item Code,Description,Reason,LBS Received,Price / LB,Value,LBS Claimed,Claim Value,Claim %,Contact,Approved,Notes,Record Status",
            // Review-queue rows (not a process table).
            PendingChanges =>
                "Table,Action,Summary,Match Json,Before Json,After Json,Requested By,Requested At,Status,Reviewed By,Reviewed At",
            // Unknown table names have no headers.
            _ => ""
        };

        return header.Split(',')
            .Select(h => h.Trim())
            .Where(h => h.Length > 0)
            .ToArray();
    }

    /// <summary>True when a process row is complete enough to move to the archive database.</summary>
    public static bool IsProcessComplete(string table, Dictionary<string, string> values)
    {
        // Purchases complete once shipped/arrived or marked closed.
        if (table.Equals(PurchaseSales, StringComparison.OrdinalIgnoreCase))
        {
            return IsClosedStatus(Lookup(values, "Status")) ||
                   HasText(values, "Ship Date") ||
                   HasText(values, "Arrival Date");
        }

        // Sales complete once invoiced, paid, or closed.
        if (table.Equals(Sales, StringComparison.OrdinalIgnoreCase))
        {
            return HasText(values, "Invoice #") ||
                   IsClosedStatus(Lookup(values, "Status")) ||
                   HasPositiveNumber(values, "Paid");
        }

        // Invoices complete once paid/closed or paid covers the amount.
        if (table.Equals(Invoices, StringComparison.OrdinalIgnoreCase))
        {
            return IsClosedStatus(Lookup(values, "Status")) ||
                   HasText(values, "Payment Date") ||
                   PaidCoversAmount(values);
        }

        // Bank rows complete once they have a date or amount.
        if (table.Equals(BankTransactions, StringComparison.OrdinalIgnoreCase))
            return HasText(values, "Date") || HasText(values, "Amount");

        // Debits complete when the vendor approved the claim.
        if (table.Equals(Debits, StringComparison.OrdinalIgnoreCase))
            return IsApproved(Lookup(values, "Vendor Approved"));

        // Credits complete when the claim is approved.
        if (table.Equals(Credits, StringComparison.OrdinalIgnoreCase))
            return IsApproved(Lookup(values, "Approved"));

        return !IsProcessTable(table);
    }

    /// <summary>Looks up a cell, defaulting blank Record Status to Live so old rows still display.</summary>
    public static string CellValue(Dictionary<string, string> values, string name)
    {
        string value = Lookup(values, name);
        // Older files omitted Record Status; treat blank as Live so inserts stay consistent.
        if (name.Equals(RecordStatus, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(value))
            return RecordLive;
        return value;
    }

    /// <summary>Case-insensitive cell lookup, with PO # / Customer PO / Lot # aliases.</summary>
    public static string Lookup(Dictionary<string, string> values, string name)
    {
        foreach (var pair in values)
        {
            // Exact (case-insensitive) header match wins.
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                return pair.Value ?? "";
        }

        // On a sale row (has SO #, no invoice Type), Invoice # is the customer PO.
        if (name.Equals("Customer PO", StringComparison.OrdinalIgnoreCase) &&
            HasKey(values, "SO #") &&
            !HasKey(values, "Type"))
        {
            foreach (var pair in values)
            {
                if (pair.Key.Equals("Invoice #", StringComparison.OrdinalIgnoreCase))
                    return pair.Value ?? "";
            }
        }

        // Older sales rows stored the purchase PO in Lot #.
        if (name.Equals("PO #", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in values)
            {
                if (pair.Key.Equals("Lot #", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(pair.Value))
                    return pair.Value ?? "";
            }
        }

        return "";
    }

    private static bool HasKey(Dictionary<string, string> values, string name)
    {
        foreach (var pair in values)
        {
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>True when the named column has non-whitespace text.</summary>
    private static bool HasText(Dictionary<string, string> values, string column) =>
        Lookup(values, column).Trim().Length > 0;

    /// <summary>True when the named column parses as a positive money amount.</summary>
    private static bool HasPositiveNumber(Dictionary<string, string> values, string column)
    {
        string raw = Lookup(values, column).Trim().Replace("$", "").Replace(",", "");
        return decimal.TryParse(raw, out var amount) && amount > 0;
    }

    /// <summary>True when Paid covers Amount, or Paid is set and Outstanding is zero or negative.</summary>
    private static bool PaidCoversAmount(Dictionary<string, string> values)
    {
        // Paid >= Amount is the usual "invoice is settled" test.
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

    /// <summary>True for status words that mean the process row is finished.</summary>
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

    /// <summary>True for common "approved" spellings used on debit/credit rows.</summary>
    private static bool IsApproved(string value)
    {
        string trimmed = (value ?? "").Trim();
        // Blank is not approval; the row stays live.
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

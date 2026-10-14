using System.Globalization;
using CrcInventory.Protocol;

namespace CrcInventory.Server;

internal sealed partial class InventoryStore
{
    private static readonly string[] BankFeedColumns =
    {
        "Account",
        "Description",
        "External Id",
        "Type",
        "Vendor Code",
        "PO #"
    };

    /// <summary>Linked Plaid accounts with decrypted tokens for host-side sync only.</summary>
    public List<(long Id, string Name, BankLinkDto Link)> ListLinkedBankAccounts()
    {
        var list = new List<(long, string, BankLinkDto)>();
        foreach (var account in ListBankAccounts())
        {
            var link = GetBankLiveLink(account.Id);
            if (!link.Linked)
                continue;
            list.Add((account.Id, account.Name, link));
        }

        return list;
    }

    /// <summary>Insert new Plaid/OFX-style lines; skip duplicates. Returns how many were written.</summary>
    public int ImportBankTxns(IEnumerable<BankTxnDto> rows, string accountName)
    {
        EnsureColumns(Schema.BankTransactions, BankFeedColumns, viewOld: false);
        var seen = LoadExistingBankKeys();
        var invoices = Read(Schema.Invoices, viewOld: false);
        var sales = Read(Schema.Sales, viewOld: false);
        var purchases = Read(Schema.PurchaseSales, viewOld: false);
        var customers = Read(Schema.Customers, viewOld: false);
        var vendors = Read(Schema.Vendors, viewOld: false);

        int added = 0;
        foreach (var row in rows)
        {
            string key = BankRowKey(accountName, row);
            if (!seen.Add(key))
                continue;

            MatchBankMemo(
                row,
                invoices,
                sales,
                purchases,
                customers,
                vendors,
                out string invoice,
                out string so,
                out string customer,
                out string vendor,
                out string po);
            Insert(Schema.BankTransactions, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Date"] = row.Date.ToString("yyyy-MM-dd"),
                ["Amount"] = row.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                ["Method"] = row.Method ?? "",
                ["Reference"] = row.Reference ?? "",
                ["Invoice #"] = invoice,
                ["SO #"] = so,
                ["Customer Code"] = customer,
                ["Vendor Code"] = vendor,
                ["PO #"] = po,
                ["Notes"] = row.Description ?? "",
                ["Account"] = accountName,
                ["Description"] = row.Description ?? "",
                ["External Id"] = row.ExternalId ?? "",
                ["Type"] = string.IsNullOrWhiteSpace(row.Type)
                    ? (row.Amount >= 0 ? "Deposit" : "Withdrawal")
                    : row.Type,
                [Schema.RecordStatus] = Schema.RecordLive
            });
            added++;
        }

        return added;
    }

    /// <summary>External Id and date+amount+description keys already stored for each account.</summary>
    private HashSet<string> LoadExistingBankKeys()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Read(Schema.BankTransactions, viewOld: false))
        {
            string ext = Schema.Lookup(row, "External Id").Trim();
            string account = Schema.Lookup(row, "Account").Trim();
            if (ext.Length > 0)
                keys.Add("id|" + account + "|" + ext);

            keys.Add(
                "row|" + account + "|" +
                Schema.Lookup(row, "Date").Trim() + "|" +
                Schema.Lookup(row, "Amount").Trim() + "|" +
                Schema.Lookup(row, "Description").Trim());
        }

        return keys;
    }

    /// <summary>Dedup key: External Id when present, otherwise date+amount+description.</summary>
    private static string BankRowKey(string account, BankTxnDto row)
    {
        if (!string.IsNullOrWhiteSpace(row.ExternalId))
            return "id|" + account + "|" + row.ExternalId;

        return "row|" + account + "|" +
               row.Date.ToString("yyyy-MM-dd") + "|" +
               row.Amount.ToString("0.00", CultureInfo.InvariantCulture) + "|" +
               (row.Description ?? "");
    }

    /// <summary>Fill invoice/SO/customer or vendor/PO from the memo when possible.</summary>
    private static void MatchBankMemo(
        BankTxnDto row,
        List<Dictionary<string, string>> invoices,
        List<Dictionary<string, string>> sales,
        List<Dictionary<string, string>> purchases,
        List<Dictionary<string, string>> customers,
        List<Dictionary<string, string>> vendors,
        out string invoice,
        out string so,
        out string customer,
        out string vendor,
        out string po)
    {
        invoice = "";
        so = "";
        customer = "";
        vendor = "";
        po = "";
        string hay = ((row.Description ?? "") + " " + (row.Reference ?? "")).Trim();
        if (hay.Length == 0)
            return;

        if (row.Amount < 0)
        {
            TryMatchVendor(hay, purchases, vendors, ref vendor, ref po);
            if (vendor.Length == 0)
                TryMatchCustomer(hay, invoices, sales, customers, ref invoice, ref so, ref customer);
            return;
        }

        TryMatchCustomer(hay, invoices, sales, customers, ref invoice, ref so, ref customer);
        if (customer.Length == 0)
            TryMatchVendor(hay, purchases, vendors, ref vendor, ref po);
    }

    private static void TryMatchCustomer(
        string hay,
        List<Dictionary<string, string>> invoices,
        List<Dictionary<string, string>> sales,
        List<Dictionary<string, string>> customers,
        ref string invoice,
        ref string so,
        ref string customer)
    {
        foreach (var rec in invoices)
        {
            string number = Schema.Lookup(rec, "Invoice #").Trim();
            if (number.Length == 0 || !ContainsToken(hay, number))
                continue;
            invoice = number;
            so = Schema.Lookup(rec, "SO #").Trim();
            customer = FirstCell(rec, "Customer Code", "Cust ID");
            return;
        }

        foreach (var rec in sales)
        {
            string saleSo = Schema.Lookup(rec, "SO #").Trim();
            string saleInv = Schema.Lookup(rec, "Invoice #").Trim();
            if (saleSo.Length > 0 && ContainsToken(hay, saleSo))
            {
                so = saleSo;
                invoice = saleInv;
                customer = Schema.Lookup(rec, "Customer Code");
                return;
            }

            if (saleInv.Length > 0 && ContainsToken(hay, saleInv))
            {
                invoice = saleInv;
                so = saleSo;
                customer = Schema.Lookup(rec, "Customer Code");
                return;
            }
        }

        foreach (var rec in customers)
        {
            if (!MemoMentions(hay, Schema.Lookup(rec, "Name")) &&
                !MemoMentions(hay, Schema.Lookup(rec, "Company")))
                continue;
            customer = Schema.Lookup(rec, "Code");
            return;
        }
    }

    private static void TryMatchVendor(
        string hay,
        List<Dictionary<string, string>> purchases,
        List<Dictionary<string, string>> vendors,
        ref string vendor,
        ref string po)
    {
        foreach (var rec in purchases)
        {
            string number = Schema.Lookup(rec, "PO #").Trim();
            if (number.Length == 0 || !ContainsToken(hay, number))
                continue;
            po = number;
            vendor = Schema.Lookup(rec, "Vendor Code").Trim();
            return;
        }

        foreach (var rec in vendors)
        {
            if (!MemoMentions(hay, Schema.Lookup(rec, "Name")) &&
                !MemoMentions(hay, Schema.Lookup(rec, "Company")))
                continue;
            vendor = Schema.Lookup(rec, "Code");
            return;
        }
    }

    private static string FirstCell(Dictionary<string, string> rec, params string[] names)
    {
        foreach (var name in names)
        {
            string value = Schema.Lookup(rec, name).Trim();
            if (value.Length > 0)
                return value;
        }

        return "";
    }

    private static bool MemoMentions(string hay, string token)
    {
        token = (token ?? "").Trim();
        if (token.Length < 4)
            return false;
        return hay.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsToken(string hay, string token)
    {
        if (token.Length < 3)
            return false;
        return hay.Contains(token, StringComparison.OrdinalIgnoreCase);
    }
}

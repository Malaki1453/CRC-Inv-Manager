using System.Globalization;
using System.Text.RegularExpressions;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Reads bank-export files (OFX, QFX, CSV) into bank_transactions.
    /// Skips duplicates and tries to attach Invoice #, SO #, Customer Code, Vendor Code, and PO #.
    /// </summary>
    internal static class BankFeed
    {
        public static readonly string[] ExtraColumns =
        {
            "Account",
            "Description",
            "External Id",
            "Type",
            "Vendor Code",
            "PO #"
        };

        /// <summary>One parsed bank line before it is written to the database.</summary>
        public sealed class Parsed
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public string Description { get; set; } = "";
            public string Reference { get; set; } = "";
            public string Method { get; set; } = "";
            public string ExternalId { get; set; } = "";
            public string Type { get; set; } = "";
        }

        /// <summary>Add Account, Description, External Id, Type, Vendor Code, and PO # if missing.</summary>
        public static void EnsureSchema()
        {
            SqliteInventory.EnsureColumns(DataFiles.BankTransactions, ExtraColumns);
        }

        /// <summary>Parse OFX/QFX or a bank CSV. Returns an error if the file cannot be read.</summary>
        public static bool TryParseFile(string path, out List<Parsed> rows, out string error)
        {
            rows = new List<Parsed>();
            error = "";
            // The picker can return a path that was deleted before we read it.
            if (!File.Exists(path))
            {
                error = "The selected file could not be found.";
                return false;
            }

            string text = File.ReadAllText(path);
            string ext = Path.GetExtension(path);
            // OFX/QFX is preferred; CSV is the fallback for bank downloads.
            if (LooksLikeOfx(text, ext))
            {
                rows = ParseOfx(text);
                // An empty statement is treated as a parse failure, not a successful no-op import.
                if (rows.Count == 0)
                {
                    error = "No transactions were found in that OFX/QFX file.";
                    return false;
                }

                return true;
            }

            rows = ParseCsv(path);
            // CSV without Date + Amount columns cannot become bank lines.
            if (rows.Count == 0)
            {
                error =
                    "No transactions were found. Use a bank OFX/QFX download, or a CSV with Date and Amount columns.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Insert new rows for this account. Existing External Id (or date+amount+description) lines are skipped.
        /// </summary>
        public static int Import(IEnumerable<Parsed> rows, string accountName, out int skipped)
        {
            EnsureSchema();
            skipped = 0;
            int added = 0;
            var seen = LoadExistingKeys();
            var invoices = DataFiles.ReadRecords(DataFiles.Invoices);
            var sales = DataFiles.ReadRecords(DataFiles.Sales);
            var purchases = DataFiles.ReadRecords(DataFiles.PurchaseSales);
            var customers = DataFiles.ReadRecords(DataFiles.Customers);
            var vendors = DataFiles.ReadRecords(DataFiles.Vendors);

            foreach (var row in rows)
            {
                string key = RowKey(accountName, row);
                // Skip External Id or date+amount+description already in this account.
                if (!seen.Add(key))
                {
                    skipped++;
                    continue;
                }

                MatchToDocuments(
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
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Date"] = row.Date.ToString("yyyy-MM-dd"),
                    ["Amount"] = row.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    ["Method"] = row.Method,
                    ["Reference"] = row.Reference,
                    ["Invoice #"] = invoice,
                    ["SO #"] = so,
                    ["Customer Code"] = customer,
                    ["Vendor Code"] = vendor,
                    ["PO #"] = po,
                    ["Notes"] = row.Description,
                    ["Account"] = accountName,
                    ["Description"] = row.Description,
                    ["External Id"] = row.ExternalId,
                    ["Type"] = row.Type.Length > 0
                        ? row.Type
                        : row.Amount >= 0 ? "Deposit" : "Withdrawal",
                    [DataFiles.RecordStatus] = DataFiles.RecordLive
                };
                SqliteInventory.Insert(DataFiles.BankTransactions, values);
                added++;
            }

            // Refresh Banking and party history only when something new was written.
            if (added > 0)
                DataFiles.NotifyDataChanged();
            return added;
        }

        /// <summary>Bank lines for a customer when company is unknown (payments grid).</summary>
        public static List<Dictionary<string, string>> PaymentsForCustomer(string code, string name) =>
            TransactionsForCustomer(code, name, "");

        /// <summary>Bank lines tagged to this customer, or whose memo / invoice / SO matches them.</summary>
        public static List<Dictionary<string, string>> TransactionsForCustomer(
            string code,
            string name,
            string company)
        {
            EnsureSchema();
            var invoiceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var soKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in DataFiles.ReadRecords(DataFiles.Invoices))
            {
                // Only invoices for this customer contribute matching invoice/SO numbers.
                if (!DataFiles.MatchesCustomer(rec, code, name))
                    continue;
                AddKey(invoiceKeys, DataFiles.GetRecord(rec, "Invoice #"));
                AddKey(soKeys, DataFiles.GetRecord(rec, "SO #"));
            }

            foreach (var rec in DataFiles.ReadRecords(DataFiles.Sales))
            {
                // Sales rows supply extra invoice/SO numbers for the same customer.
                if (!DataFiles.MatchesCustomer(rec, code, name))
                    continue;
                AddKey(invoiceKeys, DataFiles.GetRecord(rec, "Invoice #"));
                AddKey(soKeys, DataFiles.GetRecord(rec, "SO #"));
            }

            return FilterRows(vendor: false, code, name, company, invoiceKeys, soKeys);
        }

        /// <summary>Bank lines tagged to this vendor, or whose memo / PO matches them.</summary>
        public static List<Dictionary<string, string>> TransactionsForVendor(
            string code,
            string name,
            string company)
        {
            EnsureSchema();
            var poKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in DataFiles.ReadRecords(DataFiles.PurchaseSales))
            {
                // Only this vendor's POs are used to match bank memos.
                if (!DataFiles.MatchesVendor(rec, code, name))
                    continue;
                AddKey(poKeys, DataFiles.GetRecord(rec, "PO #"));
            }

            return FilterRows(vendor: true, code, name, company, poKeys, null);
        }

        /// <summary>Fill a read-only grid of bank lines for a customer or vendor form.</summary>
        public static void FillPartyGrid(
            DataGridView grid,
            bool vendor,
            string code,
            string name,
            string company)
        {
            grid.Columns.Clear();
            grid.Rows.Clear();
            grid.Columns.Add("Date", "Date");
            grid.Columns.Add("Amount", "Amount");
            grid.Columns.Add("Account", "Account");
            grid.Columns.Add("Type", "Type");
            grid.Columns.Add(vendor ? "PO #" : "Invoice #", vendor ? "PO #" : "Invoice #");
            grid.Columns.Add("Description", "Description");

            var rows = vendor
                ? TransactionsForVendor(code, name, company)
                : TransactionsForCustomer(code, name, company);
            foreach (var row in rows)
            {
                grid.Rows.Add(
                    DataFiles.GetRecord(row, "Date"),
                    DataFiles.GetRecord(row, "Amount"),
                    DataFiles.GetRecord(row, "Account"),
                    DataFiles.GetRecordAny(row, "Type", "Method"),
                    vendor
                        ? DataFiles.GetRecordAny(row, "PO #", "Reference")
                        : DataFiles.GetRecordAny(row, "Invoice #", "SO #"),
                    DataFiles.GetRecordAny(row, "Description", "Notes"));
            }

            // Keep a placeholder so the history grid is not a blank hole.
            if (grid.Rows.Count == 0)
                grid.Rows.Add("No bank transactions yet", "", "", "", "", "");
        }

        /// <summary>External Id and date+amount+description keys already stored for each account.</summary>
        private static HashSet<string> LoadExistingKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in DataFiles.ReadRecords(DataFiles.BankTransactions))
            {
                string ext = DataFiles.GetRecord(row, "External Id").Trim();
                string account = DataFiles.GetRecord(row, "Account").Trim();
                // FITID is the bank's unique line id when the file provides one.
                if (ext.Length > 0)
                    keys.Add("id|" + account + "|" + ext);

                keys.Add(
                    "row|" + account + "|" +
                    DataFiles.GetRecord(row, "Date").Trim() + "|" +
                    DataFiles.GetRecord(row, "Amount").Trim() + "|" +
                    DataFiles.GetRecord(row, "Description").Trim());
            }

            return keys;
        }

        /// <summary>Dedup key: External Id when present, otherwise date+amount+description.</summary>
        private static string RowKey(string account, Parsed row)
        {
            // Prefer FITID so a memo change does not re-import the same bank line.
            if (row.ExternalId.Length > 0)
                return "id|" + account + "|" + row.ExternalId;

            return "row|" + account + "|" +
                   row.Date.ToString("yyyy-MM-dd") + "|" +
                   row.Amount.ToString("0.00", CultureInfo.InvariantCulture) + "|" +
                   row.Description;
        }

        /// <summary>Bank rows tagged to this party, or whose memo/invoice/PO matches them.</summary>
        private static List<Dictionary<string, string>> FilterRows(
            bool vendor,
            string code,
            string name,
            string company,
            HashSet<string> primaryKeys,
            HashSet<string>? soKeys)
        {
            var list = new List<Dictionary<string, string>>();
            string codeColumn = vendor ? "Vendor Code" : "Customer Code";
            foreach (var row in DataFiles.ReadRecords(DataFiles.BankTransactions))
            {
                string rowCode = DataFiles.GetRecord(row, codeColumn).Trim();
                // Direct tag from import matching is the strongest link.
                if (code.Length > 0 && rowCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(row);
                    continue;
                }

                string hay = DataFiles.GetRecord(row, "Description") + " " +
                             DataFiles.GetRecord(row, "Notes") + " " +
                             DataFiles.GetRecord(row, "Reference") + " " +
                             DataFiles.GetRecord(row, "Invoice #") + " " +
                             DataFiles.GetRecord(row, "SO #") + " " +
                             DataFiles.GetRecord(row, "PO #");
                // Memo/name match covers lines that were never tagged to a code.
                if (MemoMentions(hay, name) || MemoMentions(hay, company))
                {
                    list.Add(row);
                    continue;
                }

                // Vendor history matches on PO #; customer history uses invoice/SO.
                if (vendor)
                {
                    string po = DataFiles.GetRecordAny(row, "PO #", "Reference");
                    // A PO on this vendor's purchases means the bank line belongs here.
                    if (po.Length > 0 && primaryKeys.Contains(po))
                        list.Add(row);
                    continue;
                }

                string invoice = DataFiles.GetRecord(row, "Invoice #").Trim();
                string so = DataFiles.GetRecord(row, "SO #").Trim();
                // Invoice or SO already stored on the bank line for this customer.
                if ((invoice.Length > 0 && primaryKeys.Contains(invoice)) ||
                    (so.Length > 0 && soKeys != null && soKeys.Contains(so)))
                    list.Add(row);
            }

            return list;
        }

        /// <summary>Fill invoice/SO/customer or vendor/PO from the memo when possible.</summary>
        private static void MatchToDocuments(
            Parsed row,
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
            string hay = (row.Description + " " + row.Reference).Trim();
            // Nothing to search when the bank line has no memo or check number.
            if (hay.Length == 0)
                return;

            bool preferVendor = row.Amount < 0;
            // Withdrawals are usually vendor payments; deposits are usually customer receipts.
            if (preferVendor)
            {
                TryMatchVendor(hay, purchases, vendors, ref vendor, ref po);
                // Fall back to a customer match if the memo is actually a receipt.
                if (vendor.Length == 0)
                    TryMatchCustomer(hay, invoices, sales, customers, ref invoice, ref so, ref customer);
            }
            // Deposits are usually customer receipts.
            else
            {
                TryMatchCustomer(hay, invoices, sales, customers, ref invoice, ref so, ref customer);
                // Fall back to a vendor match if the deposit memo is a refund/PO.
                if (customer.Length == 0)
                    TryMatchVendor(hay, purchases, vendors, ref vendor, ref po);
            }
        }

        /// <summary>Match memo tokens to invoice #, SO #, or customer name/company.</summary>
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
                string number = DataFiles.GetRecord(rec, "Invoice #").Trim();
                // Skip invoices whose number is not in the memo.
                if (number.Length == 0 || !ContainsToken(hay, number))
                    continue;
                invoice = number;
                so = DataFiles.GetRecord(rec, "SO #").Trim();
                customer = DataFiles.GetRecordAny(rec, "Customer Code", "Cust ID");
                return;
            }

            foreach (var rec in sales)
            {
                string saleSo = DataFiles.GetRecord(rec, "SO #").Trim();
                string saleInv = DataFiles.GetRecord(rec, "Invoice #").Trim();
                // SO # in the memo is enough to tag the customer even without an invoice.
                if (saleSo.Length > 0 && ContainsToken(hay, saleSo))
                {
                    so = saleSo;
                    invoice = saleInv;
                    customer = DataFiles.GetRecord(rec, "Customer Code");
                    return;
                }

                // Invoice # on a sales row is the next-best customer match.
                if (saleInv.Length > 0 && ContainsToken(hay, saleInv))
                {
                    invoice = saleInv;
                    so = saleSo;
                    customer = DataFiles.GetRecord(rec, "Customer Code");
                    return;
                }
            }

            foreach (var rec in customers)
            {
                // Name/company mentions are last because they are weaker than document numbers.
                if (!MemoMentions(hay, DataFiles.GetRecord(rec, "Name")) &&
                    !MemoMentions(hay, DataFiles.GetRecord(rec, "Company")))
                    continue;
                customer = DataFiles.GetRecord(rec, "Code");
                return;
            }
        }

        /// <summary>Match memo tokens to PO # or vendor name/company.</summary>
        private static void TryMatchVendor(
            string hay,
            List<Dictionary<string, string>> purchases,
            List<Dictionary<string, string>> vendors,
            ref string vendor,
            ref string po)
        {
            foreach (var rec in purchases)
            {
                string number = DataFiles.GetRecord(rec, "PO #").Trim();
                // Skip POs whose number is not in the memo.
                if (number.Length == 0 || !ContainsToken(hay, number))
                    continue;
                po = number;
                vendor = DataFiles.GetRecord(rec, "Vendor Code").Trim();
                return;
            }

            foreach (var rec in vendors)
            {
                // Name/company mentions when no PO number was found.
                if (!MemoMentions(hay, DataFiles.GetRecord(rec, "Name")) &&
                    !MemoMentions(hay, DataFiles.GetRecord(rec, "Company")))
                    continue;
                vendor = DataFiles.GetRecord(rec, "Code");
                return;
            }
        }

        /// <summary>Add a non-blank invoice, SO, or PO number to the match set.</summary>
        private static void AddKey(HashSet<string> keys, string value)
        {
            value = (value ?? "").Trim();
            // Blank keys would match every empty invoice/PO cell.
            if (value.Length > 0)
                keys.Add(value);
        }

        /// <summary>True when the memo contains a party name long enough to avoid false hits.</summary>
        private static bool MemoMentions(string hay, string token)
        {
            token = (token ?? "").Trim();
            // Short names like "Co" would match unrelated memos.
            if (token.Length < 4)
                return false;
            return hay.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the memo contains a document number of at least 3 characters.</summary>
        private static bool ContainsToken(string hay, string token)
        {
            // Tiny tokens like "1" are too common to treat as invoice/PO numbers.
            if (token.Length < 3)
                return false;
            return hay.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True for .ofx/.qfx extensions or files whose body looks like OFX.</summary>
        private static bool LooksLikeOfx(string text, string extension)
        {
            // Trust the extension even when the header is SGML rather than XML.
            if (extension.Equals(".ofx", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".qfx", StringComparison.OrdinalIgnoreCase))
                return true;

            return text.Contains("<OFX", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("OFXHEADER", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Read STMTTRN blocks into Parsed rows; skip lines without an amount.</summary>
        private static List<Parsed> ParseOfx(string text)
        {
            var rows = new List<Parsed>();
            foreach (Match block in Regex.Matches(
                         text,
                         @"<STMTTRN>(.*?)(?:</STMTTRN>|(?=<STMTTRN>))",
                         RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                string body = block.Groups[1].Value;
                string amountText = OfxTag(body, "TRNAMT");
                // A statement line without TRNAMT is not a usable transaction.
                if (!TryParseAmount(amountText, out decimal amount))
                    continue;
                // Missing post date still imports; today is better than dropping the line.
                if (!TryParseOfxDate(OfxTag(body, "DTPOSTED"), out var date))
                    date = DateTime.Today;

                string name = OfxTag(body, "NAME");
                string memo = OfxTag(body, "MEMO");
                string description = string.Join(" ", new[] { name, memo }.Where(s => s.Length > 0));
                string type = OfxTag(body, "TRNTYPE");
                rows.Add(new Parsed
                {
                    Date = date.Date,
                    Amount = amount,
                    Description = description,
                    Reference = OfxTag(body, "CHECKNUM"),
                    Method = type.Length > 0 ? type : (amount >= 0 ? "Deposit" : "Withdrawal"),
                    ExternalId = OfxTag(body, "FITID"),
                    Type = amount >= 0 ? "Deposit" : "Withdrawal"
                });
            }

            return rows;
        }

        /// <summary>SGML OFX tag value, or empty when the tag is missing.</summary>
        private static string OfxTag(string body, string tag)
        {
            var match = Regex.Match(
                body,
                @"<" + tag + @">\s*([^<\r\n]+)",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        /// <summary>Parse the yyyyMMdd prefix of DTPOSTED, ignoring time if present.</summary>
        private static bool TryParseOfxDate(string value, out DateTime date)
        {
            date = default;
            // OFX dates are at least YYYYMMDD; shorter strings are not dates.
            if (value.Length < 8)
                return false;
            return DateTime.TryParseExact(
                value[..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        /// <summary>Read a bank CSV with Date and Amount (or Debit/Credit) columns.</summary>
        private static List<Parsed> ParseCsv(string path)
        {
            var table = CsvIO.Read(path);
            var rows = new List<Parsed>();
            // Header-only files have no transactions to import.
            if (table.Count < 2)
                return rows;

            var header = table[0].Select(h => h.Trim()).ToArray();
            int dateCol = FindColumn(header, "Date", "Posted", "Posting Date", "Transaction Date", "Trans Date");
            int amountCol = FindColumn(header, "Amount", "Amt");
            int debitCol = FindColumn(header, "Debit", "Withdrawal", "Withdrawals");
            int creditCol = FindColumn(header, "Credit", "Deposit", "Deposits");
            int descCol = FindColumn(header, "Description", "Name", "Memo", "Payee", "Details");
            int refCol = FindColumn(header, "Reference", "Check Number", "Check #", "Check", "Ref");
            int typeCol = FindColumn(header, "Type", "Transaction Type");
            int idCol = FindColumn(header, "Id", "FITID", "Fit Id", "Transaction Id");

            // Without a date and some amount column this is not a bank CSV.
            if (dateCol < 0 || (amountCol < 0 && debitCol < 0 && creditCol < 0))
                return rows;

            for (int i = 1; i < table.Count; i++)
            {
                var cells = table[i];
                string dateText = Cell(cells, dateCol);
                // Skip totals/blank rows that are not dated transactions.
                if (!TryParseDate(dateText, out var date))
                    continue;

                decimal amount = 0;
                // Signed Amount column is used when the bank exports one money field.
                if (amountCol >= 0)
                {
                    // Unreadable amounts are not imported as zero.
                    if (!TryParseAmount(Cell(cells, amountCol), out amount))
                        continue;
                }
                // Separate Debit/Credit columns: deposits positive, withdrawals negative.
                else
                {
                    // Separate Debit/Credit columns: deposits positive, withdrawals negative.
                    TryParseAmount(Cell(cells, creditCol), out decimal credit);
                    TryParseAmount(Cell(cells, debitCol), out decimal debit);
                    amount = credit - Math.Abs(debit);
                    // A row with neither debit nor credit is not a transaction.
                    if (credit == 0 && debit == 0)
                        continue;
                }

                string description = Cell(cells, descCol);
                rows.Add(new Parsed
                {
                    Date = date.Date,
                    Amount = amount,
                    Description = description,
                    Reference = Cell(cells, refCol),
                    Method = Cell(cells, typeCol),
                    ExternalId = Cell(cells, idCol),
                    Type = amount >= 0 ? "Deposit" : "Withdrawal"
                });
            }

            return rows;
        }

        /// <summary>Column index by exact header name, then by contains, or -1.</summary>
        private static int FindColumn(string[] header, params string[] names)
        {
            for (int i = 0; i < header.Length; i++)
            {
                foreach (var name in names)
                {
                    // Exact header match first so "Date" does not steal "Posting Date" later.
                    if (header[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            for (int i = 0; i < header.Length; i++)
            {
                foreach (var name in names)
                {
                    // Banks often use "Transaction Date" instead of a bare "Date".
                    if (header[i].Contains(name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            return -1;
        }

        /// <summary>Trimmed cell text, or empty when the column is missing on this row.</summary>
        private static string Cell(string[] cells, int index)
        {
            // Missing columns (debit-only files) read as blank, not an exception.
            if (index < 0 || index >= cells.Length)
                return "";
            return (cells[index] ?? "").Trim();
        }

        /// <summary>Parse a bank date using local culture, invariant, then common exact formats.</summary>
        private static bool TryParseDate(string text, out DateTime date)
        {
            text = text.Trim();
            // US bank CSVs often follow the PC locale.
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
                return true;
            // ISO-style exports use invariant yyyy-MM-dd.
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
            return DateTime.TryParseExact(
                text,
                new[] { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "yyyyMMdd" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        /// <summary>Parse money with $ , and (negatives); empty cells are not amounts.</summary>
        private static bool TryParseAmount(string text, out decimal amount)
        {
            amount = 0;
            text = (text ?? "").Trim();
            // Blank debit/credit cells mean "no value on this side", not zero.
            if (text.Length == 0)
                return false;

            bool negative = text.StartsWith('(') && text.EndsWith(')');
            text = text.Replace("$", "").Replace(",", "").Replace("(", "").Replace(")", "").Trim();
            // Try invariant then local so both 1,234.56 and 1.234,56 exports work.
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) &&
                !decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
                return false;

            // Accounting format (1,234.56) is a withdrawal.
            if (negative)
                amount = -Math.Abs(amount);
            return true;
        }
    }
}

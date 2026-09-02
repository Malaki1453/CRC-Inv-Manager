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

        public static void EnsureSchema()
        {
            SqliteInventory.EnsureColumns(DataFiles.BankTransactions, ExtraColumns);
        }

        /// <summary>Parse OFX/QFX or a bank CSV. Returns an error if the file cannot be read.</summary>
        public static bool TryParseFile(string path, out List<Parsed> rows, out string error)
        {
            rows = new List<Parsed>();
            error = "";
            if (!File.Exists(path))
            {
                error = "The selected file could not be found.";
                return false;
            }

            string text = File.ReadAllText(path);
            string ext = Path.GetExtension(path);
            if (LooksLikeOfx(text, ext))
            {
                rows = ParseOfx(text);
                if (rows.Count == 0)
                {
                    error = "No transactions were found in that OFX/QFX file.";
                    return false;
                }

                return true;
            }

            rows = ParseCsv(path);
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
                        : row.Amount >= 0 ? "Deposit" : "Withdrawal"
                };
                SqliteInventory.Insert(DataFiles.BankTransactions, values);
                added++;
            }

            if (added > 0)
                DataFiles.NotifyDataChanged();
            return added;
        }

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
                if (!DataFiles.MatchesCustomer(rec, code, name))
                    continue;
                AddKey(invoiceKeys, DataFiles.GetRecord(rec, "Invoice #"));
                AddKey(soKeys, DataFiles.GetRecord(rec, "SO #"));
            }

            foreach (var rec in DataFiles.ReadRecords(DataFiles.Sales))
            {
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

            if (grid.Rows.Count == 0)
                grid.Rows.Add("No bank transactions yet", "", "", "", "", "");
        }

        private static HashSet<string> LoadExistingKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in DataFiles.ReadRecords(DataFiles.BankTransactions))
            {
                string ext = DataFiles.GetRecord(row, "External Id").Trim();
                string account = DataFiles.GetRecord(row, "Account").Trim();
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

        private static string RowKey(string account, Parsed row)
        {
            if (row.ExternalId.Length > 0)
                return "id|" + account + "|" + row.ExternalId;

            return "row|" + account + "|" +
                   row.Date.ToString("yyyy-MM-dd") + "|" +
                   row.Amount.ToString("0.00", CultureInfo.InvariantCulture) + "|" +
                   row.Description;
        }

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
                if (MemoMentions(hay, name) || MemoMentions(hay, company))
                {
                    list.Add(row);
                    continue;
                }

                if (vendor)
                {
                    string po = DataFiles.GetRecordAny(row, "PO #", "Reference");
                    if (po.Length > 0 && primaryKeys.Contains(po))
                        list.Add(row);
                    continue;
                }

                string invoice = DataFiles.GetRecord(row, "Invoice #").Trim();
                string so = DataFiles.GetRecord(row, "SO #").Trim();
                if ((invoice.Length > 0 && primaryKeys.Contains(invoice)) ||
                    (so.Length > 0 && soKeys != null && soKeys.Contains(so)))
                    list.Add(row);
            }

            return list;
        }

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
            if (hay.Length == 0)
                return;

            bool preferVendor = row.Amount < 0;
            if (preferVendor)
            {
                TryMatchVendor(hay, purchases, vendors, ref vendor, ref po);
                if (vendor.Length == 0)
                    TryMatchCustomer(hay, invoices, sales, customers, ref invoice, ref so, ref customer);
            }
            else
            {
                TryMatchCustomer(hay, invoices, sales, customers, ref invoice, ref so, ref customer);
                if (customer.Length == 0)
                    TryMatchVendor(hay, purchases, vendors, ref vendor, ref po);
            }
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
                string number = DataFiles.GetRecord(rec, "Invoice #").Trim();
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
                if (saleSo.Length > 0 && ContainsToken(hay, saleSo))
                {
                    so = saleSo;
                    invoice = saleInv;
                    customer = DataFiles.GetRecord(rec, "Customer Code");
                    return;
                }

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
                if (!MemoMentions(hay, DataFiles.GetRecord(rec, "Name")) &&
                    !MemoMentions(hay, DataFiles.GetRecord(rec, "Company")))
                    continue;
                customer = DataFiles.GetRecord(rec, "Code");
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
                string number = DataFiles.GetRecord(rec, "PO #").Trim();
                if (number.Length == 0 || !ContainsToken(hay, number))
                    continue;
                po = number;
                vendor = DataFiles.GetRecord(rec, "Vendor Code").Trim();
                return;
            }

            foreach (var rec in vendors)
            {
                if (!MemoMentions(hay, DataFiles.GetRecord(rec, "Name")) &&
                    !MemoMentions(hay, DataFiles.GetRecord(rec, "Company")))
                    continue;
                vendor = DataFiles.GetRecord(rec, "Code");
                return;
            }
        }

        private static void AddKey(HashSet<string> keys, string value)
        {
            value = (value ?? "").Trim();
            if (value.Length > 0)
                keys.Add(value);
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

        private static bool LooksLikeOfx(string text, string extension)
        {
            if (extension.Equals(".ofx", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".qfx", StringComparison.OrdinalIgnoreCase))
                return true;

            return text.Contains("<OFX", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("OFXHEADER", StringComparison.OrdinalIgnoreCase);
        }

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
                if (!TryParseAmount(amountText, out decimal amount))
                    continue;
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

        private static string OfxTag(string body, string tag)
        {
            var match = Regex.Match(
                body,
                @"<" + tag + @">\s*([^<\r\n]+)",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private static bool TryParseOfxDate(string value, out DateTime date)
        {
            date = default;
            if (value.Length < 8)
                return false;
            return DateTime.TryParseExact(
                value[..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        private static List<Parsed> ParseCsv(string path)
        {
            var table = CsvIO.Read(path);
            var rows = new List<Parsed>();
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

            if (dateCol < 0 || (amountCol < 0 && debitCol < 0 && creditCol < 0))
                return rows;

            for (int i = 1; i < table.Count; i++)
            {
                var cells = table[i];
                string dateText = Cell(cells, dateCol);
                if (!TryParseDate(dateText, out var date))
                    continue;

                decimal amount = 0;
                if (amountCol >= 0)
                {
                    if (!TryParseAmount(Cell(cells, amountCol), out amount))
                        continue;
                }
                else
                {
                    TryParseAmount(Cell(cells, creditCol), out decimal credit);
                    TryParseAmount(Cell(cells, debitCol), out decimal debit);
                    amount = credit - Math.Abs(debit);
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

        private static int FindColumn(string[] header, params string[] names)
        {
            for (int i = 0; i < header.Length; i++)
            {
                foreach (var name in names)
                {
                    if (header[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            for (int i = 0; i < header.Length; i++)
            {
                foreach (var name in names)
                {
                    if (header[i].Contains(name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            return -1;
        }

        private static string Cell(string[] cells, int index)
        {
            if (index < 0 || index >= cells.Length)
                return "";
            return (cells[index] ?? "").Trim();
        }

        private static bool TryParseDate(string text, out DateTime date)
        {
            text = text.Trim();
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
                return true;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
            return DateTime.TryParseExact(
                text,
                new[] { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "yyyyMMdd" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        private static bool TryParseAmount(string text, out decimal amount)
        {
            amount = 0;
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return false;

            bool negative = text.StartsWith('(') && text.EndsWith(')');
            text = text.Replace("$", "").Replace(",", "").Replace("(", "").Replace(")", "").Trim();
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) &&
                !decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
                return false;

            if (negative)
                amount = -Math.Abs(amount);
            return true;
        }
    }
}

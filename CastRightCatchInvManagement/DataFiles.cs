namespace CastRightCatchInvManagement
{
    public static class DataFiles
    {
        public const string PurchaseSales = "purchase_sales";
        public const string Sales = "sales";
        public const string Customers = "customers";
        public const string Vendors = "vendors";
        public const string ItemCodes = "item_codes";
        public const string Invoices = "invoices";
        public const string BankTransactions = "bank_transactions";
        public const string Debits = "debits";
        public const string Credits = "credits";
        public const string StoredInvoicesFolderName = "Stored Invoices";
        public const string StoredSalesOrdersFolderName = "Stored Sales Orders";
        public const string PdfKindInvoice = "invoice";
        public const string PdfKindSalesOrder = "sales_order";

        public static readonly string[] All =
        {
            PurchaseSales,
            Sales,
            Customers,
            Vendors,
            ItemCodes,
            Invoices,
            BankTransactions,
            Debits,
            Credits
        };

        public static string GetFileName(string baseName)
        {
            DateTime start = AppState.TermStartDate ?? DateTime.Today;
            return $"{baseName}_{start:yyyy-MM-dd}.csv";
        }

        public static string GetPath(string baseName)
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                throw new InvalidOperationException("No data folder has been selected.");

            return Path.Combine(AppState.InventoryFolder, GetFileName(baseName));
        }

        public static bool Exists(string baseName)
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return false;

            SqliteInventory.EnsureCreated();
            return SqliteInventory.GetPath() != null;
        }

        public static string? FindCurrentFile(string baseName)
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) ||
                !Directory.Exists(AppState.InventoryFolder))
                return null;

            var matches = Directory.GetFiles(AppState.InventoryFolder, baseName + "_*.csv");

            DateTime bestDate = DateTime.MinValue;
            string? bestPath = null;

            foreach (var path in matches)
            {
                if (TryParseStartDate(Path.GetFileName(path), baseName, out var date))
                {
                    if (date >= bestDate)
                    {
                        bestDate = date;
                        bestPath = path;
                    }
                }
            }

            return bestPath;
        }

        public static void SyncTermStartFromFiles()
        {
            DateTime? latest = SqliteInventory.LatestTerm();

            if (latest == null &&
                !string.IsNullOrWhiteSpace(AppState.InventoryFolder) &&
                Directory.Exists(AppState.InventoryFolder))
            {
                foreach (var baseName in All)
                {
                    foreach (var path in Directory.GetFiles(AppState.InventoryFolder, baseName + "_*.csv"))
                    {
                        if (TryParseStartDate(Path.GetFileName(path), baseName, out var date) &&
                            (latest == null || date > latest))
                            latest = date;
                    }
                }
            }

            if (latest != null)
            {
                AppState.TermStartDate = latest;
                AppLock.SaveSettings();
            }
        }

        public static List<string> GetMissingFiles()
        {
            SyncTermStartFromFiles();

            var missing = new List<string>();
            foreach (var file in All)
            {
                if (!Exists(file))
                    missing.Add(GetFileName(file));
            }

            return missing;
        }

        public static string GetPageFileBaseName(AppPage page)
        {
            return page switch
            {
                AppPage.PurchaseSales => PurchaseSales,
                AppPage.AddPurchase => PurchaseSales,
                AppPage.Sales => Sales,
                AppPage.AddSale => Sales,
                AppPage.SalesOrder => Sales,
                AppPage.Customers => Customers,
                AppPage.Vendors => Vendors,
                AppPage.ItemCodes => ItemCodes,
                AppPage.Invoicing => Invoices,
                AppPage.Banking => BankTransactions,
                AppPage.Debits => Debits,
                AppPage.Credits => Credits,
                _ => ""
            };
        }

        public static string? GetActiveFileName()
        {
            return GetDisplayedFileName(Navigator.CurrentPage);
        }

        public static string? GetDisplayedFileName(AppPage page)
        {
            string baseName = GetPageFileBaseName(page);
            if (string.IsNullOrWhiteSpace(baseName))
                return null;

            string term = (AppState.TermStartDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            return $"{SqliteInventory.FileName}  ·  {baseName}  ·  {term}";
        }

        public static string? GetActiveFilePath()
        {
            return SqliteInventory.GetPath();
        }

        public static bool ActiveFileExists()
        {
            return GetActiveFilePath() != null;
        }

        public static string? GetStoredInvoicesFolder()
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, StoredInvoicesFolderName);
        }

        public static void EnsureStoredInvoicesFolder()
        {
            string? path = GetStoredInvoicesFolder();
            if (path == null)
                return;

            Directory.CreateDirectory(path);
        }

        public static void OpenStoredInvoice(string? invoiceNumber)
        {
            string key = (invoiceNumber ?? "").Trim();
            if (key.Length == 0)
            {
                MessageBox.Show(
                    "This row has no invoice number.",
                    "Invoice",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string? path = FindStoredPdf(PdfKindInvoice, key);
            if (path == null)
            {
                MessageBox.Show(
                    $"No stored PDF was found for invoice {key}.",
                    "Invoice Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            OpenPdf(path);
        }

        public static string? FindStoredSalesOrder(string? soNumber)
        {
            return FindStoredPdf(PdfKindSalesOrder, soNumber);
        }

        public static string SaveStoredPdf(string kind, string key, string fileName, byte[] content)
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                throw new InvalidOperationException("Select a data folder first.");

            SqliteInventory.SavePdf(kind, key, fileName, content);

            string? folder = kind == PdfKindInvoice
                ? GetStoredInvoicesFolder()
                : GetStoredSalesOrdersFolder();
            if (folder == null)
                throw new InvalidOperationException("Select a data folder first.");

            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, content);
            return path;
        }

        public static string? FindStoredPdf(string kind, string? key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0 || string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return null;

            var stored = SqliteInventory.TryGetPdf(kind, key);
            if (stored != null)
                return WritePdfCopy(kind, stored.Value.FileName, stored.Value.Content);

            string? disk = FindPdfOnDisk(kind, key);
            if (disk == null)
                return null;

            try
            {
                SqliteInventory.SavePdf(kind, key, Path.GetFileName(disk), File.ReadAllBytes(disk));
            }
            catch
            {
                // still open the file even if the database write fails
            }

            return disk;
        }

        private static string? FindPdfOnDisk(string kind, string key)
        {
            string? folder = kind == PdfKindInvoice
                ? GetStoredInvoicesFolder()
                : GetStoredSalesOrdersFolder();
            if (folder == null || !Directory.Exists(folder))
                return null;

            var files = Directory.GetFiles(folder, "*.pdf");
            if (kind == PdfKindInvoice)
            {
                return files.FirstOrDefault(path =>
                    Path.GetFileNameWithoutExtension(path)
                        .Contains(key, StringComparison.OrdinalIgnoreCase));
            }

            string prefix = "Sales Order " + key;
            return files.FirstOrDefault(path =>
            {
                string name = Path.GetFileNameWithoutExtension(path);
                return name.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith(prefix + " -", StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string WritePdfCopy(string kind, string fileName, byte[] content)
        {
            string? folder = kind == PdfKindInvoice
                ? GetStoredInvoicesFolder()
                : GetStoredSalesOrdersFolder();
            if (folder != null)
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, fileName);
                File.WriteAllBytes(path, content);
                return path;
            }

            string temp = Path.Combine(Path.GetTempPath(), "CRC-Inv-Manager");
            Directory.CreateDirectory(temp);
            string tempPath = Path.Combine(temp, fileName);
            File.WriteAllBytes(tempPath, content);
            return tempPath;
        }

        public static string? FindExistingSalesOrderNumber(
            IEnumerable<string> purchaseOrders,
            string? customerCode,
            string? customerName)
        {
            var pos = new HashSet<string>(
                purchaseOrders
                    .Select(NormalizePo)
                    .Where(po => po.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            if (pos.Count == 0)
                return null;

            foreach (var record in ReadRecords(Sales))
            {
                if (!MatchesCustomer(record, customerCode, customerName))
                    continue;
                if (!pos.Contains(NormalizePo(SalePo(record))))
                    continue;

                string so = GetRecord(record, "SO #").Trim();
                if (so.Length > 0)
                    return so;
            }

            return null;
        }

        public static void OpenPdf(string path)
        {
            DescribePdf(path, out string title, out string? kind, out string? key);
            PdfViewForm.ShowDocument(path, title, kind, key);
        }

        public static void DescribePdf(string path, out string title, out string? kind, out string? key)
        {
            string stem = Path.GetFileNameWithoutExtension(path) ?? "";
            string folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "") ?? "";
            title = stem.Length > 0 ? stem : "PDF";
            kind = null;
            key = null;

            bool invoice = folder.Equals(StoredInvoicesFolderName, StringComparison.OrdinalIgnoreCase) ||
                           stem.StartsWith("Invoice ", StringComparison.OrdinalIgnoreCase);
            bool salesOrder = folder.Equals(StoredSalesOrdersFolderName, StringComparison.OrdinalIgnoreCase) ||
                              stem.StartsWith("Sales Order ", StringComparison.OrdinalIgnoreCase);

            if (invoice)
            {
                kind = PdfKindInvoice;
                key = KeyAfterPrefix(stem, "Invoice ");
                if (key.Length > 0)
                    title = "Invoice " + key;
            }
            else if (salesOrder)
            {
                kind = PdfKindSalesOrder;
                key = KeyAfterPrefix(stem, "Sales Order ");
                if (key.Length > 0)
                    title = "Sales Order " + key;
            }
        }

        private static string KeyAfterPrefix(string stem, string prefix)
        {
            if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return "";

            string rest = stem[prefix.Length..].Trim();
            int dash = rest.IndexOf(" - ", StringComparison.Ordinal);
            if (dash >= 0)
                rest = rest[..dash];
            return rest.Trim();
        }

        public static void OpenStoredSalesOrder(string? soNumber)
        {
            string? path = FindStoredSalesOrder(soNumber);
            if (path == null)
            {
                string key = (soNumber ?? "").Trim();
                if (key.Length == 0)
                {
                    MessageBox.Show(
                        "This row has no sales order number.",
                        "Sales Order",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                MessageBox.Show(
                    $"No stored PDF was found for sales order {key}.",
                    "Sales Order Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            OpenPdf(path);
        }

        public static string? GetStoredSalesOrdersFolder()
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, StoredSalesOrdersFolderName);
        }

        public static void EnsureStoredSalesOrdersFolder()
        {
            string? path = GetStoredSalesOrdersFolder();
            if (path == null)
                return;

            Directory.CreateDirectory(path);
        }

        public static void EnsureFilesExistOrAsk()
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return;

            EnsureStoredInvoicesFolder();
            EnsureStoredSalesOrdersFolder();
            Accounts.EnsureFile();
            if (AppState.TermStartDate == null)
            {
                AppState.TermStartDate = DateTime.Today;
                AppLock.SaveSettings();
            }

            SqliteInventory.EnsureCreated();
            SqliteInventory.ImportCsvsIfEmpty();
            SqliteInventory.ImportPdfsFromFolders();
            SyncTermStartFromFiles();
        }

        public static void CreateMissingFiles(IEnumerable<string> missingBaseNames)
        {
            SqliteInventory.EnsureCreated();
            foreach (var baseName in missingBaseNames)
                SqliteInventory.EnsureColumns(baseName);
        }

        public static void RollToNextTerm()
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) ||
                !Directory.Exists(AppState.InventoryFolder))
            {
                throw new InvalidOperationException("No data folder is selected.");
            }

            SyncTermStartFromFiles();

            DateTime start = AppState.TermStartDate ?? DateTime.Today;
            DateTime end = DateTime.Today;

            string archiveFolder = Path.Combine(AppState.InventoryFolder, "old data");
            Directory.CreateDirectory(archiveFolder);

            foreach (var baseName in All)
            {
                foreach (var currentPath in Directory.GetFiles(AppState.InventoryFolder, baseName + "_*.csv"))
                {
                    string archivedName = $"{baseName}_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv";
                    string archivePath = Path.Combine(archiveFolder, Path.GetFileName(currentPath));
                    if (File.Exists(archivePath))
                        File.Delete(archivePath);
                    File.Move(currentPath, archivePath);
                }
            }

            AppState.TermStartDate = DateTime.Today;
            AppLock.SaveSettings();
            SqliteInventory.EnsureCreated();
        }

        public static string GetExpectedHeader(string baseName)
        {
            return baseName switch
            {
                PurchaseSales =>
                    "PO #,Vendor Invoice #,Vendor Code,Vendor,Location,Item Code,Description,COO,Pack Size,CS,Volume,Volume Received,Price Paid / LB,Overhead / LB,Freight / LB,Forwarder / LB,Other / LB,Total Cost / LB,Total Cost,Agreement Date,Expected Ship Date,Vendor Terms,Vendor Due Date,Ship Date,Arrival Date,Forwarder,Logistics",

                Sales =>
                    "PO #,SO #,Customer Code,Customer,Customer Terms,Item Code,Lot #,Description,COO,Pack Size,CS,Volume,Sell Price / LB,Amount,Ship Date,Due Date,Invoice #,Paid,Status",

                Customers =>
                    "Code,Name,Company,Established,Terms,Credit Limit,Contact Name,Address,Email,Phone,Current Balance,Notes,Description",

                Vendors =>
                    "Code,Name,Company,Type,Terms,Amount,Phone,Current Balance,Notes,Description,Finalized",

                ItemCodes =>
                    "Code,Description,COO,Farmed / Wild,Fresh / Frozen,Proc Country,Species,Scientific Name",

                Invoices =>
                    "Invoice #,SO #,Customer Code,Customer,Ship Date,Due Date,Amount,Paid,Outstanding,Status,Payment Date,Payment Method",

                BankTransactions =>
                    "Date,Amount,Method,Reference,Invoice #,SO #,Customer Code,Notes",

                Debits =>
                    "Debit #,Date Submitted,Vendor Code,Vendor,PO #,Date Received,Date of Issue,Item Code,Description,Reason,LBS Received,Price / LB,Value,LBS Claimed,Claim Value,Claim %,Sales Rep,Vendor Approved,Notes",

                Credits =>
                    "Credit #,Date Submitted,Customer Code,Customer,Invoice #,Date Received,Date of Issue,Item Code,Description,Reason,LBS Received,Price / LB,Value,LBS Claimed,Claim Value,Claim %,Contact,Approved,Notes",

                _ => ""
            };
        }

        public static List<Dictionary<string, string>> ReadRecords(string baseName)
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return new List<Dictionary<string, string>>();

            if (baseName == Customers)
                EnsureFileColumns(Customers, "Address", "Email", "Phone", "Company", "Current Balance", "Notes", "Description");
            if (baseName == Vendors)
                EnsureFileColumns(Vendors, "Company", "Phone", "Current Balance", "Notes", "Description");

            return SqliteInventory.Read(baseName);
        }

        public static string GetRecord(Dictionary<string, string> record, string column)
        {
            return record.TryGetValue(column, out var value) ? value ?? "" : "";
        }

        public static string GetRecordAny(Dictionary<string, string> record, params string[] columns)
        {
            foreach (var column in columns)
            {
                string value = GetRecord(record, column).Trim();
                if (value.Length > 0)
                    return value;
            }

            return "";
        }

        public static string NormalizePo(string? po)
        {
            if (string.IsNullOrWhiteSpace(po))
                return "";

            return new string(po.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        }

        public static Dictionary<string, string>? FindPurchaseByPo(string? poNumber)
        {
            return FindByNormalized(PurchaseSales, "PO #", poNumber);
        }

        public static string SalePo(Dictionary<string, string> record)
        {
            if (record.ContainsKey("Lot #"))
                return GetRecord(record, "PO #").Trim();

            string customerPo = GetRecord(record, "Customer PO").Trim();
            if (customerPo.Length > 0)
                return customerPo;

            return GetRecord(record, "PO #").Trim();
        }

        public static string SaleLot(Dictionary<string, string> record)
        {
            string lot = GetRecord(record, "Lot #").Trim();
            if (lot.Length > 0)
                return lot;

            if (GetRecord(record, "Customer PO").Trim().Length > 0)
                return GetRecord(record, "PO #").Trim();

            return "";
        }

        public static Dictionary<string, string>? FindSaleByPo(
            string? poNumber,
            string? customerCode = null,
            string? customerName = null,
            string? itemCode = null)
        {
            return FindSale(poNumber, "PO #", customerCode, customerName, 3, itemCode);
        }

        public static Dictionary<string, string>? FindSaleBySo(
            string? soNumber,
            string? customerCode = null,
            string? customerName = null,
            string? itemCode = null)
        {
            return FindSale(soNumber, "SO #", customerCode, customerName, 3, itemCode);
        }

        public static Dictionary<string, string>? FindInvoiceSource(
            string? key,
            string? customerCode = null,
            string? customerName = null,
            string? itemCode = null)
        {
            var all = FindInvoiceSourcesForKey(key, customerCode, customerName);
            if (all.Count == 0)
                return null;

            string item = (itemCode ?? "").Trim();
            if (item.Length == 0)
                return all[0];

            return all.FirstOrDefault(record =>
                GetRecord(record, "Item Code").Equals(item, StringComparison.OrdinalIgnoreCase));
        }

        public static bool InvoiceNumberExists(string? invoiceNumber)
        {
            string needle = (invoiceNumber ?? "").Trim();
            if (needle.Length == 0)
                return false;

            foreach (var record in ReadRecords(Invoices))
            {
                if (GetRecord(record, "Invoice #").Trim()
                    .Equals(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static List<Dictionary<string, string>> FindInvoiceSourcesForInvoice(
            string? invoiceNumber,
            string? soNumber,
            string? customerCode = null,
            string? customerName = null)
        {
            var result = new List<Dictionary<string, string>>();
            string invoice = (invoiceNumber ?? "").Trim();
            string so = NormalizePo(soNumber);
            if (invoice.Length == 0 && so.Length == 0)
                return result;

            var seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sale in ReadRecords(Sales))
            {
                if (!MatchesCustomer(sale, customerCode, customerName))
                    continue;

                string saleInvoice = GetRecord(sale, "Invoice #").Trim();
                string saleSo = NormalizePo(GetRecord(sale, "SO #"));
                bool matchInvoice = invoice.Length > 0 &&
                    saleInvoice.Equals(invoice, StringComparison.OrdinalIgnoreCase);
                bool matchSo = so.Length > 0 &&
                    saleSo.Equals(so, StringComparison.OrdinalIgnoreCase);
                if (!matchInvoice && !matchSo)
                    continue;

                var purchase = FindPurchaseByPo(SaleLot(sale));
                string item = GetRecord(sale, "Item Code").Trim();
                string distinct = item.Length > 0 ? item : GetRecord(sale, "Description").Trim();
                if (distinct.Length == 0)
                    distinct = result.Count.ToString();
                if (!seenItems.Add(distinct))
                    continue;

                result.Add(MergeSaleAndPurchase(sale, purchase));
            }

            return result;
        }

        public static List<Dictionary<string, string>> FindInvoiceSourcesForKey(
            string? key,
            string? customerCode = null,
            string? customerName = null)
        {
            var result = new List<Dictionary<string, string>>();
            string needle = NormalizePo(key);
            if (needle.Length < 3)
                return result;

            var seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sale in ReadRecords(Sales))
            {
                if (!MatchesCustomer(sale, customerCode, customerName))
                    continue;

                string salePo = NormalizePo(SalePo(sale));
                string so = NormalizePo(GetRecord(sale, "SO #"));
                if (!salePo.Equals(needle, StringComparison.OrdinalIgnoreCase) &&
                    !so.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    continue;

                var purchase = FindPurchaseByPo(SaleLot(sale));

                string item = GetRecord(sale, "Item Code").Trim();
                string distinct = item.Length > 0 ? item : GetRecord(sale, "Description").Trim();
                if (distinct.Length == 0)
                    distinct = result.Count.ToString();
                if (!seenItems.Add(distinct))
                    continue;

                result.Add(MergeSaleAndPurchase(sale, purchase));
            }

            return result;
        }

        public static List<Dictionary<string, string>> FindSalesOrderSourcesForKey(
            string? key,
            string? customerCode = null,
            string? customerName = null)
        {
            var result = new List<Dictionary<string, string>>();
            string needle = NormalizePo(key);
            if (needle.Length < 3)
                return result;

            foreach (var sale in ReadRecords(Sales))
            {
                if (!MatchesCustomer(sale, customerCode, customerName))
                    continue;

                string salePo = NormalizePo(SalePo(sale));
                if (!salePo.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    continue;

                var purchase = FindPurchaseByPo(SaleLot(sale));
                result.Add(MergeSaleAndPurchase(sale, purchase));
            }

            return result;
        }

        public static int AssignSalesOrderNumber(
            IEnumerable<string> purchaseOrders,
            string? customerCode,
            string? customerName,
            string soNumber)
        {
            soNumber = (soNumber ?? "").Trim();
            if (soNumber.Length == 0)
                return 0;

            var pos = new HashSet<string>(
                purchaseOrders
                    .Select(NormalizePo)
                    .Where(po => po.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            if (pos.Count == 0)
                return 0;

            return UpdateRecords(Sales, record =>
            {
                if (!MatchesCustomer(record, customerCode, customerName))
                    return false;
                if (!pos.Contains(NormalizePo(SalePo(record))))
                    return false;

                string existing = GetRecord(record, "SO #").Trim();
                return existing.Length == 0 ||
                       existing.Equals(soNumber, StringComparison.OrdinalIgnoreCase);
            }, record => record["SO #"] = soNumber);
        }

        public static int UpdateRecords(
            string baseName,
            Func<Dictionary<string, string>, bool> match,
            Action<Dictionary<string, string>> mutate)
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return 0;

            int updated = 0;
            foreach (var (id, map) in SqliteInventory.ReadWithIds(baseName))
            {
                if (!match(map))
                    continue;

                mutate(map);
                if (SqliteInventory.UpdateById(baseName, id, map))
                    updated++;
            }

            if (updated == 0)
                return 0;

            NotifyDataChanged();
            return updated;
        }

        public static void EnsureFileColumns(string baseName, params string[] columns)
        {
            SqliteInventory.EnsureColumns(baseName, columns);
        }

        private static Dictionary<string, string> MergeSaleAndPurchase(
            Dictionary<string, string> sale,
            Dictionary<string, string>? purchase)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (purchase != null)
            {
                foreach (var pair in purchase)
                    merged[pair.Key] = pair.Value;
            }

            foreach (var pair in sale)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                    merged[pair.Key] = pair.Value;
            }

            return merged;
        }

        public static AutoCompleteStringCollection InvoicePoSuggestions(
            string? customerCode = null,
            string? customerName = null,
            IEnumerable<string>? excludePos = null)
        {
            var source = new AutoCompleteStringCollection();
            bool filter = !string.IsNullOrWhiteSpace(customerCode) ||
                          !string.IsNullOrWhiteSpace(customerName);
            var skip = new HashSet<string>(
                (excludePos ?? Array.Empty<string>())
                    .Select(NormalizePo)
                    .Where(po => po.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            foreach (var record in ReadRecords(Sales))
            {
                if (!MatchesCustomer(record, customerCode, customerName))
                    continue;

                string po = SalePo(record);
                if (po.Length == 0 || skip.Contains(NormalizePo(po)) || source.Contains(po))
                    continue;
                source.Add(po);
            }

            return source;
        }

        public static bool MatchesCustomer(
            Dictionary<string, string> record,
            string? customerCode,
            string? customerName)
        {
            string code = (customerCode ?? "").Trim();
            string name = (customerName ?? "").Trim();
            if (code.Length == 0 && name.Length == 0)
                return true;

            string recCode = GetRecordAny(record, "Customer Code", "Cust ID");
            string recName = GetRecordAny(record, "Customer", "Customer Name");

            if (code.Length > 0 && recCode.Length > 0 &&
                recCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                return true;

            if (name.Length > 0 && recName.Length > 0 &&
                recName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static Dictionary<string, string>? FindSale(
            string? key,
            string column,
            string? customerCode,
            string? customerName,
            int minLength = 3,
            string? itemCode = null)
        {
            string needle = NormalizePo(key);
            if (needle.Length < minLength)
                return null;

            string item = (itemCode ?? "").Trim();
            Dictionary<string, string>? startsWith = null;
            foreach (var record in ReadRecords(Sales))
            {
                if (!MatchesCustomer(record, customerCode, customerName))
                    continue;

                if (item.Length > 0)
                {
                    string recItem = GetRecord(record, "Item Code").Trim();
                    if (!recItem.Equals(item, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                string value = NormalizePo(GetRecord(record, column));
                if (value.Length == 0)
                    continue;

                if (value.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    return record;

                if (startsWith == null && value.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                    startsWith = record;
            }

            return startsWith;
        }

        private static Dictionary<string, string>? FindByNormalized(
            string baseName,
            string column,
            string? key,
            int minLength = 3)
        {
            string needle = NormalizePo(key);
            if (needle.Length < minLength)
                return null;

            Dictionary<string, string>? startsWith = null;
            foreach (var record in ReadRecords(baseName))
            {
                string value = NormalizePo(GetRecord(record, column));
                if (value.Length == 0)
                    continue;

                if (value.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    return record;

                if (startsWith == null && value.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                    startsWith = record;
            }

            return startsWith;
        }

        public static string NextPurchasePo()
        {
            string pattern = (AppState.ProductNumberPattern ?? "").Trim();
            if (pattern.Length > 0)
                return NextFromPattern(PurchaseSales, "PO #", pattern, AppState.ProductNumberStart);

            int year = (AppState.TermStartDate ?? DateTime.Today).Year % 100;
            string prefix = $"CRC{year:00}-";
            int max = 10000;

            foreach (var record in ReadRecords(PurchaseSales))
            {
                string po = GetRecord(record, "PO #");
                int dash = po.IndexOf('-');
                if (dash < 0 || dash + 1 >= po.Length)
                    continue;

                int i = dash + 1;
                while (i < po.Length && char.IsWhiteSpace(po[i]))
                    i++;
                int start = i;
                while (i < po.Length && char.IsDigit(po[i]))
                    i++;
                if (i > start && int.TryParse(po[start..i], out int n) && n > max)
                    max = n;
            }

            return prefix + (max + 1);
        }

        public static string NextNumber(string baseName, string column, int fallback)
        {
            int max = fallback - 1;
            foreach (var record in ReadRecords(baseName))
            {
                if (int.TryParse(GetRecord(record, column).Trim(), out int n) && n > max)
                    max = n;
            }

            return (max + 1).ToString();
        }

        public static string NextSalesOrderNumber()
        {
            string pattern = (AppState.SalesOrderPattern ?? "").Trim();
            if (pattern.Length == 0)
                return NextNumber(Sales, "SO #", 10001);

            return NextFromPattern(Sales, "SO #", pattern, AppState.SalesOrderStart);
        }

        public static string PreviewSalesOrderNumber() => NextSalesOrderNumber();

        public static string PreviewProductNumber() => NextPurchasePo();

        private static string NextFromPattern(string baseName, string column, string pattern, string? startText)
        {
            ParseHashPattern(pattern, out string prefix, out int width, out string suffix);
            int floor = 1;
            if (int.TryParse((startText ?? "").Trim(), out int start) && start > 0)
                floor = start;

            int max = floor - 1;
            foreach (var record in ReadRecords(baseName))
            {
                if (!TryReadPatternNumber(GetRecord(record, column), prefix, suffix, out int n))
                    continue;
                if (n > max)
                    max = n;
            }

            int next = max + 1;
            string digits = next.ToString().PadLeft(width, '0');
            return prefix + digits + suffix;
        }

        private static void ParseHashPattern(string pattern, out string prefix, out int width, out string suffix)
        {
            int hashEnd = pattern.LastIndexOf('#');
            if (hashEnd < 0)
            {
                prefix = pattern;
                width = 4;
                suffix = "";
                return;
            }

            int hashStart = hashEnd;
            while (hashStart > 0 && pattern[hashStart - 1] == '#')
                hashStart--;

            prefix = pattern[..hashStart];
            width = Math.Max(1, hashEnd - hashStart + 1);
            suffix = pattern[(hashEnd + 1)..];
        }

        private static bool TryReadPatternNumber(string value, string prefix, string suffix, out int number)
        {
            number = 0;
            value = (value ?? "").Trim();
            if (value.Length == 0)
                return false;

            if (prefix.Length > 0 &&
                !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            if (suffix.Length > 0 &&
                !value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return false;

            int start = prefix.Length;
            int end = value.Length - suffix.Length;
            if (end <= start)
                return false;

            string digits = value[start..end];
            return int.TryParse(digits, out number);
        }

        public static void AppendNamedRow(string baseName, Dictionary<string, string> values)
        {
            SqliteInventory.Insert(baseName, values);
            NotifyDataChanged();
        }

        public static string[] NamedRow(string baseName, Dictionary<string, string> values)
        {
            return MapNamedRow(SqliteInventory.Headers(baseName), values);
        }

        private static string[] MapNamedRow(string[] header, Dictionary<string, string> values)
        {
            bool hasLot = header.Any(h => h.Trim().Equals("Lot #", StringComparison.OrdinalIgnoreCase));
            var cells = new string[header.Length];
            for (int i = 0; i < header.Length; i++)
            {
                string name = header[i].Trim();
                if (TryNamed(values, name, out var value))
                    cells[i] = value;
                else if (name.Equals("Customer PO", StringComparison.OrdinalIgnoreCase) &&
                         TryNamed(values, "PO #", out value))
                    cells[i] = value;
                else if (!hasLot &&
                         name.Equals("PO #", StringComparison.OrdinalIgnoreCase) &&
                         TryNamed(values, "Lot #", out value))
                    cells[i] = value;
                else
                    cells[i] = "";
            }

            return cells;
        }

        private static bool TryNamed(Dictionary<string, string> values, string key, out string value)
        {
            foreach (var pair in values)
            {
                if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value ?? "";
                    return true;
                }
            }

            value = "";
            return false;
        }

        public static void AppendRow(string baseName, IEnumerable<string> fields)
        {
            var header = SqliteInventory.Headers(baseName);
            var cells = fields.ToList();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
                values[header[i]] = i < cells.Count ? cells[i] ?? "" : "";
            SqliteInventory.Insert(baseName, values);
            NotifyDataChanged();
        }

        public static Dictionary<string, string> GridRowToRecord(DataGridView grid, int rowIndex)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewColumn col in grid.Columns)
            {
                if (Theme.IsAddColumn(col))
                    continue;
                string key = col.Tag as string ?? col.Name;
                if (string.IsNullOrWhiteSpace(key))
                    key = col.HeaderText;
                map[key] = grid.Rows[rowIndex].Cells[col.Index].Value?.ToString() ?? "";
            }

            return map;
        }

        public static bool ReplaceMatchingRow(
            string baseName,
            Func<Dictionary<string, string>, bool> match,
            IReadOnlyList<string> fields)
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return false;

            var header = SqliteInventory.Headers(baseName);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
                values[header[i]] = i < fields.Count ? fields[i] ?? "" : "";

            foreach (var (id, map) in SqliteInventory.ReadWithIds(baseName))
            {
                if (!match(map))
                    continue;

                SqliteInventory.UpdateById(baseName, id, values);
                NotifyDataChanged();
                return true;
            }

            return false;
        }

        public static string DisplayColumnHeader(string baseName, string[] fileHeader, string name)
        {
            name = name.Trim();
            if (baseName == PurchaseSales &&
                name.Equals("Agreement Date", StringComparison.OrdinalIgnoreCase))
                return "Order Date";

            if (baseName != Sales)
                return name;

            bool hasLot = fileHeader.Any(h =>
                h.Trim().Equals("Lot #", StringComparison.OrdinalIgnoreCase) ||
                h.Trim().Equals("Lot Number", StringComparison.OrdinalIgnoreCase));
            if (hasLot)
                return name.Equals("Lot Number", StringComparison.OrdinalIgnoreCase) ? "Lot #" : name;

            if (name.Equals("PO #", StringComparison.OrdinalIgnoreCase))
                return "Lot #";
            if (name.Equals("Customer PO", StringComparison.OrdinalIgnoreCase))
                return "PO #";
            return name;
        }

        private static int[] ColumnDisplayOrder(string baseName, string[] fileHeader)
        {
            string[]? first = baseName switch
            {
                PurchaseSales => new[]
                {
                    "PO #",
                    "Ship Date",
                    "Order Date"
                },
                Sales => new[]
                {
                    "SO #",
                    "Ship Date",
                    "PO #"
                },
                Customers => new[]
                {
                    "Name",
                    "Company",
                    "Phone",
                    "Current Balance"
                },
                Vendors => new[]
                {
                    "Name",
                    "Company",
                    "Phone",
                    "Current Balance"
                },
                Invoices => new[]
                {
                    "SO #",
                    "Customer",
                    "Ship Date",
                    "Due Date",
                    "Status",
                    "Paid"
                },
                _ => null
            };

            if (first == null)
                return Enumerable.Range(0, fileHeader.Length).ToArray();

            var used = new bool[fileHeader.Length];
            var order = new List<int>(fileHeader.Length);
            foreach (var want in first)
            {
                for (int i = 0; i < fileHeader.Length; i++)
                {
                    if (used[i])
                        continue;
                    if (!DisplayColumnHeader(baseName, fileHeader, fileHeader[i])
                            .Equals(want, StringComparison.OrdinalIgnoreCase))
                        continue;
                    order.Add(i);
                    used[i] = true;
                    break;
                }
            }

            for (int i = 0; i < fileHeader.Length; i++)
            {
                if (!used[i])
                    order.Add(i);
            }

            return order.ToArray();
        }

        public static void ResetGridColumns(DataGridView grid)
        {
            string? baseName = grid.Tag is ColumnSearch search ? search.FileBaseName : null;
            foreach (DataGridViewColumn col in grid.Columns)
            {
                if (Theme.IsAddColumn(col))
                {
                    col.Visible = true;
                    continue;
                }

                col.Visible = IsSummaryColumn(baseName ?? "", col.HeaderText);
            }

            Theme.FitAllColumns(grid);
            if (grid.Tag is ColumnSearch layout)
                layout.NotifyColumnsChanged();
            GridLayout.Save(grid);
        }

        private static bool IsSummaryColumn(string baseName, string displayHeader)
        {
            string[]? visible = baseName switch
            {
                PurchaseSales => new[] { "PO #", "Ship Date", "Order Date" },
                Sales => new[] { "SO #", "Ship Date", "PO #" },
                Customers => new[] { "Name", "Company", "Phone", "Current Balance" },
                Vendors => new[] { "Name", "Company", "Phone", "Current Balance" },
                Invoices => new[] { "SO #", "Customer", "Ship Date", "Due Date", "Status", "Paid" },
                _ => null
            };
            if (visible == null)
                return true;

            return visible.Any(name =>
                name.Equals(displayHeader, StringComparison.OrdinalIgnoreCase));
        }

        public static event Action? DataChanged;

        public static void NotifyDataChanged() => DataChanged?.Invoke();

        public static int CountDataRows(string baseName)
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return 0;

            return SqliteInventory.Count(baseName);
        }

        public static void FillGrid(DataGridView grid, string baseName)
        {
            grid.Columns.Clear();
            grid.Rows.Clear();

            GridLayout.BeginUpdate();
            try
            {
                if (baseName == Customers)
                    EnsureFileColumns(Customers, "Address", "Email", "Phone", "Company", "Current Balance", "Notes", "Description");
                if (baseName == Vendors)
                    EnsureFileColumns(Vendors, "Company", "Phone", "Current Balance", "Notes", "Description");

                if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) || !Exists(baseName))
                {
                    grid.Columns.Add("Status", "Status");
                    grid.Rows.Add("Select a data folder in Settings");
                    return;
                }

                var header = SqliteInventory.Headers(baseName);
                var records = ReadRecords(baseName);
                int[] order = ColumnDisplayOrder(baseName, header);
                foreach (int c in order)
                {
                    string fileName = header[c].Trim();
                    string display = DisplayColumnHeader(baseName, header, fileName);
                    int index = grid.Columns.Add(fileName, display);
                    grid.Columns[index].Tag = fileName;
                    grid.Columns[index].Visible = IsSummaryColumn(baseName, display);
                }

                Theme.EnsureAddColumn(grid);
                GridLayout.Apply(grid, baseName);

                foreach (var record in records)
                {
                    var cells = new object[order.Length];
                    for (int n = 0; n < order.Length; n++)
                    {
                        int c = order[n];
                        cells[n] = GetRecord(record, header[c]);
                    }
                    grid.Rows.Add(cells);
                }
            }
            finally
            {
                GridLayout.EndUpdate();
                if (grid.Tag is ColumnSearch search)
                {
                    search.FileBaseName = baseName;
                    search.Rebuild();
                }
            }
        }

        public static bool TryImportCsv(string sourcePath, out string message)
        {
            string baseName = GetPageFileBaseName(Navigator.CurrentPage);

            if (string.IsNullOrWhiteSpace(baseName))
            {
                message = "This page does not have a table in the database.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
            {
                message = "Select a data folder in Settings first.";
                return false;
            }

            if (!File.Exists(sourcePath))
            {
                message = "The selected file could not be found.";
                return false;
            }

            var sourceLines = File.ReadAllLines(sourcePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (sourceLines.Count == 0)
            {
                message = "The selected file is empty.";
                return false;
            }

            string[] expectedHeader = GetExpectedHeader(baseName).Split(',');
            var sourceRows = CsvIO.Read(sourcePath);
            if (sourceRows.Count == 0)
            {
                message = "The selected file is empty.";
                return false;
            }

            var incomingHeader = sourceRows[0];
            bool exact = NormalizeHeader(string.Join(",", incomingHeader)) ==
                         NormalizeHeader(GetExpectedHeader(baseName));
            if (!exact &&
                ((baseName != Customers && baseName != Vendors) ||
                 !IncomingMapsToExpected(incomingHeader, expectedHeader)))
            {
                message =
                    "The file headings do not match this page.\n\n" +
                    "Expected:\n" + GetExpectedHeader(baseName) + "\n\n" +
                    "Found:\n" + sourceLines[0];
                return false;
            }

            if (baseName == Customers)
                EnsureFileColumns(Customers, "Address", "Email", "Phone", "Company", "Current Balance", "Notes", "Description");
            if (baseName == Vendors)
                EnsureFileColumns(Vendors, "Company", "Phone", "Current Balance", "Notes", "Description");

            SqliteInventory.EnsureCreated();

            if (sourceRows.Count < 2)
            {
                message = "Headings matched, but there were no data rows to import.";
                return false;
            }

            var batch = new List<Dictionary<string, string>>();
            for (int i = 1; i < sourceRows.Count; i++)
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < incomingHeader.Length; c++)
                    values[incomingHeader[c].Trim()] = c < sourceRows[i].Length ? sourceRows[i][c] : "";
                batch.Add(values);
            }

            int imported = SqliteInventory.InsertMany(baseName, batch);
            message = $"{imported} row(s) imported into {SqliteInventory.FileName} ({baseName}).";
            NotifyDataChanged();
            return true;
        }

        private static bool IncomingMapsToExpected(string[] incoming, string[] expected)
        {
            if (incoming.Length == 0)
                return false;

            foreach (var column in incoming)
            {
                string name = column.Trim();
                if (name.Length == 0)
                    continue;
                if (!expected.Any(e => e.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            return true;
        }

        internal static bool TryParseStartDate(string fileName, string baseName, out DateTime date)
        {
            date = default;
            string noExt = Path.GetFileNameWithoutExtension(fileName);
            string prefix = baseName + "_";

            if (!noExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string rest = noExt.Substring(prefix.Length);

            // live file: deals_2026-08-27
            // ignore already-archived style names that accidentally sit in root
            if (rest.Contains('_'))
                return false;

            return DateTime.TryParse(rest, out date);
        }

        private static string NormalizeHeader(string header)
        {
            var parts = header
                .Split(',')
                .Select(part => part.Trim().Trim('"').ToLowerInvariant());

            return string.Join(",", parts);
        }
    }
}
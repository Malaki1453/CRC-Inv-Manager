using System.Globalization;
using System.Text.Json;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Facade over inventory tables: grids, CSV import, PDFs, numbering, and term roll-over.
    /// Table names match SQLite tables. Pages should call this instead of SqliteInventory directly.
    /// </summary>
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
        public const string PendingChanges = "pending_changes";
        public const string RecordStatus = "Record Status";
        public const string RecordLive = "Live";
        public const string RecordWaitingAdd = "Waiting for confirmation to add";
        public const string RecordWaitingEdit = "Waiting for confirmation to edit";
        public const string RecordWaitingDelete = "Waiting for confirmation to delete";
        public const string StoredInvoicesFolderName = "Stored Invoices";
        public const string StoredSalesOrdersFolderName = "Stored Sales Orders";
        public const string PdfKindInvoice = "invoice";
        public const string PdfKindInvoiceSource = "invoice_source";
        public const string PdfKindSalesOrder = "sales_order";
        public const string PdfKindPurchase = "purchase";
        public const string PdfKindPurchaseInvoice = "purchase_invoice";
        public const string PdfKindSale = "sale";
        public const string RoutingNumber = "Routing Number";
        public const string AccountNumber = "Account Number";
        public const string InvoiceLinesColumn = "Lines Json";
        public const string InvoiceDateColumn = "Invoice Date";
        public const string InvoiceTaxModeColumn = "Tax Mode";
        public const string InvoiceTypeColumn = "Type";
        public const string InvoiceTypeIssued = "Issued";
        public const string InvoiceTypeReceived = "Received";
        public const string FreightCompanyColumn = "Freight Company";

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

        /// <summary>Legacy CSV name for a table in the current term: {table}_{yyyy-MM-dd}.csv.</summary>
        public static string GetFileName(string baseName)
        {
            DateTime start = AppState.TermStartDate ?? DateTime.Today;
            return $"{baseName}_{start:yyyy-MM-dd}.csv";
        }

        /// <summary>Full path of that leftover CSV inside the chosen inventory folder.</summary>
        public static string GetPath(string baseName)
        {
            // InventoryFolder is blank: there is no directory to combine with the leftover CSV name.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                throw new InvalidOperationException("No data folder has been selected.");

            return Path.Combine(AppState.InventoryFolder, GetFileName(baseName));
        }

        /// <summary>True when the live database exists locally, or when a remote session is connected.</summary>
        public static bool Exists(string baseName)
        {
            // Remote clients never see a folder file; the server owns the tables.
            if (DataLink.IsRemote)
            {
                SqliteInventory.EnsureCreated();
                return true;
            }

            // InventoryFolder is blank in local mode: no database file can exist until a folder is chosen.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return false;

            SqliteInventory.EnsureCreated();
            return SqliteInventory.GetPath() != null;
        }

        /// <summary>Newest leftover CSV for this table in the folder, by parsed term date.</summary>
        public static string? FindCurrentFile(string baseName)
        {
            // InventoryFolder is blank or missing: GetFiles would throw and leftover CSVs cannot exist.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) ||
                !Directory.Exists(AppState.InventoryFolder))
                return null;

            var matches = Directory.GetFiles(AppState.InventoryFolder, baseName + "_*.csv");

            DateTime bestDate = DateTime.MinValue;
            // Newest leftover CSV whose name parsed as table_yyyy-MM-dd.csv.
            string? bestPath = null;

            // path is one leftover CSV in the inventory folder matching this table prefix.
            foreach (var path in matches)
            {
                // Filename is a live table_yyyy-MM-dd.csv: skip archived or unparseable names.
                if (TryParseStartDate(Path.GetFileName(path), baseName, out var date))
                {
                    // This CSV's term date is at least as new as the current winner: keep the newest leftover.
                    if (date >= bestDate)
                    {
                        bestDate = date;
                        bestPath = path;
                    }
                }
            }

            return bestPath;
        }

        /// <summary>Set TermStartDate from the newest SQLite term, or leftover CSV names if the database is empty.</summary>
        public static void SyncTermStartFromFiles()
        {
            DateTime? latest = SqliteInventory.LatestTerm();

            // Empty new databases still inherit a term from leftover CSVs in the folder.
            if (latest == null &&
                !string.IsNullOrWhiteSpace(AppState.InventoryFolder) &&
                Directory.Exists(AppState.InventoryFolder))
            {
                foreach (var baseName in All)
                {
                    // path is a leftover CSV for this table that may still encode a term date.
                    foreach (var path in Directory.GetFiles(AppState.InventoryFolder, baseName + "_*.csv"))
                    {
                        // Name parses and is newer than the current latest: inherit the newest leftover term.
                        if (TryParseStartDate(Path.GetFileName(path), baseName, out var date) &&
                            (latest == null || date > latest))
                            latest = date;
                    }
                }
            }

            // A term was found in SQLite or leftover CSVs: persist it so later pages are not undated.
            if (latest != null)
            {
                AppState.TermStartDate = latest;
                AppLock.SaveSettings();
            }
        }

        /// <summary>CSV names that have no matching table yet, after syncing the term date.</summary>
        public static List<string> GetMissingFiles()
        {
            SyncTermStartFromFiles();

            var missing = new List<string>();
            foreach (var file in All)
            {
                // This table has no live database yet: list its expected leftover CSV name for CreateMissingFiles.
                if (!Exists(file))
                    missing.Add(GetFileName(file));
            }

            return missing;
        }

        /// <summary>SQLite table used by a navigation page, or empty for pages that are not tables.</summary>
        public static string GetPageFileBaseName(AppPage page)
        {
            return page switch
            {
                AppPage.PurchaseSales => PurchaseSales,
                AppPage.AddPurchase => PurchaseSales,
                AppPage.Sales => Sales,
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

        /// <summary>Status-bar file label for the page currently on screen.</summary>
        public static string? GetActiveFileName()
        {
            return GetDisplayedFileName(Navigator.CurrentPage);
        }

        /// <summary>Status-bar label: live db + table + term, or archive + live when Old is on.</summary>
        public static string? GetDisplayedFileName(AppPage page)
        {
            string baseName = GetPageFileBaseName(page);
            // Page is Help/Settings/etc. with no SQLite table: status bar would show a blank table name.
            if (string.IsNullOrWhiteSpace(baseName))
                return null;

            // Old Inventory is on and this table has archive rows: show archive + live so the toggle is obvious.
            if (SqliteInventory.UsingArchive(baseName))
                return $"{SqliteInventory.ArchiveFileName} + {SqliteInventory.FileName}  ·  {baseName}";

            string term = (AppState.TermStartDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            return $"{SqliteInventory.FileName}  ·  {baseName}  ·  {term}";
        }

        /// <summary>Live database path, or the archive path while Old Inventory is on.</summary>
        public static string? GetActiveFilePath()
        {
            return AppState.ViewingOldInventory
                ? SqliteInventory.GetArchivePath()
                : SqliteInventory.GetPath();
        }

        /// <summary>True when the live or archive database file exists for the current view.</summary>
        public static bool ActiveFileExists()
        {
            return GetActiveFilePath() != null;
        }

        /// <summary>Folder for leftover invoice PDFs next to the database, or null if no folder is chosen.</summary>
        public static string? GetStoredInvoicesFolder()
        {
            // InventoryFolder is blank: Stored Invoices lives next to the database, so there is no path.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, StoredInvoicesFolderName);
        }

        /// <summary>Create the Stored Invoices folder when a data folder is selected.</summary>
        public static void EnsureStoredInvoicesFolder()
        {
            // Stored Invoices directory next to the database, or null when no folder is chosen.
            string? path = GetStoredInvoicesFolder();
            // No data folder selected: CreateDirectory would throw on a null path.
            if (path == null)
                return;

            Directory.CreateDirectory(path);
        }

        /// <summary>Open the stored invoice PDF for this number, or explain why none was found.</summary>
        public static void OpenStoredInvoice(string? invoiceNumber)
        {
            // Trimmed invoice number used to look up the stored PDF.
            string key = (invoiceNumber ?? "").Trim();
            // Invoice number is blank: searching would later show a misleading "not found".
            if (key.Length == 0)
            {
                MessageBox.Show(
                    "This row has no invoice number.",
                    "Invoice",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Temp viewer file or leftover disk PDF for this invoice number.
            string? path = FindStoredPdf(PdfKindInvoice, key);
            // No invoice PDF in the database or leftover folder: OpenPdf would crash on a null path.
            if (path == null)
            {
                MessageBox.Show(
                    $"No stored PDF was found for invoice {key}.",
                    "Invoice Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            OpenPdf(path, PdfKindInvoice, key);
        }

        /// <summary>Disk or database path of a stored sales-order PDF, or null.</summary>
        public static string? FindStoredSalesOrder(string? soNumber)
        {
            return FindStoredPdf(PdfKindSalesOrder, soNumber);
        }

        /// <summary>Store a PDF in the database and write a temp copy for the in-app viewer.</summary>
        public static string SaveStoredPdf(string kind, string key, string fileName, byte[] content)
        {
            SqliteInventory.SavePdf(kind, key, fileName, content);
            return WritePdfViewFile(kind, fileName, content);
        }

        /// <summary>Remove a stored PDF from the database, leftover disk copy, and temp viewer files.</summary>
        public static void DeleteStoredPdf(string kind, string? key)
        {
            key = (key ?? "").Trim();
            // Document number is blank: deleting by an empty key would match unrelated PDFs.
            if (key.Length == 0)
                return;

            // Leftover invoice or sales-order PDF on disk for this kind and number.
            string? disk = FindPdfOnDisk(kind, key);
            SqliteInventory.DeletePdf(kind, key);
            // A leftover file was found: the database row is gone, but the disk copy would re-import later.
            if (disk != null)
            {
                try
                {
                    // Leftover file is still present: File.Delete throws if it vanished between Find and Delete.
                    if (File.Exists(disk))
                        File.Delete(disk);
                }
                // Leftover may be locked or in use; the database row is already gone so the disk copy can stay.
                catch
                {
                }
            }

            try
            {
                string folder = PdfViewFolder();
                // Temp PdfView folder is missing: GetFiles would throw and there is nothing to clean up.
                if (!Directory.Exists(folder))
                    return;
                string prefix = (kind ?? "").Trim() + "-";
                // path is a temp viewer PDF under LocalApplicationData\...\PdfView.
                foreach (var path in Directory.GetFiles(folder, "*.pdf"))
                {
                    string name = Path.GetFileName(path);
                    // Temp file is not this PDF kind: deleting it would wipe other kinds' viewer copies.
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    // File name does not include this document number: would delete other documents of the same kind.
                    if (!name.Contains(key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    File.Delete(path);
                }
            }
            // Viewer copies are disposable temps; a locked file must not fail the database delete.
            catch
            {
            }
        }

        /// <summary>True when the database already has a PDF for this kind and key.</summary>
        public static bool HasStoredPdf(string kind, string? key) =>
            SqliteInventory.HasPdf(kind, (key ?? "").Trim());

        /// <summary>Attached vendor invoice PDF, or the generated purchase PDF, for this PO.</summary>
        public static string? FindPurchaseViewPdf(string? po)
        {
            return FindStoredPdf(PdfKindPurchaseInvoice, po) ??
                   FindStoredPdf(PdfKindPurchase, po);
        }

        /// <summary>Open an attached purchase invoice, else the generated PDF, else offer to create one.</summary>
        public static void ShowPurchasePdf(string po, Action? create)
        {
            po = (po ?? "").Trim();
            // PO number is blank: there is no document number to look up or generate.
            if (po.Length == 0)
            {
                MessageBox.Show(
                    "This row has no number to open a PDF.",
                    "PDF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Vendor invoice PDF imported for this PO, if one exists.
            string? attached = FindStoredPdf(PdfKindPurchaseInvoice, po);
            // An attached vendor invoice exists: prefer it over the generated purchase PDF.
            if (attached != null)
            {
                OpenPdf(attached, PdfKindPurchaseInvoice, po);
                return;
            }

            ShowPdf(PdfKindPurchase, po, "purchase " + po, create);
        }

        /// <summary>Resolve a stored PDF from the database, importing a leftover disk file if needed.</summary>
        public static string? FindStoredPdf(string kind, string? key)
        {
            key = (key ?? "").Trim();
            // Document number is blank: an empty key would match the wrong PDF or every leftover.
            if (key.Length == 0)
                return null;
            // Local session with no folder: there is no database and no leftover folder to search (remote still has the server DB).
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) && !DataLink.IsRemote)
                return null;

            // Database blob and filename if this kind+key was already imported.
            var stored = SqliteInventory.TryGetPdf(kind, key);
            // PDF is already in the database: write a temp viewer copy instead of re-reading leftover disk files.
            if (stored != null)
                return WritePdfViewFile(kind, stored.Value.FileName, stored.Value.Content);

            // Leftover PDF on disk for this kind and document number.
            string? disk = FindPdfOnDisk(kind, key);
            // No leftover file either: nothing to import or open.
            if (disk == null)
                return null;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(disk);
                SqliteInventory.SavePdf(kind, key, Path.GetFileName(disk), bytes);
            }
            // Locked leftover files can still be opened from disk.
            catch
            {
                return disk;
            }

            return WritePdfViewFile(kind, Path.GetFileName(disk), bytes);
        }

        /// <summary>Open a stored PDF, or ask to create one when none is on file.</summary>
        public static void ShowPdf(string kind, string key, string label, Action? create)
        {
            key = (key ?? "").Trim();
            // Document number is blank: cannot look up or create a PDF without a number.
            if (key.Length == 0)
            {
                MessageBox.Show(
                    "This row has no number to open a PDF.",
                    "PDF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Temp viewer path of the stored PDF, or null if none is on file.
            string? path = FindStoredPdf(kind, key);
            // A stored PDF was found: open it instead of prompting to create a duplicate.
            if (path != null)
            {
                OpenPdf(path, kind, key);
                return;
            }

            var ask = MessageBox.Show(
                "No PDF was found for " + label + ".\n\nCreate a new one?",
                "PDF",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            // User chose No: invoking create would still generate a PDF after they declined.
            if (ask != DialogResult.Yes)
                return;

            create?.Invoke();
        }

        /// <summary>Find a leftover invoice or sales-order PDF on disk by document number.</summary>
        private static string? FindPdfOnDisk(string kind, string key)
        {
            string? folder = kind == PdfKindInvoice
                ? GetStoredInvoicesFolder()
                : GetStoredSalesOrdersFolder();
            // Leftover folder path is missing or not created yet: GetFiles would throw.
            if (folder == null || !Directory.Exists(folder))
                return null;

            var files = Directory.GetFiles(folder, "*.pdf");
            // Looking for an invoice leftover: invoices match by number-in-filename, not the "Sales Order {key}" prefix.
            if (kind == PdfKindInvoice)
            {
                // path is a leftover invoice PDF whose stem may contain the invoice number.
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

        /// <summary>LocalApplicationData folder for temp copies the in-app PDF viewer opens.</summary>
        private static string PdfViewFolder() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CastRightCatchInvManagement",
                "PdfView");

        /// <summary>Temp file for the in-app viewer only. The database stays the stored copy.</summary>
        private static string WritePdfViewFile(string kind, string fileName, byte[] content)
        {
            Directory.CreateDirectory(PdfViewFolder());
            string safe = string.Join("_", (fileName ?? "document.pdf").Split(Path.GetInvalidFileNameChars()));
            // Sanitized name was all invalid chars: WriteAllBytes needs a usable file name.
            if (safe.Length == 0)
                safe = "document.pdf";
            // Sanitized name has no .pdf extension: the viewer and GetFiles("*.pdf") expect a PDF suffix.
            if (!safe.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                safe += ".pdf";
            string prefix = string.IsNullOrWhiteSpace(kind) ? "pdf" : kind.Trim();
            // Temp file the in-app viewer opens; the database remains the stored copy.
            string path = Path.Combine(PdfViewFolder(), prefix + "-" + safe);
            File.WriteAllBytes(path, content);
            return path;
        }

        /// <summary>Existing SO # already assigned to this customer for any of the given purchase lots.</summary>
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
            // No usable purchase-order numbers after normalize: scanning sales would match nothing useful.
            if (pos.Count == 0)
                return null;

            // record is one visible sales row that may already hold an SO # for these lots.
            foreach (var record in ReadRecords(Sales))
            {
                // Sale is not this customer: another customer's SO on the same PO would be reused incorrectly.
                if (!MatchesCustomer(record, customerCode, customerName))
                    continue;
                // Sale's customer PO is not in the requested lots: would return an SO from a different purchase.
                if (!pos.Contains(NormalizePo(SalePo(record))))
                    continue;

                string so = GetRecord(record, "SO #").Trim();
                // This sale already has an SO #: reuse it instead of assigning a new number.
                if (so.Length > 0)
                    return so;
            }

            return null;
        }

        /// <summary>Open a PDF in the in-app viewer, inferring kind and key from the file name when omitted.</summary>
        public static void OpenPdf(string path, string? kind = null, string? key = null)
        {
            // path is the PDF file the in-app viewer should open; kind/key are optional document type and number.
            DescribePdf(path, out string title, out string? inferredKind, out string? inferredKey);
            PdfViewForm.ShowDocument(
                path,
                title,
                kind ?? inferredKind,
                key ?? inferredKey);
        }

        /// <summary>Infer invoice vs sales-order title, kind, and document number from path and file name.</summary>
        public static void DescribePdf(string path, out string title, out string? kind, out string? key)
        {
            // path is the PDF being opened; kind/key are inferred invoice vs sales-order type and document number.
            string stem = Path.GetFileNameWithoutExtension(path) ?? "";
            string folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "") ?? "";
            title = stem.Length > 0 ? stem : "PDF";
            kind = null;
            key = null;

            bool invoice = folder.Equals(StoredInvoicesFolderName, StringComparison.OrdinalIgnoreCase) ||
                           stem.StartsWith("Invoice ", StringComparison.OrdinalIgnoreCase);
            bool salesOrder = folder.Equals(StoredSalesOrdersFolderName, StringComparison.OrdinalIgnoreCase) ||
                              stem.StartsWith("Sales Order ", StringComparison.OrdinalIgnoreCase);

            // Path looks like a stored invoice: set kind/key so the viewer title and save-back use invoice, not SO.
            if (invoice)
            {
                kind = PdfKindInvoice;
                key = KeyAfterPrefix(stem, "Invoice ");
                // Number was parsed after "Invoice ": title should be "Invoice {number}", not the raw filename.
                if (key.Length > 0)
                    title = "Invoice " + key;
            }
            // Not an invoice, but the path looks like a sales order: same kind/key inference for SO viewer titles.
            else if (salesOrder)
            {
                kind = PdfKindSalesOrder;
                key = KeyAfterPrefix(stem, "Sales Order ");
                // Number was parsed after "Sales Order ": title should be "Sales Order {number}".
                if (key.Length > 0)
                    title = "Sales Order " + key;
            }
        }

        /// <summary>Document number after a prefix such as "Invoice ", stopping at " - ".</summary>
        private static string KeyAfterPrefix(string stem, string prefix)
        {
            // Filename does not start with the expected prefix: the rest of the stem is not a document number.
            if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return "";

            string rest = stem[prefix.Length..].Trim();
            int dash = rest.IndexOf(" - ", StringComparison.Ordinal);
            // " - " appears after the prefix (customer/vendor suffix): the document number is only the part before the dash.
            if (dash >= 0)
                rest = rest[..dash];
            return rest.Trim();
        }

        /// <summary>Open the stored sales-order PDF, or explain why none was found.</summary>
        public static void OpenStoredSalesOrder(string? soNumber)
        {
            // Temp viewer path of the stored sales-order PDF, or null if none is on file.
            string? path = FindStoredSalesOrder(soNumber);
            // No stored SO PDF: tell the user why instead of crashing OpenPdf.
            if (path == null)
            {
                // Trimmed sales-order number used in the not-found message.
                string key = (soNumber ?? "").Trim();
                // SO number itself is blank: "not found for sales order {blank}" is worse than "row has no number".
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

            OpenPdf(path, PdfKindSalesOrder, soNumber);
        }

        /// <summary>Folder for leftover sales-order PDFs next to the database, or null if no folder is chosen.</summary>
        public static string? GetStoredSalesOrdersFolder()
        {
            // InventoryFolder is blank: leftover sales-order PDFs live next to the database.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, StoredSalesOrdersFolderName);
        }

        /// <summary>Create the Stored Sales Orders folder when a data folder is selected.</summary>
        public static void EnsureStoredSalesOrdersFolder()
        {
            // Stored Sales Orders directory next to the database, or null when no folder is chosen.
            string? path = GetStoredSalesOrdersFolder();
            // No data folder selected: CreateDirectory would throw on a null path.
            if (path == null)
                return;

            Directory.CreateDirectory(path);
        }

        /// <summary>Create the database, import leftover CSVs/PDFs, and default the term date if needed.</summary>
        public static void EnsureFilesExistOrAsk()
        {
            // This client talks to a server: do not create local folders/CSVs; the server already owns the DB.
            if (DataLink.IsRemote)
            {
                SqliteInventory.EnsureCreated();
                return;
            }

            // No local folder chosen: EnsureCreated/Import would have nowhere to put files.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return;

            Accounts.EnsureFile();
            // Term date was never set: CSV names and roll-over need a start date, so default to today.
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

        /// <summary>Ensure each named table exists with its expected columns.</summary>
        public static void CreateMissingFiles(IEnumerable<string> missingBaseNames)
        {
            SqliteInventory.EnsureCreated();
            foreach (var baseName in missingBaseNames)
                SqliteInventory.EnsureColumns(baseName);
        }

        /// <summary>
        /// Archive leftover CSVs, move completed process rows into old_inventory.db,
        /// and start a new term. Unfinished rows stay live and undated.
        /// </summary>
        public static void RollToNextTerm()
        {
            // No folder or the folder is gone: archiving CSVs and old_inventory.db needs a real directory.
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
                // currentPath is a leftover live CSV about to be moved into old data.
                foreach (var currentPath in Directory.GetFiles(AppState.InventoryFolder, baseName + "_*.csv"))
                {
                    string archivedName = $"{baseName}_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}.csv";
                    string archivePath = Path.Combine(archiveFolder, Path.GetFileName(currentPath));
                    // Leftover CSV already sits in old data under that name: File.Move would fail on an existing dest.
                    if (File.Exists(archivePath))
                        File.Delete(archivePath);
                    File.Move(currentPath, archivePath);
                }
            }

            SqliteInventory.ArchiveCompleted(start);
            SetViewingOldInventory(false);
            AppState.TermStartDate = DateTime.Today;
            AppLock.SaveSettings();
            SqliteInventory.EnsureCreated();
            NotifyDataChanged();
        }

        /// <summary>Canonical CSV/SQLite column list for a table, used to create schema and validate imports.</summary>
        public static string GetExpectedHeader(string baseName)
        {
            return baseName switch
            {
                PurchaseSales =>
                    "PO #,Vendor Code,Vendor,Location,Item Code,Description,COO,Pack Size,CS,Volume,Price Paid / LB,Overhead / LB,Freight / LB,Freight Company,Forwarder / LB,Other / LB,Total Cost / LB,Total Cost,Agreement Date,Expected Ship Date,Vendor Terms,Vendor Due Date,Ship Date,Arrival Date,Forwarder,Logistics,Status,Record Status",

                Sales =>
                    "PO #,SO #,Customer Code,Customer,Customer Terms,Item Code,Description,COO,Pack Size,CS,Volume,Sell Price / LB,Amount,Ship Date,Due Date,Invoice #,Paid,Status,Freight Company,Record Status",

                Customers =>
                    "Code,Name,Company,Established,Terms,Credit Limit,Contact Name,Address,Email,Phone,Current Balance,Notes,Description,Routing Number,Account Number,Record Status",

                Vendors =>
                    "Code,Name,Company,Type,Terms,Amount,Phone,Contact Name,Current Balance,Notes,Description,Finalized,Routing Number,Account Number,Record Status",

                ItemCodes =>
                    "Code,Description,COO,Farmed / Wild,Fresh / Frozen,Proc Country,Species,Scientific Name,Record Status",

                Invoices =>
                    "Invoice #,Type,SO #,PO #,Customer Code,Customer,Vendor Code,Vendor,Ship Date,Due Date,Amount,Paid,Outstanding,Status,Payment Date,Payment Method,Invoice Date,Terms,Ship Via,Sales Rep,Sold To,Ship To,Discount,Freight,Freight Company,Tax,Tax Mode,Lines Json,Record Status",

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
        }

        /// <summary>Rows this user is allowed to see. Blocked parties/products and hidden columns are omitted.</summary>
        public static List<Dictionary<string, string>> VisibleRecords(string baseName) =>
            ReadRecords(baseName);

        /// <summary>Visible rows for a table, adding newer customer/vendor columns when missing.</summary>
        public static List<Dictionary<string, string>> ReadRecords(string baseName)
        {
            // InventoryFolder is blank: Read would fail; empty list is safer for grids.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return new List<Dictionary<string, string>>();

            // Reading customers: older DBs lack Address/Email/etc.; add those columns before Read.
            if (baseName == Customers)
                EnsureFileColumns(Customers, "Address", "Email", "Phone", "Company", "Current Balance", "Notes", "Description", RoutingNumber, AccountNumber);
            // Reading vendors: same schema backfill for vendor extra columns.
            if (baseName == Vendors)
                EnsureFileColumns(Vendors, "Company", "Phone", "Current Balance", "Notes", "Description", RoutingNumber, AccountNumber);

            return SqliteInventory.Read(baseName);
        }

        /// <summary>Every row, including blocked ones. Used for numbering and unique-code checks.</summary>
        public static List<Dictionary<string, string>> ReadAllRecords(string baseName)
        {
            // InventoryFolder is blank: unrestricted read still needs a database path.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return new List<Dictionary<string, string>>();
            return SqliteInventory.ReadUnrestricted(baseName);
        }

        /// <summary>Cell text for a column, or empty when the key is missing.</summary>
        public static string GetRecord(Dictionary<string, string> record, string column)
        {
            return record.TryGetValue(column, out var value) ? value ?? "" : "";
        }

        /// <summary>Keep only digit characters, used for routing and account numbers.</summary>
        public static string DigitsOnly(string? text)
        {
            // Input is null or empty: Where(char.IsDigit) would throw on null.
            if (string.IsNullOrEmpty(text))
                return "";
            var chars = text.Where(char.IsDigit).ToArray();
            return new string(chars);
        }

        /// <summary>Show only the last four account digits; short numbers stay unmasked.</summary>
        public static string MaskAccountNumber(string? raw)
        {
            string digits = DigitsOnly(raw);
            // No digits in the account number: masking nothing would show "•••• " junk.
            if (digits.Length == 0)
                return "";
            // Number is four digits or fewer: a last-four mask would hide the whole number.
            if (digits.Length <= 4)
                return digits;
            return "•••• " + digits[^4..];
        }

        /// <summary>Keep the stored account number when the user retyped only the mask or last four digits.</summary>
        public static string ResolveAccountNumber(string typed, string stored)
        {
            string typedDigits = DigitsOnly(typed);
            string storedDigits = DigitsOnly(stored);
            // User cleared the field: treat as a blank account, not "keep stored".
            if (typedDigits.Length == 0)
                return "";
            // Stored number is longer than four digits and the user retyped only the last four or the mask: keep the real account instead of saving the mask.
            if (storedDigits.Length > 4 &&
                (typedDigits == storedDigits[^4..] ||
                 typed.Trim() == MaskAccountNumber(storedDigits)))
                return storedDigits;
            return typedDigits;
        }

        /// <summary>Row workflow state. Blank (older rows) counts as Live.</summary>
        public static string StatusOf(Dictionary<string, string> record)
        {
            string value = GetRecord(record, RecordStatus).Trim();
            return value.Length == 0 ? RecordLive : value;
        }

        /// <summary>True when the row is queued as a new record awaiting review.</summary>
        public static bool IsWaitingAdd(Dictionary<string, string> record) =>
            StatusOf(record).Equals(RecordWaitingAdd, StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the row is waiting for add, edit, or delete confirmation.</summary>
        public static bool IsWaiting(Dictionary<string, string> record)
        {
            string status = StatusOf(record);
            return status.Equals(RecordWaitingAdd, StringComparison.OrdinalIgnoreCase) ||
                   status.Equals(RecordWaitingEdit, StringComparison.OrdinalIgnoreCase) ||
                   status.Equals(RecordWaitingDelete, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Parse a money or quantity cell ($1,234.50), (50), or blank → 0.</summary>
        public static decimal ParseMoney(string? text)
        {
            text = (text ?? "").Trim();
            // Money cell is blank: blank means 0, not a parse error.
            if (text.Length == 0)
                return 0;

            bool negative = text.StartsWith('(') && text.EndsWith(')');
            text = text.Replace("$", "").Replace(",", "").Replace("(", "").Replace(")", "").Trim();
            // Invariant first (CSV), then the PC's culture for typed values.
            // Text isn't a number in either culture: garbage cells must not throw; treat as 0.
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) &&
                !decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
                return 0;

            return negative ? -Math.Abs(amount) : amount;
        }

        /// <summary>
        /// Home-screen totals from sales and invoices in the current database view
        /// (live only, or archive + live when Old is on).
        /// </summary>
        public static DashboardSummary GetDashboardSummary()
        {
            decimal revenue = 0;
            decimal outstanding = 0;
            decimal late = 0;
            int overdue = 0;
            var deals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // User can read sales: Home revenue and deal count come from live sale amounts.
            if (TableAccess.Can(TableAccess.Sales))
            {
                foreach (var sale in ReadRecords(Sales))
                {
                    // Queued adds are not live revenue yet.
                    if (IsWaitingAdd(sale))
                        continue;
                    revenue += ParseMoney(GetRecord(sale, "Amount"));
                    string po = SalePo(sale);
                    string so = GetRecord(sale, "SO #").Trim();
                    // Deal identity: customer PO when present, otherwise SO #.
                    string key = po.Length > 0 ? po : so;
                    // Sale has a PO or SO: use that as the deal identity so multi-line lots count once.
                    if (key.Length > 0)
                        deals.Add(key);
                    // No PO/SO on this sale: still count the row via a unique placeholder so blank lots aren't dropped.
                    else
                        deals.Add("row:" + deals.Count);
                }
            }
            else if (TableAccess.Can(TableAccess.Invoices))
            {
                // Users without sales still see issued-invoice totals on Home.
                foreach (var invoice in ReadRecords(Invoices))
                {
                    // Queued add or vendor (received) invoice: those are not customer revenue.
                    if (IsWaitingAdd(invoice) || IsReceivedInvoice(invoice))
                        continue;
                    revenue += ParseMoney(GetRecord(invoice, "Amount"));
                }
            }

            // User can read invoices: outstanding and late cards need issued open invoices.
            if (TableAccess.Can(TableAccess.Invoices))
            {
                foreach (var invoice in ReadRecords(Invoices))
                {
                    // Queued, vendor, or closed invoice: outstanding would include bills, waiting rows, and paid invoices.
                    if (IsWaitingAdd(invoice) || IsReceivedInvoice(invoice) || InvoiceIsClosed(invoice))
                        continue;

                    decimal due = InvoiceOutstanding(invoice);
                    // Nothing left to collect: do not add to outstanding or count as overdue.
                    if (due <= 0)
                        continue;

                    outstanding += due;
                    // Due date is before today: split late vs current outstanding and increment overdue count.
                    if (InvoiceIsPastDue(invoice))
                    {
                        late += due;
                        overdue++;
                    }
                }
            }

            int dealCount = TableAccess.Can(TableAccess.Sales) ? deals.Count : 0;
            // Invoice-only users get a deal count from issued invoices instead of sales lots.
            if (dealCount == 0 && TableAccess.Can(TableAccess.Invoices) && !TableAccess.Can(TableAccess.Sales))
            {
                foreach (var invoice in ReadRecords(Invoices))
                {
                    // Queued or vendor invoice: deal count should only count issued customer invoices.
                    if (IsWaitingAdd(invoice) || IsReceivedInvoice(invoice))
                        continue;
                    string so = GetRecord(invoice, "SO #").Trim();
                    string number = GetRecord(invoice, "Invoice #").Trim();
                    // Deal identity for invoice-only users: SO # when present, otherwise Invoice #.
                    string key = so.Length > 0 ? so : number;
                    // Invoice has an SO or invoice number: skip blanks so HashSet is not one empty-string deal.
                    if (key.Length > 0)
                        deals.Add(key);
                }

                dealCount = deals.Count;
            }

            return new DashboardSummary(
                revenue,
                outstanding,
                late,
                dealCount,
                overdue,
                AppState.ViewingOldInventory);
        }

        /// <summary>Amount still due: the Outstanding cell, or Amount minus Paid when that cell is blank.</summary>
        internal static decimal InvoiceOutstanding(Dictionary<string, string> invoice)
        {
            decimal outstanding = ParseMoney(GetRecord(invoice, "Outstanding"));
            // Outstanding cell already has a positive amount: trust it; Amount minus Paid is only a fallback.
            if (outstanding > 0)
                return outstanding;

            decimal amount = ParseMoney(GetRecord(invoice, "Amount"));
            decimal paid = ParseMoney(GetRecord(invoice, "Paid"));
            return Math.Max(0, amount - paid);
        }

        /// <summary>True for vendor (received) invoices; older rows with a vendor and no customer count as received.</summary>
        internal static bool IsReceivedInvoice(Dictionary<string, string> invoice)
        {
            string type = GetRecord(invoice, InvoiceTypeColumn).Trim();
            // Type column is Received: vendor invoices must not be treated as issued customer invoices.
            if (type.Equals(InvoiceTypeReceived, StringComparison.OrdinalIgnoreCase))
                return true;
            // Type is Issued: stop the vendor/customer heuristic from flipping a labeled issued invoice.
            if (type.Equals(InvoiceTypeIssued, StringComparison.OrdinalIgnoreCase))
                return false;
            return GetRecord(invoice, "Vendor").Trim().Length > 0 &&
                   GetRecord(invoice, "Customer").Trim().Length == 0;
        }

        /// <summary>Letterhead block: business name (or Cast Right Catch Co.), address, and phone.</summary>
        public static string CompanyAddressBlock()
        {
            var lines = new List<string>();
            string name = (AppState.BusinessName ?? "").Trim();
            // BusinessName is blank: letterhead would print an empty first line.
            if (name.Length == 0)
                name = "Cast Right Catch Co.";
            lines.Add(name);
            string address = (AppState.Address ?? "").Trim();
            // Address is set: skip adding a blank line to the letterhead block.
            if (address.Length > 0)
                lines.Add(address);
            string phone = (AppState.Phone ?? "").Trim();
            // Phone is set: skip adding a blank phone line.
            if (phone.Length > 0)
                lines.Add(phone);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>True when status is paid/closed/complete/settled, or outstanding is zero on a billed invoice.</summary>
        internal static bool InvoiceIsClosed(Dictionary<string, string> invoice)
        {
            string status = GetRecord(invoice, "Status").Trim();
            // Status is a closed-state word: these invoices must not appear in outstanding even if the Outstanding cell is stale.
            if (status.Equals("paid", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("closed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("complete", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("settled", StringComparison.OrdinalIgnoreCase))
                return true;

            return InvoiceOutstanding(invoice) <= 0 && ParseMoney(GetRecord(invoice, "Amount")) > 0;
        }

        /// <summary>True when Due Date parses and is before today; unparseable dates are not treated as late.</summary>
        internal static bool InvoiceIsPastDue(Dictionary<string, string> invoice)
        {
            string dueText = GetRecord(invoice, "Due Date").Trim();
            // Due Date is not a date: unparseable dates must not count as late.
            if (!DateTime.TryParse(dueText, out var due))
                return false;
            return due.Date < DateTime.Today;
        }

        /// <summary>First non-empty cell among the given columns, for party names that use several headers.</summary>
        public static string GetRecordAny(Dictionary<string, string> record, params string[] columns)
        {
            foreach (var column in columns)
            {
                string value = GetRecord(record, column).Trim();
                // This column has text: return the first non-empty so callers can pass several party-name headers.
                if (value.Length > 0)
                    return value;
            }

            return "";
        }

        /// <summary>Uppercase PO/SO key with whitespace removed so CRC26-10001 matches CRC26 - 10001.</summary>
        public static string NormalizePo(string? po)
        {
            // PO is null or blank: normalizing empty should be "" rather than throwing on null.
            if (string.IsNullOrWhiteSpace(po))
                return "";

            return new string(po.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        }

        /// <summary>First purchase row whose PO # matches, allowing a prefix fallback.</summary>
        public static Dictionary<string, string>? FindPurchaseByPo(string? poNumber)
        {
            return FindByNormalized(PurchaseSales, "PO #", poNumber);
        }

        /// <summary>Every purchase line on this PO (one row per item).</summary>
        public static List<Dictionary<string, string>> FindPurchasesByPo(string? poNumber)
        {
            // Purchase lines whose PO # matches poNumber.
            var result = new List<Dictionary<string, string>>();
            string needle = NormalizePo(poNumber);
            // PO number is blank after normalize: every purchase with an empty PO # would match.
            if (needle.Length == 0)
                return result;

            foreach (var purchase in ReadRecords(PurchaseSales))
            {
                // This purchase's PO equals the requested PO: collect every line on that lot.
                if (NormalizePo(GetRecord(purchase, "PO #"))
                    .Equals(needle, StringComparison.OrdinalIgnoreCase))
                    result.Add(purchase);
            }

            return result;
        }

        /// <summary>Every sale whose customer PO matches <paramref name="poNumber"/>.</summary>
        public static List<Dictionary<string, string>> FindSalesByPo(string? poNumber)
        {
            // Sales whose customer PO matches poNumber.
            var result = new List<Dictionary<string, string>>();
            string needle = NormalizePo(poNumber);
            // PO is blank: would match every sale with a blank customer PO.
            if (needle.Length == 0)
                return result;

            foreach (var sale in ReadRecords(Sales))
            {
                // Sale's customer PO matches the requested lot.
                if (NormalizePo(SalePo(sale)).Equals(needle, StringComparison.OrdinalIgnoreCase))
                    result.Add(sale);
            }

            return result;
        }

        /// <summary>Customer PO for a sale. Stored in Invoice # after Lot # was removed.</summary>
        public static string SalePo(Dictionary<string, string> record)
        {
            // After the Lot # drop, Invoice # holds the customer PO.
            string customerPo = GetRecord(record, "Invoice #").Trim();
            // Invoice # cell has a customer PO: that's the stored customer PO after the Lot # drop.
            if (customerPo.Length > 0)
                return customerPo;

            string named = GetRecord(record, "Customer PO").Trim();
            // Customer PO column is filled: some schemas store the customer PO under that name.
            if (named.Length > 0)
                return named;

            // Pre-migration rows with a Lot # column stored the customer PO in PO #.
            if (record.Keys.Any(k => k.Equals("Lot #", StringComparison.OrdinalIgnoreCase)))
                return GetRecord(record, "PO #").Trim();

            return "";
        }

        /// <summary>Purchase PO for a sale. PO # is the lot; Lot # is only a fallback for old rows.</summary>
        public static string SaleLot(Dictionary<string, string> record)
        {
            string po = GetRecord(record, "PO #").Trim();
            // PO # has a lot number: that's the purchase PO; Lot # is only a fallback for old rows.
            if (po.Length > 0)
                return po;

            return GetRecord(record, "Lot #").Trim();
        }

        /// <summary>Purchase-order suggestions for an item: PO # - vendor, field stores only the PO #.</summary>
        internal static List<LookupSuggest.Hit> PurchasePosForItem(string itemCode)
        {
            itemCode = (itemCode ?? "").Trim();
            var groups = new Dictionary<string, LookupSuggest.Hit>(StringComparer.OrdinalIgnoreCase);
            // No item code to filter: scanning all purchases would suggest every PO in the table.
            if (itemCode.Length == 0)
                return new List<LookupSuggest.Hit>();

            SqliteInventory.ForEachWhere(
                PurchaseSales,
                "Item Code",
                itemCode,
                purchase =>
                {
                    // Purchase is a queued add: unconfirmed lots should not appear in PO suggestions.
                    if (IsWaitingAdd(purchase))
                        return;
                    string po = GetRecord(purchase, "PO #").Trim();
                    // Purchase has no PO #: cannot suggest a blank PO.
                    if (po.Length == 0)
                        return;
                    // Normalized PO used to de-dupe multi-item lots in the suggestion list.
                    string key = NormalizePo(po);
                    // This PO is already in the list: one suggestion per PO even when the lot has many items.
                    if (groups.ContainsKey(key))
                        return;
                    string vendor = GetRecordAny(purchase, "Vendor", "Name");
                    groups[key] = new LookupSuggest.Hit(po, vendor, GetRecord(purchase, "Vendor Code"));
                });

            return groups.Values
                .OrderBy(hit => hit.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>First sale matching a customer PO, optional customer, and optional item.</summary>
        public static Dictionary<string, string>? FindSaleByPo(
            string? poNumber,
            string? customerCode = null,
            string? customerName = null,
            string? itemCode = null)
        {
            return FindSale(poNumber, "PO #", customerCode, customerName, 3, itemCode);
        }

        /// <summary>First sale matching an SO #, optional customer, and optional item.</summary>
        public static Dictionary<string, string>? FindSaleBySo(
            string? soNumber,
            string? customerCode = null,
            string? customerName = null,
            string? itemCode = null)
        {
            return FindSale(soNumber, "SO #", customerCode, customerName, 3, itemCode);
        }

        /// <summary>Sale+purchase source row for invoicing: first match, or the line for <paramref name="itemCode"/>.</summary>
        public static Dictionary<string, string>? FindInvoiceSource(
            string? key,
            string? customerCode = null,
            string? customerName = null,
            string? itemCode = null)
        {
            var all = FindInvoiceSourcesForKey(key, customerCode, customerName);
            // No invoice source lines for this key: return null rather than all[0], which would throw.
            if (all.Count == 0)
                return null;

            string item = (itemCode ?? "").Trim();
            // No item filter: the first matching line is the source row for invoicing.
            if (item.Length == 0)
                return all[0];

            // record is one merged sale+purchase line; keep the line whose Item Code matches.
            return all.FirstOrDefault(record =>
                GetRecord(record, "Item Code").Equals(item, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when an invoice row already uses this number (case-insensitive).</summary>
        public static bool InvoiceNumberExists(string? invoiceNumber)
        {
            string needle = (invoiceNumber ?? "").Trim();
            // Invoice number is blank: empty would match every blank Invoice # cell.
            if (needle.Length == 0)
                return false;

            // record is one visible invoice row checked for a duplicate number.
            foreach (var record in ReadRecords(Invoices))
            {
                // This invoice row already uses that number: uniqueness check for new invoice numbers.
                if (GetRecord(record, "Invoice #").Trim()
                    .Equals(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>True when Lines Json holds at least one line; fills the invoice number from the row if blank.</summary>
        internal static bool TryInvoiceDraft(Dictionary<string, string> invoice, out InvoiceDraft draft)
        {
            draft = InvoiceDraft.FromJson(GetRecord(invoice, InvoiceLinesColumn)) ?? new InvoiceDraft();
            // Lines Json parsed to at least one line: empty draft is not a usable invoice body.
            if (draft.Lines.Count > 0)
            {
                // JSON did not store the invoice number: fill it from the Invoice # cell so the editor has a number.
                if (draft.InvoiceNumber.Length == 0)
                    draft.InvoiceNumber = GetRecord(invoice, "Invoice #").Trim();
                return true;
            }

            return false;
        }

        /// <summary>Issued invoice by SO #, or received invoice by PO # (falling back to SO #).</summary>
        internal static Dictionary<string, string>? FindInvoiceByOrder(string number, bool received)
        {
            string needle = NormalizePo(number);
            // Order number is blank: would match invoices with empty PO/SO.
            if (needle.Length == 0)
                return null;

            // record is one visible invoice row that may belong to this SO or PO.
            foreach (var record in ReadRecords(Invoices))
            {
                // This invoice's issued/received side does not match the lookup: issued SO lookup must not return a vendor PO invoice with the same number.
                if (IsReceivedInvoice(record) != received)
                    continue;

                string order = received
                    ? FirstNonEmpty(GetRecord(record, "PO #"), GetRecord(record, "SO #"))
                    : GetRecord(record, "SO #");
                // This invoice's order number matches the requested SO/PO.
                if (NormalizePo(order).Equals(needle, StringComparison.OrdinalIgnoreCase))
                    return record;
            }

            return null;
        }

        /// <summary>First non-blank trimmed value, used when received invoices store the order on PO # or SO #.</summary>
        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                // This candidate has text: received invoices store the order on PO # or SO #.
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        /// <summary>Insert or update the invoices table from an editor draft, preserving Paid when already set.</summary>
        internal static void UpsertInvoiceFromDraft(InvoiceDraft draft, DateTime due)
        {
            string number = (draft.InvoiceNumber ?? "").Trim();
            // Draft has no invoice number: the invoices table is keyed by Invoice #; blank would collide.
            if (number.Length == 0)
                throw new InvalidOperationException("Enter an invoice number.");

            Dictionary<string, string>? existing = null;
            // record is an invoice row (including blocked) checked as the existing row to update.
            foreach (var record in ReadAllRecords(Invoices))
            {
                // This row is not the invoice being upserted: keep scanning for the existing row.
                if (!GetRecord(record, "Invoice #").Trim()
                        .Equals(number, StringComparison.OrdinalIgnoreCase))
                    continue;
                existing = record;
                break;
            }

            var values = existing == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

            decimal paid = ParseMoney(GetRecord(values, "Paid"));
            decimal total = draft.InvoiceTotal;
            values["Invoice #"] = number;
            values[InvoiceTypeColumn] = draft.Received ? InvoiceTypeReceived : InvoiceTypeIssued;
            values["SO #"] = draft.SoNumber ?? "";
            values["PO #"] = draft.PoNumber ?? "";
            values["Customer Code"] = draft.Received ? "" : (draft.CustomerCode ?? "");
            values["Customer"] = draft.Received ? "" : (draft.CustomerName ?? "");
            values["Vendor Code"] = draft.Received ? (draft.VendorCode ?? "") : "";
            values["Vendor"] = draft.Received ? (draft.VendorName ?? "") : "";
            values["Ship Date"] = CsvIO.Date(draft.ShipDate);
            values["Due Date"] = CsvIO.Date(due);
            values["Amount"] = CsvIO.Money((double)total);
            // Paid cell is blank: keep an explicit empty Paid so outstanding can fall back to Amount minus Paid.
            if (GetRecord(values, "Paid").Length == 0)
                values["Paid"] = "";
            values["Outstanding"] = CsvIO.Money((double)Math.Max(0m, total - paid));
            // Status is blank: default new invoices to Open so they appear in outstanding.
            if (GetRecord(values, "Status").Length == 0)
                values["Status"] = "Open";
            values[InvoiceDateColumn] = CsvIO.Date(draft.InvoiceDate);
            values["Terms"] = draft.Terms ?? "";
            values["Ship Via"] = draft.ShipVia ?? "";
            values["Sales Rep"] = draft.SalesRep ?? "";
            values["Sold To"] = draft.SoldTo ?? "";
            values["Ship To"] = draft.ShipTo ?? "";
            values["Discount"] = draft.Discount.ToString("0.##", CultureInfo.InvariantCulture);
            values["Freight"] = draft.Freight.ToString("0.##", CultureInfo.InvariantCulture);
            values[FreightCompanyColumn] = draft.FreightCompany ?? "";
            values["Tax"] = draft.TaxRate.ToString("0.##", CultureInfo.InvariantCulture);
            values[InvoiceTaxModeColumn] = draft.TaxIsPercent ? "%" : "#";
            values[InvoiceLinesColumn] = draft.ToJson();

            // Insert vs update outcome; denied writes must surface result.Message.
            MutateResult result = existing == null
                ? MutateInsert(Invoices, values)
                : MutateUpdate(
                    Invoices,
                    row => GetRecord(row, "Invoice #").Trim()
                        .Equals(number, StringComparison.OrdinalIgnoreCase),
                    values);
            // Insert/update was denied or failed: surface the access/error message instead of silently skipping.
            if (!result.Ok)
                throw new InvalidOperationException(result.Message);
        }

        /// <summary>Sale+purchase lines that belong on an invoice, matched by invoice # and/or SO #.</summary>
        public static List<Dictionary<string, string>> FindInvoiceSourcesForInvoice(
            string? invoiceNumber,
            string? soNumber,
            string? customerCode = null,
            string? customerName = null)
        {
            // Merged sale+purchase lines that belong on this invoice.
            var result = new List<Dictionary<string, string>>();
            string invoice = (invoiceNumber ?? "").Trim();
            string so = NormalizePo(soNumber);
            // Both invoice # and SO # are blank: would match every sale.
            if (invoice.Length == 0 && so.Length == 0)
                return result;

            var seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sale in ReadRecords(Sales))
            {
                // Sale is not this customer: other customers' lines would appear on this invoice.
                if (!MatchesCustomer(sale, customerCode, customerName))
                    continue;

                string saleInvoice = GetRecord(sale, "Invoice #").Trim();
                string saleSo = NormalizePo(GetRecord(sale, "SO #"));
                bool matchInvoice = invoice.Length > 0 &&
                    saleInvoice.Equals(invoice, StringComparison.OrdinalIgnoreCase);
                bool matchSo = so.Length > 0 &&
                    saleSo.Equals(so, StringComparison.OrdinalIgnoreCase);
                // Sale matches neither invoice # nor SO #: skip unrelated sales.
                if (!matchInvoice && !matchSo)
                    continue;

                var purchase = FindPurchaseByPo(SaleLot(sale));
                string item = GetRecord(sale, "Item Code").Trim();
                string distinct = item.Length > 0 ? item : GetRecord(sale, "Description").Trim();
                // No item code or description: still need a unique key so seenItems can de-dupe blank lines.
                if (distinct.Length == 0)
                    distinct = result.Count.ToString();
                // This item is already added: one invoice line per item even if multiple sale rows exist.
                if (!seenItems.Add(distinct))
                    continue;

                result.Add(MergeSaleAndPurchase(sale, purchase));
            }

            return result;
        }

        /// <summary>SO # suggestion rows grouped by order, with customer name and item count.</summary>
        internal static List<LookupSuggest.Hit> SalesOrderSuggestHits() =>
            OrderSuggestHits(
                Sales,
                "SO #",
                "SO",
                new[] { "Customer", "Customer Name" },
                new[] { "Customer Code", "Cust ID" },
                purchase: false);

        /// <summary>PO # suggestion rows grouped by order, with vendor name and item count.</summary>
        internal static List<LookupSuggest.Hit> PurchaseOrderSuggestHits() =>
            OrderSuggestHits(
                PurchaseSales,
                "PO #",
                "PO",
                new[] { "Vendor", "Name", "Company" },
                new[] { "Vendor Code", "Code" },
                purchase: true);

        /// <summary>Group order lines by number and build lookup hits for the suggestion list.</summary>
        private static List<LookupSuggest.Hit> OrderSuggestHits(
            string table,
            string numberColumn,
            string kind,
            string[] partyNameColumns,
            string[] partyCodeColumns,
            bool purchase)
        {
            var groups = new Dictionary<string, OrderSuggest>(StringComparer.OrdinalIgnoreCase);
            // record is one order line (sale or purchase) grouped by SO # or PO #.
            foreach (var record in ReadRecords(table))
            {
                string number = GetRecord(record, numberColumn).Trim();
                // Order number is blank: do not group blank SO/PO as one suggestion.
                if (number.Length == 0)
                    continue;

                // Normalized order number used as the group key.
                string key = NormalizePo(number);
                // First line for this order number: start a new group; later lines only increment item count.
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new OrderSuggest
                    {
                        Number = number,
                        Party = GetRecordAny(record, partyNameColumns),
                        Code = GetRecordAny(record, partyCodeColumns)
                    };
                    groups[key] = group;
                }

                group.Items++;
                // First line had no party name: fill party from a later line on the same order.
                if (group.Party.Length == 0)
                    group.Party = GetRecordAny(record, partyNameColumns);
                // First line had no party code: same backfill for code.
                if (group.Code.Length == 0)
                    group.Code = GetRecordAny(record, partyCodeColumns);
            }

            var hits = new List<LookupSuggest.Hit>();
            foreach (var group in groups.Values)
            {
                string items = group.Items == 1 ? "1 item" : group.Items + " items";
                // Suggestion subtitle: party name and item count, or item count alone.
                string body = group.Party.Length > 0
                    ? group.Party + " - " + items
                    : items;
                // These groups are purchase orders: mark the hint so PO vs SO suggestions are distinguishable.
                if (purchase)
                    body += " · purchase";
                hits.Add(new LookupSuggest.Hit(
                    group.Number,
                    body,
                    kind,
                    group.Party + " " + group.Code + " " + kind));
            }

            hits.Sort((a, b) => string.Compare(a.Code, b.Code, StringComparison.OrdinalIgnoreCase));
            return hits;
        }

        /// <summary>Accumulator while grouping order lines for lookup suggestions.</summary>
        private sealed class OrderSuggest
        {
            public string Number { get; set; } = "";
            public string Party { get; set; } = "";
            public string Code { get; set; } = "";
            public int Items { get; set; }
        }

        /// <summary>Sale+purchase lines for a PO or SO key; short keys are ignored unless salesOrderOnly.</summary>
        public static List<Dictionary<string, string>> FindInvoiceSourcesForKey(
            string? key,
            string? customerCode = null,
            string? customerName = null,
            bool salesOrderOnly = false)
        {
            // Merged sale+purchase lines for this PO or SO key.
            var result = new List<Dictionary<string, string>>();
            string needle = NormalizePo(key);
            // Key is blank: would match every sale.
            if (needle.Length == 0)
                return result;
            // Short PO fragments would match too many lots while typing.
            if (!salesOrderOnly && needle.Length < 3)
                return result;

            var seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sale in ReadRecords(Sales))
            {
                // Sale is not this customer: other customers' lots would fill the invoice.
                if (!MatchesCustomer(sale, customerCode, customerName))
                    continue;

                string salePo = NormalizePo(SalePo(sale));
                string so = NormalizePo(GetRecord(sale, "SO #"));
                bool matchSo = so.Equals(needle, StringComparison.OrdinalIgnoreCase);
                // Caller asked for SO matches only: PO equality would pull in customer-PO lots with the same digits as an SO.
                if (salesOrderOnly)
                {
                    // This sale's SO does not match the needle: skip when salesOrderOnly.
                    if (!matchSo)
                        continue;
                }
                // Sale's customer PO and SO both miss the needle: skip unrelated sales when matching either key.
                else if (!salePo.Equals(needle, StringComparison.OrdinalIgnoreCase) && !matchSo)
                    continue;

                var purchase = FindPurchaseByPo(SaleLot(sale));

                string item = GetRecord(sale, "Item Code").Trim();
                string distinct = item.Length > 0 ? item : GetRecord(sale, "Description").Trim();
                // No item/description: still need a unique seenItems key.
                if (distinct.Length == 0)
                    distinct = result.Count.ToString();
                // Item already included: one line per item.
                if (!seenItems.Add(distinct))
                    continue;

                result.Add(MergeSaleAndPurchase(sale, purchase));
            }

            return result;
        }

        /// <summary>Purchase lines for a PO, optionally limited to a vendor; short keys ignored unless allowShort.</summary>
        public static List<Dictionary<string, string>> FindPurchaseSourcesForKey(
            string? key,
            string? vendorCode = null,
            string? vendorName = null,
            bool allowShort = false)
        {
            // Purchase lines for this PO, optionally limited to a vendor.
            var result = new List<Dictionary<string, string>>();
            string needle = NormalizePo(key);
            // PO key is blank: would return every purchase.
            if (needle.Length == 0)
                return result;
            // Short PO fragment while typing and short keys are not allowed: 1-2 char needles match too many lots.
            if (!allowShort && needle.Length < 3)
                return result;

            var seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var purchase in ReadRecords(PurchaseSales))
            {
                // Purchase is not this vendor: other vendors' POs would appear.
                if (!MatchesVendor(purchase, vendorCode, vendorName))
                    continue;

                string po = NormalizePo(GetRecord(purchase, "PO #"));
                // This purchase's PO does not match the needle: skip other lots.
                if (!po.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    continue;

                string item = GetRecord(purchase, "Item Code").Trim();
                string distinct = item.Length > 0 ? item : GetRecord(purchase, "Description").Trim();
                // No item/description: still need a unique seenItems key.
                if (distinct.Length == 0)
                    distinct = result.Count.ToString();
                // Item already added: one line per item on the PO.
                if (!seenItems.Add(distinct))
                    continue;

                result.Add(purchase);
            }

            return result;
        }

        /// <summary>Distinct purchase PO numbers for autocomplete, optionally filtered by vendor.</summary>
        public static AutoCompleteStringCollection PurchasePoSuggestions(
            string? vendorCode = null,
            string? vendorName = null)
        {
            var source = new AutoCompleteStringCollection();
            // record is a purchase line whose PO # may be offered in autocomplete.
            foreach (var record in ReadRecords(PurchaseSales))
            {
                // Purchase is not this vendor: autocomplete would list other vendors' POs.
                if (!MatchesVendor(record, vendorCode, vendorName))
                    continue;

                string po = GetRecord(record, "PO #").Trim();
                // PO is blank or already in the list: skip empty and duplicate suggestions.
                if (po.Length == 0 || source.Contains(po))
                    continue;
                source.Add(po);
            }

            return source;
        }

        /// <summary>Sale+purchase lines whose customer PO matches a key of at least three characters.</summary>
        public static List<Dictionary<string, string>> FindSalesOrderSourcesForKey(
            string? key,
            string? customerCode = null,
            string? customerName = null)
        {
            // Merged sale+purchase lines whose customer PO matches the key.
            var result = new List<Dictionary<string, string>>();
            string needle = NormalizePo(key);
            // Key is shorter than three characters: short fragments match too many customer POs while typing.
            if (needle.Length < 3)
                return result;

            foreach (var sale in ReadRecords(Sales))
            {
                // Sale is not this customer: other customers' lots would fill the sales order.
                if (!MatchesCustomer(sale, customerCode, customerName))
                    continue;

                string salePo = NormalizePo(SalePo(sale));
                // Sale's customer PO does not match the needle: skip unrelated lots.
                if (!salePo.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    continue;

                var purchase = FindPurchaseByPo(SaleLot(sale));
                result.Add(MergeSaleAndPurchase(sale, purchase));
            }

            return result;
        }

        /// <summary>Write SO # (and optional freight company) onto matching sale lines that have no conflicting SO.</summary>
        public static int AssignSalesOrderNumber(
            IEnumerable<string> purchaseOrders,
            string? customerCode,
            string? customerName,
            string soNumber,
            string? freightCompany = null)
        {
            soNumber = (soNumber ?? "").Trim();
            // No SO number to assign: writing empty would clear existing SO #s.
            if (soNumber.Length == 0)
                return 0;

            var pos = new HashSet<string>(
                purchaseOrders
                    .Select(NormalizePo)
                    .Where(po => po.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            // No POs in the set: would match every sale for the customer.
            if (pos.Count == 0)
                return 0;

            string company = (freightCompany ?? "").Trim();
            // record is a sale row being considered for SO # assignment.
            return UpdateRecords(Sales, record =>
            {
                // Sale is not this customer: do not stamp another customer's lines.
                if (!MatchesCustomer(record, customerCode, customerName))
                    return false;
                // Sale's customer PO is not in the requested lots: would assign SO to the wrong purchase.
                if (!pos.Contains(NormalizePo(SalePo(record))))
                    return false;

                string existing = GetRecord(record, "SO #").Trim();
                return existing.Length == 0 ||
                       existing.Equals(soNumber, StringComparison.OrdinalIgnoreCase);
            }, record =>
            {
                record["SO #"] = soNumber;
                // Freight company was provided: do not overwrite freight with blank when only assigning SO #.
                if (company.Length > 0)
                    record[FreightCompanyColumn] = company;
            });
        }

        /// <summary>Apply <paramref name="mutate"/> to every matching live row and notify pages.</summary>
        public static int UpdateRecords(
            string baseName,
            Func<Dictionary<string, string>, bool> match,
            Action<Dictionary<string, string>> mutate)
        {
            // InventoryFolder is blank: nothing to update; ReadWithIds would fail.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return 0;

            int updated = 0;
            foreach (var (id, map) in SqliteInventory.ReadWithIds(baseName))
            {
                // This row is not one the caller asked to mutate: skip unrelated rows.
                if (!match(map))
                    continue;

                mutate(map);
                // Update succeeded: only count real writes.
                if (SqliteInventory.UpdateById(baseName, id, map))
                    updated++;
            }

            // No rows changed: skip DataChanged so pages do not reload for a no-op.
            if (updated == 0)
                return 0;

            NotifyDataChanged();
            return updated;
        }

        /// <summary>Add missing columns to a table without rewriting existing rows.</summary>
        public static void EnsureFileColumns(string baseName, params string[] columns)
        {
            SqliteInventory.EnsureColumns(baseName, columns);
        }

        /// <summary>Purchase fields first, then non-empty sale fields so the invoice line shows both lots.</summary>
        private static Dictionary<string, string> MergeSaleAndPurchase(
            Dictionary<string, string> sale,
            Dictionary<string, string>? purchase)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // A matching purchase row was found: start merged from purchase fields; missing purchase still returns sale-only.
            if (purchase != null)
            {
                foreach (var pair in purchase)
                    merged[pair.Key] = pair.Value;
            }

            foreach (var pair in sale)
            {
                // Sale cell is non-empty: do not let blank sale cells wipe purchase values.
                if (!string.IsNullOrWhiteSpace(pair.Value))
                    merged[pair.Key] = pair.Value;
            }

            return merged;
        }

        /// <summary>Distinct customer POs for invoice autocomplete, skipping already-used lots.</summary>
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

            // record is a sale whose customer PO may be offered as an invoice lot suggestion.
            foreach (var record in ReadRecords(Sales))
            {
                // Sale is not this customer: other customers' POs would appear in autocomplete.
                if (!MatchesCustomer(record, customerCode, customerName))
                    continue;

                string po = SalePo(record);
                // PO is blank, already on the invoice, or already suggested: autocomplete should not re-offer used lots or empties.
                if (po.Length == 0 || skip.Contains(NormalizePo(po)) || source.Contains(po))
                    continue;
                source.Add(po);
            }

            return source;
        }

        /// <summary>True when no vendor filter is set, or the row's vendor code or name matches.</summary>
        public static bool MatchesVendor(
            Dictionary<string, string> record,
            string? vendorCode,
            string? vendorName)
        {
            string code = (vendorCode ?? "").Trim();
            string name = (vendorName ?? "").Trim();
            // No vendor filter: unfiltered callers should see every row.
            if (code.Length == 0 && name.Length == 0)
                return true;

            string recCode = GetRecordAny(record, "Vendor Code", "Code");
            string recName = GetRecordAny(record, "Vendor", "Name", "Company");

            // Both codes are present and equal: match by vendor code even when names differ.
            if (code.Length > 0 && recCode.Length > 0 &&
                recCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                return true;

            // Both names are present and equal: match by name when codes are missing or different.
            if (name.Length > 0 && recName.Length > 0 &&
                recName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>True when no customer filter is set, or the row's customer code or name matches.</summary>
        public static bool MatchesCustomer(
            Dictionary<string, string> record,
            string? customerCode,
            string? customerName)
        {
            string code = (customerCode ?? "").Trim();
            string name = (customerName ?? "").Trim();
            // No customer filter: unfiltered callers should see every row.
            if (code.Length == 0 && name.Length == 0)
                return true;

            string recCode = GetRecordAny(record, "Customer Code", "Cust ID");
            string recName = GetRecordAny(record, "Customer", "Customer Name");

            // Both codes are present and equal: match by customer code even when names differ.
            if (code.Length > 0 && recCode.Length > 0 &&
                recCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                return true;

            // Both names are present and equal: match by name when codes are missing or different.
            if (name.Length > 0 && recName.Length > 0 &&
                recName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>First exact sale match on <paramref name="column"/>, else the first prefix match.</summary>
        private static Dictionary<string, string>? FindSale(
            string? key,
            string column,
            string? customerCode,
            string? customerName,
            int minLength = 3,
            string? itemCode = null)
        {
            string needle = NormalizePo(key);
            // Key is shorter than minLength (default 3): prefix search on 1-2 chars would return the wrong first sale.
            if (needle.Length < minLength)
                return null;

            string item = (itemCode ?? "").Trim();
            Dictionary<string, string>? startsWith = null;
            // record is a visible sale row considered as an exact or prefix match.
            foreach (var record in ReadRecords(Sales))
            {
                // Sale is not this customer: skip other customers' orders.
                if (!MatchesCustomer(record, customerCode, customerName))
                    continue;

                // Caller filtered by item code: multi-item orders would return the wrong line.
                if (item.Length > 0)
                {
                    string recItem = GetRecord(record, "Item Code").Trim();
                    // This sale is a different item: skip other lines on the same SO/PO.
                    if (!recItem.Equals(item, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                string value = NormalizePo(GetRecord(record, column));
                // This sale has no value in the search column: empty should not prefix-match everything.
                if (value.Length == 0)
                    continue;

                // Exact match: prefer it over a longer prefix hit.
                if (value.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    return record;

                // First prefix match and none kept yet: fallback when no exact match; keep the first so later prefixes do not replace it.
                if (startsWith == null && value.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                    startsWith = record;
            }

            return startsWith;
        }

        /// <summary>First exact column match, else the first prefix match, ignoring keys shorter than minLength.</summary>
        private static Dictionary<string, string>? FindByNormalized(
            string baseName,
            string column,
            string? key,
            int minLength = 3)
        {
            string needle = NormalizePo(key);
            // Key is shorter than minLength: prefix search on 1-2 chars would return the wrong first row.
            if (needle.Length < minLength)
                return null;

            Dictionary<string, string>? startsWith = null;
            // record is a visible row in baseName considered as an exact or prefix match.
            foreach (var record in ReadRecords(baseName))
            {
                string value = NormalizePo(GetRecord(record, column));
                // This row has no value in the search column: empty should not prefix-match everything.
                if (value.Length == 0)
                    continue;

                // Exact match: prefer it over a longer prefix hit.
                if (value.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    return record;

                // First prefix match and none kept yet: fallback when no exact match.
                if (startsWith == null && value.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                    startsWith = record;
            }

            return startsWith;
        }

        /// <summary>Next purchase PO from the product-number pattern, or CRC{yy}-10001 by default.</summary>
        public static string NextPurchasePo()
        {
            string pattern = (AppState.ProductNumberPattern ?? "").Trim();
            // ProductNumberPattern is set: use the custom pattern instead of CRC{yy}-10001.
            if (pattern.Length > 0)
                return NextFromPattern(PurchaseSales, "PO #", pattern, AppState.ProductNumberStart);

            int year = (AppState.TermStartDate ?? DateTime.Today).Year % 100;
            string prefix = $"CRC{year:00}-";
            var used = new List<int>();

            // record is any purchase row (including blocked) whose PO # may occupy this year's CRC sequence.
            foreach (var record in ReadAllRecords(PurchaseSales))
            {
                string po = GetRecord(record, "PO #");
                // PO is not this year's CRC prefix: other patterns' numbers would pollute the sequence.
                if (!po.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string rest = po[prefix.Length..].Trim();
                int i = 0;
                while (i < rest.Length && char.IsDigit(rest[i]))
                    i++;
                // Digits were found after the prefix: skip POs with no numeric tail.
                if (i > 0 && int.TryParse(rest[..i], out int n))
                    used.Add(n);
            }

            return prefix + NextSequenceNumber(used, 10001);
        }

        /// <summary>Next integer for a numeric column, starting at <paramref name="fallback"/>.</summary>
        public static string NextNumber(string baseName, string column, int fallback)
        {
            var used = new List<int>();
            // record is any row whose numeric column may occupy the sequence.
            foreach (var record in ReadAllRecords(baseName))
            {
                // The column is an integer: skip non-numeric cells so they do not crash numbering.
                if (int.TryParse(GetRecord(record, column).Trim(), out int n))
                    used.Add(n);
            }

            return NextSequenceNumber(used, fallback).ToString();
        }

        /// <summary>Next SO # from the sales-order pattern, or 10001, 10002, … if the pattern is blank.</summary>
        public static string NextSalesOrderNumber()
        {
            string pattern = (AppState.SalesOrderPattern ?? "").Trim();
            // SalesOrderPattern is blank: fall back to a plain 10001 sequence.
            if (pattern.Length == 0)
                return NextNumber(Sales, "SO #", 10001);

            return NextFromPattern(Sales, "SO #", pattern, AppState.SalesOrderStart);
        }

        /// <summary>Next SO # without inserting a row, for Settings previews.</summary>
        public static string PreviewSalesOrderNumber() => NextSalesOrderNumber();

        /// <summary>Next purchase PO without inserting a row, for Settings previews.</summary>
        public static string PreviewProductNumber() => NextPurchasePo();

        /// <summary>
        /// Build the next value from a pattern such as CRCyy-####.
        /// yy/yyyy, mm, and dd use today’s date. # is the running number.
        /// </summary>
        private static string NextFromPattern(string baseName, string column, string pattern, string? startText)
        {
            ParseHashPattern(
                ExpandDateTokens(pattern, DateTime.Today),
                out string prefix,
                out int width,
                out string suffix);
            int floor = 1;
            // Start text is a positive int: invalid or zero start would number from 1 and collide with existing or skip the configured floor.
            if (int.TryParse((startText ?? "").Trim(), out int start) && start > 0)
                floor = start;

            var used = new List<int>();
            // record is any row whose stored number may occupy this pattern's sequence.
            foreach (var record in ReadAllRecords(baseName))
            {
                // Stored value matches prefix+digits+suffix: only count numbers from this pattern.
                if (TryReadPatternNumber(GetRecord(record, column), prefix, suffix, out int n))
                    used.Add(n);
            }

            int next = NextSequenceNumber(used, floor);
            string digits = next.ToString().PadLeft(width, '0');
            return prefix + digits + suffix;
        }

        /// <summary>
        /// Lowest unused integer at or above <paramref name="floor"/> when reuse is on;
        /// otherwise one higher than the largest used value.
        /// </summary>
        private static int NextSequenceNumber(IEnumerable<int> usedNumbers, int floor)
        {
            // Floor is 0 or negative: sequence numbers should start at 1.
            if (floor < 1)
                floor = 1;

            var used = new HashSet<int>();
            int max = floor - 1;
            foreach (var n in usedNumbers)
            {
                // This used number is the largest so far: the reuse-off path needs max+1.
                if (n > max)
                    max = n;
                // Used number is in the reusable range: numbers below floor are not holes to fill.
                if (n >= floor)
                    used.Add(n);
            }

            // Reuse-missing is off: always allocate max+1 so deleted numbers stay retired.
            if (!AppState.ReuseMissingNumbers)
                return max + 1;

            int next = floor;
            while (used.Contains(next))
                next++;
            return next;
        }

        /// <summary>Replace yyyy, yy, mm, and dd (any case) with parts of <paramref name="date"/>.</summary>
        private static string ExpandDateTokens(string pattern, DateTime date)
        {
            // Pattern is null or empty: nothing to expand.
            if (string.IsNullOrEmpty(pattern))
                return "";

            var built = new System.Text.StringBuilder(pattern.Length + 4);
            int i = 0;
            while (i < pattern.Length)
            {
                // Four-digit year token at this index: replace with today's year; must check yyyy before yy.
                if (TokenAt(pattern, i, "yyyy"))
                {
                    built.Append(date.ToString("yyyy"));
                    i += 4;
                    continue;
                }

                // Two-digit year token: CRCyy patterns need the 2-digit year.
                if (TokenAt(pattern, i, "yy"))
                {
                    built.Append(date.ToString("yy"));
                    i += 2;
                    continue;
                }

                // Month token: mm in the pattern is a date part, not literal letters.
                if (TokenAt(pattern, i, "mm"))
                {
                    built.Append(date.ToString("MM"));
                    i += 2;
                    continue;
                }

                // Day token: same for day.
                if (TokenAt(pattern, i, "dd"))
                {
                    built.Append(date.ToString("dd"));
                    i += 2;
                    continue;
                }

                built.Append(pattern[i]);
                i++;
            }

            return built.ToString();
        }

        /// <summary>True when <paramref name="token"/> occurs at <paramref name="index"/>, ignoring case.</summary>
        private static bool TokenAt(string pattern, int index, string token)
        {
            // Token would run past the end: string.Compare with a too-long length is invalid.
            if (index + token.Length > pattern.Length)
                return false;

            return string.Compare(
                pattern, index, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>Split a pattern into prefix, hash-run width, and suffix around the last # sequence.</summary>
        private static void ParseHashPattern(string pattern, out string prefix, out int width, out string suffix)
        {
            int hashEnd = pattern.LastIndexOf('#');
            // Pattern has no # placeholders: treat the whole pattern as prefix and default width 4 so numbering still works.
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

        /// <summary>Read the running number from a stored value that matches prefix + digits + suffix.</summary>
        private static bool TryReadPatternNumber(string value, string prefix, string suffix, out int number)
        {
            number = 0;
            value = (value ?? "").Trim();
            // Stored value is blank: cannot parse a number from empty.
            if (value.Length == 0)
                return false;

            // Value does not start with this pattern's prefix: other number formats would be misread as this sequence.
            if (prefix.Length > 0 &&
                !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            // Value does not end with this pattern's suffix: skip unrelated numbers.
            if (suffix.Length > 0 &&
                !value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return false;

            int start = prefix.Length;
            int end = value.Length - suffix.Length;
            // Nothing left between prefix and suffix: TryParse of an empty or inverted slice would fail.
            if (end <= start)
                return false;

            string digits = value[start..end];
            return int.TryParse(digits, out number);
        }

        /// <summary>Insert a named row, throwing when access denies the write.</summary>
        public static void AppendNamedRow(string baseName, Dictionary<string, string> values)
        {
            // Insert outcome; denied writes must throw so callers do not think the row was saved.
            var result = MutateInsert(baseName, values);
            // Insert was denied: throw so callers do not think the row was saved.
            if (!result.Ok)
                throw new InvalidOperationException(result.Message);
            NotifyDataChanged();
        }

        /// <summary>Insert a row as Live or Waiting for add, and queue pending review when required.</summary>
        public static MutateResult MutateInsert(string baseName, Dictionary<string, string> values)
        {
            var gate = GateWrite(baseName, "add", values, null);
            // View-only or blocked company: do not insert then fail; return deny first.
            if (!gate.Ok)
                return gate;

            values[RecordStatus] = gate.Queued ? RecordWaitingAdd : RecordLive;
            SqliteInventory.Insert(baseName, values);
            // Confirm mode queued this add: pending_changes must get a row so reviewers can accept it.
            if (gate.Queued)
                QueuePending(baseName, "add", values, null);
            NotifyDataChanged();
            return gate.Queued ? MutateResult.QueuedForReview() : MutateResult.Saved();
        }

        /// <summary>Update the matching row as Live or Waiting for edit, queuing review when required.</summary>
        public static MutateResult MutateUpdate(
            string baseName,
            Func<Dictionary<string, string>, bool> match,
            Dictionary<string, string> values)
        {
            Dictionary<string, string>? before = null;
            // record is a candidate before-image for the row being edited.
            foreach (var record in ReadAllRecords(baseName))
            {
                // This row is not the one being edited: keep looking for the before-image.
                if (!match(record))
                    continue;
                before = record;
                break;
            }

            var gate = GateWrite(baseName, "edit", values, before);
            // Write denied: do not update.
            if (!gate.Ok)
                return gate;

            values[RecordStatus] = gate.Queued ? RecordWaitingEdit : RecordLive;
            bool updated = ReplaceMatchingRowRaw(baseName, match, values);
            // No row matched: tell the caller instead of claiming saved.
            if (!updated)
                return MutateResult.Deny("Could not find that record to update.");
            // Confirm mode: queue the edit for review.
            if (gate.Queued)
                QueuePending(baseName, "edit", values, before);
            NotifyDataChanged();
            return gate.Queued ? MutateResult.QueuedForReview() : MutateResult.Saved();
        }

        /// <summary>Delete immediately, or mark Waiting for delete and queue review when Confirm is on.</summary>
        public static MutateResult MutateDelete(string baseName, Dictionary<string, string> record)
        {
            var gate = GateWrite(baseName, "delete", record, record);
            // Write denied: do not delete.
            if (!gate.Ok)
                return gate;

            var identity = IdentityOf(baseName, record);
            bool found = false;
            foreach (var (id, map) in SqliteInventory.ReadWithIdsUnrestricted(baseName))
            {
                // This row is not the identity being deleted: skip other rows.
                if (!MatchesIdentity(identity, map))
                    continue;
                found = true;
                // Confirm mode: mark Waiting for delete instead of removing the row.
                if (gate.Queued)
                {
                    map[RecordStatus] = RecordWaitingDelete;
                    SqliteInventory.UpdateById(baseName, id, map);
                    QueuePending(baseName, "delete", record, record);
                }
                // Auto write mode: delete immediately; leaving the row would keep it live.
                else
                {
                    SqliteInventory.DeleteById(baseName, id);
                }

                break;
            }

            // Identity never matched: report missing rather than claiming deleted.
            if (!found)
                return MutateResult.Deny("Could not find that record to delete.");
            NotifyDataChanged();
            return gate.Queued ? MutateResult.QueuedForReview() : MutateResult.Saved();
        }

        /// <summary>Deny view-only or blocked-company writes; Confirm returns queued instead of saved.</summary>
        private static MutateResult GateWrite(
            string baseName,
            string action,
            Dictionary<string, string> after,
            Dictionary<string, string>? before)
        {
            // This account is view-only for the table: block the write.
            if (DataAccess.WriteMode(baseName) == DataWriteMode.View)
                return MutateResult.Deny("This account can only view that table.");
            // The row's company is blocked for this user: prevent working around the block via edit/delete.
            if (DataAccess.IsCompanyBlocked(after) || DataAccess.IsCompanyBlocked(before))
                return MutateResult.Deny("You cannot work with that company.");

            // Confirm mode: queue instead of saving live.
            if (DataAccess.WriteMode(baseName) == DataWriteMode.Confirm)
                return MutateResult.QueuedForReview();
            return MutateResult.Saved();
        }

        /// <summary>Insert a pending_changes row so a reviewer can accept or reject this write.</summary>
        private static void QueuePending(
            string baseName,
            string action,
            Dictionary<string, string> after,
            Dictionary<string, string>? before)
        {
            string name = after.TryGetValue("Name", out var n) ? n :
                after.TryGetValue("Vendor", out n) ? n :
                after.TryGetValue("Customer", out n) ? n :
                after.TryGetValue("PO #", out n) ? n :
                after.TryGetValue("Code", out n) ? n : "";
            string summary = action + " · " + baseName + (name.Length > 0 ? " · " + name : "");
            SqliteInventory.Insert(PendingChanges, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Table"] = baseName,
                ["Action"] = action,
                ["Summary"] = summary,
                ["Match Json"] = JsonSerializer.Serialize(IdentityOf(baseName, before ?? after)),
                ["Before Json"] = before == null ? "" : JsonSerializer.Serialize(before),
                ["After Json"] = action == "delete" ? "" : JsonSerializer.Serialize(after),
                ["Requested By"] = AppState.CurrentUsername,
                ["Requested At"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                ["Status"] = "pending"
            });
        }

        /// <summary>Natural key for a row (PO+item, Code, Invoice #, …) used to match pending changes.</summary>
        public static Dictionary<string, string> IdentityOf(
            string baseName,
            Dictionary<string, string> record)
        {
            string[] keys = baseName switch
            {
                PurchaseSales => new[] { "PO #", "Item Code" },
                Sales => new[] { "PO #", "Item Code", "Invoice #" },
                Customers or Vendors or ItemCodes => new[] { "Code" },
                Invoices => new[] { "Invoice #" },
                Debits => new[] { "Debit #" },
                Credits => new[] { "Credit #" },
                _ => Array.Empty<string>()
            };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Table has no dedicated identity columns: fall back to the whole row so pending match still works (bank txns etc.).
            if (keys.Length == 0)
            {
                foreach (var pair in record)
                    map[pair.Key] = pair.Value ?? "";
                return map;
            }

            // key is one identity column name for this table (PO #, Item Code, Invoice #, …).
            foreach (var key in keys)
                map[key] = GetRecord(record, key);
            return map;
        }

        /// <summary>True when every identity field matches the row (case-insensitive).</summary>
        public static bool MatchesIdentity(
            Dictionary<string, string> identity,
            Dictionary<string, string> record)
        {
            foreach (var pair in identity)
            {
                // This identity field does not match: all fields must match; one miss means a different row.
                if (!GetRecord(record, pair.Key).Equals(pair.Value ?? "", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return identity.Count > 0;
        }

        /// <summary>Apply a pending add/edit/delete as Live, skipping the confirm queue, then mark it accepted.</summary>
        public static bool AcceptPending(Dictionary<string, string> pending)
        {
            string table = GetRecord(pending, "Table");
            string action = GetRecord(pending, "Action");
            var after = ParseJsonMap(GetRecord(pending, "After Json"));
            var match = ParseJsonMap(GetRecord(pending, "Match Json"));
            DataAccess.ApplyingReview = true;
            try
            {
                // Pending is an add or edit: write After Json as Live; delete is handled separately.
                if (action.Equals("add", StringComparison.OrdinalIgnoreCase) ||
                    action.Equals("edit", StringComparison.OrdinalIgnoreCase))
                {
                    after[RecordStatus] = RecordLive;
                    ReplaceMatchingRowRaw(table, row => MatchesIdentity(match, row), after);
                }
                // Pending is a delete: remove the live row now that review accepted it.
                else if (action.Equals("delete", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (id, map) in SqliteInventory.ReadWithIdsUnrestricted(table))
                    {
                        // This row is not the pending match: do not delete the wrong row.
                        if (!MatchesIdentity(match, map))
                            continue;
                        SqliteInventory.DeleteById(table, id);
                        break;
                    }
                }
            }
            finally
            {
                DataAccess.ApplyingReview = false;
            }

            return MarkPending(pending, "accepted");
        }

        /// <summary>Undo a queued add/edit/delete and mark the pending row rejected.</summary>
        public static bool RejectPending(Dictionary<string, string> pending)
        {
            string table = GetRecord(pending, "Table");
            string action = GetRecord(pending, "Action");
            var before = ParseJsonMap(GetRecord(pending, "Before Json"));
            var match = ParseJsonMap(GetRecord(pending, "Match Json"));
            DataAccess.ApplyingReview = true;
            try
            {
                // Rejecting a queued add: remove the waiting-add row so it does not stay in the table.
                if (action.Equals("add", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (id, map) in SqliteInventory.ReadWithIdsUnrestricted(table))
                    {
                        // This row is not the pending match: do not delete the wrong waiting-add row.
                        if (!MatchesIdentity(match, map))
                            continue;
                        SqliteInventory.DeleteById(table, id);
                        break;
                    }
                }
                // Rejecting a queued edit: restore Before Json as Live so the edit is undone.
                else if (action.Equals("edit", StringComparison.OrdinalIgnoreCase))
                {
                    before[RecordStatus] = RecordLive;
                    ReplaceMatchingRowRaw(table, row => MatchesIdentity(match, row), before);
                }
                // Rejecting a queued delete: clear Waiting for delete so the row stays live.
                else if (action.Equals("delete", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (id, map) in SqliteInventory.ReadWithIdsUnrestricted(table))
                    {
                        // This row is not the pending match: do not restore the wrong row.
                        if (!MatchesIdentity(match, map))
                            continue;
                        map[RecordStatus] = RecordLive;
                        SqliteInventory.UpdateById(table, id, map);
                        break;
                    }
                }
            }
            finally
            {
                DataAccess.ApplyingReview = false;
            }

            return MarkPending(pending, "rejected");
        }

        /// <summary>Set Status/Reviewed By/At on the matching pending_changes row.</summary>
        private static bool MarkPending(Dictionary<string, string> pending, string status)
        {
            string requestedAt = GetRecord(pending, "Requested At");
            string by = GetRecord(pending, "Requested By");
            string summary = GetRecord(pending, "Summary");
            bool ok = false;
            foreach (var (id, map) in SqliteInventory.ReadWithIds(PendingChanges))
            {
                // This pending row is not the one just reviewed, or it is already decided: only the matching pending row should be marked.
                if (!GetRecord(map, "Summary").Equals(summary, StringComparison.OrdinalIgnoreCase) ||
                    !GetRecord(map, "Requested By").Equals(by, StringComparison.OrdinalIgnoreCase) ||
                    !GetRecord(map, "Requested At").Equals(requestedAt, StringComparison.OrdinalIgnoreCase) ||
                    !GetRecord(map, "Status").Equals("pending", StringComparison.OrdinalIgnoreCase))
                    continue;

                map["Status"] = status;
                map["Reviewed By"] = AppState.CurrentUsername;
                map["Reviewed At"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                SqliteInventory.UpdateById(PendingChanges, id, map);
                ok = true;
                break;
            }

            // We updated a pending row: notify pages so pending lists refresh; skip notify if nothing matched.
            if (ok)
                NotifyDataChanged();
            return ok;
        }

        /// <summary>Deserialize a stored JSON object into a case-insensitive string map.</summary>
        private static Dictionary<string, string> ParseJsonMap(string json)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            json = (json ?? "").Trim();
            // JSON blob is blank: Parse would throw; delete pending has empty After Json.
            if (json.Length == 0)
                return map;
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var pair in doc.RootElement.EnumerateObject())
                    map[pair.Name] = pair.Value.GetString() ?? pair.Value.ToString();
            }
            // Malformed Match/Before/After Json must not crash review; return the empty map.
            catch
            {
            }

            return map;
        }

        /// <summary>Overwrite the first matching row by id without raising DataChanged.</summary>
        private static bool ReplaceMatchingRowRaw(
            string baseName,
            Func<Dictionary<string, string>, bool> match,
            Dictionary<string, string> values)
        {
            foreach (var (id, map) in SqliteInventory.ReadWithIds(baseName))
            {
                // This row is not the one to replace: keep scanning.
                if (!match(map))
                    continue;
                SqliteInventory.UpdateById(baseName, id, values);
                return true;
            }

            return false;
        }

        /// <summary>Map named values onto the table header order, including Lot # / Customer PO aliases.</summary>
        public static string[] NamedRow(string baseName, Dictionary<string, string> values)
        {
            return MapNamedRow(SqliteInventory.Headers(baseName), values);
        }

        /// <summary>Fill header cells from a dictionary, mapping PO # ↔ Customer PO / Lot # on older schemas.</summary>
        private static string[] MapNamedRow(string[] header, Dictionary<string, string> values)
        {
            bool hasLot = header.Any(h => h.Trim().Equals("Lot #", StringComparison.OrdinalIgnoreCase));
            var cells = new string[header.Length];
            for (int i = 0; i < header.Length; i++)
            {
                string name = header[i].Trim();
                // Dictionary has this header: use the matching value for this cell.
                if (TryNamed(values, name, out var value))
                    cells[i] = value;
                // Header is Customer PO but values used PO #: older sales schemas store customer PO as PO #.
                else if (name.Equals("Customer PO", StringComparison.OrdinalIgnoreCase) &&
                         TryNamed(values, "PO #", out value))
                    cells[i] = value;
                // No Lot # column, header is PO #, values used Lot #: map Lot # onto PO # after the Lot # column was dropped.
                else if (!hasLot &&
                         name.Equals("PO #", StringComparison.OrdinalIgnoreCase) &&
                         TryNamed(values, "Lot #", out value))
                    cells[i] = value;
                // No value for this header: leave the cell blank rather than null.
                else
                    cells[i] = "";
            }

            return cells;
        }

        /// <summary>Case-insensitive dictionary lookup used while mapping named rows onto headers.</summary>
        private static bool TryNamed(Dictionary<string, string> values, string key, out string value)
        {
            foreach (var pair in values)
            {
                // This dictionary key matches (case-insensitive): headers and tags differ in casing.
                if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value ?? "";
                    return true;
                }
            }

            value = "";
            return false;
        }

        /// <summary>Insert positional fields as a named row, throwing when access denies the write.</summary>
        public static void AppendRow(string baseName, IEnumerable<string> fields)
        {
            // Insert outcome from positional fields; denied writes must throw so the UI shows the denial.
            var result = MutateInsert(baseName, RowFromFields(baseName, fields));
            // Insert was denied: throw so the UI shows the denial.
            if (!result.Ok)
                throw new InvalidOperationException(result.Message);
        }

        /// <summary>Zip positional cells onto the table header as a named dictionary.</summary>
        public static Dictionary<string, string> RowFromFields(string baseName, IEnumerable<string> fields)
        {
            var header = SqliteInventory.Headers(baseName);
            var cells = fields.ToList();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
                values[header[i]] = i < cells.Count ? cells[i] ?? "" : "";
            return values;
        }

        /// <summary>Build a named record from a grid row, skipping the add-column button.</summary>
        public static Dictionary<string, string> GridRowToRecord(DataGridView grid, int rowIndex)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewColumn col in grid.Columns)
            {
                // This is the "+" add-column button: it is not a data field; including it would store a junk key.
                if (Theme.IsAddColumn(col))
                    continue;
                // File column name from Tag or Name, used as the dictionary key.
                string key = col.Tag as string ?? col.Name;
                // Column Tag/Name is blank: fall back to HeaderText so the cell still maps to a column.
                if (string.IsNullOrWhiteSpace(key))
                    key = col.HeaderText;
                map[key] = grid.Rows[rowIndex].Cells[col.Index].Value?.ToString() ?? "";
            }

            return map;
        }

        /// <summary>Overwrite the first matching row from positional fields and notify pages.</summary>
        public static bool ReplaceMatchingRow(
            string baseName,
            Func<Dictionary<string, string>, bool> match,
            IReadOnlyList<string> fields)
        {
            // InventoryFolder is blank: cannot update without a database.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return false;

            var header = SqliteInventory.Headers(baseName);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
                values[header[i]] = i < fields.Count ? fields[i] ?? "" : "";

            foreach (var (id, map) in SqliteInventory.ReadWithIds(baseName))
            {
                // This row does not match: skip unrelated rows.
                if (!match(map))
                    continue;

                SqliteInventory.UpdateById(baseName, id, values);
                NotifyDataChanged();
                return true;
            }

            return false;
        }

        /// <summary>User-facing header: Order Date for Agreement Date, Lot # / PO # swap on older sales files.</summary>
        public static string DisplayColumnHeader(string baseName, string[] fileHeader, string name)
        {
            name = name.Trim();
            // Purchase-sales table: Agreement Date and Description have friendlier display names only on that table.
            if (baseName == PurchaseSales)
            {
                // Column is Agreement Date: show Order Date in the grid.
                if (name.Equals("Agreement Date", StringComparison.OrdinalIgnoreCase))
                    return "Order Date";
                // Column is Description: show Species for purchase lines.
                if (name.Equals("Description", StringComparison.OrdinalIgnoreCase))
                    return "Species";
            }

            // Not the sales table: Lot # / Customer PO aliases only apply to sales.
            if (baseName != Sales)
                return name;

            // PO # on a sale is the purchase lot; Invoice # is the customer PO.
            if (name.Equals("PO #", StringComparison.OrdinalIgnoreCase))
                return "Lot #";
            if (name.Equals("Invoice #", StringComparison.OrdinalIgnoreCase))
                return "Customer PO";
            return name;
        }

        /// <summary>Preferred column order for each table, then any remaining file columns.</summary>
        private static int[] ColumnDisplayOrder(string baseName, string[] fileHeader)
        {
            // Preferred display-column names for this table, or null to keep file-header order.
            string[]? first = baseName switch
            {
                PurchaseSales => new[]
                {
                    "PO #",
                    "Record Status",
                    "Status",
                    "Ship Date",
                    "Order Date"
                },
                Sales => new[]
                {
                    "SO #",
                    "Record Status",
                    "Status",
                    "Ship Date",
                    "Lot #"
                },
                Customers => new[]
                {
                    "Record Status",
                    "Name",
                    "Company",
                    "Phone",
                    "Current Balance"
                },
                Vendors => new[]
                {
                    "Record Status",
                    "Name",
                    "Company",
                    "Phone",
                    "Current Balance"
                },
                Invoices => new[]
                {
                    "SO #",
                    "PO #",
                    "Record Status",
                    "Type",
                    "Customer",
                    "Vendor",
                    "Ship Date",
                    "Due Date",
                    "Status",
                    "Paid"
                },
                BankTransactions => new[]
                {
                    "Date",
                    "Record Status",
                    "Amount",
                    "Account",
                    "Description",
                    "Invoice #"
                },
                ItemCodes => new[]
                {
                    "Record Status",
                    "Code",
                    "Description",
                    "Species"
                },
                Debits => new[]
                {
                    "Record Status",
                    "Debit #",
                    "Vendor",
                    "PO #",
                    "Date Submitted"
                },
                Credits => new[]
                {
                    "Record Status",
                    "Credit #",
                    "Customer",
                    "Invoice #",
                    "Date Submitted"
                },
                _ => null
            };

            // This table has no preferred column order: keep file-header order as-is.
            if (first == null)
                return Enumerable.Range(0, fileHeader.Length).ToArray();

            var used = new bool[fileHeader.Length];
            var order = new List<int>(fileHeader.Length);
            foreach (var want in first)
            {
                for (int i = 0; i < fileHeader.Length; i++)
                {
                    // This file column is already placed: do not add it twice when two preferred names map to it.
                    if (used[i])
                        continue;
                    // This column's display name is not the preferred one we are looking for: keep scanning.
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
                // Column was not in the preferred list: append remaining columns after the preferred ones.
                if (!used[i])
                    order.Add(i);
            }

            return order.ToArray();
        }

        /// <summary>Restore default or summary columns, then hide columns this user is not allowed to see.</summary>
        public static void ResetGridColumns(DataGridView grid)
        {
            string? baseName = grid.Tag is ColumnSearch search ? search.FileBaseName : null;
            // No saved default layout (or apply failed): fall back to summary-column visibility.
            if (!GridLayout.ApplyDefault(grid))
            {
                GridLayout.BeginUpdate();
                try
                {
                    foreach (DataGridViewColumn col in grid.Columns)
                    {
                        // Add-column button: that column must stay visible.
                        if (Theme.IsAddColumn(col))
                        {
                            col.Visible = true;
                            continue;
                        }

                        col.Visible = IsSummaryColumn(baseName ?? "", col.HeaderText);
                        // Record Status column and the user is allowed to see it: waiting rows would be invisible without this column.
                        if (IsRecordStatusColumn(col) &&
                            (string.IsNullOrWhiteSpace(baseName) ||
                             !DataAccess.IsColumnHidden(baseName, RecordStatus)))
                            col.Visible = true;
                    }
                }
                finally
                {
                    GridLayout.EndUpdate();
                }

                GridLayout.Save(grid);
            }

            // Grid is bound to a table: hide per-user hidden columns; skip if we do not know the table.
            if (!string.IsNullOrWhiteSpace(baseName))
            {
                foreach (DataGridViewColumn col in grid.Columns)
                {
                    // Add-column button: do not hide it as a "hidden data column".
                    if (Theme.IsAddColumn(col))
                        continue;
                    // File column name from Tag or Name, used for the hidden-column check.
                    string key = col.Tag as string ?? col.Name;
                    // This user is not allowed to see this column: hide it even if summary/default layout showed it.
                    if (DataAccess.IsColumnHidden(baseName, key) ||
                        DataAccess.IsColumnHidden(baseName, col.HeaderText))
                        col.Visible = false;
                }
            }

            Theme.FitAllColumns(grid);
            // Grid has a ColumnSearch attached: refresh the search UI after column visibility changed.
            if (grid.Tag is ColumnSearch layout)
                layout.NotifyColumnsChanged();
        }

        /// <summary>True for the compact set of columns shown before the user customizes layout.</summary>
        private static bool IsSummaryColumn(string baseName, string displayHeader)
        {
            string[]? visible = baseName switch
            {
                PurchaseSales => new[] { "PO #", "Record Status", "Status", "Ship Date", "Order Date" },
                Sales => new[] { "SO #", "Record Status", "Status", "Ship Date", "Lot #" },
                Customers => new[] { "Record Status", "Name", "Company", "Phone", "Current Balance" },
                Vendors => new[] { "Record Status", "Name", "Company", "Phone", "Current Balance" },
                Invoices => new[] { "SO #", "PO #", "Record Status", "Type", "Customer", "Vendor", "Ship Date", "Due Date", "Status", "Paid" },
                BankTransactions => new[] { "Date", "Record Status", "Amount", "Account", "Description", "Invoice #" },
                _ => null
            };
            // This table has no compact summary set: show every column until the user customizes.
            if (visible == null)
                return true;

            return visible.Any(name =>
                name.Equals(displayHeader, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when the column is Record Status, which must stay visible for waiting rows.</summary>
        private static bool IsRecordStatusColumn(DataGridViewColumn col)
        {
            string key = col.Tag as string ?? col.Name;
            return key.Equals(RecordStatus, StringComparison.OrdinalIgnoreCase) ||
                   col.HeaderText.Equals(RecordStatus, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Color waiting-add/edit/delete rows and bold the Record Status cell.</summary>
        private static void StyleRecordStatusRow(DataGridViewRow row, string status)
        {
            // Queued add: gold/navy so waiting-add rows are obvious.
            if (status.Equals(RecordWaitingAdd, StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Theme.WaitAddFill;
                row.DefaultCellStyle.SelectionBackColor = Theme.GoldLight;
                row.DefaultCellStyle.ForeColor = Theme.Navy;
            }
            // Queued edit: different fill so edit vs add vs delete are distinct.
            else if (status.Equals(RecordWaitingEdit, StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Theme.WaitEditFill;
                row.DefaultCellStyle.SelectionBackColor = Theme.GoldLight;
                row.DefaultCellStyle.ForeColor = Theme.Navy;
            }
            // Queued delete: danger colors so delete-pending rows stand out.
            else if (status.Equals(RecordWaitingDelete, StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Theme.DangerFill;
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(240, 180, 170);
                row.DefaultCellStyle.ForeColor = Theme.Danger;
            }

            // Row is Live (or other non-waiting): do not bold Record Status on normal rows.
            if (!IsWaitingStatus(status))
                return;

            foreach (DataGridViewCell cell in row.Cells)
            {
                // Owning DataGridView for this row; null when the row is detached.
                var grid = row.DataGridView;
                // Row is not attached to a grid: Columns lookup would throw.
                if (grid == null)
                    break;
                var col = grid.Columns[cell.ColumnIndex];
                // This cell is not Record Status: only bold that cell, not the whole row's fonts.
                if (!IsRecordStatusColumn(col))
                    continue;
                cell.Style.Font = Theme.BodyBold;
                break;
            }
        }

        /// <summary>Display account numbers as last-four only in grids.</summary>
        private static void MaskAccountCells(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            // Not a grid event, or header/new-row index: formatting would throw or mask the wrong cell.
            if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            var col = grid.Columns[e.ColumnIndex];
            // File column name from Tag or Name, used to detect the Account Number column.
            string key = col.Tag as string ?? col.Name;
            // This column is not Account Number: do not mask other numeric columns.
            if (!key.Equals(AccountNumber, StringComparison.OrdinalIgnoreCase) &&
                !col.HeaderText.Equals(AccountNumber, StringComparison.OrdinalIgnoreCase))
                return;
            e.Value = MaskAccountNumber(e.Value?.ToString());
            e.FormattingApplied = true;
        }

        /// <summary>True for any of the three queued Record Status values.</summary>
        private static bool IsWaitingStatus(string status) =>
            status.Equals(RecordWaitingAdd, StringComparison.OrdinalIgnoreCase) ||
            status.Equals(RecordWaitingEdit, StringComparison.OrdinalIgnoreCase) ||
            status.Equals(RecordWaitingDelete, StringComparison.OrdinalIgnoreCase);

        /// <summary>Raised after inserts, updates, deletes, imports, and term roll-over so open pages reload.</summary>
        public static event Action? DataChanged;

        /// <summary>Tell open pages the inventory tables changed.</summary>
        public static void NotifyDataChanged() => DataChanged?.Invoke();

        /// <summary>Flip the Current/Old toggle and reload open pages.</summary>
        public static void SetViewingOldInventory(bool oldInventory)
        {
            // Toggle is already in the requested state: skip reload so flipping Current/Old to the same value does not refresh every page.
            if (AppState.ViewingOldInventory == oldInventory)
                return;

            AppState.ViewingOldInventory = oldInventory;
            NotifyDataChanged();
            Navigator.RefreshOpenPages();
        }

        /// <summary>Row count for a table in the current view, or 0 when no folder is selected.</summary>
        public static int CountDataRows(string baseName)
        {
            // InventoryFolder is blank: Count would fail; 0 is the empty-state count.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return 0;

            return SqliteInventory.Count(baseName);
        }

        /// <summary>Rebuild grid columns and rows off the UI thread, with a faded spinner until apply finishes.</summary>
        public static void FillGrid(DataGridView grid, string baseName)
        {
            // DataPageHasIdleSearch is true when TableSearchStage._active is false (centered overlay).
            // Skip the worker + spinner until the user types or sets dates; SetActive(true) then
            // calls HighlightCurrentPage which re-enters FillGrid with the overlay gone.
            if (grid.FindForm() is Form page && UiStyle.DataPageHasIdleSearch(page))
                return;

            GridLoadHost.Run(
                grid,
                token => PrepareGridFill(baseName, token),
                data => ApplyGridFill(grid, baseName, data));
        }

        /// <summary>Read headers and row cells without touching the grid. Safe on a worker thread.</summary>
        private static GridFillData PrepareGridFill(string baseName, CancellationToken token)
        {
            // Reading customers: older DBs lack Address/Email/etc.; add those columns before the grid read.
            if (baseName == Customers)
                EnsureFileColumns(Customers, "Address", "Email", "Phone", "Company", "Current Balance", "Notes", "Description", RoutingNumber, AccountNumber);
            // Reading vendors: same schema backfill for vendor extra columns.
            if (baseName == Vendors)
                EnsureFileColumns(Vendors, "Company", "Phone", "Current Balance", "Notes", "Description", RoutingNumber, AccountNumber);

            var data = new GridFillData();
            // No folder or table/db missing: cannot read headers/rows; ApplyGridFill shows "Select a data folder".
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) || !Exists(baseName))
            {
                data.NoFolder = true;
                return data;
            }

            data.Header = SqliteInventory.Headers(baseName);
            data.Order = ColumnDisplayOrder(baseName, data.Header);
            var records = VisibleRecords(baseName);
            data.Rows = new List<(object[] Cells, string Status)>(records.Count);
            // record is one visible table row turned into grid cells plus Record Status.
            foreach (var record in records)
            {
                token.ThrowIfCancellationRequested();
                var cells = new object[data.Order.Length];
                for (int n = 0; n < data.Order.Length; n++)
                {
                    int c = data.Order[n];
                    string value = GetRecord(record, data.Header[c]);
                    // This cell is Record Status: blank older rows should display Live, not empty.
                    if (data.Header[c].Trim().Equals(RecordStatus, StringComparison.OrdinalIgnoreCase))
                        value = string.IsNullOrWhiteSpace(value) ? RecordLive : value.Trim();
                    cells[n] = value;
                }

                data.Rows.Add((cells, StatusOf(record)));
            }

            return data;
        }

        /// <summary>Apply prepared columns and rows on the UI thread.</summary>
        private static void ApplyGridFill(DataGridView grid, string baseName, GridFillData data)
        {
            grid.Columns.Clear();
            grid.Rows.Clear();
            GridLayout.BeginUpdate();
            try
            {
                // Prepare found no folder/table: show the Settings hint instead of an empty grid that looks like no data.
                if (data.NoFolder)
                {
                    grid.Columns.Add("Status", "Status");
                    grid.Rows.Add("Select a data folder in Settings");
                    return;
                }

                foreach (int c in data.Order)
                {
                    string fileName = data.Header[c].Trim();
                    string display = DisplayColumnHeader(baseName, data.Header, fileName);
                    int index = grid.Columns.Add(fileName, display);
                    grid.Columns[index].Tag = fileName;
                    grid.Columns[index].Visible = IsSummaryColumn(baseName, display);
                }

                Theme.EnsureAddColumn(grid);
                grid.CellFormatting -= MaskAccountCells;
                grid.CellFormatting += MaskAccountCells;
                GridLayout.Apply(grid, baseName);
                foreach (DataGridViewColumn col in grid.Columns)
                {
                    // Add-column button: skip hidden-column checks so the "+" stays visible.
                    if (Theme.IsAddColumn(col))
                        continue;
                    // File column name from Tag or Name, used for the hidden-column check.
                    string key = col.Tag as string ?? col.Name;
                    // This user is not allowed to see this column: hide it even if the summary layout showed it.
                    if (DataAccess.IsColumnHidden(baseName, key) ||
                        DataAccess.IsColumnHidden(baseName, col.HeaderText))
                    {
                        col.Visible = false;
                        continue;
                    }

                    // Record Status column: force visible so waiting-row colors have a status cell to show.
                    if (IsRecordStatusColumn(col))
                        col.Visible = true;
                }

                // row is one prepared grid line: cell values in display order plus Record Status for coloring.
                foreach (var row in data.Rows)
                {
                    int rowIndex = grid.Rows.Add(row.Cells);
                    StyleRecordStatusRow(grid.Rows[rowIndex], row.Status);
                }
            }
            finally
            {
                GridLayout.EndUpdate();
                // Grid has a ColumnSearch attached: rebuild the search UI against the new columns; skip if none.
                if (grid.Tag is ColumnSearch search)
                {
                    search.FileBaseName = baseName;
                    search.Rebuild();
                }
            }
        }

        private sealed class GridFillData
        {
            public bool NoFolder;
            public string[] Header = Array.Empty<string>();
            public int[] Order = Array.Empty<int>();
            public List<(object[] Cells, string Status)> Rows = new();
        }

        /// <summary>Import a CSV into the current page's table when headings match and the user has auto-write.</summary>
        public static bool TryImportCsv(string sourcePath, out string message)
        {
            string baseName = GetPageFileBaseName(Navigator.CurrentPage);

            // Current page is not a table page: import has nowhere to put rows.
            if (string.IsNullOrWhiteSpace(baseName))
            {
                message = "This page does not have a table in the database.";
                return false;
            }

            // User does not have auto-write: import would bypass Confirm review.
            if (DataAccess.WriteMode(baseName) != DataWriteMode.Auto)
            {
                message = "Import requires automatic add / edit / delete access.";
                return false;
            }

            // No data folder: InsertMany needs the database.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
            {
                message = "Select a data folder in Settings first.";
                return false;
            }

            // Chosen CSV is gone: ReadAllLines would throw.
            if (!File.Exists(sourcePath))
            {
                message = "The selected file could not be found.";
                return false;
            }

            var sourceLines = File.ReadAllLines(sourcePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            // File has no non-blank lines: no header to compare.
            if (sourceLines.Count == 0)
            {
                message = "The selected file is empty.";
                return false;
            }

            string[] expectedHeader = GetExpectedHeader(baseName).Split(',');
            var sourceRows = CsvIO.Read(sourcePath);
            // CsvIO parsed zero rows: empty after CSV parse (quotes/blank).
            if (sourceRows.Count == 0)
            {
                message = "The selected file is empty.";
                return false;
            }

            var incomingHeader = sourceRows[0];
            bool exact = NormalizeHeader(string.Join(",", incomingHeader)) ==
                         NormalizeHeader(GetExpectedHeader(baseName));
            // Header is not exact, and it is not a compatible customer/vendor subset: importing mismatched columns would write values under the wrong headers.
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

            // Importing customers: older DBs lack extra party columns; add them before InsertMany.
            if (baseName == Customers)
                EnsureFileColumns(Customers, "Address", "Email", "Phone", "Company", "Current Balance", "Notes", "Description", RoutingNumber, AccountNumber);
            // Importing vendors: same schema backfill for vendor extra columns.
            if (baseName == Vendors)
                EnsureFileColumns(Vendors, "Company", "Phone", "Current Balance", "Notes", "Description", RoutingNumber, AccountNumber);

            SqliteInventory.EnsureCreated();

            // Header only, no data rows: do not claim a successful import when nothing was loaded.
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

        /// <summary>True when every incoming heading exists on the expected customer/vendor header.</summary>
        private static bool IncomingMapsToExpected(string[] incoming, string[] expected)
        {
            // No incoming headings: an empty header is not a valid mapping.
            if (incoming.Length == 0)
                return false;

            foreach (var column in incoming)
            {
                string name = column.Trim();
                // Blank heading: skip empty names so they do not fail the mapping.
                if (name.Length == 0)
                    continue;
                // This incoming heading is not on the expected schema: unknown columns would be imported under names the table does not use.
                if (!expected.Any(e => e.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            return true;
        }

        /// <summary>Parse the term date from a live CSV name table_yyyy-MM-dd.csv; archived names with two dates fail.</summary>
        internal static bool TryParseStartDate(string fileName, string baseName, out DateTime date)
        {
            date = default;
            string noExt = Path.GetFileNameWithoutExtension(fileName);
            string prefix = baseName + "_";

            // Filename is not table_...: other CSVs in the folder are not this table's leftovers.
            if (!noExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string rest = noExt.Substring(prefix.Length);

            // live file: deals_2026-08-27
            // ignore already-archived style names that accidentally sit in root
            if (rest.Contains('_'))
                return false;

            return DateTime.TryParse(rest, out date);
        }

        /// <summary>Lowercase, trimmed heading list used to compare import files to the expected schema.</summary>
        private static string NormalizeHeader(string header)
        {
            var parts = header
                .Split(',')
                .Select(part => part.Trim().Trim('"').ToLowerInvariant());

            return string.Join(",", parts);
        }
    }

    /// <summary>Home-screen totals for the four Command Center cards.</summary>
    public readonly record struct DashboardSummary(
        decimal Revenue,
        decimal Outstanding,
        decimal LateFees,
        int Deals,
        int OverdueInvoices,
        bool ViewingOld);
}
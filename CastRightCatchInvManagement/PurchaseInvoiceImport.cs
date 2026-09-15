using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Reads another company's invoice PDF into a purchase draft. Layouts vary, so the form
    /// is filled for review rather than saved blindly.
    /// </summary>
    internal static class PurchaseInvoiceImport
    {
        /// <summary>Ask the user for an invoice PDF and return its name and bytes.</summary>
        public static bool TryPick(IWin32Window? owner, out string fileName, out byte[] bytes)
        {
            fileName = "";
            bytes = Array.Empty<byte>();
            using var dialog = new OpenFileDialog
            {
                Title = "Choose an invoice PDF",
                Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                CheckFileExists = true
            };
            // Cancel should not treat the last path as a pick.
            if (dialog.ShowDialog(owner) != DialogResult.OK)
                return false;

            fileName = Path.GetFileName(dialog.FileName);
            bytes = File.ReadAllBytes(dialog.FileName);
            return bytes.Length > 0;
        }

        /// <summary>Store the vendor invoice PDF on the purchase PO so it can be reopened later.</summary>
        public static void AttachToPo(string po, string fileName, byte[] bytes)
        {
            po = (po ?? "").Trim();
            // Source PDFs are keyed by PO #.
            if (po.Length == 0)
                throw new InvalidOperationException("This purchase has no PO number.");
            // An empty file would overwrite a stored invoice with nothing.
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("That PDF is empty.");

            DataFiles.SaveStoredPdf(
                DataFiles.PdfKindPurchaseInvoice,
                po,
                string.IsNullOrWhiteSpace(fileName) ? "vendor-invoice.pdf" : fileName,
                bytes);
        }

        /// <summary>Parse a vendor/customer invoice PDF into a draft for Create Invoice to review.</summary>
        public static PurchaseInvoiceDraft Read(byte[] pdf, string fileName)
        {
            var draft = new PurchaseInvoiceDraft
            {
                FileName = string.IsNullOrWhiteSpace(fileName) ? "vendor-invoice.pdf" : fileName,
                Pdf = pdf ?? Array.Empty<byte>()
            };
            // Keep the empty file so Create Invoice can still attach it by hand.
            if (draft.Pdf.Length == 0)
                return draft;

            List<PdfLine> lines;
            try
            {
                lines = ExtractLines(draft.Pdf);
            }
            // Scans and broken PDFs should still be attached; fill the form by hand.
            catch (Exception ex)
            {
                draft.Error = ex.Message;
                return draft;
            }

            draft.HasText = lines.Any(line => line.Text.Length > 0);
            // No extractable text: still return the PDF bytes for a manual fill.
            if (!draft.HasText)
                return draft;

            string blob = string.Join("\n", lines.Select(line => line.Text));
            string header = string.Join("\n", lines.Take(14).Select(line => line.Text));
            string soldTo = SectionAfter(lines, @"Sold\s*To", @"Bill\s*To", @"Invoice\s*To");
            string shipTo = SectionAfter(lines, @"Ship\s*To", @"Deliver\s*To", @"Ship\s*To\s*Address");
            MatchVendor(draft, header);
            MatchCustomer(draft, soldTo.Length > 0 ? soldTo : blob);
            MatchLabels(draft, blob, lines);
            MatchLines(draft, lines);
            draft.SoldTo = CleanBlock(soldTo);
            draft.ShipTo = CleanBlock(shipTo);
            DetectDirection(draft, header, soldTo, blob);
            return draft;
        }

        /// <summary>Group PDF words into visual lines, falling back to page text when words are missing.</summary>
        private static List<PdfLine> ExtractLines(byte[] pdf)
        {
            var result = new List<PdfLine>();
            using var doc = PdfDocument.Open(pdf);
            int pageNo = 0;
            foreach (var page in doc.GetPages())
            {
                pageNo++;
                var words = page.GetWords()
                    .Select(word => new PdfWord(
                        Clean(word.Text),
                        word.BoundingBox.Left,
                        word.BoundingBox.Bottom,
                        word.BoundingBox.Width))
                    .Where(word => word.Text.Length > 0)
                    .OrderByDescending(word => word.Y)
                    .ThenBy(word => word.X)
                    .ToList();
                // Some invoices have no word boxes; page.Text is the only extractable content.
                if (words.Count == 0)
                {
                    string pageText = Clean(page.Text);
                    // Empty page.Text is a scan with nothing to parse.
                    if (pageText.Length > 0)
                    {
                        foreach (string raw in pageText.Split('\n'))
                        {
                            string text = Clean(raw);
                            // Skip blank splits so they do not become empty product rows.
                            if (text.Length > 0)
                                result.Add(new PdfLine(pageNo, 0, text, Array.Empty<PdfWord>()));
                        }
                    }

                    continue;
                }

                var bucket = new List<PdfWord>();
                double baseline = words[0].Y;
                foreach (var word in words)
                {
                    // A jump in Y starts a new visual line.
                    if (bucket.Count > 0 && Math.Abs(word.Y - baseline) > 3.2)
                    {
                        result.Add(ToLine(pageNo, bucket));
                        bucket.Clear();
                        baseline = word.Y;
                    }

                    // First word on a line sets the baseline for the rest.
                    if (bucket.Count == 0)
                        baseline = word.Y;
                    bucket.Add(word);
                }

                // Flush the last line on the page.
                if (bucket.Count > 0)
                    result.Add(ToLine(pageNo, bucket));
            }

            return result;
        }

        /// <summary>Join words on one baseline, using extra spaces where the PDF left a column gap.</summary>
        private static PdfLine ToLine(int page, List<PdfWord> words)
        {
            words.Sort((a, b) => a.X.CompareTo(b.X));
            var text = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                // Wide gaps become extra spaces so column text stays separable.
                if (i > 0)
                {
                    double gap = words[i].X - (words[i - 1].X + words[i - 1].Width);
                    text.Append(gap > 8 ? "   " : " ");
                }

                text.Append(words[i].Text);
            }

            return new PdfLine(page, words[0].Y, Clean(text.ToString()), words.ToArray());
        }

        /// <summary>Fill vendor code/name when the header text scores high enough against Vendors.</summary>
        private static void MatchVendor(PurchaseInvoiceDraft draft, string hay)
        {
            var best = BestParty(hay, DataFiles.Vendors);
            // Weak matches are more often letterhead noise than a real vendor.
            if (best.Score < 70)
                return;

            draft.VendorCode = best.Hit.Code;
            draft.VendorName = best.Hit.Name;
            // Terms from the vendor record only fill when the PDF did not already name them.
            if (best.Hit.Extra.Length > 0 && draft.Terms.Length == 0)
                draft.Terms = best.Hit.Extra;
        }

        /// <summary>Fill customer code/name when Sold To scores high enough against Customers.</summary>
        private static void MatchCustomer(PurchaseInvoiceDraft draft, string hay)
        {
            var best = BestParty(hay, DataFiles.Customers);
            // Weak matches are more often our own letterhead than a customer.
            if (best.Score < 70)
                return;

            draft.CustomerCode = best.Hit.Code;
            draft.CustomerName = best.Hit.Name;
            // Terms from the customer record only fill when the PDF did not already name them.
            if (best.Hit.Extra.Length > 0 && draft.Terms.Length == 0)
                draft.Terms = best.Hit.Extra;
        }

        /// <summary>Score vendors or customers against invoice text, ignoring our own company name.</summary>
        private static (LookupSuggest.Hit Hit, int Score) BestParty(string hay, string table)
        {
            LookupSuggest.Hit best = default;
            int bestScore = 0;
            foreach (var record in DataFiles.VisibleRecords(table))
            {
                string code = DataFiles.GetRecord(record, "Code").Trim();
                string name = DataFiles.GetRecord(record, "Name").Trim();
                string company = DataFiles.GetRecord(record, "Company").Trim();
                // Matching ourselves would flip incoming/outgoing detection.
                if (IsOurCompany(name) || IsOurCompany(company))
                    continue;
                int score = 0;
                // A unique party code in the PDF is stronger than a partial name hit.
                if (code.Length >= 3 && ContainsWord(hay, code))
                    score = Math.Max(score, 90 + Math.Min(10, code.Length));
                score = Math.Max(score, NameScore(hay, name));
                score = Math.Max(score, NameScore(hay, company));
                // Keep the first best score; later ties do not replace it.
                if (score <= bestScore)
                    continue;
                bestScore = score;
                best = new LookupSuggest.Hit(
                    code,
                    name.Length > 0 ? name : company,
                    DataFiles.GetRecord(record, "Terms"));
            }

            return (best, bestScore);
        }

        /// <summary>Guess incoming vs issued from where our company name appears, then vendor/customer hits.</summary>
        private static void DetectDirection(
            PurchaseInvoiceDraft draft,
            string header,
            string soldTo,
            string blob)
        {
            int usHeader = OurCompanyScore(header);
            int usSold = OurCompanyScore(soldTo.Length > 0 ? soldTo : blob);
            bool hasVendor = draft.VendorName.Length > 0 || draft.VendorCode.Length > 0;
            bool hasCustomer = draft.CustomerName.Length > 0 || draft.CustomerCode.Length > 0;

            // We are Sold To: this is a vendor invoice we received.
            if (usSold >= 70 && usSold >= usHeader)
                draft.Incoming = true;
            // We appear in the letterhead: we issued the invoice.
            else if (usHeader >= 70 && usHeader > usSold)
                draft.Incoming = false;
            // A vendor hit with no customer is typically incoming.
            else if (hasVendor && !hasCustomer)
                draft.Incoming = true;
            // A customer hit with no vendor is typically outgoing.
            else if (hasCustomer && !hasVendor)
                draft.Incoming = false;
            // Both parties matched: prefer Sold To over letterhead.
            else if (hasVendor && hasCustomer)
                draft.Incoming = usSold >= usHeader;
            // No party or company hit: leave direction unset so Create Invoice can ask.
            else
                draft.Incoming = null;
        }

        /// <summary>Best name-match score of our company aliases against a text block.</summary>
        private static int OurCompanyScore(string hay)
        {
            int best = 0;
            foreach (string name in OurNames())
                best = Math.Max(best, NameScore(hay, name));
            return best;
        }

        /// <summary>True when this party name is Cast Right Catch (or the configured business name).</summary>
        private static bool IsOurCompany(string? name)
        {
            name = (name ?? "").Trim();
            // Short names collide with ordinary words.
            if (name.Length < 4)
                return false;
            foreach (string ours in OurNames())
            {
                // Exact match is our company even when the alias is short.
                if (name.Equals(ours, StringComparison.OrdinalIgnoreCase))
                    return true;
                // Longer aliases can appear inside "Cast Right Catch Co. LLC".
                if (ours.Length >= 8 && name.Contains(ours, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Configured business name plus the default Cast Right Catch aliases.</summary>
        private static IEnumerable<string> OurNames()
        {
            string biz = (AppState.BusinessName ?? "").Trim();
            // Settings can override the printed company name.
            if (biz.Length > 0)
                yield return biz;
            yield return "Cast Right Catch Co.";
            yield return "Cast Right Catch Co";
            yield return "Cast Right Catch";
        }

        /// <summary>Collect a few lines after Sold To / Ship To until the next section header.</summary>
        private static string SectionAfter(List<PdfLine> lines, params string[] labels)
        {
            var sb = new StringBuilder();
            bool grab = false;
            int grabbed = 0;
            foreach (var line in lines)
            {
                // Wait for a Sold To / Ship To label before capturing address lines.
                if (!grab)
                {
                    foreach (string label in labels)
                    {
                        var match = Regex.Match(
                            line.Text,
                            label + @"\s*[:.]?\s*(.*)$",
                            RegexOptions.IgnoreCase);
                        // This line is not the Sold To / Ship To label we are waiting for.
                        if (!match.Success)
                            continue;
                        grab = true;
                        string rest = match.Groups[1].Value.Trim();
                        // Some PDFs put the first address on the same line as the label.
                        if (rest.Length > 0)
                        {
                            sb.AppendLine(rest);
                            grabbed++;
                        }

                        break;
                    }

                    continue;
                }

                // Stop before the next labeled block or the item table.
                if (LooksLikeHeader(line.Text) || IsSectionLabel(line.Text))
                    break;
                // Blank lines after the first address line end the Sold To / Ship To block.
                if (line.Text.Length == 0)
                {
                    // A blank line after address text ends the block.
                    if (grabbed > 0)
                        break;
                    continue;
                }

                sb.AppendLine(line.Text);
                // Address blocks are short; more lines are usually the next section.
                if (++grabbed >= 4)
                    break;
            }

            return sb.ToString().Trim();
        }

        /// <summary>True when a line is a Sold To / Bill To / Terms heading rather than address text.</summary>
        private static bool IsSectionLabel(string text) =>
            Regex.IsMatch(
                text,
                @"^(sold\s*to|bill\s*to|ship\s*to|deliver\s*to|terms|invoice|po\s*#|customer|vendor)\b",
                RegexOptions.IgnoreCase);

        /// <summary>Trim an address block to a few non-empty lines for the form.</summary>
        private static string CleanBlock(string text)
        {
            text = Clean(text);
            // Empty address blocks stay empty rather than becoming a newline.
            if (text.Length == 0)
                return "";
            var lines = text.Replace("\r", "")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Take(4);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>Score how clearly a party name appears in invoice text.</summary>
        private static int NameScore(string hay, string name)
        {
            name = (name ?? "").Trim();
            // Short names collide with ordinary words on invoices.
            if (name.Length < 4)
                return 0;
            // Whole-name hits score higher than a single token.
            if (ContainsWord(hay, name))
                return 80 + Math.Min(15, name.Length);
            foreach (string part in name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // Skip "Co", "Inc", and similar tokens that appear on every invoice.
                if (part.Length < 5 || IsNoise(part))
                    continue;
                // A distinctive token from the name is enough for a weak match.
                if (ContainsWord(hay, part))
                    return 72;
            }

            return 0;
        }

        /// <summary>Pull PO, invoice #, terms, and dates from labeled fields in the PDF text.</summary>
        private static void MatchLabels(PurchaseInvoiceDraft draft, string blob, List<PdfLine> lines)
        {
            draft.Po = FirstGroup(blob,
                @"\b(?:P\.?\s*O\.?|Purchase\s+Order)(?:\s*(?:#|No\.?|Number))?\s*[:#]?\s*([A-Z0-9][A-Z0-9\-_/.]*)",
                @"\bOur\s+PO\s*[:#]?\s*([A-Z0-9][A-Z0-9\-_/.]*)");
            draft.InvoiceNumber = FirstGroup(blob,
                @"\bInvoice\s*(?:#|No\.?|Number)?\s*[:#]?\s*([A-Z0-9][A-Z0-9\-_/.]*)");
            draft.SoNumber = FirstGroup(blob,
                @"\bS\.?O\.?\s*(?:#|No\.?|Number)?\s*[:#]?\s*([A-Z0-9][A-Z0-9\-_/.]*)",
                @"\bSales\s+Order\s*(?:#|No\.?|Number)?\s*[:#]?\s*([A-Z0-9][A-Z0-9\-_/.]*)");

            string terms = FirstGroup(blob,
                @"\bTerms?\s*[:#]?\s*(NET\s*\d+(?:\s*DAYS?)?)",
                @"\bPayment\s+Terms?\s*[:#]?\s*([A-Z0-9][A-Z0-9 /-]{2,24})");
            // Explicit payment terms on the PDF override the vendor-record default.
            if (terms.Length > 0)
                draft.Terms = terms;

            draft.InvoiceDate = FirstDate(blob, lines,
                @"Invoice\s*Date", @"Inv\.?\s*Date", @"Date\s+Issued", @"^Date$");
            draft.DueDate = FirstDate(blob, lines, @"Due\s*Date", @"Payment\s*Due", @"Pay\s*By");
            draft.ShipDate = FirstDate(blob, lines, @"Ship(?:ping)?\s*Date", @"Shipped", @"Ship\s+On");

            // Many invoices only print a date in the header with no "Invoice Date" label.
            if (draft.InvoiceDate == null)
            {
                foreach (var line in lines.Take(12))
                {
                    // First parseable date in the header is treated as the invoice date.
                    if (TryFindDate(line.Text, out var date))
                    {
                        draft.InvoiceDate = date;
                        break;
                    }
                }
            }
        }

        /// <summary>Read item rows from a detected table, then fall back to scanning every line.</summary>
        private static void MatchLines(PurchaseInvoiceDraft draft, List<PdfLine> lines)
        {
            var items = LoadItems();
            int header = FindHeader(lines);
            // A column header row is the most reliable way to parse item tables.
            if (header >= 0)
                ReadTable(draft, lines, header, items);

            // Table parse already filled the draft; skip the looser per-line scan.
            if (draft.Lines.Count > 0)
                return;

            foreach (var line in lines)
            {
                // Totals/tax/freight rows are not products.
                if (IsTotalLine(line.Text))
                    continue;
                var parsed = ParseItemLine(line.Text, items);
                // Keep only rows that looked like a product with qty/price.
                if (parsed != null)
                    draft.Lines.Add(parsed);
            }
        }

        /// <summary>Find the item-table header by counting qty/price/description tokens.</summary>
        private static int FindHeader(List<PdfLine> lines)
        {
            int best = -1;
            int bestHits = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i].Text;
                int hits = 0;
                // Each matching column kind is one vote toward "this is the item table header".
                if (HasToken(text, "item", "code", "sku", "product", "plu"))
                    hits++;
                if (HasToken(text, "description", "desc", "product"))
                    hits++;
                if (HasToken(text, "cs", "cases", "ctn", "qty", "quantity"))
                    hits++;
                if (HasToken(text, "lb", "lbs", "weight", "net", "volume"))
                    hits++;
                if (HasToken(text, "price", "rate", "unit"))
                    hits++;
                if (HasToken(text, "amount", "total", "ext"))
                    hits++;
                // Need several column labels so a lone "Total" line is not treated as the table.
                if (hits > bestHits && hits >= 3)
                {
                    bestHits = hits;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Parse item rows under a classified header until totals or a new header.</summary>
        private static void ReadTable(
            PurchaseInvoiceDraft draft,
            List<PdfLine> lines,
            int headerIndex,
            List<ItemHit> items)
        {
            var header = lines[headerIndex];
            var columns = ClassifyColumns(header);
            // Without X positions, fall back to the whole-line parser later.
            if (columns.Count == 0)
                return;

            for (int i = headerIndex + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                // Skip spacer rows between item lines.
                if (line.Text.Length == 0)
                    continue;
                // Totals or another header ends the item table.
                if (IsTotalLine(line.Text) || LooksLikeHeader(line.Text))
                    break;

                var parsed = ParseItemLine(line.Text, items);
                // Column X positions recover rows the free-text parser missed.
                if (parsed == null)
                    parsed = ParseByColumns(line, columns, items);
                // Neither parser recognized a product on this row.
                if (parsed == null)
                    continue;
                // A qty-only row with no product identity is not a line we can save.
                if (parsed.ItemCode.Length == 0 && parsed.Description.Length == 0)
                    continue;
                draft.Lines.Add(parsed);
            }
        }

        /// <summary>Map header words to item/desc/qty/price columns by their X position.</summary>
        private static List<(string Kind, double X)> ClassifyColumns(PdfLine header)
        {
            var columns = new List<(string Kind, double X)>();
            // Page-text fallback lines have no word boxes to classify.
            if (header.Words.Count == 0)
                return columns;

            foreach (var word in header.Words)
            {
                string kind = ColumnKind(word.Text);
                // Ignore filler words on the header row.
                if (kind.Length == 0)
                    continue;
                columns.Add((kind, word.X));
            }

            return columns;
        }

        /// <summary>Normalize a header token into an item-table column kind, or empty if unknown.</summary>
        private static string ColumnKind(string token)
        {
            token = token.Trim().Trim(':').ToLowerInvariant();
            return token switch
            {
                // Product identity columns.
                "item" or "code" or "sku" or "plu" or "product#" or "item#" => "item",
                "description" or "desc" or "product" or "name" => "desc",
                "cs" or "cases" or "ctn" or "qty" or "quantity" or "ord" => "cs",
                "pack" or "size" or "packsize" => "pack",
                "lb" or "lbs" or "weight" or "net" or "nw" or "volume" or "kgs" => "vol",
                "price" or "rate" or "unit" or "cost" => "price",
                "amount" or "total" or "ext" or "extension" => "amount",
                "coo" or "origin" or "country" => "coo",
                _ => ""
            };
        }

        /// <summary>Bucket words into header columns and build a purchase line from those cells.</summary>
        private static PurchaseLine? ParseByColumns(PdfLine line, List<(string Kind, double X)> columns, List<ItemHit> items)
        {
            // No word boxes: parse the joined text instead.
            if (line.Words.Count == 0)
                return ParseItemLine(line.Text, items);

            var buckets = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in line.Words)
            {
                string kind = NearestColumn(columns, word.X);
                // Words far from every header column are ignored.
                if (kind.Length == 0)
                    continue;
                // First word in a column starts the cell; later words append.
                if (!buckets.TryGetValue(kind, out var sb))
                {
                    sb = new StringBuilder();
                    buckets[kind] = sb;
                }

                // Space-separate words that land in the same column.
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(word.Text);
            }

            string item = GetBucket(buckets, "item");
            string desc = GetBucket(buckets, "desc");
            var hit = MatchItem(item, desc, items);
            var parsed = new PurchaseLine
            {
                ItemCode = hit?.Code ?? item,
                Description = hit?.Name.Length > 0 ? hit.Name : desc,
                Coo = hit?.Coo ?? GetBucket(buckets, "coo"),
                PackSize = GetBucket(buckets, "pack"),
                Cases = Qty(GetBucket(buckets, "cs")),
                Volume = Qty(GetBucket(buckets, "vol")),
                Price = Money(GetBucket(buckets, "price"))
            };
            string amount = Money(GetBucket(buckets, "amount"));
            // Some invoices only print extension amount; derive price / lb from weight.
            if (parsed.Price.Length == 0 && amount.Length > 0)
            {
                decimal lbs = PurchaseLineRow.ParseNumber(parsed.Volume);
                decimal total = PurchaseLineRow.ParseNumber(amount);
                // Guard against divide-by-zero when the PDF has an amount but no weight.
                if (lbs > 0 && total > 0)
                    parsed.Price = (total / lbs).ToString("0.####", CultureInfo.InvariantCulture);
            }

            // Need a product identity to save the line.
            if (parsed.ItemCode.Length == 0 && parsed.Description.Length == 0)
                return null;
            // A name with no qty or price is likely a wrapped description, not a new line.
            if (parsed.Volume.Length == 0 && parsed.Cases.Length == 0 && parsed.Price.Length == 0)
                return null;
            return parsed;
        }

        /// <summary>Assign a word to the nearest header column within 48 PDF units.</summary>
        private static string NearestColumn(List<(string Kind, double X)> columns, double x)
        {
            string kind = "";
            double best = 48;
            foreach (var column in columns)
            {
                double dist = Math.Abs(column.X - x);
                // Farther than the current nearest (or the 48-unit cutoff) is ignored.
                if (dist >= best)
                    continue;
                best = dist;
                kind = column.Kind;
            }

            return kind;
        }

        /// <summary>Parse a free-text invoice row by matching a known item and leftover numbers.</summary>
        private static PurchaseLine? ParseItemLine(string text, List<ItemHit> items)
        {
            var hit = MatchItemInText(text, items);
            var numbers = Regex.Matches(text, @"\$?\d{1,3}(?:,\d{3})*(?:\.\d+)?|\d+\.\d+")
                .Select(match => match.Value.Trim())
                .Where(value => !LooksLikeDateToken(value))
                .Select(value => PurchaseLineRow.ParseNumber(value))
                .Where(n => n > 0)
                .ToList();
            // Without a known item, a single number is not enough to guess qty and price.
            if (hit == null && numbers.Count < 2)
                return null;

            decimal cs = 0;
            decimal volume = 0;
            decimal price = 0;
            AssignNumbers(numbers, ref cs, ref volume, ref price);
            // Numbers that did not look like volume or price are not a product row.
            if (hit == null && volume == 0 && price == 0)
                return null;

            string desc = hit?.Name ?? "";
            // Use the PDF text minus codes and numbers when the catalog has no name.
            if (desc.Length == 0)
                desc = StripLeadingCode(text, hit?.Code);

            return new PurchaseLine
            {
                ItemCode = hit?.Code ?? "",
                Description = desc,
                Coo = hit?.Coo ?? "",
                Cases = cs > 0 ? cs.ToString("0.###", CultureInfo.InvariantCulture) : "",
                Volume = volume > 0 ? volume.ToString("0.###", CultureInfo.InvariantCulture) : "",
                Price = price > 0 ? price.ToString("0.####", CultureInfo.InvariantCulture) : ""
            };
        }

        /// <summary>Guess cases, volume, and price from the numbers on an invoice row.</summary>
        private static void AssignNumbers(List<decimal> numbers, ref decimal cs, ref decimal volume, ref decimal price)
        {
            // Nothing to assign when the row had no numeric tokens.
            if (numbers.Count == 0)
                return;

            var copy = numbers.ToList();
            // Four+ numbers: cases, volume, unit price, then extension.
            if (copy.Count >= 4)
            {
                cs = copy[0];
                volume = copy[1];
                price = copy[2];
                return;
            }

            // Three numbers: try cases/volume/price, else volume + extension.
            if (copy.Count == 3)
            {
                // Integer first value in a typical case count, with volume at least as large.
                if (copy[0] == Math.Truncate(copy[0]) && copy[0] <= 400 && copy[1] >= copy[0])
                {
                    cs = copy[0];
                    volume = copy[1];
                    // Large third number is usually extension, not price / lb.
                    price = copy[2] > 200 && copy[1] > 0 ? copy[2] / copy[1] : copy[2];
                    return;
                }

                volume = copy[0];
                // Same extension-vs-price heuristic when cases were not detected.
                price = copy[1] > 200 && copy[0] > 0 ? copy[2] / copy[0] : copy[1];
                return;
            }

            // Two numbers: volume+price or cases+volume.
            if (copy.Count == 2)
            {
                // Larger first value, or a small second value, looks like volume then price.
                if (copy[0] > copy[1] || copy[1] < 80)
                {
                    volume = copy[0];
                    price = copy[1];
                }
                // Small first value, large second: cases then volume.
                else
                {
                    cs = copy[0];
                    volume = copy[1];
                }
            }
            // A single large number is more often pounds than cases.
            else if (copy[0] >= 10)
            {
                volume = copy[0];
            }
            // A single small number is more often a case count.
            else
            {
                cs = copy[0];
            }
        }

        /// <summary>Find a catalog item whose code or long description appears in the row text.</summary>
        private static ItemHit? MatchItemInText(string text, List<ItemHit> items)
        {
            foreach (var item in items)
            {
                // Prefer an exact item-code token over a description phrase.
                if (item.Code.Length >= 3 && ContainsWord(text, item.Code))
                    return item;
            }

            foreach (var item in items)
            {
                // Short descriptions collide with ordinary invoice words.
                if (item.Name.Length >= 8 && ContainsWord(text, item.Name))
                    return item;
            }

            return null;
        }

        /// <summary>Resolve a parsed code or description to a catalog item.</summary>
        private static ItemHit? MatchItem(string code, string desc, List<ItemHit> items)
        {
            // Exact code match wins over fuzzy description.
            if (code.Length > 0)
            {
                foreach (var item in items)
                {
                    // Catalog code must match the parsed cell exactly (case-insensitive).
                    if (item.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                        return item;
                }
            }

            // Short descriptions collide with ordinary invoice words.
            if (desc.Length >= 4)
            {
                ItemHit? best = null;
                int bestScore = 0;
                foreach (var item in items)
                {
                    int score = TextMatch.Score(item.Name, desc);
                    // Keep the first best score; later ties do not replace it.
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    best = item;
                }

                // Weak description scores are more often a different product.
                if (bestScore >= 70)
                    return best;
            }

            return null;
        }

        /// <summary>Load item-code catalog hits, longest codes first so prefixes do not steal matches.</summary>
        private static List<ItemHit> LoadItems()
        {
            var list = new List<ItemHit>();
            foreach (var record in DataFiles.VisibleRecords(DataFiles.ItemCodes))
            {
                string code = DataFiles.GetRecord(record, "Code").Trim();
                string description = DataFiles.GetRecord(record, "Description").Trim();
                // Species is the fallback label when Description is blank.
                if (description.Length == 0)
                    description = DataFiles.GetRecord(record, "Species").Trim();
                // An item with no code and no name cannot be matched.
                if (code.Length == 0 && description.Length == 0)
                    continue;
                list.Add(new ItemHit(code, description, DataFiles.GetRecord(record, "COO").Trim()));
            }

            list.Sort((a, b) => b.Code.Length.CompareTo(a.Code.Length));
            return list;
        }

        /// <summary>Find a date after one of the given labels, then on a line that contains the label.</summary>
        private static DateTime? FirstDate(string blob, List<PdfLine> lines, params string[] labels)
        {
            foreach (string label in labels)
            {
                var match = Regex.Match(
                    blob,
                    label + @"\s*[:#]?\s*(\d{1,2}[./-]\d{1,2}[./-]\d{2,4})",
                    RegexOptions.IgnoreCase);
                // Label + date in the blob is the most reliable invoice/due/ship date.
                if (match.Success && NumericDateBox.TryParseCell(match.Groups[1].Value, out var dated))
                    return dated;
            }

            foreach (var line in lines)
            {
                // Label and date are often on the same visual line with extra words between.
                if (!labels.Any(label => Regex.IsMatch(line.Text, label, RegexOptions.IgnoreCase)))
                    continue;
                // Date on the same visual line as the label, with extra words between.
                if (TryFindDate(line.Text, out var dated))
                    return dated;
            }

            return null;
        }

        /// <summary>Parse the first MM/DD/YYYY-style token on a line.</summary>
        private static bool TryFindDate(string text, out DateTime date)
        {
            date = default;
            foreach (Match match in Regex.Matches(text, @"\b\d{1,2}[./-]\d{1,2}[./-]\d{2,4}\b"))
            {
                // First token that parses as a real date wins; skip 99/99/99 junk.
                if (NumericDateBox.TryParseCell(match.Value, out date))
                    return true;
            }

            return false;
        }

        /// <summary>Return the first regex capture that is not a noise word such as Invoice or Date.</summary>
        private static string FirstGroup(string blob, params string[] patterns)
        {
            foreach (string pattern in patterns)
            {
                var match = Regex.Match(blob, pattern, RegexOptions.IgnoreCase);
                // Try the next pattern when this label is missing from the PDF.
                if (!match.Success)
                    continue;
                string value = match.Groups[1].Value.Trim().TrimEnd('.', ',');
                // Skip captures that are just the label itself.
                if (value.Length > 0 && !IsNoise(value))
                    return value;
            }

            return "";
        }

        /// <summary>Phrase match for multi-word names; whole-word match for codes.</summary>
        private static bool ContainsWord(string hay, string needle)
        {
            // Blank hay or needle cannot be a word match.
            if (string.IsNullOrWhiteSpace(hay) || string.IsNullOrWhiteSpace(needle))
                return false;
            // Multi-word names should not require word-boundary regex around every token.
            if (needle.Contains(' ', StringComparison.Ordinal))
                return hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
            return Regex.IsMatch(hay, @"\b" + Regex.Escape(needle) + @"\b", RegexOptions.IgnoreCase);
        }

        /// <summary>True when any of the tokens appears as a whole word.</summary>
        private static bool HasToken(string text, params string[] tokens)
        {
            foreach (string token in tokens)
            {
                // Any matching column word is enough to count that header kind.
                if (ContainsWord(text, token))
                    return true;
            }

            return false;
        }

        /// <summary>True when a line looks like an item-table header rather than a product row.</summary>
        private static bool LooksLikeHeader(string text) =>
            HasToken(text, "description") && HasToken(text, "item", "code", "product");

        /// <summary>True when a line is totals, tax, freight, or footer text instead of a product.</summary>
        private static bool IsTotalLine(string text)
        {
            return Regex.IsMatch(
                text,
                @"\b(subtotal|total|balance|amount due|tax|gst|vat|freight|shipping|thank you|remit|page)\b",
                RegexOptions.IgnoreCase);
        }

        /// <summary>True for generic invoice words that should not be treated as a PO or party name.</summary>
        private static bool IsNoise(string value)
        {
            value = value.Trim();
            return value.Equals("date", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("invoice", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("total", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("page", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("number", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when a numeric token is a date, not a quantity or price.</summary>
        private static bool LooksLikeDateToken(string value) =>
            Regex.IsMatch(value, @"^\d{1,2}[./-]\d{1,2}([./-]\d{2,4})?$");

        /// <summary>Remove a known item code and numbers so leftover text can be used as description.</summary>
        private static string StripLeadingCode(string text, string? code)
        {
            // Drop a known item code so leftover text can be used as description.
            if (!string.IsNullOrWhiteSpace(code))
                text = Regex.Replace(text, @"\b" + Regex.Escape(code) + @"\b", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\$?\d[\d,]*\.?\d*", " ");
            return Clean(text);
        }

        /// <summary>Joined text for one classified column, or empty when that column was not filled.</summary>
        private static string GetBucket(Dictionary<string, StringBuilder> buckets, string kind) =>
            buckets.TryGetValue(kind, out var sb) ? Clean(sb.ToString()) : "";

        /// <summary>Format a parsed quantity, leaving zero as blank.</summary>
        private static string Qty(string value)
        {
            decimal n = PurchaseLineRow.ParseNumber(value);
            return n == 0 ? "" : n.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Format a parsed money/rate cell, leaving zero as blank.</summary>
        private static string Money(string value)
        {
            decimal n = PurchaseLineRow.ParseNumber(value);
            return n == 0 ? "" : n.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>Strip control characters and collapse whitespace from PDF-extracted text.</summary>
        private static string Clean(string? text)
        {
            // Null/whitespace PDF text becomes empty so callers can skip it.
            if (string.IsNullOrWhiteSpace(text))
                return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                // PDF text can include NULs and other controls that break later regex.
                if (c == '\0' || char.IsControl(c))
                    continue;
                sb.Append(c == '\u00A0' ? ' ' : c);
            }

            return Regex.Replace(sb.ToString(), @"[ \t]+", " ").Trim();
        }

        /// <summary>One PDF word with its page coordinates.</summary>
        private readonly record struct PdfWord(string Text, double X, double Y, double Width);
        /// <summary>Visual line of words on a PDF page.</summary>
        private readonly record struct PdfLine(int Page, double Y, string Text, IReadOnlyList<PdfWord> Words);
        /// <summary>Catalog item used while matching invoice rows.</summary>
        private sealed record ItemHit(string Code, string Name, string Coo);
    }

    /// <summary>Parsed vendor/customer invoice waiting for review on Create Invoice.</summary>
    internal sealed class PurchaseInvoiceDraft
    {
        public string FileName { get; set; } = "";
        public byte[] Pdf { get; set; } = Array.Empty<byte>();
        public bool HasText { get; set; }
        public string? Error { get; set; }
        public string Po { get; set; } = "";
        public string InvoiceNumber { get; set; } = "";
        public string VendorCode { get; set; } = "";
        public string VendorName { get; set; } = "";
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string SoldTo { get; set; } = "";
        public string ShipTo { get; set; } = "";
        public string SoNumber { get; set; } = "";
        public bool? Incoming { get; set; }
        public string Terms { get; set; } = "";
        public DateTime? InvoiceDate { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? ShipDate { get; set; }
        public List<PurchaseLine> Lines { get; } = new();
    }
}

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
            if (dialog.ShowDialog(owner) != DialogResult.OK)
                return false;

            fileName = Path.GetFileName(dialog.FileName);
            bytes = File.ReadAllBytes(dialog.FileName);
            return bytes.Length > 0;
        }

        public static void AttachToPo(string po, string fileName, byte[] bytes)
        {
            po = (po ?? "").Trim();
            if (po.Length == 0)
                throw new InvalidOperationException("This purchase has no PO number.");
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("That PDF is empty.");

            DataFiles.SaveStoredPdf(
                DataFiles.PdfKindPurchaseInvoice,
                po,
                string.IsNullOrWhiteSpace(fileName) ? "vendor-invoice.pdf" : fileName,
                bytes);
        }

        public static PurchaseInvoiceDraft Read(byte[] pdf, string fileName)
        {
            var draft = new PurchaseInvoiceDraft
            {
                FileName = string.IsNullOrWhiteSpace(fileName) ? "vendor-invoice.pdf" : fileName,
                Pdf = pdf ?? Array.Empty<byte>()
            };
            if (draft.Pdf.Length == 0)
                return draft;

            List<PdfLine> lines;
            try
            {
                lines = ExtractLines(draft.Pdf);
            }
            catch (Exception ex)
            {
                draft.Error = ex.Message;
                return draft;
            }

            draft.HasText = lines.Any(line => line.Text.Length > 0);
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
                if (words.Count == 0)
                {
                    string pageText = Clean(page.Text);
                    if (pageText.Length > 0)
                    {
                        foreach (string raw in pageText.Split('\n'))
                        {
                            string text = Clean(raw);
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
                    if (bucket.Count > 0 && Math.Abs(word.Y - baseline) > 3.2)
                    {
                        result.Add(ToLine(pageNo, bucket));
                        bucket.Clear();
                        baseline = word.Y;
                    }

                    if (bucket.Count == 0)
                        baseline = word.Y;
                    bucket.Add(word);
                }

                if (bucket.Count > 0)
                    result.Add(ToLine(pageNo, bucket));
            }

            return result;
        }

        private static PdfLine ToLine(int page, List<PdfWord> words)
        {
            words.Sort((a, b) => a.X.CompareTo(b.X));
            var text = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0)
                {
                    double gap = words[i].X - (words[i - 1].X + words[i - 1].Width);
                    text.Append(gap > 8 ? "   " : " ");
                }

                text.Append(words[i].Text);
            }

            return new PdfLine(page, words[0].Y, Clean(text.ToString()), words.ToArray());
        }

        private static void MatchVendor(PurchaseInvoiceDraft draft, string hay)
        {
            var best = BestParty(hay, DataFiles.Vendors);
            if (best.Score < 70)
                return;

            draft.VendorCode = best.Hit.Code;
            draft.VendorName = best.Hit.Name;
            if (best.Hit.Extra.Length > 0 && draft.Terms.Length == 0)
                draft.Terms = best.Hit.Extra;
        }

        private static void MatchCustomer(PurchaseInvoiceDraft draft, string hay)
        {
            var best = BestParty(hay, DataFiles.Customers);
            if (best.Score < 70)
                return;

            draft.CustomerCode = best.Hit.Code;
            draft.CustomerName = best.Hit.Name;
            if (best.Hit.Extra.Length > 0 && draft.Terms.Length == 0)
                draft.Terms = best.Hit.Extra;
        }

        private static (LookupSuggest.Hit Hit, int Score) BestParty(string hay, string table)
        {
            LookupSuggest.Hit best = default;
            int bestScore = 0;
            foreach (var record in DataFiles.VisibleRecords(table))
            {
                string code = DataFiles.GetRecord(record, "Code").Trim();
                string name = DataFiles.GetRecord(record, "Name").Trim();
                string company = DataFiles.GetRecord(record, "Company").Trim();
                if (IsOurCompany(name) || IsOurCompany(company))
                    continue;
                int score = 0;
                if (code.Length >= 3 && ContainsWord(hay, code))
                    score = Math.Max(score, 90 + Math.Min(10, code.Length));
                score = Math.Max(score, NameScore(hay, name));
                score = Math.Max(score, NameScore(hay, company));
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

            if (usSold >= 70 && usSold >= usHeader)
                draft.Incoming = true;
            else if (usHeader >= 70 && usHeader > usSold)
                draft.Incoming = false;
            else if (hasVendor && !hasCustomer)
                draft.Incoming = true;
            else if (hasCustomer && !hasVendor)
                draft.Incoming = false;
            else if (hasVendor && hasCustomer)
                draft.Incoming = usSold >= usHeader;
            else
                draft.Incoming = null;
        }

        private static int OurCompanyScore(string hay)
        {
            int best = 0;
            foreach (string name in OurNames())
                best = Math.Max(best, NameScore(hay, name));
            return best;
        }

        private static bool IsOurCompany(string? name)
        {
            name = (name ?? "").Trim();
            if (name.Length < 4)
                return false;
            foreach (string ours in OurNames())
            {
                if (name.Equals(ours, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (ours.Length >= 8 && name.Contains(ours, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> OurNames()
        {
            string biz = (AppState.BusinessName ?? "").Trim();
            if (biz.Length > 0)
                yield return biz;
            yield return "Cast Right Catch Co.";
            yield return "Cast Right Catch Co";
            yield return "Cast Right Catch";
        }

        private static string SectionAfter(List<PdfLine> lines, params string[] labels)
        {
            var sb = new StringBuilder();
            bool grab = false;
            int grabbed = 0;
            foreach (var line in lines)
            {
                if (!grab)
                {
                    foreach (string label in labels)
                    {
                        var match = Regex.Match(
                            line.Text,
                            label + @"\s*[:.]?\s*(.*)$",
                            RegexOptions.IgnoreCase);
                        if (!match.Success)
                            continue;
                        grab = true;
                        string rest = match.Groups[1].Value.Trim();
                        if (rest.Length > 0)
                        {
                            sb.AppendLine(rest);
                            grabbed++;
                        }

                        break;
                    }

                    continue;
                }

                if (LooksLikeHeader(line.Text) || IsSectionLabel(line.Text))
                    break;
                if (line.Text.Length == 0)
                {
                    if (grabbed > 0)
                        break;
                    continue;
                }

                sb.AppendLine(line.Text);
                if (++grabbed >= 4)
                    break;
            }

            return sb.ToString().Trim();
        }

        private static bool IsSectionLabel(string text) =>
            Regex.IsMatch(
                text,
                @"^(sold\s*to|bill\s*to|ship\s*to|deliver\s*to|terms|invoice|po\s*#|customer|vendor)\b",
                RegexOptions.IgnoreCase);

        private static string CleanBlock(string text)
        {
            text = Clean(text);
            if (text.Length == 0)
                return "";
            var lines = text.Replace("\r", "")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Take(4);
            return string.Join(Environment.NewLine, lines);
        }

        private static int NameScore(string hay, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length < 4)
                return 0;
            if (ContainsWord(hay, name))
                return 80 + Math.Min(15, name.Length);
            foreach (string part in name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Length < 5 || IsNoise(part))
                    continue;
                if (ContainsWord(hay, part))
                    return 72;
            }

            return 0;
        }

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
            if (terms.Length > 0)
                draft.Terms = terms;

            draft.InvoiceDate = FirstDate(blob, lines,
                @"Invoice\s*Date", @"Inv\.?\s*Date", @"Date\s+Issued", @"^Date$");
            draft.DueDate = FirstDate(blob, lines, @"Due\s*Date", @"Payment\s*Due", @"Pay\s*By");
            draft.ShipDate = FirstDate(blob, lines, @"Ship(?:ping)?\s*Date", @"Shipped", @"Ship\s+On");

            if (draft.InvoiceDate == null)
            {
                foreach (var line in lines.Take(12))
                {
                    if (TryFindDate(line.Text, out var date))
                    {
                        draft.InvoiceDate = date;
                        break;
                    }
                }
            }
        }

        private static void MatchLines(PurchaseInvoiceDraft draft, List<PdfLine> lines)
        {
            var items = LoadItems();
            int header = FindHeader(lines);
            if (header >= 0)
                ReadTable(draft, lines, header, items);

            if (draft.Lines.Count > 0)
                return;

            foreach (var line in lines)
            {
                if (IsTotalLine(line.Text))
                    continue;
                var parsed = ParseItemLine(line.Text, items);
                if (parsed != null)
                    draft.Lines.Add(parsed);
            }
        }

        private static int FindHeader(List<PdfLine> lines)
        {
            int best = -1;
            int bestHits = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i].Text;
                int hits = 0;
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
                if (hits > bestHits && hits >= 3)
                {
                    bestHits = hits;
                    best = i;
                }
            }

            return best;
        }

        private static void ReadTable(
            PurchaseInvoiceDraft draft,
            List<PdfLine> lines,
            int headerIndex,
            List<ItemHit> items)
        {
            var header = lines[headerIndex];
            var columns = ClassifyColumns(header);
            if (columns.Count == 0)
                return;

            for (int i = headerIndex + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Text.Length == 0)
                    continue;
                if (IsTotalLine(line.Text) || LooksLikeHeader(line.Text))
                    break;

                var parsed = ParseItemLine(line.Text, items);
                if (parsed == null)
                    parsed = ParseByColumns(line, columns, items);
                if (parsed == null)
                    continue;
                if (parsed.ItemCode.Length == 0 && parsed.Description.Length == 0)
                    continue;
                draft.Lines.Add(parsed);
            }
        }

        private static List<(string Kind, double X)> ClassifyColumns(PdfLine header)
        {
            var columns = new List<(string Kind, double X)>();
            if (header.Words.Count == 0)
                return columns;

            foreach (var word in header.Words)
            {
                string kind = ColumnKind(word.Text);
                if (kind.Length == 0)
                    continue;
                columns.Add((kind, word.X));
            }

            return columns;
        }

        private static string ColumnKind(string token)
        {
            token = token.Trim().Trim(':').ToLowerInvariant();
            return token switch
            {
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

        private static PurchaseLine? ParseByColumns(PdfLine line, List<(string Kind, double X)> columns, List<ItemHit> items)
        {
            if (line.Words.Count == 0)
                return ParseItemLine(line.Text, items);

            var buckets = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in line.Words)
            {
                string kind = NearestColumn(columns, word.X);
                if (kind.Length == 0)
                    continue;
                if (!buckets.TryGetValue(kind, out var sb))
                {
                    sb = new StringBuilder();
                    buckets[kind] = sb;
                }

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
            if (parsed.Price.Length == 0 && amount.Length > 0)
            {
                decimal lbs = PurchaseLineRow.ParseNumber(parsed.Volume);
                decimal total = PurchaseLineRow.ParseNumber(amount);
                if (lbs > 0 && total > 0)
                    parsed.Price = (total / lbs).ToString("0.####", CultureInfo.InvariantCulture);
            }

            if (parsed.ItemCode.Length == 0 && parsed.Description.Length == 0)
                return null;
            if (parsed.Volume.Length == 0 && parsed.Cases.Length == 0 && parsed.Price.Length == 0)
                return null;
            return parsed;
        }

        private static string NearestColumn(List<(string Kind, double X)> columns, double x)
        {
            string kind = "";
            double best = 48;
            foreach (var column in columns)
            {
                double dist = Math.Abs(column.X - x);
                if (dist >= best)
                    continue;
                best = dist;
                kind = column.Kind;
            }

            return kind;
        }

        private static PurchaseLine? ParseItemLine(string text, List<ItemHit> items)
        {
            var hit = MatchItemInText(text, items);
            var numbers = Regex.Matches(text, @"\$?\d{1,3}(?:,\d{3})*(?:\.\d+)?|\d+\.\d+")
                .Select(match => match.Value.Trim())
                .Where(value => !LooksLikeDateToken(value))
                .Select(value => PurchaseLineRow.ParseNumber(value))
                .Where(n => n > 0)
                .ToList();
            if (hit == null && numbers.Count < 2)
                return null;

            decimal cs = 0;
            decimal volume = 0;
            decimal price = 0;
            AssignNumbers(numbers, ref cs, ref volume, ref price);
            if (hit == null && volume == 0 && price == 0)
                return null;

            string desc = hit?.Name ?? "";
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

        private static void AssignNumbers(List<decimal> numbers, ref decimal cs, ref decimal volume, ref decimal price)
        {
            if (numbers.Count == 0)
                return;

            var copy = numbers.ToList();
            if (copy.Count >= 4)
            {
                cs = copy[0];
                volume = copy[1];
                price = copy[2];
                return;
            }

            if (copy.Count == 3)
            {
                if (copy[0] == Math.Truncate(copy[0]) && copy[0] <= 400 && copy[1] >= copy[0])
                {
                    cs = copy[0];
                    volume = copy[1];
                    price = copy[2] > 200 && copy[1] > 0 ? copy[2] / copy[1] : copy[2];
                    return;
                }

                volume = copy[0];
                price = copy[1] > 200 && copy[0] > 0 ? copy[2] / copy[0] : copy[1];
                return;
            }

            if (copy.Count == 2)
            {
                if (copy[0] > copy[1] || copy[1] < 80)
                {
                    volume = copy[0];
                    price = copy[1];
                }
                else
                {
                    cs = copy[0];
                    volume = copy[1];
                }
            }
            else if (copy[0] >= 10)
            {
                volume = copy[0];
            }
            else
            {
                cs = copy[0];
            }
        }

        private static ItemHit? MatchItemInText(string text, List<ItemHit> items)
        {
            foreach (var item in items)
            {
                if (item.Code.Length >= 3 && ContainsWord(text, item.Code))
                    return item;
            }

            foreach (var item in items)
            {
                if (item.Name.Length >= 8 && ContainsWord(text, item.Name))
                    return item;
            }

            return null;
        }

        private static ItemHit? MatchItem(string code, string desc, List<ItemHit> items)
        {
            if (code.Length > 0)
            {
                foreach (var item in items)
                {
                    if (item.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                        return item;
                }
            }

            if (desc.Length >= 4)
            {
                ItemHit? best = null;
                int bestScore = 0;
                foreach (var item in items)
                {
                    int score = TextMatch.Score(item.Name, desc);
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    best = item;
                }

                if (bestScore >= 70)
                    return best;
            }

            return null;
        }

        private static List<ItemHit> LoadItems()
        {
            var list = new List<ItemHit>();
            foreach (var record in DataFiles.VisibleRecords(DataFiles.ItemCodes))
            {
                string code = DataFiles.GetRecord(record, "Code").Trim();
                string description = DataFiles.GetRecord(record, "Description").Trim();
                if (description.Length == 0)
                    description = DataFiles.GetRecord(record, "Species").Trim();
                if (code.Length == 0 && description.Length == 0)
                    continue;
                list.Add(new ItemHit(code, description, DataFiles.GetRecord(record, "COO").Trim()));
            }

            list.Sort((a, b) => b.Code.Length.CompareTo(a.Code.Length));
            return list;
        }

        private static DateTime? FirstDate(string blob, List<PdfLine> lines, params string[] labels)
        {
            foreach (string label in labels)
            {
                var match = Regex.Match(
                    blob,
                    label + @"\s*[:#]?\s*(\d{1,2}[./-]\d{1,2}[./-]\d{2,4})",
                    RegexOptions.IgnoreCase);
                if (match.Success && NumericDateBox.TryParseCell(match.Groups[1].Value, out var dated))
                    return dated;
            }

            foreach (var line in lines)
            {
                if (!labels.Any(label => Regex.IsMatch(line.Text, label, RegexOptions.IgnoreCase)))
                    continue;
                if (TryFindDate(line.Text, out var dated))
                    return dated;
            }

            return null;
        }

        private static bool TryFindDate(string text, out DateTime date)
        {
            date = default;
            foreach (Match match in Regex.Matches(text, @"\b\d{1,2}[./-]\d{1,2}[./-]\d{2,4}\b"))
            {
                if (NumericDateBox.TryParseCell(match.Value, out date))
                    return true;
            }

            return false;
        }

        private static string FirstGroup(string blob, params string[] patterns)
        {
            foreach (string pattern in patterns)
            {
                var match = Regex.Match(blob, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;
                string value = match.Groups[1].Value.Trim().TrimEnd('.', ',');
                if (value.Length > 0 && !IsNoise(value))
                    return value;
            }

            return "";
        }

        private static bool ContainsWord(string hay, string needle)
        {
            if (string.IsNullOrWhiteSpace(hay) || string.IsNullOrWhiteSpace(needle))
                return false;
            if (needle.Contains(' ', StringComparison.Ordinal))
                return hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
            return Regex.IsMatch(hay, @"\b" + Regex.Escape(needle) + @"\b", RegexOptions.IgnoreCase);
        }

        private static bool HasToken(string text, params string[] tokens)
        {
            foreach (string token in tokens)
            {
                if (ContainsWord(text, token))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeHeader(string text) =>
            HasToken(text, "description") && HasToken(text, "item", "code", "product");

        private static bool IsTotalLine(string text)
        {
            return Regex.IsMatch(
                text,
                @"\b(subtotal|total|balance|amount due|tax|gst|vat|freight|shipping|thank you|remit|page)\b",
                RegexOptions.IgnoreCase);
        }

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

        private static bool LooksLikeDateToken(string value) =>
            Regex.IsMatch(value, @"^\d{1,2}[./-]\d{1,2}([./-]\d{2,4})?$");

        private static string StripLeadingCode(string text, string? code)
        {
            if (!string.IsNullOrWhiteSpace(code))
                text = Regex.Replace(text, @"\b" + Regex.Escape(code) + @"\b", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\$?\d[\d,]*\.?\d*", " ");
            return Clean(text);
        }

        private static string GetBucket(Dictionary<string, StringBuilder> buckets, string kind) =>
            buckets.TryGetValue(kind, out var sb) ? Clean(sb.ToString()) : "";

        private static string Qty(string value)
        {
            decimal n = PurchaseLineRow.ParseNumber(value);
            return n == 0 ? "" : n.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Money(string value)
        {
            decimal n = PurchaseLineRow.ParseNumber(value);
            return n == 0 ? "" : n.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Clean(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == '\0' || char.IsControl(c))
                    continue;
                sb.Append(c == '\u00A0' ? ' ' : c);
            }

            return Regex.Replace(sb.ToString(), @"[ \t]+", " ").Trim();
        }

        private readonly record struct PdfWord(string Text, double X, double Y, double Width);
        private readonly record struct PdfLine(int Page, double Y, string Text, IReadOnlyList<PdfWord> Words);
        private sealed record ItemHit(string Code, string Name, string Coo);
    }

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

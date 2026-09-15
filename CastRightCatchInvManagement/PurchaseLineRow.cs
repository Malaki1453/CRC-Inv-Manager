using System.Drawing.Drawing2D;
using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>One product line on New Purchase. Item code fills description, COO, and pack size.</summary>
    internal sealed class PurchaseLineRow : Panel
    {
        public const int RowHeight = 42;

        private readonly TextBox _item;
        private readonly TextBox _description;
        private readonly TextBox _coo;
        private readonly TextBox _packSize;
        private readonly TextBox _cs;
        private readonly TextBox _volume;
        private readonly TextBox _price;
        private readonly Label _totalPerLb;
        private readonly Label _total;
        private readonly Button _remove;
        private bool _filling;
        private decimal _overhead;
        private decimal _freight;
        private decimal _forwarder;
        private decimal _other;
        private LookupSuggest? _itemSuggest;
        private LookupSuggest? _descSuggest;

        public event EventHandler? Changed;
        public event EventHandler? RemoveRequested;

        /// <summary>Build the product-line editors and wire volume/cost recalculation.</summary>
        public PurchaseLineRow()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            Height = RowHeight;
            MinimumSize = new Size(120, RowHeight);
            BackColor = Theme.Paper;
            Theme.EnableDoubleBuffer(this);

            _item = MakeBox();
            _description = MakeBox();
            _coo = MakeBox();
            _packSize = MakeBox();
            _cs = MakeBox();
            _volume = MakeBox();
            _price = MakeBox();
            _totalPerLb = MakeTotal();
            _total = MakeTotal();

            _remove = new Button
            {
                Text = "–",
                TextAlign = ContentAlignment.MiddleCenter,
                TabStop = false
            };
            Theme.StyleOutlineButton(_remove);
            _remove.FlatAppearance.BorderSize = 0;
            _remove.Font = Theme.BodyBold;
            _remove.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);

            Controls.Add(_item);
            Controls.Add(_description);
            Controls.Add(_coo);
            Controls.Add(_packSize);
            Controls.Add(_cs);
            Controls.Add(_volume);
            Controls.Add(_price);
            Controls.Add(_totalPerLb);
            Controls.Add(_total);
            Controls.Add(_remove);

            _packSize.TextChanged += (_, _) => RecalcVolume();
            _cs.TextChanged += (_, _) => RecalcVolume();
            foreach (var box in Fields())
                box.TextChanged += (_, _) => OnFieldChanged();

            Resize += (_, _) => LayoutFields();
            LayoutFields();
        }

        public string ItemCode => _item.Text.Trim();
        public string Description => _description.Text.Trim();

        /// <summary>Put the caret on Item Code so the user can type the next product.</summary>
        public void FocusItem()
        {
            _item.Focus();
        }

        /// <summary>Bind item-code and description lookups so picking a hit fills the rest of the line.</summary>
        public void AttachLookups(Func<IReadOnlyList<LookupSuggest.Hit>> items)
        {
            _itemSuggest?.Dispose();
            _descSuggest?.Dispose();
            _itemSuggest = new LookupSuggest(_item, items, codeFirst: true, ApplyHit);
            _descSuggest = new LookupSuggest(_description, items, codeFirst: false, ApplyHit);
        }

        /// <summary>Apply header per-lb costs to this line and refresh total cost.</summary>
        public void SetSharedCosts(decimal overhead, decimal freight, decimal forwarder, decimal other)
        {
            _overhead = overhead;
            _freight = freight;
            _forwarder = forwarder;
            _other = other;
            RecalcCost();
        }

        /// <summary>Fill code, description, and COO from a lookup pick without firing mid-fill recalcs.</summary>
        private void ApplyHit(LookupSuggest.Hit hit)
        {
            _filling = true;
            try
            {
                // Keep whatever the user typed when the hit has no code.
                if (hit.Code.Length > 0)
                    _item.Text = hit.Code;
                // Name is the catalog description for this item.
                if (hit.Name.Length > 0)
                    _description.Text = hit.Name;
                // Extra on item hits is country of origin.
                if (hit.Extra.Length > 0)
                    _coo.Text = hit.Extra;
            }
            finally
            {
                // TextChanged must run again after the lookup write finishes.
                _filling = false;
            }

            RecalcVolume();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Load this line from a purchases-table row.</summary>
        public void FillFromRecord(Dictionary<string, string> record)
        {
            Fill(
                DataFiles.GetRecord(record, "Item Code"),
                DataFiles.GetRecord(record, "Description"),
                DataFiles.GetRecord(record, "COO"),
                DataFiles.GetRecord(record, "Pack Size"),
                DataFiles.GetRecord(record, "CS"),
                DataFiles.GetRecord(record, "Volume"),
                DataFiles.GetRecord(record, "Price Paid / LB"));
        }

        /// <summary>Load this line from a draft product line.</summary>
        public void FillFromLine(PurchaseLine line)
        {
            Fill(
                line.ItemCode,
                line.Description,
                line.Coo,
                line.PackSize,
                line.Cases,
                line.Volume,
                line.Price);
        }

        /// <summary>Write every editor at once, then recalc cost after the fill flag drops.</summary>
        private void Fill(
            string item,
            string description,
            string coo,
            string pack,
            string cases,
            string volume,
            string price)
        {
            _filling = true;
            try
            {
                _item.Text = item;
                _description.Text = description;
                _coo.Text = coo;
                _packSize.Text = pack;
                _cs.Text = cases;
                _volume.Text = volume;
                _price.Text = price;
            }
            finally
            {
                // Recalc once after all fields are set, not on each TextChanged.
                _filling = false;
            }

            RecalcCost();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Snapshot the current editors, including computed totals.</summary>
        public PurchaseLine GetLine()
        {
            RecalcCost();
            return new PurchaseLine
            {
                ItemCode = _item.Text.Trim(),
                Description = _description.Text.Trim(),
                Coo = _coo.Text.Trim(),
                PackSize = _packSize.Text.Trim(),
                Cases = _cs.Text.Trim(),
                Volume = _volume.Text.Trim(),
                Price = _price.Text.Trim(),
                TotalPerLb = _totalPerLb.Text.Trim(),
                TotalCost = _total.Text.Trim()
            };
        }

        /// <summary>True when any editor on this line has text.</summary>
        public bool HasContent() =>
            Fields().Any(box => !string.IsNullOrWhiteSpace(box.Text));

        /// <summary>Dispose lookup popups with this row.</summary>
        protected override void Dispose(bool disposing)
        {
            // Managed lookup windows must be closed with this row.
            if (disposing)
            {
                _itemSuggest?.Dispose();
                _descSuggest?.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Draw the row border and gold accent used by purchase lines.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.None;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.None;
            using var border = new Pen(Theme.CreamDark, 1);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            using var gold = new SolidBrush(Theme.Gold);
            e.Graphics.FillRectangle(gold, 0, 0, 3, Height);
        }

        /// <summary>Recalc totals when the user edits a field, but not during programmatic fills.</summary>
        private void OnFieldChanged()
        {
            // Fill/ApplyHit writes several boxes; wait until that batch finishes.
            if (_filling)
                return;
            RecalcCost();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Derive volume from pack size × cases when both values are present.</summary>
        private void RecalcVolume()
        {
            // Skip while Fill is writing pack/cases/volume together.
            if (_filling)
                return;

            decimal pack = ParseNumber(_packSize.Text);
            decimal cs = ParseNumber(_cs.Text);
            // Incomplete qty should not overwrite a volume the user already typed.
            if (pack <= 0 || cs <= 0)
            {
                RecalcCost();
                return;
            }

            _filling = true;
            _volume.Text = (pack * cs).ToString("0.##", CultureInfo.InvariantCulture);
            _filling = false;
            RecalcCost();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Total / LB is price plus header costs; Total is that rate times volume.</summary>
        private void RecalcCost()
        {
            decimal perLb = ParseNumber(_price.Text) + _overhead + _freight + _forwarder + _other;
            decimal lbs = ParseNumber(_volume.Text);
            _totalPerLb.Text = perLb.ToString("0.####", CultureInfo.InvariantCulture);
            _total.Text = (perLb * lbs).ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>Place editors in the shared purchase-line column slots.</summary>
        private void LayoutFields()
        {
            var slots = PurchaseLineLayout.Slots(Width);
            _item.Bounds = slots.Item;
            _description.Bounds = slots.Description;
            _coo.Bounds = slots.Coo;
            _packSize.Bounds = slots.Pack;
            _cs.Bounds = slots.Cases;
            _volume.Bounds = slots.Volume;
            _price.Bounds = slots.Price;
            _totalPerLb.Bounds = slots.TotalPerLb;
            _total.Bounds = slots.Total;
            _remove.Bounds = slots.Remove;
        }

        /// <summary>Editable boxes that participate in HasContent and change events.</summary>
        private IEnumerable<TextBox> Fields()
        {
            yield return _item;
            yield return _description;
            yield return _coo;
            yield return _packSize;
            yield return _cs;
            yield return _volume;
            yield return _price;
        }

        /// <summary>Themed single-line editor used by every product field.</summary>
        private static TextBox MakeBox()
        {
            var box = new TextBox();
            Theme.StyleField(box);
            box.Height = 26;
            return box;
        }

        /// <summary>Read-only total cell styled like a grid amount.</summary>
        private static Label MakeTotal()
        {
            return new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.Body,
                ForeColor = Theme.Ink,
                BackColor = Theme.GridAlt,
                Padding = new Padding(6, 0, 4, 0)
            };
        }

        /// <summary>Parse a money or quantity cell, treating blank or junk as zero.</summary>
        public static decimal ParseNumber(string? text)
        {
            // Blank cells are zero so totals can sum mixed empty/filled lines.
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string cleaned = text.Replace("$", "", StringComparison.OrdinalIgnoreCase)
                .Replace("LB", "", StringComparison.OrdinalIgnoreCase)
                .Replace("lbs", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            // Prefer the user's locale (typed values).
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.CurrentCulture, out var value))
                return value;
            // CSV/PDF numbers often use invariant format.
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                return value;
            // Unparseable text should not throw from a grid cell.
            return 0;
        }
    }

    /// <summary>Column rectangles for a purchase product line at a given width.</summary>
    internal static class PurchaseLineLayout
    {
        /// <summary>Compute editor bounds, giving leftover width to Description.</summary>
        public static PurchaseLineSlots Slots(int width)
        {
            int pad = 10;
            int y = 8;
            int h = 26;
            int gap = 6;
            int remove = 28;
            int inner = Math.Max(520, width - pad * 2 - remove - gap);
            int item = 96;
            int coo = 52;
            int pack = 56;
            int cs = 44;
            int vol = 72;
            int price = 72;
            int totLb = 72;
            int total = 78;
            int used = item + coo + pack + cs + vol + price + totLb + total + gap * 8;
            int desc = Math.Max(80, inner - used);

            int x = pad;
            var itemR = new Rectangle(x, y, item, h); x += item + gap;
            var descR = new Rectangle(x, y, desc, h); x += desc + gap;
            var cooR = new Rectangle(x, y, coo, h); x += coo + gap;
            var packR = new Rectangle(x, y, pack, h); x += pack + gap;
            var csR = new Rectangle(x, y, cs, h); x += cs + gap;
            var volR = new Rectangle(x, y, vol, h); x += vol + gap;
            var priceR = new Rectangle(x, y, price, h); x += price + gap;
            var totLbR = new Rectangle(x, y, totLb, h); x += totLb + gap;
            var totalR = new Rectangle(x, y, total, h); x += total + gap;
            var removeR = new Rectangle(x, y, remove, h);
            return new PurchaseLineSlots(itemR, descR, cooR, packR, csR, volR, priceR, totLbR, totalR, removeR);
        }
    }

    /// <summary>Pixel bounds for each editor on a purchase line.</summary>
    internal readonly record struct PurchaseLineSlots(
        Rectangle Item,
        Rectangle Description,
        Rectangle Coo,
        Rectangle Pack,
        Rectangle Cases,
        Rectangle Volume,
        Rectangle Price,
        Rectangle TotalPerLb,
        Rectangle Total,
        Rectangle Remove);

    /// <summary>One product line as saved on a purchase order.</summary>
    internal sealed class PurchaseLine
    {
        public string ItemCode { get; set; } = "";
        public string Description { get; set; } = "";
        public string Coo { get; set; } = "";
        public string PackSize { get; set; } = "";
        public string Cases { get; set; } = "";
        public string Volume { get; set; } = "";
        public string Price { get; set; } = "";
        public string TotalPerLb { get; set; } = "";
        public string TotalCost { get; set; } = "";
    }
}

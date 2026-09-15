using System.Drawing.Drawing2D;
using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>One product line on Create Sales Order. Item code fills description and COO.</summary>
    internal sealed class SalesOrderLineRow : Panel
    {
        public const int RowHeight = 42;

        private string _po = "";
        private readonly TextBox _item;
        private readonly TextBox _lot;
        private readonly TextBox _description;
        private readonly TextBox _coo;
        private readonly TextBox _unitSize;
        private readonly TextBox _cases;
        private readonly TextBox _volume;
        private readonly TextBox _price;
        private readonly Label _amount;
        private readonly Button _remove;
        private bool _filling;
        private LookupSuggest? _itemSuggest;
        private LookupSuggest? _descSuggest;
        private LookupSuggest? _lotSuggest;
        private string _poHitsItem = "\0";
        private List<LookupSuggest.Hit> _poHits = new();

        public event EventHandler? Changed;
        public event EventHandler? RemoveRequested;

        /// <summary>Build the sales-order line editors and wire volume/amount recalculation.</summary>
        public SalesOrderLineRow()
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
            _lot = MakeBox();
            _description = MakeBox();
            _coo = MakeBox();
            _unitSize = MakeBox();
            _cases = MakeBox();
            _volume = MakeBox();
            _price = MakeBox();
            _amount = MakeTotal();

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
            Controls.Add(_lot);
            Controls.Add(_description);
            Controls.Add(_coo);
            Controls.Add(_unitSize);
            Controls.Add(_cases);
            Controls.Add(_volume);
            Controls.Add(_price);
            Controls.Add(_amount);
            Controls.Add(_remove);

            _unitSize.TextChanged += (_, _) => RecalcVolume();
            _cases.TextChanged += (_, _) => RecalcVolume();
            foreach (var box in Fields())
                box.TextChanged += (_, _) => OnFieldChanged();

            Resize += (_, _) => LayoutFields();
            LayoutFields();
        }

        public string ItemCode => _item.Text.Trim();

        /// <summary>Put the caret on Item Code so the user can type the next product.</summary>
        public void FocusItem() => _item.Focus();

        /// <summary>Bind item-code and description lookups so picking a hit fills the rest of the line.</summary>
        public void AttachLookups(Func<IReadOnlyList<LookupSuggest.Hit>> items)
        {
            _itemSuggest?.Dispose();
            _descSuggest?.Dispose();
            _lotSuggest?.Dispose();
            _itemSuggest = new LookupSuggest(_item, items, codeFirst: true, ApplyHit);
            _descSuggest = new LookupSuggest(_description, items, codeFirst: false, ApplyHit);
            // PO # suggestions are purchase POs that already have this item code.
            _lotSuggest = new LookupSuggest(_lot, PoHits, codeFirst: true, ApplyPoHit);
        }

        /// <summary>Purchase POs for the item currently on this line. Cached until the item code changes.</summary>
        private IReadOnlyList<LookupSuggest.Hit> PoHits()
        {
            string item = _item.Text.Trim();
            if (!item.Equals(_poHitsItem, StringComparison.OrdinalIgnoreCase))
            {
                _poHitsItem = item;
                _poHits = item.Length == 0
                    ? new List<LookupSuggest.Hit>()
                    : DataFiles.PurchasePosForItem(item);
            }

            return _poHits;
        }

        /// <summary>Write only the purchase PO # into the PO box, not "PO - vendor".</summary>
        private void ApplyPoHit(LookupSuggest.Hit hit)
        {
            _filling = true;
            try
            {
                _lot.Text = hit.Code;
            }
            finally
            {
                _filling = false;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Remember the customer PO this line came from.</summary>
        public void SetPo(string po) => _po = (po ?? "").Trim();

        /// <summary>Fill code, description, and COO from a lookup pick without firing mid-fill recalcs.</summary>
        private void ApplyHit(LookupSuggest.Hit hit)
        {
            _filling = true;
            try
            {
                // Keep whatever the user typed when the hit has no code.
                if (hit.Code.Length > 0)
                    _item.Text = hit.Code;
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
            _poHitsItem = "\0";
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Load this line from a sales-table row, including its customer PO.</summary>
        public void FillFromRecord(Dictionary<string, string> record)
        {
            string po = DataFiles.SalePo(record);
            // Keep the sale PO so later identity checks can skip duplicates.
            if (po.Length > 0)
                _po = po;
            Fill(
                DataFiles.GetRecord(record, "Item Code"),
                DataFiles.SaleLot(record),
                DataFiles.GetRecord(record, "Description"),
                DataFiles.GetRecord(record, "COO"),
                DataFiles.GetRecord(record, "Pack Size"),
                DataFiles.GetRecord(record, "CS"),
                DataFiles.GetRecord(record, "Volume"),
                DataFiles.GetRecord(record, "Sell Price / LB"));
        }

        /// <summary>Load this line from a draft product line.</summary>
        public void FillFromLine(SalesOrderLine line)
        {
            if (line.PoNumber.Length > 0)
                _po = line.PoNumber;
            Fill(
                line.ItemCode,
                line.LotNumber,
                line.Description,
                line.Coo,
                line.UnitSize,
                line.Cases,
                line.Volume,
                line.Price);
        }

        /// <summary>Write every editor at once, then recalc amount after the fill flag drops.</summary>
        private void Fill(
            string item,
            string lot,
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
                _lot.Text = lot;
                _description.Text = description;
                _coo.Text = coo;
                _unitSize.Text = pack;
                _cases.Text = cases;
                _volume.Text = volume;
                _price.Text = price;
            }
            finally
            {
                // Recalc once after all fields are set, not on each TextChanged.
                _filling = false;
            }

            RecalcAmount();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Snapshot the current editors, including computed amount.</summary>
        public SalesOrderLine GetLine()
        {
            RecalcAmount();
            return new SalesOrderLine
            {
                PoNumber = _po,
                ItemCode = _item.Text.Trim(),
                LotNumber = _lot.Text.Trim(),
                Description = _description.Text.Trim(),
                Coo = _coo.Text.Trim(),
                UnitSize = _unitSize.Text.Trim(),
                Cases = _cases.Text.Trim(),
                Volume = _volume.Text.Trim(),
                Price = _price.Text.Trim(),
                Amount = _amount.Text.Trim()
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
                _lotSuggest?.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Draw the row border and gold accent used by sales-order lines.</summary>
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

        /// <summary>Recalc amount when the user edits a field, but not during programmatic fills.</summary>
        private void OnFieldChanged()
        {
            // Fill/ApplyHit writes several boxes; wait until that batch finishes.
            if (_filling)
                return;
            RecalcAmount();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Derive volume from pack size × cases when volume is still blank.</summary>
        private void RecalcVolume()
        {
            // Skip while Fill is writing pack/cases/volume together.
            if (_filling)
                return;

            decimal pack = ParseNumber(_unitSize.Text);
            decimal cs = ParseNumber(_cases.Text);
            // Incomplete qty should not overwrite a volume the user already typed.
            if (pack <= 0 || cs <= 0)
            {
                RecalcAmount();
                return;
            }

            _filling = true;
            // Keep a volume the user entered by hand.
            if (string.IsNullOrWhiteSpace(_volume.Text))
                _volume.Text = (pack * cs).ToString("0.##", CultureInfo.InvariantCulture);
            _filling = false;
            RecalcAmount();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Amount is volume times sell price / lb.</summary>
        private void RecalcAmount()
        {
            decimal amount = ParseNumber(_volume.Text) * ParseNumber(_price.Text);
            _amount.Text = amount.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>Place editors in the shared sales-order line column slots.</summary>
        private void LayoutFields()
        {
            var slots = SalesOrderLineLayout.Slots(Width);
            _item.Bounds = slots.Item;
            _lot.Bounds = slots.Lot;
            _description.Bounds = slots.Description;
            _coo.Bounds = slots.Coo;
            _unitSize.Bounds = slots.UnitSize;
            _cases.Bounds = slots.Cases;
            _volume.Bounds = slots.Volume;
            _price.Bounds = slots.Price;
            _amount.Bounds = slots.Amount;
            _remove.Bounds = slots.Remove;
        }

        /// <summary>Editable boxes that participate in HasContent and change events.</summary>
        private IEnumerable<TextBox> Fields()
        {
            yield return _item;
            yield return _lot;
            yield return _description;
            yield return _coo;
            yield return _unitSize;
            yield return _cases;
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

        /// <summary>Read-only amount cell styled like a grid total.</summary>
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
        public static decimal ParseNumber(string? text) => PurchaseLineRow.ParseNumber(text);
    }

    /// <summary>Column rectangles for a sales-order product line at a given width.</summary>
    internal static class SalesOrderLineLayout
    {
        /// <summary>Compute editor bounds, giving leftover width to Description.</summary>
        public static SalesOrderLineSlots Slots(int width)
        {
            int pad = 10;
            int y = 8;
            int h = 26;
            int gap = 6;
            int remove = 28;
            int inner = Math.Max(520, width - pad * 2 - remove - gap);
            int item = 88;
            int lot = 88;
            int coo = 48;
            int pack = 52;
            int cs = 44;
            int vol = 64;
            int price = 68;
            int amount = 72;
            int used = item + lot + coo + pack + cs + vol + price + amount + gap * 8;
            int desc = Math.Max(80, inner - used);

            int x = pad;
            var itemR = new Rectangle(x, y, item, h); x += item + gap;
            var lotR = new Rectangle(x, y, lot, h); x += lot + gap;
            var descR = new Rectangle(x, y, desc, h); x += desc + gap;
            var cooR = new Rectangle(x, y, coo, h); x += coo + gap;
            var unitR = new Rectangle(x, y, pack, h); x += pack + gap;
            var casesR = new Rectangle(x, y, cs, h); x += cs + gap;
            var volumeR = new Rectangle(x, y, vol, h); x += vol + gap;
            var priceR = new Rectangle(x, y, price, h); x += price + gap;
            var amountR = new Rectangle(x, y, amount, h); x += amount + gap;
            var removeR = new Rectangle(x, y, remove, h);
            return new SalesOrderLineSlots(itemR, lotR, descR, cooR, unitR, casesR, volumeR, priceR, amountR, removeR);
        }
    }

    /// <summary>Pixel bounds for each editor on a sales-order line.</summary>
    internal readonly record struct SalesOrderLineSlots(
        Rectangle Item,
        Rectangle Lot,
        Rectangle Description,
        Rectangle Coo,
        Rectangle UnitSize,
        Rectangle Cases,
        Rectangle Volume,
        Rectangle Price,
        Rectangle Amount,
        Rectangle Remove);

    /// <summary>One product line as saved on a sales order.</summary>
    internal sealed class SalesOrderLine
    {
        public string ItemCode { get; set; } = "";
        public string LotNumber { get; set; } = "";
        public string Description { get; set; } = "";
        public string Coo { get; set; } = "";
        public string UnitSize { get; set; } = "";
        public string Cases { get; set; } = "";
        public string Volume { get; set; } = "";
        public string Price { get; set; } = "";
        public string Amount { get; set; } = "";
        public string PoNumber { get; set; } = "";
    }

    /// <summary>Full sales-order payload used to save rows and draw the pick-ticket PDF.</summary>
    internal sealed class SalesOrderDraft
    {
        public string SoNumber { get; set; } = "";
        public DateTime OrderDate { get; set; } = DateTime.Today;
        public DateTime ReleaseDate { get; set; } = DateTime.Today;
        public DateTime DueDate { get; set; } = DateTime.Today;
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string Address { get; set; } = "";
        public string CustomerPhone { get; set; } = "";
        public string Contact { get; set; } = "";
        public string Email { get; set; } = "";
        public string ContactPhone { get; set; } = "";
        public string Warehouse { get; set; } = "";
        public string CustomerPo { get; set; } = "";
        public string Terms { get; set; } = "";
        public string Status { get; set; } = "";
        public string FreightCompany { get; set; } = "";
        public string FreightTerms { get; set; } = "";
        public List<SalesOrderLine> Lines { get; set; } = new();

        public decimal TotalCases => Lines.Sum(line => InvoiceLineRow.ParseNumber(line.Cases));
        public decimal TotalVolume => Lines.Sum(line => InvoiceLineRow.ParseNumber(line.Volume));
        public decimal TotalAmount => Lines.Sum(line => InvoiceLineRow.ParseNumber(line.Amount));
    }
}

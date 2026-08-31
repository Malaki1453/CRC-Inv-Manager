using System.Drawing.Drawing2D;
using System.Globalization;

namespace CastRightCatchInvManagement
{
    internal sealed class InvoiceLineRow : Panel
    {
        public const int RowHeight = 42;

        private readonly TextBox _po;
        private readonly TextBox _product;
        private readonly TextBox _lot;
        private readonly TextBox _ordered;
        private readonly TextBox _shipped;
        private readonly TextBox _description;
        private readonly TextBox _weight;
        private readonly TextBox _price;
        private readonly Label _amount;
        private readonly Button _remove;
        private bool _locked;
        private bool _filling;

        public event EventHandler? Changed;
        public event EventHandler? RemoveRequested;
        public event EventHandler<string>? PoRequested;

        public InvoiceLineRow()
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
            Cursor = Cursors.Default;
            Theme.EnableDoubleBuffer(this);

            _po = MakeBox();
            _product = MakeBox();
            _lot = MakeBox();
            _ordered = MakeBox();
            _shipped = MakeBox();
            _description = MakeBox();
            _weight = MakeBox();
            _price = MakeBox();
            _amount = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.Body,
                ForeColor = Theme.Ink,
                BackColor = Theme.GridAlt,
                Padding = new Padding(6, 0, 4, 0)
            };

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

            Controls.Add(_po);
            Controls.Add(_product);
            Controls.Add(_lot);
            Controls.Add(_ordered);
            Controls.Add(_shipped);
            Controls.Add(_description);
            Controls.Add(_weight);
            Controls.Add(_price);
            Controls.Add(_amount);
            Controls.Add(_remove);

            _po.Leave += (_, _) => RequestPoFill();
            _po.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;
                e.SuppressKeyPress = true;
                RequestPoFill();
            };

            foreach (var box in Fields())
                box.TextChanged += (_, _) => OnFieldChanged();

            Resize += (_, _) => LayoutFields();
            LayoutFields();
        }

        public bool Locked => _locked;

        public void SetPoSuggestions(AutoCompleteStringCollection source)
        {
            _po.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _po.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _po.AutoCompleteCustomSource = source;
        }

        public void Lock()
        {
            if (_locked)
                return;

            _locked = true;
            foreach (var box in Fields())
            {
                box.Visible = false;
                box.Enabled = false;
                box.TabStop = false;
                box.ReadOnly = true;
            }

            _amount.Visible = false;
            BackColor = Theme.GridAlt;
            Cursor = Cursors.Default;
            TabStop = false;
            Refresh();
        }

        public void FocusPo()
        {
            if (!_locked)
                _po.Focus();
        }

        public void FillFromRecord(Dictionary<string, string> record)
        {
            if (_locked || record.Count == 0)
                return;

            _filling = true;
            try
            {
                ApplyRecord(record);
            }
            finally
            {
                _filling = false;
            }

            RecalcAmount();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public InvoiceLine GetLine()
        {
            return new InvoiceLine
            {
                PoNumber = _po.Text.Trim(),
                ProductId = _product.Text.Trim(),
                LotNumber = _lot.Text.Trim(),
                Ordered = _ordered.Text.Trim(),
                Shipped = _shipped.Text.Trim(),
                Description = _description.Text.Trim(),
                Weight = _weight.Text.Trim(),
                Price = _price.Text.Trim(),
                Amount = ParseNumber(_amount.Text)
            };
        }

        public bool HasContent()
        {
            return Fields().Any(box => !string.IsNullOrWhiteSpace(box.Text));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_locked)
            {
                _remove.Focus();
                return;
            }

            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.None;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.None;
            using var border = new Pen(_locked ? Theme.GridLine : Theme.CreamDark, 1);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            using var gold = new SolidBrush(_locked ? Theme.GoldLight : Theme.Gold);
            e.Graphics.FillRectangle(gold, 0, 0, 3, Height);

            if (!_locked)
                return;

            var slots = InvoiceLineLayout.Slots(Width);
            DrawLocked(e.Graphics, _po.Text, slots.Po);
            DrawLocked(e.Graphics, _product.Text, slots.Product);
            DrawLocked(e.Graphics, _lot.Text, slots.Lot);
            DrawLocked(e.Graphics, _ordered.Text, slots.Ordered);
            DrawLocked(e.Graphics, _shipped.Text, slots.Shipped);
            DrawLocked(e.Graphics, _description.Text, slots.Description);
            DrawLocked(e.Graphics, _weight.Text, slots.Weight);
            DrawLocked(e.Graphics, _price.Text, slots.Price);
            DrawLocked(e.Graphics, _amount.Text, slots.Amount);
        }

        private static void DrawLocked(Graphics g, string text, Rectangle slot)
        {
            TextRenderer.DrawText(
                g,
                text,
                Theme.Body,
                slot,
                Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void RequestPoFill()
        {
            if (_locked || _filling)
                return;

            string po = _po.Text.Trim();
            if (po.Length == 0)
                return;

            PoRequested?.Invoke(this, po);
        }

        private void ApplyRecord(Dictionary<string, string> record)
        {
            string item = DataFiles.GetRecord(record, "Item Code");
            string description = DataFiles.GetRecord(record, "Description");
            string pack = DataFiles.GetRecord(record, "Pack Size");
            string coo = DataFiles.GetRecord(record, "COO");
            string cs = DataFiles.GetRecord(record, "CS");
            string received = DataFiles.GetRecord(record, "Volume Received");
            string volume = DataFiles.GetRecord(record, "Volume");
            string po = DataFiles.SalePo(record);
            if (po.Length == 0)
                po = DataFiles.GetRecord(record, "PO #").Trim();
            string lot = DataFiles.SaleLot(record);

            if (po.Length > 0)
                _po.Text = po;
            if (item.Length > 0)
                _product.Text = item;
            if (lot.Length > 0)
                _lot.Text = lot;
            if (cs.Length > 0)
            {
                _ordered.Text = cs;
                _shipped.Text = cs;
            }

            var parts = new List<string>();
            if (description.Length > 0)
                parts.Add(description);
            if (pack.Length > 0)
                parts.Add(pack);
            if (coo.Length > 0)
                parts.Add(coo);
            if (parts.Count > 0)
                _description.Text = string.Join("  ·  ", parts);

            string weight = received.Length > 0 ? received : volume;
            if (weight.Length > 0)
                _weight.Text = weight;

            string sell = DataFiles.GetRecordAny(record, "Sell Price / LB", "Price LB", "Price / LB Sold");
            if (sell.Length > 0)
                _price.Text = sell;
        }

        private void OnFieldChanged()
        {
            if (_filling)
                return;
            RecalcAmount();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void RecalcAmount()
        {
            decimal weight = ParseNumber(_weight.Text);
            decimal price = ParseNumber(_price.Text);
            string text = (weight * price).ToString("0.00", CultureInfo.InvariantCulture);
            if (_amount.Text == text)
                return;
            _filling = true;
            _amount.Text = text;
            _filling = false;
        }

        private void LayoutFields()
        {
            var slots = InvoiceLineLayout.Slots(Width);
            _po.Bounds = slots.Po;
            _product.Bounds = slots.Product;
            _lot.Bounds = slots.Lot;
            _ordered.Bounds = slots.Ordered;
            _shipped.Bounds = slots.Shipped;
            _description.Bounds = slots.Description;
            _weight.Bounds = slots.Weight;
            _price.Bounds = slots.Price;
            _amount.Bounds = slots.Amount;
            _remove.Bounds = slots.Remove;
        }

        private IEnumerable<TextBox> Fields()
        {
            yield return _po;
            yield return _product;
            yield return _lot;
            yield return _ordered;
            yield return _shipped;
            yield return _description;
            yield return _weight;
            yield return _price;
        }

        private static TextBox MakeBox()
        {
            var box = new FlatTextBox();
            Theme.StyleField(box);
            box.BorderStyle = BorderStyle.None;
            box.Height = 26;
            return box;
        }

        public static decimal ParseNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string cleaned = text.Replace("$", "", StringComparison.OrdinalIgnoreCase)
                .Replace("LB", "", StringComparison.OrdinalIgnoreCase)
                .Replace("lbs", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.CurrentCulture, out var value))
                return value;
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                return value;
            return 0;
        }
    }

    internal sealed class FlatTextBox : TextBox
    {
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle &= ~0x00000200;
                cp.Style &= ~0x00800000;
                return cp;
            }
        }
    }

    internal static class InvoiceLineLayout
    {
        public static InvoiceLineSlots Slots(int width)
        {
            int pad = 10;
            int y = 8;
            int h = 26;
            int gap = 6;
            int remove = 28;
            int inner = Math.Max(400, width - pad * 2 - remove - gap);
            int po = 130;
            int product = 72;
            int lot = 128;
            int qty = 52;
            int weight = 64;
            int price = 64;
            int amount = 74;
            int used = po + product + lot + qty + qty + weight + price + amount + gap * 7;
            int desc = Math.Max(80, inner - used);

            int x = pad;
            var poR = new Rectangle(x, y, po, h); x += po + gap;
            var productR = new Rectangle(x, y, product, h); x += product + gap;
            var lotR = new Rectangle(x, y, lot, h); x += lot + gap;
            var orderedR = new Rectangle(x, y, qty, h); x += qty + gap;
            var shippedR = new Rectangle(x, y, qty, h); x += qty + gap;
            var descR = new Rectangle(x, y, desc, h); x += desc + gap;
            var weightR = new Rectangle(x, y, weight, h); x += weight + gap;
            var priceR = new Rectangle(x, y, price, h); x += price + gap;
            var amountR = new Rectangle(x, y, amount, h); x += amount + gap;
            var removeR = new Rectangle(x, y, remove, h);
            return new InvoiceLineSlots(poR, productR, lotR, orderedR, shippedR, descR, weightR, priceR, amountR, removeR);
        }
    }

    internal readonly record struct InvoiceLineSlots(
        Rectangle Po,
        Rectangle Product,
        Rectangle Lot,
        Rectangle Ordered,
        Rectangle Shipped,
        Rectangle Description,
        Rectangle Weight,
        Rectangle Price,
        Rectangle Amount,
        Rectangle Remove);

    internal sealed class InvoiceLine
    {
        public string PoNumber { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string LotNumber { get; set; } = "";
        public string Ordered { get; set; } = "";
        public string Shipped { get; set; } = "";
        public string Description { get; set; } = "";
        public string Weight { get; set; } = "";
        public string Price { get; set; } = "";
        public decimal Amount { get; set; }
    }

    internal sealed class InvoiceDraft
    {
        public string InvoiceNumber { get; set; } = "";
        public DateTime InvoiceDate { get; set; } = DateTime.Today;
        public string SoNumber { get; set; } = "";
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string Terms { get; set; } = "";
        public string ShipVia { get; set; } = "";
        public string SalesRep { get; set; } = "";
        public DateTime ShipDate { get; set; } = DateTime.Today;
        public string SoldTo { get; set; } = "";
        public string ShipTo { get; set; } = "";
        public decimal Discount { get; set; }
        public decimal Freight { get; set; }
        public decimal Tax { get; set; }
        public List<InvoiceLine> Lines { get; set; } = new();

        public decimal TotalWeight => Lines.Sum(line => InvoiceLineRow.ParseNumber(line.Weight));
        public decimal SubTotal => Lines.Sum(line => line.Amount);
        public decimal InvoiceTotal => SubTotal - Discount + Freight + Tax;
    }
}

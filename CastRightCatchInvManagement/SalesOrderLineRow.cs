using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    internal sealed class SalesOrderLineRow : Panel
    {
        public const int RowHeight = 42;

        private string _po = "";
        private readonly TextBox _item;
        private readonly TextBox _lot;
        private readonly TextBox _description;
        private readonly TextBox _unitSize;
        private readonly TextBox _cases;
        private readonly TextBox _volume;
        private readonly Button _remove;
        private bool _locked;
        private bool _filling;

        public event EventHandler? Changed;
        public event EventHandler? RemoveRequested;

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
            _unitSize = MakeBox();
            _cases = MakeBox();
            _volume = MakeBox();

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
            Controls.Add(_unitSize);
            Controls.Add(_cases);
            Controls.Add(_volume);
            Controls.Add(_remove);

            foreach (var box in Fields())
                box.TextChanged += (_, _) => OnFieldChanged();

            Resize += (_, _) => LayoutFields();
            LayoutFields();
        }

        public bool Locked => _locked;

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

            BackColor = Theme.GridAlt;
            TabStop = false;
            Refresh();
        }

        public void FocusItem()
        {
            if (!_locked)
                _item.Focus();
        }

        public void FillFromRecord(Dictionary<string, string> record)
        {
            if (_locked || record.Count == 0)
                return;

            _filling = true;
            try
            {
                string po = DataFiles.SalePo(record);
                if (po.Length > 0)
                    _po = po;

                string item = DataFiles.GetRecord(record, "Item Code");
                string lot = DataFiles.SaleLot(record);
                string description = DataFiles.GetRecord(record, "Description");
                string unit = DataFiles.GetRecord(record, "Pack Size");
                string cases = DataFiles.GetRecord(record, "CS");
                string volume = DataFiles.GetRecord(record, "Volume");

                if (item.Length > 0)
                    _item.Text = item;
                if (lot.Length > 0)
                    _lot.Text = lot;
                if (description.Length > 0)
                    _description.Text = description;
                if (unit.Length > 0)
                    _unitSize.Text = unit;
                if (cases.Length > 0)
                    _cases.Text = cases;
                if (volume.Length > 0)
                    _volume.Text = volume;
            }
            finally
            {
                _filling = false;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        public SalesOrderLine GetLine()
        {
            return new SalesOrderLine
            {
                PoNumber = _po,
                ItemCode = _item.Text.Trim(),
                LotNumber = _lot.Text.Trim(),
                Description = _description.Text.Trim(),
                UnitSize = _unitSize.Text.Trim(),
                Cases = _cases.Text.Trim(),
                Volume = _volume.Text.Trim()
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

            var slots = SalesOrderLineLayout.Slots(Width);
            DrawLocked(e.Graphics, _item.Text, slots.Item);
            DrawLocked(e.Graphics, _lot.Text, slots.Lot);
            DrawLocked(e.Graphics, _description.Text, slots.Description);
            DrawLocked(e.Graphics, _unitSize.Text, slots.UnitSize);
            DrawLocked(e.Graphics, _cases.Text, slots.Cases);
            DrawLocked(e.Graphics, _volume.Text, slots.Volume);
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

        private void OnFieldChanged()
        {
            if (_filling)
                return;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void LayoutFields()
        {
            var slots = SalesOrderLineLayout.Slots(Width);
            _item.Bounds = slots.Item;
            _lot.Bounds = slots.Lot;
            _description.Bounds = slots.Description;
            _unitSize.Bounds = slots.UnitSize;
            _cases.Bounds = slots.Cases;
            _volume.Bounds = slots.Volume;
            _remove.Bounds = slots.Remove;
        }

        private IEnumerable<TextBox> Fields()
        {
            yield return _item;
            yield return _lot;
            yield return _description;
            yield return _unitSize;
            yield return _cases;
            yield return _volume;
        }

        private static TextBox MakeBox()
        {
            var box = new FlatTextBox();
            Theme.StyleField(box);
            box.BorderStyle = BorderStyle.None;
            box.Height = 26;
            return box;
        }
    }

    internal static class SalesOrderLineLayout
    {
        public static SalesOrderLineSlots Slots(int width)
        {
            int pad = 10;
            int y = 8;
            int h = 26;
            int gap = 6;
            int remove = 28;
            int inner = Math.Max(400, width - pad * 2 - remove - gap);
            int item = 90;
            int lot = 120;
            int unit = 80;
            int cases = 70;
            int volume = 90;
            int used = item + lot + unit + cases + volume + gap * 4;
            int desc = Math.Max(80, inner - used);

            int x = pad;
            var itemR = new Rectangle(x, y, item, h); x += item + gap;
            var lotR = new Rectangle(x, y, lot, h); x += lot + gap;
            var descR = new Rectangle(x, y, desc, h); x += desc + gap;
            var unitR = new Rectangle(x, y, unit, h); x += unit + gap;
            var casesR = new Rectangle(x, y, cases, h); x += cases + gap;
            var volumeR = new Rectangle(x, y, volume, h); x += volume + gap;
            var removeR = new Rectangle(x, y, remove, h);
            return new SalesOrderLineSlots(itemR, lotR, descR, unitR, casesR, volumeR, removeR);
        }
    }

    internal readonly record struct SalesOrderLineSlots(
        Rectangle Item,
        Rectangle Lot,
        Rectangle Description,
        Rectangle UnitSize,
        Rectangle Cases,
        Rectangle Volume,
        Rectangle Remove);

    internal sealed class SalesOrderLine
    {
        public string ItemCode { get; set; } = "";
        public string LotNumber { get; set; } = "";
        public string Description { get; set; } = "";
        public string UnitSize { get; set; } = "";
        public string Cases { get; set; } = "";
        public string Volume { get; set; } = "";
        public string PoNumber { get; set; } = "";
    }

    internal sealed class SalesOrderDraft
    {
        public string SoNumber { get; set; } = "";
        public DateTime OrderDate { get; set; } = DateTime.Today;
        public DateTime ReleaseDate { get; set; } = DateTime.Today;
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string Address { get; set; } = "";
        public string CustomerPhone { get; set; } = "";
        public string Contact { get; set; } = "";
        public string Email { get; set; } = "";
        public string ContactPhone { get; set; } = "";
        public string Warehouse { get; set; } = "";
        public string CustomerPo { get; set; } = "";
        public string FreightCompany { get; set; } = "";
        public string FreightTerms { get; set; } = "";
        public List<SalesOrderLine> Lines { get; set; } = new();

        public decimal TotalCases => Lines.Sum(line => InvoiceLineRow.ParseNumber(line.Cases));
        public decimal TotalVolume => Lines.Sum(line => InvoiceLineRow.ParseNumber(line.Volume));
    }
}

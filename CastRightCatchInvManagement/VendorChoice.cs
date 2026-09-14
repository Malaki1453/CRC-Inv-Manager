namespace CastRightCatchInvManagement
{
    /// <summary>Vendor row in a combo (freight company) or a lookup hit.</summary>
    internal sealed class VendorChoice
    {
        public string Code { get; }
        public string Name { get; }
        public string Terms { get; }
        public string Display => Name.Length > 0 ? Name : Code;

        public VendorChoice(string code, string name, string terms = "")
        {
            Code = (code ?? "").Trim();
            Name = (name ?? "").Trim();
            Terms = (terms ?? "").Trim();
        }

        public override string ToString() => Display;

        public bool Matches(string? value)
        {
            value = (value ?? "").Trim();
            return value.Length > 0 &&
                   (Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                    Code.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                    Display.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        public static string TextOf(ComboBox box)
        {
            if (box.SelectedItem is VendorChoice choice)
                return choice.Display;
            return (box.Text ?? "").Trim();
        }

        public static void Fill(ComboBox box, string? selected = null)
        {
            string keep = selected ?? TextOf(box);
            box.BeginUpdate();
            try
            {
                box.Items.Clear();
                foreach (var record in DataFiles.VisibleRecords(DataFiles.Vendors))
                {
                    var vendor = new VendorChoice(
                        DataFiles.GetRecord(record, "Code"),
                        DataFiles.GetRecordAny(record, "Name", "Company"),
                        DataFiles.GetRecord(record, "Terms"));
                    if (vendor.Display.Length == 0)
                        continue;
                    box.Items.Add(vendor);
                }
            }
            finally
            {
                box.EndUpdate();
            }

            Select(box, keep);
        }

        public static void Select(ComboBox box, string? value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0)
            {
                box.SelectedIndex = -1;
                box.Text = "";
                return;
            }

            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is VendorChoice choice && choice.Matches(value))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            box.SelectedIndex = -1;
            box.Text = value;
        }
    }
}

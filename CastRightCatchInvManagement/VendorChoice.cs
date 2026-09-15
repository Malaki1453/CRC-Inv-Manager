namespace CastRightCatchInvManagement
{
    /// <summary>Vendor row in a combo (freight company) or a lookup hit.</summary>
    internal sealed class VendorChoice
    {
        public string Code { get; }
        public string Name { get; }
        public string Terms { get; }
        public string Display => Name.Length > 0 ? Name : Code;

        /// <summary>Store trimmed code, display name, and payment terms for a combo item.</summary>
        public VendorChoice(string code, string name, string terms = "")
        {
            Code = (code ?? "").Trim();
            Name = (name ?? "").Trim();
            Terms = (terms ?? "").Trim();
        }

        /// <summary>Combo boxes show the vendor name, falling back to the code.</summary>
        public override string ToString() => Display;

        /// <summary>True when the typed value is this vendor's name, code, or display text.</summary>
        public bool Matches(string? value)
        {
            value = (value ?? "").Trim();
            return value.Length > 0 &&
                   (Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                    Code.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                    Display.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Selected vendor display text, or the free-typed combo text.</summary>
        public static string TextOf(ComboBox box)
        {
            // Prefer the bound choice so a typed fragment does not replace the selected vendor.
            if (box.SelectedItem is VendorChoice choice)
                return choice.Display;
            return (box.Text ?? "").Trim();
        }

        /// <summary>Reload live vendors into the combo and restore the previous selection.</summary>
        public static void Fill(ComboBox box, string? selected = null)
        {
            string keep = selected ?? TextOf(box);
            box.BeginUpdate();
            // Batch combo updates so the dropdown does not flicker per vendor.
            try
            {
                box.Items.Clear();
                foreach (var record in DataFiles.VisibleRecords(DataFiles.Vendors))
                {
                    var vendor = new VendorChoice(
                        DataFiles.GetRecord(record, "Code"),
                        DataFiles.GetRecordAny(record, "Name", "Company"),
                        DataFiles.GetRecord(record, "Terms"));
                    // Skip blank rows so the dropdown never shows empty items.
                    if (vendor.Display.Length == 0)
                        continue;
                    box.Items.Add(vendor);
                }
            }
            finally
            {
                // Always end the batch even if a vendor row throws while filling.
                box.EndUpdate();
            }

            Select(box, keep);
        }

        /// <summary>Select a matching vendor, or keep the typed value when none match.</summary>
        public static void Select(ComboBox box, string? value)
        {
            value = (value ?? "").Trim();
            // Empty selection means no freight vendor on this document.
            if (value.Length == 0)
            {
                box.SelectedIndex = -1;
                box.Text = "";
                return;
            }

            for (int i = 0; i < box.Items.Count; i++)
            {
                // Restore the previous vendor by name or code after a reload.
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

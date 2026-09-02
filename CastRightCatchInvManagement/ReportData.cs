using System.Globalization;

namespace CastRightCatchInvManagement
{
    internal enum ReportKind
    {
        Aging,
        Commission,
        ProfitLoss,
        Suppliers,
        CustomerRisk,
        Species
    }

    /// <summary>One built report: title, summary chips, and a table for the current database view.</summary>
    internal sealed class ReportResult
    {
        public required string Title { get; init; }
        public required string Hint { get; init; }
        public List<(string Label, string Value)> Stats { get; } = new();
        public required string[] Columns { get; init; }
        public List<string[]> Rows { get; } = new();
        public string Empty { get; init; } = "No rows for this report in the current view.";
    }

    /// <summary>
    /// Builds the six Reports page tables from sales, invoices, purchases, and lookups.
    /// Honors Current vs Old the same way the grids do.
    /// </summary>
    internal static class ReportData
    {
        public static string ScopeHint() =>
            AppState.ViewingOldInventory
                ? "All inventory (archive and live)"
                : "This term (live database)";

        public static ReportResult Build(ReportKind kind) =>
            kind switch
            {
                ReportKind.Aging => Aging(),
                ReportKind.Commission => Commission(),
                ReportKind.ProfitLoss => ProfitLoss(),
                ReportKind.Suppliers => Suppliers(),
                ReportKind.CustomerRisk => CustomerRisk(),
                ReportKind.Species => Species(),
                _ => Aging()
            };

        private static ReportResult Aging()
        {
            var result = new ReportResult
            {
                Title = "Aging Report",
                Hint = "Open invoices grouped by how long they are past due.",
                Columns = new[]
                {
                    "Invoice #", "Customer", "Due Date", "Amount", "Outstanding", "Days past due", "Bucket"
                },
                Empty = TableAccess.Can(TableAccess.Invoices)
                    ? "No open invoices in this view."
                    : "You do not have access to invoices."
            };
            if (!TableAccess.Can(TableAccess.Invoices))
                return result;

            decimal current = 0, d30 = 0, d60 = 0, d90 = 0, older = 0, total = 0;
            var rows = new List<(int sort, string[] cells)>();
            foreach (var invoice in DataFiles.ReadRecords(DataFiles.Invoices))
            {
                if (DataFiles.InvoiceIsClosed(invoice))
                    continue;
                decimal due = DataFiles.InvoiceOutstanding(invoice);
                if (due <= 0)
                    continue;

                int days = DaysPastDue(invoice);
                string bucket = AgingBucket(days);
                decimal amount = DataFiles.ParseMoney(DataFiles.GetRecord(invoice, "Amount"));
                total += due;
                switch (bucket)
                {
                    case "Current": current += due; break;
                    case "1–30": d30 += due; break;
                    case "31–60": d60 += due; break;
                    case "61–90": d90 += due; break;
                    default: older += due; break;
                }

                rows.Add((days, new[]
                {
                    DataFiles.GetRecord(invoice, "Invoice #"),
                    DataFiles.GetRecordAny(invoice, "Customer", "Customer Code"),
                    DataFiles.GetRecord(invoice, "Due Date"),
                    Money(amount),
                    Money(due),
                    days == int.MinValue ? "—" : days.ToString(CultureInfo.InvariantCulture),
                    bucket
                }));
            }

            foreach (var row in rows.OrderByDescending(r => r.sort))
                result.Rows.Add(row.cells);

            result.Stats.Add(("Open", Money(total)));
            result.Stats.Add(("Current", Money(current)));
            result.Stats.Add(("1–30 days", Money(d30)));
            result.Stats.Add(("31–60 / 61–90 / 90+", $"{Money(d60)}  ·  {Money(d90)}  ·  {Money(older)}"));
            return result;
        }

        private static ReportResult Commission()
        {
            var result = new ReportResult
            {
                Title = "Commission Tracker",
                Hint = "Deals in this view. No commission rate is stored, so this is sale volume by PO / SO.",
                Columns = new[] { "Deal", "Customer", "Ship Date", "SO #", "Lines", "Amount" },
                Empty = TableAccess.Can(TableAccess.Sales)
                    ? "No sales in this view."
                    : "You do not have access to sales."
            };
            if (!TableAccess.Can(TableAccess.Sales))
                return result;

            var deals = new Dictionary<string, Deal>(StringComparer.OrdinalIgnoreCase);
            foreach (var sale in DataFiles.ReadRecords(DataFiles.Sales))
            {
                string po = DataFiles.SalePo(sale);
                string so = DataFiles.GetRecord(sale, "SO #").Trim();
                string key = po.Length > 0 ? po : (so.Length > 0 ? so : "row:" + deals.Count);
                if (!deals.TryGetValue(key, out var deal))
                {
                    deal = new Deal { Key = po.Length > 0 ? po : so };
                    deals[key] = deal;
                }

                deal.Lines++;
                deal.Amount += DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Amount"));
                if (deal.Customer.Length == 0)
                    deal.Customer = DataFiles.GetRecordAny(sale, "Customer", "Customer Code");
                if (deal.So.Length == 0)
                    deal.So = so;
                string ship = DataFiles.GetRecord(sale, "Ship Date");
                if (DateTime.TryParse(ship, out var date) && date > deal.Ship)
                    deal.Ship = date;
            }

            decimal total = 0;
            foreach (var deal in deals.Values.OrderByDescending(d => d.Amount))
            {
                total += deal.Amount;
                result.Rows.Add(new[]
                {
                    deal.Key.Length > 0 ? deal.Key : "—",
                    deal.Customer,
                    deal.Ship == DateTime.MinValue ? "" : deal.Ship.ToString("yyyy-MM-dd"),
                    deal.So,
                    deal.Lines.ToString(CultureInfo.InvariantCulture),
                    Money(deal.Amount)
                });
            }

            result.Stats.Add(("Deals", deals.Count.ToString("N0")));
            result.Stats.Add(("Sale volume", Money(total)));
            result.Stats.Add(("Commission", "Not tracked"));
            result.Stats.Add(("Scope", ScopeHint()));
            return result;
        }

        private static ReportResult ProfitLoss()
        {
            var result = new ReportResult
            {
                Title = "Monthly P&L",
                Hint = "Sale revenue vs lot cost (purchase cost / lb × sold pounds), by ship month.",
                Columns = new[] { "Month", "Sales", "Revenue", "COGS", "Gross profit", "Margin" },
                Empty = TableAccess.Can(TableAccess.Sales)
                    ? "No sales in this view."
                    : "You do not have access to sales."
            };
            if (!TableAccess.Can(TableAccess.Sales))
                return result;

            var purchases = PurchaseIndex();
            var months = new Dictionary<string, Month>(StringComparer.OrdinalIgnoreCase);
            foreach (var sale in DataFiles.ReadRecords(DataFiles.Sales))
            {
                string month = MonthKey(DataFiles.GetRecord(sale, "Ship Date"));
                if (!months.TryGetValue(month, out var row))
                {
                    row = new Month { Key = month };
                    months[month] = row;
                }

                row.Sales++;
                row.Revenue += DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Amount"));
                row.Cogs += SaleCogs(sale, purchases);
            }

            decimal revenue = 0, cogs = 0;
            foreach (var month in months.Values.OrderBy(m => m.Key))
            {
                revenue += month.Revenue;
                cogs += month.Cogs;
                decimal profit = month.Revenue - month.Cogs;
                result.Rows.Add(new[]
                {
                    month.Key,
                    month.Sales.ToString("N0"),
                    Money(month.Revenue),
                    Money(month.Cogs),
                    Money(profit),
                    Percent(profit, month.Revenue)
                });
            }

            decimal gross = revenue - cogs;
            result.Stats.Add(("Revenue", Money(revenue)));
            result.Stats.Add(("COGS", Money(cogs)));
            result.Stats.Add(("Gross profit", Money(gross)));
            result.Stats.Add(("Margin", Percent(gross, revenue)));
            return result;
        }

        private static ReportResult Suppliers()
        {
            var result = new ReportResult
            {
                Title = "Supplier Performance",
                Hint = "Purchase volume and cost by vendor in this view.",
                Columns = new[] { "Vendor", "POs", "Volume received", "Total cost", "Avg cost / lb" },
                Empty = TableAccess.Can(TableAccess.Purchases)
                    ? "No purchases in this view."
                    : "You do not have access to purchases."
            };
            if (!TableAccess.Can(TableAccess.Purchases))
                return result;

            var vendors = new Dictionary<string, Supplier>(StringComparer.OrdinalIgnoreCase);
            foreach (var purchase in DataFiles.ReadRecords(DataFiles.PurchaseSales))
            {
                string name = DataFiles.GetRecordAny(purchase, "Vendor", "Vendor Code");
                if (name.Length == 0)
                    name = "Unknown";
                if (!vendors.TryGetValue(name, out var row))
                {
                    row = new Supplier { Name = name };
                    vendors[name] = row;
                }

                string po = DataFiles.GetRecord(purchase, "PO #").Trim();
                if (po.Length > 0)
                    row.Pos.Add(po);
                row.Volume += DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Volume Received"));
                if (row.Volume == 0)
                    row.Volume += DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Volume"));
                row.Cost += DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Total Cost"));
            }

            decimal volume = 0, cost = 0;
            foreach (var vendor in vendors.Values.OrderByDescending(v => v.Cost))
            {
                volume += vendor.Volume;
                cost += vendor.Cost;
                decimal avg = vendor.Volume > 0 ? vendor.Cost / vendor.Volume : 0;
                result.Rows.Add(new[]
                {
                    vendor.Name,
                    vendor.Pos.Count.ToString("N0"),
                    vendor.Volume.ToString("N2"),
                    Money(vendor.Cost),
                    Money(avg)
                });
            }

            result.Stats.Add(("Vendors", vendors.Count.ToString("N0")));
            result.Stats.Add(("Volume", volume.ToString("N2") + " lb"));
            result.Stats.Add(("Total cost", Money(cost)));
            result.Stats.Add(("Scope", ScopeHint()));
            return result;
        }

        private static ReportResult CustomerRisk()
        {
            var result = new ReportResult
            {
                Title = "Customer Risk Report",
                Hint = "Credit limit, open invoices, and overdue balances.",
                Columns = new[]
                {
                    "Customer", "Terms", "Credit limit", "On file", "Open invoices", "Outstanding", "Overdue", "Over limit"
                },
                Empty = TableAccess.Can(TableAccess.Invoices)
                    ? "No customers with invoice activity in this view."
                    : "You do not have access to invoices."
            };
            if (!TableAccess.Can(TableAccess.Invoices))
                return result;

            var customers = new Dictionary<string, Risk>(StringComparer.OrdinalIgnoreCase);
            foreach (var customer in DataFiles.ReadRecords(DataFiles.Customers))
            {
                string code = DataFiles.GetRecord(customer, "Code").Trim();
                string name = DataFiles.GetRecord(customer, "Name").Trim();
                string key = code.Length > 0 ? code : name;
                if (key.Length == 0)
                    continue;
                customers[key] = new Risk
                {
                    Name = name.Length > 0 ? name : code,
                    Terms = DataFiles.GetRecord(customer, "Terms"),
                    Limit = DataFiles.ParseMoney(DataFiles.GetRecord(customer, "Credit Limit")),
                    OnFile = DataFiles.ParseMoney(DataFiles.GetRecord(customer, "Current Balance"))
                };
            }

            foreach (var invoice in DataFiles.ReadRecords(DataFiles.Invoices))
            {
                if (DataFiles.InvoiceIsClosed(invoice))
                    continue;
                decimal due = DataFiles.InvoiceOutstanding(invoice);
                if (due <= 0)
                    continue;

                string code = DataFiles.GetRecordAny(invoice, "Customer Code", "Cust ID");
                string name = DataFiles.GetRecord(invoice, "Customer");
                string key = code.Length > 0 && customers.ContainsKey(code) ? code : name;
                if (key.Length == 0)
                    key = "Unknown";
                if (!customers.TryGetValue(key, out var risk))
                {
                    risk = new Risk { Name = name.Length > 0 ? name : key };
                    customers[key] = risk;
                }

                risk.OpenInvoices++;
                risk.Outstanding += due;
                if (DataFiles.InvoiceIsPastDue(invoice))
                    risk.Overdue += due;
            }

            decimal outstanding = 0, overdue = 0;
            int overLimit = 0;
            var active = customers.Values
                .Where(r => r.OpenInvoices > 0 || r.Outstanding > 0 || r.Overdue > 0)
                .OrderByDescending(r => r.Overdue)
                .ThenByDescending(r => r.Outstanding)
                .ToList();
            foreach (var risk in active)
            {
                outstanding += risk.Outstanding;
                overdue += risk.Overdue;
                bool over = risk.Limit > 0 &&
                            Math.Max(risk.OnFile, risk.Outstanding) > risk.Limit;
                if (over)
                    overLimit++;
                result.Rows.Add(new[]
                {
                    risk.Name,
                    risk.Terms,
                    risk.Limit > 0 ? Money(risk.Limit) : "—",
                    Money(risk.OnFile),
                    risk.OpenInvoices.ToString("N0"),
                    Money(risk.Outstanding),
                    Money(risk.Overdue),
                    over ? "Yes" : ""
                });
            }

            result.Stats.Add(("Customers", active.Count.ToString("N0")));
            result.Stats.Add(("Outstanding", Money(outstanding)));
            result.Stats.Add(("Overdue", Money(overdue)));
            result.Stats.Add(("Over limit", overLimit.ToString("N0")));
            return result;
        }

        private static ReportResult Species()
        {
            var result = new ReportResult
            {
                Title = "Profit Per Species",
                Hint = "Sale revenue and lot cost rolled up by item-code species.",
                Columns = new[] { "Species", "Lines", "Volume", "Revenue", "COGS", "Gross profit", "Margin" },
                Empty = TableAccess.Can(TableAccess.Sales)
                    ? "No sales in this view."
                    : "You do not have access to sales."
            };
            if (!TableAccess.Can(TableAccess.Sales))
                return result;

            var speciesOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in DataFiles.ReadRecords(DataFiles.ItemCodes))
            {
                string code = DataFiles.GetRecord(item, "Code").Trim();
                if (code.Length == 0)
                    continue;
                string species = DataFiles.GetRecord(item, "Species").Trim();
                speciesOf[code] = species.Length > 0 ? species : "Unspecified";
            }

            var purchases = PurchaseIndex();
            var groups = new Dictionary<string, SpeciesRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var sale in DataFiles.ReadRecords(DataFiles.Sales))
            {
                string item = DataFiles.GetRecord(sale, "Item Code").Trim();
                string species = item.Length > 0 && speciesOf.TryGetValue(item, out var named)
                    ? named
                    : (item.Length > 0 ? item : "Unspecified");
                if (!groups.TryGetValue(species, out var row))
                {
                    row = new SpeciesRow { Name = species };
                    groups[species] = row;
                }

                row.Lines++;
                row.Volume += DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Volume"));
                row.Revenue += DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Amount"));
                row.Cogs += SaleCogs(sale, purchases);
            }

            decimal revenue = 0, cogs = 0;
            foreach (var row in groups.Values.OrderByDescending(r => r.Revenue))
            {
                revenue += row.Revenue;
                cogs += row.Cogs;
                decimal profit = row.Revenue - row.Cogs;
                result.Rows.Add(new[]
                {
                    row.Name,
                    row.Lines.ToString("N0"),
                    row.Volume.ToString("N2"),
                    Money(row.Revenue),
                    Money(row.Cogs),
                    Money(profit),
                    Percent(profit, row.Revenue)
                });
            }

            decimal gross = revenue - cogs;
            result.Stats.Add(("Species", groups.Count.ToString("N0")));
            result.Stats.Add(("Revenue", Money(revenue)));
            result.Stats.Add(("Gross profit", Money(gross)));
            result.Stats.Add(("Margin", Percent(gross, revenue)));
            return result;
        }

        private static Dictionary<string, Dictionary<string, string>> PurchaseIndex()
        {
            var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!TableAccess.Can(TableAccess.Purchases))
                return map;

            foreach (var purchase in DataFiles.ReadRecords(DataFiles.PurchaseSales))
            {
                string po = DataFiles.NormalizePo(DataFiles.GetRecord(purchase, "PO #"));
                if (po.Length > 0)
                    map[po] = purchase;
            }

            return map;
        }

        private static decimal SaleCogs(
            Dictionary<string, string> sale,
            Dictionary<string, Dictionary<string, string>> purchases)
        {
            string lot = DataFiles.NormalizePo(DataFiles.SaleLot(sale));
            if (lot.Length == 0 || !purchases.TryGetValue(lot, out var purchase))
                return 0;

            decimal perLb = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Total Cost / LB"));
            if (perLb == 0)
            {
                decimal total = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Total Cost"));
                decimal lbs = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Volume Received"));
                if (lbs <= 0)
                    lbs = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Volume"));
                if (lbs > 0)
                    perLb = total / lbs;
            }

            decimal volume = DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Volume"));
            return perLb * volume;
        }

        private static int DaysPastDue(Dictionary<string, string> invoice)
        {
            string dueText = DataFiles.GetRecord(invoice, "Due Date").Trim();
            if (!DateTime.TryParse(dueText, out var due))
                return int.MinValue;
            return (DateTime.Today - due.Date).Days;
        }

        private static string AgingBucket(int days)
        {
            if (days == int.MinValue)
                return "No due date";
            if (days <= 0)
                return "Current";
            if (days <= 30)
                return "1–30";
            if (days <= 60)
                return "31–60";
            if (days <= 90)
                return "61–90";
            return "90+";
        }

        private static string MonthKey(string text)
        {
            if (DateTime.TryParse(text, out var date))
                return date.ToString("yyyy-MM");
            return "No ship date";
        }

        private static string Money(decimal amount) => amount.ToString("C");

        private static string Percent(decimal part, decimal whole)
        {
            if (whole == 0)
                return "—";
            return (part / whole).ToString("P1");
        }

        private sealed class Deal
        {
            public string Key = "";
            public string Customer = "";
            public string So = "";
            public DateTime Ship = DateTime.MinValue;
            public int Lines;
            public decimal Amount;
        }

        private sealed class Month
        {
            public string Key = "";
            public int Sales;
            public decimal Revenue;
            public decimal Cogs;
        }

        private sealed class Supplier
        {
            public string Name = "";
            public HashSet<string> Pos = new(StringComparer.OrdinalIgnoreCase);
            public decimal Volume;
            public decimal Cost;
        }

        private sealed class Risk
        {
            public string Name = "";
            public string Terms = "";
            public decimal Limit;
            public decimal OnFile;
            public int OpenInvoices;
            public decimal Outstanding;
            public decimal Overdue;
        }

        private sealed class SpeciesRow
        {
            public string Name = "";
            public int Lines;
            public decimal Volume;
            public decimal Revenue;
            public decimal Cogs;
        }
    }
}

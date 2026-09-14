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
        public List<ReportGroup>? Groups { get; set; }
        public List<ReportTab>? Tabs { get; set; }
        public string Empty { get; init; } = "No rows for this report in the current view.";
    }

    internal sealed class ReportTab
    {
        public required string Name { get; init; }
        public required string[] Columns { get; init; }
        public List<string[]> Rows { get; } = new();
        public List<(string Label, string Value)> Stats { get; } = new();
        public string Empty { get; init; } = "No rows for this report in the current view.";
    }

    internal sealed class ReportGroup
    {
        public required string Key { get; init; }
        public required string[] Parent { get; init; }
        public List<string[]> Children { get; } = new();
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
            var customers = AgingCustomers();
            var vendors = AgingVendors();
            return new ReportResult
            {
                Title = "Aging Report",
                Hint = "Customers are open invoices. Vendors are open purchases. Use the tabs, then filter if you want.",
                Columns = customers.Columns,
                Empty = customers.Empty,
                Tabs = new List<ReportTab> { customers, vendors }
            };
        }

        private static ReportTab AgingCustomers()
        {
            var tab = new ReportTab
            {
                Name = "Customers",
                Columns = new[]
                {
                    "Invoice #", "Customer", "Due Date", "Amount", "Outstanding", "Days past due", "Bucket"
                },
                Empty = TableAccess.Can(TableAccess.Invoices)
                    ? "No open invoices in this view."
                    : "You do not have access to invoices."
            };
            if (!TableAccess.Can(TableAccess.Invoices))
                return tab;

            decimal current = 0, d30 = 0, d60 = 0, d90 = 0, older = 0, total = 0;
            var rows = new List<(int sort, string[] cells)>();
            foreach (var invoice in DataFiles.VisibleRecords(DataFiles.Invoices))
            {
                if (DataFiles.IsWaitingAdd(invoice) ||
                    DataFiles.IsReceivedInvoice(invoice) ||
                    DataFiles.InvoiceIsClosed(invoice))
                    continue;
                decimal due = DataFiles.InvoiceOutstanding(invoice);
                if (due <= 0)
                    continue;

                int days = DaysPastDue(DataFiles.GetRecord(invoice, "Due Date"));
                string bucket = AgingBucket(days);
                decimal amount = DataFiles.ParseMoney(DataFiles.GetRecord(invoice, "Amount"));
                AddAging(bucket, due, ref current, ref d30, ref d60, ref d90, ref older, ref total);
                rows.Add((days, new[]
                {
                    DataFiles.GetRecord(invoice, "Invoice #"),
                    DataFiles.GetRecordAny(invoice, "Customer", "Customer Code"),
                    DataFiles.GetRecord(invoice, "Due Date"),
                    Money(amount),
                    Money(due),
                    DaysText(days),
                    bucket
                }));
            }

            foreach (var row in rows.OrderByDescending(r => r.sort))
                tab.Rows.Add(row.cells);
            AddAgingStats(tab, total, current, d30, d60, d90, older);
            return tab;
        }

        private static ReportTab AgingVendors()
        {
            var tab = new ReportTab
            {
                Name = "Vendors",
                Columns = new[]
                {
                    "PO #", "Vendor", "Due Date", "Total Cost", "Days past due", "Bucket", "Status"
                },
                Empty = TableAccess.Can(TableAccess.Purchases)
                    ? "No open purchases in this view."
                    : "You do not have access to purchases."
            };
            if (!TableAccess.Can(TableAccess.Purchases))
                return tab;

            decimal current = 0, d30 = 0, d60 = 0, d90 = 0, older = 0, total = 0;
            var rows = new List<(int sort, string[] cells)>();
            foreach (var purchase in DataFiles.VisibleRecords(DataFiles.PurchaseSales))
            {
                if (DataFiles.IsWaitingAdd(purchase))
                    continue;
                string status = DataFiles.GetRecord(purchase, "Status").Trim();
                if (status.Equals("Complete", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("Paid", StringComparison.OrdinalIgnoreCase))
                    continue;

                decimal due = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Total Cost"));
                if (due <= 0)
                    continue;

                string dueDate = FirstDate(
                    DataFiles.GetRecord(purchase, "Vendor Due Date"),
                    DataFiles.GetRecord(purchase, "Expected Ship Date"),
                    DataFiles.GetRecord(purchase, "Agreement Date"));
                int days = DaysPastDue(dueDate);
                string bucket = AgingBucket(days);
                AddAging(bucket, due, ref current, ref d30, ref d60, ref d90, ref older, ref total);
                rows.Add((days, new[]
                {
                    DataFiles.GetRecord(purchase, "PO #"),
                    DataFiles.GetRecordAny(purchase, "Vendor", "Vendor Code"),
                    dueDate,
                    Money(due),
                    DaysText(days),
                    bucket,
                    status
                }));
            }

            foreach (var row in rows.OrderByDescending(r => r.sort))
                tab.Rows.Add(row.cells);
            AddAgingStats(tab, total, current, d30, d60, d90, older);
            return tab;
        }

        private static void AddAging(
            string bucket,
            decimal due,
            ref decimal current,
            ref decimal d30,
            ref decimal d60,
            ref decimal d90,
            ref decimal older,
            ref decimal total)
        {
            total += due;
            switch (bucket)
            {
                case "Current": current += due; break;
                case "1–30": d30 += due; break;
                case "31–60": d60 += due; break;
                case "61–90": d90 += due; break;
                default: older += due; break;
            }
        }

        private static void AddAgingStats(
            ReportTab tab,
            decimal total,
            decimal current,
            decimal d30,
            decimal d60,
            decimal d90,
            decimal older)
        {
            tab.Stats.Add(("Open", Money(total)));
            tab.Stats.Add(("Current", Money(current)));
            tab.Stats.Add(("1–30 days", Money(d30)));
            tab.Stats.Add(("31–60 / 61–90 / 90+", $"{Money(d60)}  ·  {Money(d90)}  ·  {Money(older)}"));
        }

        private static string FirstDate(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string DaysText(int days) =>
            days == int.MinValue ? "—" : days.ToString(CultureInfo.InvariantCulture);

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
            foreach (var sale in DataFiles.VisibleRecords(DataFiles.Sales))
            {
                if (DataFiles.IsWaitingAdd(sale))
                    continue;
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
            foreach (var sale in DataFiles.VisibleRecords(DataFiles.Sales))
            {
                if (DataFiles.IsWaitingAdd(sale))
                    continue;
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
            foreach (var purchase in DataFiles.VisibleRecords(DataFiles.PurchaseSales))
            {
                if (DataFiles.IsWaitingAdd(purchase))
                    continue;
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
            foreach (var customer in DataFiles.VisibleRecords(DataFiles.Customers))
            {
                if (DataFiles.IsWaitingAdd(customer))
                    continue;
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

            foreach (var invoice in DataFiles.VisibleRecords(DataFiles.Invoices))
            {
                if (DataFiles.IsWaitingAdd(invoice) ||
                    DataFiles.IsReceivedInvoice(invoice) ||
                    DataFiles.InvoiceIsClosed(invoice))
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
                Hint = "Profit by species and item code. Click a species to expand its item codes.",
                Columns = new[]
                {
                    "Species / item code", "Lines", "Volume", "Revenue", "COGS", "Gross profit", "Margin"
                },
                Empty = TableAccess.Can(TableAccess.Sales)
                    ? "No sales in this view."
                    : "You do not have access to sales."
            };
            if (!TableAccess.Can(TableAccess.Sales))
                return result;

            var speciesOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in DataFiles.VisibleRecords(DataFiles.ItemCodes))
            {
                string code = DataFiles.GetRecord(item, "Code").Trim();
                if (code.Length == 0)
                    continue;
                string species = DataFiles.GetRecord(item, "Species").Trim();
                speciesOf[code] = species.Length > 0 ? species : "Unspecified";
            }

            var purchases = PurchaseIndex();
            var groups = new Dictionary<string, SpeciesRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var sale in DataFiles.VisibleRecords(DataFiles.Sales))
            {
                if (DataFiles.IsWaitingAdd(sale))
                    continue;
                string item = DataFiles.GetRecord(sale, "Item Code").Trim();
                if (item.Length == 0)
                    item = "(no item code)";
                string species = speciesOf.TryGetValue(item, out var named)
                    ? named
                    : (item == "(no item code)" ? "Unspecified" : item);
                if (!groups.TryGetValue(species, out var row))
                {
                    row = new SpeciesRow { Name = species };
                    groups[species] = row;
                }

                decimal volume = DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Volume"));
                decimal revenue = DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Amount"));
                decimal cogs = SaleCogs(sale, purchases);
                row.Add(volume, revenue, cogs);
                if (!row.Items.TryGetValue(item, out var child))
                {
                    child = new SpeciesRow { Name = item };
                    row.Items[item] = child;
                }

                child.Add(volume, revenue, cogs);
            }

            result.Groups = new List<ReportGroup>();
            decimal revenueTotal = 0, cogsTotal = 0;
            int itemCount = 0;
            foreach (var row in groups.Values.OrderByDescending(r => r.Revenue))
            {
                revenueTotal += row.Revenue;
                cogsTotal += row.Cogs;
                itemCount += row.Items.Count;
                var parent = ProfitCells(row.Name, row);
                var group = new ReportGroup { Key = row.Name, Parent = parent };
                foreach (var child in row.Items.Values.OrderByDescending(c => c.Revenue))
                    group.Children.Add(ProfitCells("    " + child.Name, child));
                result.Groups.Add(group);
                result.Rows.Add(parent);
            }

            decimal gross = revenueTotal - cogsTotal;
            result.Stats.Add(("Species", groups.Count.ToString("N0")));
            result.Stats.Add(("Item codes", itemCount.ToString("N0")));
            result.Stats.Add(("Gross profit", Money(gross)));
            result.Stats.Add(("Margin", Percent(gross, revenueTotal)));
            return result;
        }

        private static string[] ProfitCells(string name, SpeciesRow row)
        {
            decimal profit = row.Revenue - row.Cogs;
            return new[]
            {
                name,
                row.Lines.ToString("N0"),
                row.Volume.ToString("N2"),
                Money(row.Revenue),
                Money(row.Cogs),
                Money(profit),
                Percent(profit, row.Revenue)
            };
        }

        private static Dictionary<string, Dictionary<string, string>> PurchaseIndex()
        {
            var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!TableAccess.Can(TableAccess.Purchases))
                return map;

            foreach (var purchase in DataFiles.VisibleRecords(DataFiles.PurchaseSales))
            {
                if (DataFiles.IsWaitingAdd(purchase))
                    continue;
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
                decimal lbs = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Volume"));
                if (lbs > 0)
                    perLb = total / lbs;
            }

            decimal volume = DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Volume"));
            return perLb * volume;
        }

        private static int DaysPastDue(string dueText)
        {
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
            public Dictionary<string, SpeciesRow> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

            public void Add(decimal volume, decimal revenue, decimal cogs)
            {
                Lines++;
                Volume += volume;
                Revenue += revenue;
                Cogs += cogs;
            }
        }
    }
}

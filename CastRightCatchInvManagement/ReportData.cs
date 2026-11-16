using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>The six Reports-page tables.</summary>
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

    /// <summary>One Aging (or similar) tab: columns, rows, chips, and optional expand groups.</summary>
    internal sealed class ReportTab
    {
        public required string Name { get; init; }
        public required string[] Columns { get; init; }
        public List<string[]> Rows { get; } = new();
        public List<ReportGroup>? Groups { get; set; }
        public List<(string Label, string Value)> Stats { get; } = new();
        public string Empty { get; init; } = "No rows for this report in the current view.";
    }

    /// <summary>Expandable parent row plus child item-code or invoice rows.</summary>
    internal sealed class ReportGroup
    {
        public required string Key { get; init; }
        public required string[] Parent { get; init; }
        public List<string[]> Children { get; } = new();
        /// <summary>Open invoices for an aging customer or vendor; null on species groups.</summary>
        public AgingParty? Party { get; set; }
    }

    /// <summary>One customer or vendor on the aging report, used to draw a statement PDF.</summary>
    internal sealed class AgingParty
    {
        public required string Name { get; init; }
        public string Code { get; init; } = "";
        public string Address { get; init; } = "";
        public bool Received { get; init; }
        public List<AgingInvoiceLine> Invoices { get; } = new();
    }

    /// <summary>One open invoice under an aging customer or vendor.</summary>
    internal sealed class AgingInvoiceLine
    {
        public string Number { get; init; } = "";
        public string Date { get; init; } = "";
        public string DueDate { get; init; } = "";
        public string Reference { get; init; } = "";
        public decimal Amount { get; init; }
        public decimal Paid { get; init; }
        public decimal Outstanding { get; init; }
        public int Days { get; init; }
        public string Bucket { get; init; } = "";
    }

    /// <summary>
    /// Builds the six Reports page tables from sales, invoices, purchases, and lookups.
    /// Honors Current vs Old the same way the grids do.
    /// </summary>
    internal static class ReportData
    {
        /// <summary>Live-only vs archive-plus-live, matching the sidebar toggle.</summary>
        public static string ScopeHint() =>
            AppState.ViewingOldInventory
                ? "All inventory (archive and live)"
                : "This term (live database)";

        /// <summary>Dispatch to the matching builder.</summary>
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

        /// <summary>Customers and vendors aging tabs.</summary>
        private static ReportResult Aging()
        {
            var customers = AgingParties(received: false);
            var vendors = AgingParties(received: true);
            return new ReportResult
            {
                Title = "Aging Report",
                Hint = "Open invoices grouped by customer and vendor. Click a name to expand invoices. Right-click to create a statement PDF.",
                Columns = customers.Columns,
                Empty = customers.Empty,
                Tabs = new List<ReportTab> { customers, vendors }
            };
        }

        /// <summary>Open issued invoices under customers, or open received invoices under vendors.</summary>
        private static ReportTab AgingParties(bool received)
        {
            string partyLabel = received ? "Vendor" : "Customer";
            var tab = new ReportTab
            {
                Name = received ? "Vendors" : "Customers",
                Columns = new[]
                {
                    partyLabel + " / Invoice #", "Due Date", "Amount", "Paid", "Outstanding", "Days past due", "Bucket"
                },
                Empty = TableAccess.Can(TableAccess.Invoices)
                    ? received
                        ? "No open vendor invoices in this view."
                        : "No open invoices in this view."
                    : "You do not have access to invoices."
            };
            // Hidden invoice tables must not leak into aging or risk.
            if (!TableAccess.Can(TableAccess.Invoices))
                return tab;

            decimal current = 0, d30 = 0, d60 = 0, d90 = 0, older = 0, total = 0;
            var parties = new Dictionary<string, AgingParty>(StringComparer.OrdinalIgnoreCase);
            foreach (var invoice in DataFiles.VisibleRecords(DataFiles.Invoices))
            {
                // Queued adds are not yet live totals.
                if (DataFiles.IsWaitingAdd(invoice) || DataFiles.InvoiceIsClosed(invoice))
                    continue;
                // Customers tab is issued invoices we are owed; Vendors tab is received invoices we still owe.
                if (DataFiles.IsReceivedInvoice(invoice) != received)
                    continue;
                decimal due = DataFiles.InvoiceOutstanding(invoice);
                // Paid-in-full invoices stay out of aging even if Status is still open.
                if (due <= 0)
                    continue;

                int days = DaysPastDue(DataFiles.GetRecord(invoice, "Due Date"));
                string bucket = AgingBucket(days);
                decimal amount = DataFiles.ParseMoney(DataFiles.GetRecord(invoice, "Amount"));
                decimal paid = DataFiles.ParseMoney(DataFiles.GetRecord(invoice, "Paid"));
                AddAging(bucket, due, ref current, ref d30, ref d60, ref d90, ref older, ref total);

                string code = received
                    ? DataFiles.GetRecordAny(invoice, "Vendor Code", "Code")
                    : DataFiles.GetRecordAny(invoice, "Customer Code", "Cust ID");
                string name = received
                    ? DataFiles.GetRecordAny(invoice, "Vendor", "Vendor Name")
                    : DataFiles.GetRecordAny(invoice, "Customer", "Customer Name");
                string key = AgingPartyKey(received, code, name);
                if (!parties.TryGetValue(key, out var party))
                {
                    var master = FindAgingMaster(received, code, name);
                    string display = master != null
                        ? DataFiles.PartyDocumentName(master)
                        : (name.Length > 0 ? name : (received ? "(no vendor)" : "(no customer)"));
                    party = new AgingParty
                    {
                        Name = display,
                        Code = master != null
                            ? DataFiles.GetRecord(master, "Code")
                            : code,
                        Address = master != null
                            ? DataFiles.GetRecord(master, "Address")
                            : "",
                        Received = received
                    };
                    parties[key] = party;
                }

                party.Invoices.Add(new AgingInvoiceLine
                {
                    Number = DataFiles.GetRecord(invoice, "Invoice #"),
                    Date = DataFiles.GetRecord(invoice, DataFiles.InvoiceDateColumn),
                    DueDate = DataFiles.GetRecord(invoice, "Due Date"),
                    Reference = received
                        ? DataFiles.GetRecord(invoice, "PO #")
                        : DataFiles.GetRecord(invoice, "SO #"),
                    Amount = amount,
                    Paid = paid,
                    Outstanding = due,
                    Days = days,
                    Bucket = bucket
                });
            }

            tab.Groups = new List<ReportGroup>();
            foreach (var pair in parties.OrderByDescending(p => p.Value.Invoices.Sum(i => i.Outstanding))
                         .ThenBy(p => p.Value.Name, StringComparer.OrdinalIgnoreCase))
            {
                var party = pair.Value;
                var invoices = party.Invoices
                    .OrderByDescending(i => i.Days == int.MinValue ? int.MinValue : i.Days)
                    .ToList();
                party.Invoices.Clear();
                party.Invoices.AddRange(invoices);

                decimal partyDue = invoices.Sum(i => i.Outstanding);
                decimal partyAmount = invoices.Sum(i => i.Amount);
                decimal partyPaid = invoices.Sum(i => i.Paid);
                int oldest = invoices.Max(i => i.Days);
                string oldestBucket = AgingBucket(oldest);
                string oldestDue = invoices
                    .Select(i => i.DueDate)
                    .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? "";
                var group = new ReportGroup
                {
                    Key = pair.Key,
                    Parent = new[]
                    {
                        party.Name,
                        oldestDue,
                        Money(partyAmount),
                        Money(partyPaid),
                        Money(partyDue),
                        DaysText(oldest),
                        oldestBucket
                    },
                    Party = party
                };
                foreach (var line in invoices)
                {
                    string number = line.Number.Length > 0 ? line.Number : "(no invoice #)";
                    group.Children.Add(new[]
                    {
                        "    " + number,
                        line.DueDate,
                        Money(line.Amount),
                        Money(line.Paid),
                        Money(line.Outstanding),
                        DaysText(line.Days),
                        line.Bucket
                    });
                }

                tab.Groups.Add(group);
                tab.Rows.Add(group.Parent);
            }

            AddAgingStats(tab, total, current, d30, d60, d90, older);
            return tab;
        }

        /// <summary>Stable group key: code when present, otherwise the document name.</summary>
        private static string AgingPartyKey(bool received, string code, string name)
        {
            string prefix = received ? "vend:" : "cust:";
            if (code.Length > 0)
                return prefix + "code:" + code;
            if (name.Length > 0)
                return prefix + "name:" + name;
            return prefix + "unknown";
        }

        /// <summary>Customer or vendor master row for the aging parent label and statement address.</summary>
        private static Dictionary<string, string>? FindAgingMaster(bool received, string code, string name)
        {
            string table = received ? DataFiles.Vendors : DataFiles.Customers;
            string access = received ? TableAccess.Vendors : TableAccess.Customers;
            // Hidden master tables still group invoices by the name printed on the invoice.
            if (!TableAccess.Can(access))
                return null;

            foreach (var rec in DataFiles.VisibleRecords(table))
            {
                if (DataFiles.IsWaitingAdd(rec))
                    continue;
                string recCode = DataFiles.GetRecord(rec, "Code");
                if (code.Length > 0 && recCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                    return rec;
                if (name.Length > 0 && DataFiles.PartyNameEquals(rec, name))
                    return rec;
            }

            return null;
        }

        /// <summary>Append one aging row and accumulate bucket totals.</summary>
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

        /// <summary>Current / 1–30 / 31–60 / 61+ chips.</summary>
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

        /// <summary>Human days-past-due label.</summary>
        private static string DaysText(int days) =>
            days == int.MinValue ? "—" : days.ToString(CultureInfo.InvariantCulture);

        /// <summary>Deals by PO/SO with sale volume (no commission rate stored).</summary>
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
            // Hidden sales must not appear on commission or P&L.
            if (!TableAccess.Can(TableAccess.Sales))
                return result;

            var deals = new Dictionary<string, Deal>(StringComparer.OrdinalIgnoreCase);
            foreach (var sale in DataFiles.VisibleRecords(DataFiles.Sales))
            {
                // Queued sales are left out of report totals.
                if (DataFiles.IsWaitingAdd(sale))
                    continue;
                string po = DataFiles.SalePo(sale);
                string so = DataFiles.GetRecord(sale, "SO #").Trim();
                string key = po.Length > 0 ? po : (so.Length > 0 ? so : "row:" + deals.Count);
                // First line on a PO/SO starts a new commission deal.
                if (!deals.TryGetValue(key, out var deal))
                {
                    deal = new Deal { Key = po.Length > 0 ? po : so };
                    deals[key] = deal;
                }

                deal.Lines++;
                deal.Amount += DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Amount"));
                // Fill customer from a later line if the first was blank.
                if (deal.Customer.Length == 0)
                    deal.Customer = DataFiles.GetRecordAny(sale, "Customer", "Customer Code");
                // Fill SO # from a later line if the first was blank.
                if (deal.So.Length == 0)
                    deal.So = so;
                string ship = DataFiles.GetRecord(sale, "Ship Date");
                // Keep the latest ship date on the deal.
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

        /// <summary>Revenue vs lot cost by ship month.</summary>
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
            // Hidden sales must not appear on commission or P&L.
            if (!TableAccess.Can(TableAccess.Sales))
                return result;

            var purchases = PurchaseIndex();
            var months = new Dictionary<string, Month>(StringComparer.OrdinalIgnoreCase);
            foreach (var sale in DataFiles.VisibleRecords(DataFiles.Sales))
            {
                // Queued sales are left out of report totals.
                if (DataFiles.IsWaitingAdd(sale))
                    continue;
                string month = MonthKey(DataFiles.GetRecord(sale, "Ship Date"));
                // First sale in a month starts a P&L row.
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

        /// <summary>Purchase volume and cost by vendor.</summary>
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
            // Hidden purchase tables must not leak into vendor aging.
            if (!TableAccess.Can(TableAccess.Purchases))
                return result;

            var vendors = new Dictionary<string, Supplier>(StringComparer.OrdinalIgnoreCase);
            foreach (var purchase in DataFiles.VisibleRecords(DataFiles.PurchaseSales))
            {
                // Queued purchases are not vendor performance yet.
                if (DataFiles.IsWaitingAdd(purchase))
                    continue;
                string name = DataFiles.GetRecordAny(purchase, "Vendor", "Vendor Code");
                // Purchases without a vendor name cannot group.
                if (name.Length == 0)
                    name = "Unknown";
                // First PO for a vendor starts a performance row.
                if (!vendors.TryGetValue(name, out var row))
                {
                    row = new Supplier { Name = name };
                    vendors[name] = row;
                }

                string po = DataFiles.GetRecord(purchase, "PO #").Trim();
                // Count distinct POs, not line rows.
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

        /// <summary>Credit limit, open invoices, and overdue amounts.</summary>
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
            // Hidden invoice tables must not leak into aging or risk.
            if (!TableAccess.Can(TableAccess.Invoices))
                return result;

            var customers = new Dictionary<string, Risk>(StringComparer.OrdinalIgnoreCase);
            foreach (var customer in DataFiles.VisibleRecords(DataFiles.Customers))
            {
                // Queued customer adds are not risk rows yet.
                if (DataFiles.IsWaitingAdd(customer))
                    continue;
                string code = DataFiles.GetRecord(customer, "Code").Trim();
                string name = DataFiles.GetRecord(customer, "Name").Trim();
                string key = code.Length > 0 ? code : name;
                // Need a code or name to attach open invoices.
                if (key.Length == 0)
                    continue;
                customers[key] = new Risk
                {
                    Name = name.Length > 0 ? name : code,
                    Terms = DataFiles.GetRecord(customer, "Terms"),
                    Limit = DataFiles.ParseMoney(DataFiles.GetRecord(customer, "Credit Limit")),
                    OnFile = 0
                };
            }

            foreach (var invoice in DataFiles.VisibleRecords(DataFiles.Invoices))
            {
                // Queued adds are not yet live totals.
                if (DataFiles.IsWaitingAdd(invoice) ||
                    DataFiles.IsReceivedInvoice(invoice) ||
                    DataFiles.InvoiceIsClosed(invoice))
                    continue;
                decimal due = DataFiles.InvoiceOutstanding(invoice);
                // Not-due invoices stay out of past-due buckets.
                if (due <= 0)
                    continue;

                string code = DataFiles.GetRecordAny(invoice, "Customer Code", "Cust ID");
                string name = DataFiles.GetRecord(invoice, "Customer");
                string key = code.Length > 0 && customers.ContainsKey(code) ? code : name;
                // Need a code or name to attach open invoices.
                if (key.Length == 0)
                    key = "Unknown";
                // Open invoices can create a risk row even without a customer card.
                if (!customers.TryGetValue(key, out var risk))
                {
                    risk = new Risk { Name = name.Length > 0 ? name : key };
                    customers[key] = risk;
                }

                risk.OpenInvoices++;
                risk.Outstanding += due;
                // Overdue flag drives the risk chip.
                if (DataFiles.InvoiceIsPastDue(invoice))
                    risk.Overdue += due;
            }

            // On file is the same receivable as Current Balance: open issued invoices.
            foreach (var risk in customers.Values)
                risk.OnFile = risk.Outstanding;

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
                // Past-due customers sort and highlight first.
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

        /// <summary>Profit per species with expandable item codes.</summary>
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
            // Hidden sales must not appear on commission or P&L.
            if (!TableAccess.Can(TableAccess.Sales))
                return result;

            var speciesOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in DataFiles.VisibleRecords(DataFiles.ItemCodes))
            {
                string code = DataFiles.GetRecord(item, "Code").Trim();
                // Sales without an item code cannot roll into species.
                if (code.Length == 0)
                    continue;
                string species = DataFiles.GetRecord(item, "Species").Trim();
                speciesOf[code] = species.Length > 0 ? species : "Unspecified";
            }

            var purchases = PurchaseIndex();
            var groups = new Dictionary<string, SpeciesRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var sale in DataFiles.VisibleRecords(DataFiles.Sales))
            {
                // Queued sales are left out of report totals.
                if (DataFiles.IsWaitingAdd(sale))
                    continue;
                string item = DataFiles.GetRecord(sale, "Item Code").Trim();
                // Need an item code for the expandable child row.
                if (item.Length == 0)
                    item = "(no item code)";
                string species = speciesOf.TryGetValue(item, out var named)
                    ? named
                    : (item == "(no item code)" ? "Unspecified" : item);
                // First sale of a species starts the parent total.
                if (!groups.TryGetValue(species, out var row))
                {
                    row = new SpeciesRow { Name = species };
                    groups[species] = row;
                }

                decimal volume = DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Volume"));
                decimal revenue = DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Amount"));
                decimal cogs = SaleCogs(sale, purchases);
                row.Add(volume, revenue, cogs);
                // First sale of an item code starts the child total.
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

        /// <summary>Volume, revenue, COGS, and margin cells.</summary>
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

        /// <summary>Live purchases keyed by PO # for lot cost.</summary>
        private static Dictionary<string, Dictionary<string, string>> PurchaseIndex()
        {
            var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            // Hidden purchase tables must not leak into vendor aging.
            if (!TableAccess.Can(TableAccess.Purchases))
                return map;

            foreach (var purchase in DataFiles.VisibleRecords(DataFiles.PurchaseSales))
            {
                // Queued purchases are not vendor performance yet.
                if (DataFiles.IsWaitingAdd(purchase))
                    continue;
                string po = DataFiles.NormalizePo(DataFiles.GetRecord(purchase, "PO #"));
                // Count distinct POs, not line rows.
                if (po.Length > 0)
                    map[po] = purchase;
            }

            return map;
        }

        /// <summary>Purchase cost/lb × pounds sold for one sale line.</summary>
        private static decimal SaleCogs(
            Dictionary<string, string> sale,
            Dictionary<string, Dictionary<string, string>> purchases)
        {
            string lot = DataFiles.NormalizePo(DataFiles.SaleLot(sale));
            // No matching PO means COGS cannot be computed.
            if (lot.Length == 0 || !purchases.TryGetValue(lot, out var purchase))
                return 0;

            decimal perLb = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Total Cost / LB"));
            // Zero cost/lb would report false zero COGS as if costed.
            if (perLb == 0)
            {
                decimal total = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Total Cost"));
                decimal lbs = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Volume"));
                // Prefer pounds sold; fall back to cases when weight is blank.
                if (lbs > 0)
                    perLb = total / lbs;
            }

            decimal volume = DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Volume"));
            return perLb * volume;
        }

        /// <summary>Whole days after the due date, or 0 if not overdue.</summary>
        private static int DaysPastDue(string dueText)
        {
            // Unparseable due dates are not overdue.
            if (!DateTime.TryParse(dueText, out var due))
                return int.MinValue;
            return (DateTime.Today - due.Date).Days;
        }

        /// <summary>Current / 1–30 / 31–60 / 61+ label.</summary>
        private static string AgingBucket(int days)
        {
            // Missing due date stays out of buckets.
            if (days == int.MinValue)
                return "No due date";
            // Not yet due stays in Current.
            if (days <= 0)
                return "Current";
            // 1–30 bucket.
            if (days <= 30)
                return "1–30";
            // 31–60 bucket.
            if (days <= 60)
                return "31–60";
            // 61–90 sits in 61+ with a distinct label.
            if (days <= 90)
                return "61–90";
            return "90+";
        }

        /// <summary>yyyy-MM from a date, or blank if unparseable.</summary>
        private static string MonthKey(string text)
        {
            // Unparseable ship dates are omitted from monthly P&L.
            if (DateTime.TryParse(text, out var date))
                return date.ToString("yyyy-MM");
            return "No ship date";
        }

        /// <summary>Currency for chips and table cells.</summary>
        private static string Money(decimal amount) => MoneyFormat.Display(amount);

        /// <summary>Margin percent, or em dash when whole is zero.</summary>
        private static string Percent(decimal part, decimal whole)
        {
            // Avoid divide-by-zero on empty species totals.
            if (whole == 0)
                return "—";
            return (part / whole).ToString("P1");
        }

        /// <summary>One PO/SO deal on the commission tracker.</summary>
        private sealed class Deal
        {
            public string Key = "";
            public string Customer = "";
            public string So = "";
            public DateTime Ship = DateTime.MinValue;
            public int Lines;
            public decimal Amount;
        }

        /// <summary>One ship-month row on Monthly P&L.</summary>
        private sealed class Month
        {
            public string Key = "";
            public int Sales;
            public decimal Revenue;
            public decimal Cogs;
        }

        /// <summary>Purchase volume and cost rolled up by vendor.</summary>
        private sealed class Supplier
        {
            public string Name = "";
            public HashSet<string> Pos = new(StringComparer.OrdinalIgnoreCase);
            public decimal Volume;
            public decimal Cost;
        }

        /// <summary>Credit, open invoices, and overdue totals for one customer.</summary>
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

        /// <summary>Volume, revenue, and COGS for a species or item code.</summary>
        private sealed class SpeciesRow
        {
            public string Name = "";
            public int Lines;
            public decimal Volume;
            public decimal Revenue;
            public decimal Cogs;
            public Dictionary<string, SpeciesRow> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Accumulate one sale into a species or item total.</summary>
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

using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>Bill by bill settlement (spec 13): which bills are open, and the rules every allocation is checked against.</summary>
public sealed partial class VoucherService
{
    static readonly HashSet<VoucherType> SettlingTypes = [VoucherType.Receipt, VoucherType.Payment, VoucherType.CreditNote, VoucherType.DebitNote];
    static readonly HashSet<VoucherType> BillTypes = [VoucherType.Sale, VoucherType.Purchase, VoucherType.CreditNote, VoucherType.DebitNote];

    /// <summary>The party's bills with what is still open. Needs CreateVoucher or ViewAllVouchers.</summary>
    public IReadOnlyList<OpenBill> OpenBills(CallContext ctx, int company, LedgerId party, long? excludingVoucher)
    {
        if (!ctx.Has(Permission.CreateVoucher) && !ctx.Has(Permission.ViewAllVouchers)) throw ServiceException.Denied();
        ctx.RequireCompany(company);
        using var c = hub.Open(company);
        var p = PartyRepository.Get(c, party) ?? throw new ServiceException(ServiceError.NotFound, "No such party.");
        return BillRepository.Bills(c, party, MasterService.SideOf(ChartRepository.Load(c), party), p.CreditDays, excludingVoucher);
    }

    /// <summary>The numbers of the bills these vouchers' allocations settle, for showing them.</summary>
    public IReadOnlyDictionary<BillRef, string> BillNumbers(CallContext ctx, int company, IEnumerable<VoucherRow> vouchers)
    {
        ctx.RequireCompany(company);
        var refs = vouchers.SelectMany(v => v.Allocations).Select(a => a.Bill).Distinct().ToList();
        if (refs.Count == 0) return new Dictionary<BillRef, string>();
        using var c = hub.Open(company);
        return refs.Select(r => (Ref: r, Bill: BillRepository.Describe(c, r)))
            .Where(x => x.Bill is not null)
            .ToDictionary(x => x.Ref, x => x.Bill!.Value.Number);
    }

    // A retried save: the voucher its request id made, if the same user saved the same type; anything else is refused.
    static Outcome? Repeated(CallContext ctx, SqliteConnection c, VoucherType type, string requestId)
    {
        if (BillRepository.VoucherByRequestId(c, requestId) is not { } id) return null;
        var v = VoucherRepository.Get(c, id)!;
        if (v.CreatedBy != ctx.UserId || v.Type != type)
            throw new ServiceException(ServiceError.Invalid, "This request id was already used for another voucher.");
        return new Outcome(new SavedVoucher(v.Id, v.DisplayNumber, v.RowVersion, AlreadySaved: true), [], Repeat: true);
    }

    static void CheckAllocations(SqliteConnection c, ChartOfAccounts chart, VoucherType type, IReadOnlyList<PostingLine> postings,
        IReadOnlyList<AllocationRow> allocations, long? editing)
    {
        if (allocations.Count == 0) return;
        if (!SettlingTypes.Contains(type))
            throw new ServiceException(ServiceError.Invalid, "Only receipts, payments, and credit and debit notes settle bills.");
        foreach (var group in allocations.GroupBy(a => a.Party))
        {
            var partyId = group.Key;
            var party = chart.Contains(partyId) ? PartyRepository.Get(c, partyId) : null;
            if (party is null) throw new ServiceException(ServiceError.Invalid, "Unknown party.");
            var name = chart.Ledger(partyId).Name;
            var side = MasterService.SideOf(chart, partyId);
            var net = Money.Sum(postings.Where(p => p.Ledger == partyId).Select(p => p.Amount));
            if (net == Money.Zero || PartySides.IsBill(side, net))
                throw new ServiceException(ServiceError.Invalid, $"This voucher does not settle bills of {name}.");

            var bills = BillRepository.Bills(c, partyId, side, party.CreditDays, editing).ToDictionary(b => b.Ref);
            // A bill of another party is named as such, not as unknown.
            foreach (var a in group.Where(a => !bills.ContainsKey(a.Bill)))
                if (BillRepository.Describe(c, a.Bill) is { } other && other.Party != partyId)
                    bills[a.Bill] = new OpenBill(a.Bill, other.Party, other.Number, null, default, null, Money.Zero, Money.Zero);

            var settlement = net < Money.Zero ? -net : net;
            var problem = AllocationCheck.Check(partyId, settlement, [.. group.Select(a => new Allocation(a.Bill, a.Amount))], bills);
            if (problem is null) continue;
            string Number() => bills[problem.Bill!.Value].Number;
            throw new ServiceException(ServiceError.Invalid, problem.Error switch
            {
                AllocationError.NotPositive => "Enter an allocation more than zero.",
                AllocationError.UnknownBill => "Unknown bill.",
                AllocationError.OtherParty => $"Bill {Number()} is not a bill of {name}.",
                AllocationError.Duplicate => $"Bill {Number()} is allocated twice.",
                AllocationError.MoreThanOpen => $"Bill {Number()} has only {IndianNumberFormat.Format(bills[problem.Bill!.Value].Open)} open.",
                AllocationError.MoreThanSettlement =>
                    $"Allocations are more than the {IndianNumberFormat.Format(settlement)} this voucher settles for {name}.",
                _ => throw new ArgumentOutOfRangeException(nameof(allocations)),
            });
        }
    }

    // A bill that vouchers settle keeps its party, and cannot drop below what they settle.
    static void CheckSettledBillEdit(SqliteConnection c, ChartOfAccounts chart, VoucherRow existing, LedgerId? party, IReadOnlyList<PostingLine> postings)
    {
        if (!BillTypes.Contains(existing.Type) || existing.Invoice is not { } before) return;
        var settled = BillRepository.SettledAgainst(c, new BillRef(BillSource.Voucher, existing.Id));
        if (settled == Money.Zero) return;
        var net = Money.Sum(postings.Where(p => p.Ledger == before.Party).Select(p => p.Amount));
        var amount = PartySides.IsBill(MasterService.SideOf(chart, before.Party), net) ? (net < Money.Zero ? -net : net) : Money.Zero;
        if (party != before.Party || amount < settled) throw Settled(settled);
    }

    static void CheckNotSettled(SqliteConnection c, long voucherId)
    {
        var settled = BillRepository.SettledAgainst(c, new BillRef(BillSource.Voucher, voucherId));
        if (settled != Money.Zero) throw Settled(settled);
    }

    static ServiceException Settled(Money settled) =>
        new(ServiceError.Invalid, $"This bill has {IndianNumberFormat.Format(settled)} settled. Change those vouchers first.");

    // Spec 13, Sales invoice types: export invoices add a port code, shipping bill number, and shipping bill date.
    static ExportDetails? CheckedExport(InvoiceKind kind, ExportDetails? export)
    {
        if (export is null) return null;
        var port = string.IsNullOrWhiteSpace(export.PortCode) ? null : export.PortCode.Trim().ToUpperInvariant();
        var number = string.IsNullOrWhiteSpace(export.ShippingBillNumber) ? null : export.ShippingBillNumber.Trim();
        if (port is null && number is null && export.ShippingBillDate is null) return null;
        if (kind is not (InvoiceKind.ExportWithTax or InvoiceKind.ExportUnderLut))
            throw new ServiceException(ServiceError.Invalid, "Port code and shipping bill are only for exports.");
        if (port is not null && !PortPattern().IsMatch(port)) throw new ServiceException(ServiceError.Invalid, "Port code must be 6 letters or digits.");
        if (number is not null && !ShippingBillPattern().IsMatch(number))
            throw new ServiceException(ServiceError.Invalid, "Shipping bill number must be 3 to 7 digits.");
        if ((number is null) != (export.ShippingBillDate is null))
            throw new ServiceException(ServiceError.Invalid, "Enter both the shipping bill number and date.");
        return new ExportDetails(port, number, export.ShippingBillDate);
    }

    // Rule 46(o) and the export proviso: where goods go, for documents this business issues. Exports name a country.
    static ShipTo? CheckedShipTo(TradeDirection direction, InvoiceKind kind, ShipTo? shipTo)
    {
        if (shipTo is null) return null;
        var name = shipTo.Name.Trim();
        // A text box inserts CRLF for Enter; lines are kept with LF alone.
        var address = shipTo.Address.ReplaceLineEndings("\n").Trim();
        var country = string.IsNullOrWhiteSpace(shipTo.Country) ? null : shipTo.Country.Trim();
        if (name.Length == 0 && address.Length == 0 && country is null && shipTo.State is null) return null;
        if (direction != TradeDirection.Outward)
            throw new ServiceException(ServiceError.Invalid, "Ship to is only for documents this business issues.");
        if (name.Length == 0 || address.Length == 0) throw new ServiceException(ServiceError.Invalid, "Enter the ship to name and address.");
        if (name.Length > 100) throw TooLong("name");
        if (address.Length > 300) throw TooLong("address");
        if (country?.Length > 60) throw TooLong("country");
        if (name.Any(char.IsControl) || address.Any(c => char.IsControl(c) && c != '\n') || (country?.Any(char.IsControl) ?? false))
            throw new ServiceException(ServiceError.Invalid, "Ship to cannot contain control characters.");
        var export = kind is InvoiceKind.ExportWithTax or InvoiceKind.ExportUnderLut;
        if (country is not null && !export) throw new ServiceException(ServiceError.Invalid, "Country of destination is only for exports.");
        if ((!export && shipTo.State is null) || (shipTo.State is { } state && !Gst.States.Any(s => s.Code == state)))
            throw new ServiceException(ServiceError.Invalid, "Choose the ship to state.");
        return new ShipTo(name, address, shipTo.State, country);
    }

    static ServiceException TooLong(string field) => new(ServiceError.Invalid, $"Ship to {field} is too long.");

    [GeneratedRegex("^[A-Z0-9]{6}$")]
    private static partial Regex PortPattern();

    [GeneratedRegex(@"^\d{3,7}$")]
    private static partial Regex ShippingBillPattern();
}

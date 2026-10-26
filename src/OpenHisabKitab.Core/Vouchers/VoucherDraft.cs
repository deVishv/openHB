namespace OpenHisabKitab.Core.Vouchers;

public sealed record VoucherDraft(VoucherType Type, DateOnly Date, IReadOnlyList<PostingLine> Lines);

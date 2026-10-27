using OpenHisabKitab.Core.Reports;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// Told about every committed change that affects report figures. Calls arrive while the company's write lock is
/// still held, so they arrive in commit order.
/// </summary>
public interface IBooksObserver
{
    /// <summary>A voucher was saved, edited, or cancelled. An empty list means it now posts nothing.</summary>
    void VoucherSaved(int company, long voucherId, IReadOnlyList<PostingRecord> postings);

    /// <summary>Groups, ledgers, or opening balances changed.</summary>
    void ChartChanged(int company);
}

using Grpc.Core;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Connection;

/// <summary>Every host service over one channel.</summary>
public sealed class HostClients(CallInvoker invoker)
{
    public Session.SessionClient Session { get; } = new(invoker);
    public Companies.CompaniesClient Companies { get; } = new(invoker);
    public Admin.AdminClient Admin { get; } = new(invoker);
    public Tokens.TokensClient Tokens { get; } = new(invoker);
    public UserSettings.UserSettingsClient UserSettings { get; } = new(invoker);
    public Changes.ChangesClient Changes { get; } = new(invoker);
    public Masters.MastersClient Masters { get; } = new(invoker);
    public Vouchers.VouchersClient Vouchers { get; } = new(invoker);
    public Reports.ReportsClient Reports { get; } = new(invoker);
}

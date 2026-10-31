using OpenHisabKitab.Contracts.V1;
using static OpenHisabKitab.Host.Tests.Grpc.HostFactory;
using V1Empty = OpenHisabKitab.Contracts.V1.Empty;

namespace OpenHisabKitab.Host.Tests.Grpc;

public sealed class SettingsGrpcTests : IDisposable
{
    readonly HostFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public void User_and_company_settings_round_trip_over_grpc()
    {
        var channel = factory.Channel();
        var token = new Contracts.V1.Session.SessionClient(channel).SetupOwner(new Credentials { Name = "owner", Password = "owner-pass-1" }, Headers()).Token;
        var settings = new UserSettings.UserSettingsClient(channel);
        settings.Set(new SettingValue { Key = "ui.fontSize", Json = "20" }, Headers(token));
        Assert.Equal([("ui.fontSize", "20")], settings.List(new V1Empty(), Headers(token)).Values.Select(v => (v.Key, v.Json)));
        settings.Reset(new SettingKey { Key = "ui.fontSize" }, Headers(token));
        Assert.Empty(settings.List(new V1Empty(), Headers(token)).Values);

        var companies = new Companies.CompaniesClient(channel);
        var company = companies.Create(new CompanyProfileMsg
        {
            Name = "A Traders", State = "27", Registration = "Regular", Gstin = "27AAPFU0939F1ZV", Address = "Pune", FirstFyStartYear = 2026,
        }, Headers(token)).Company;
        companies.SetSetting(new SettingRequest { Company = company, Key = "invoice.roundOff", Json = "false" }, Headers(token));
        Assert.Equal([("invoice.roundOff", "false")],
            companies.ListSettings(new CompanyRef { Company = company }, Headers(token)).Values.Select(v => (v.Key, v.Json)));
        companies.ResetSetting(new CompanySettingKey { Company = company, Key = "invoice.roundOff" }, Headers(token));
        Assert.Empty(companies.ListSettings(new CompanyRef { Company = company }, Headers(token)).Values);
    }
}

namespace OpenHisabKitab.Host.Tests;

public sealed class CompanyServiceTests : IDisposable
{
    readonly TestHost host = new();

    public void Dispose() => host.Dispose();

    static ServiceException Fails(ServiceError error, Action act)
    {
        var ex = Assert.Throws<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    static async Task<ServiceException> FailsAsync(ServiceError error, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    [Fact]
    public void Created_company_has_the_default_chart_and_first_year()
    {
        var id = host.NewCompany();
        var chart = host.Masters.Chart(host.Owner.Context, id);
        Assert.True(chart.Contains(chart.Required(RequiredLedger.Cash)));
        Assert.Equal(TestHost.Profile(), host.Companies.Get(host.Owner.Context, id));
        Assert.Equal([new FinancialYear(2026)], host.Companies.Years(host.Owner.Context, id));
        Assert.Equal("A Traders", host.Companies.List(host.Owner.Context).Single().Name);
    }

    [Fact]
    public void Creation_is_audited_in_both_databases()
    {
        var id = host.NewCompany();
        using (var c = host.Data.OpenHost())
            Assert.Contains(AuditLog.Read(c, new AuditFilter()), r => r.Entry.Action == "create" && r.Entry.Entity == "company");
        using (var c = host.Hub.Open(id))
            Assert.Contains(AuditLog.Read(c, new AuditFilter()), r => r.Entry.Action == "create" && r.Entry.Entity == "company");
    }

    [Fact]
    public void User_without_access_cannot_see_the_company()
    {
        var id = host.NewCompany();
        var viewer = host.NewUser("ravi", BuiltInRole.Viewer);
        Fails(ServiceError.PermissionDenied, () => host.Companies.Get(viewer.Context, id));
        Assert.Empty(host.Companies.List(viewer.Context));
    }

    [Fact]
    public void Creator_without_all_companies_gets_access_in_their_session()
    {
        var accountant = host.NewUser("meena", BuiltInRole.Accountant);
        var id = host.Companies.Create(accountant.Context, TestHost.Profile("B Traders"));
        var refreshed = host.Sessions.Resolve(accountant.Token)!;
        Assert.Equal("B Traders", host.Companies.Get(refreshed, id).Name);
    }

    [Fact]
    public void Gstin_state_must_match()
    {
        var p = TestHost.Profile() with { Gstin = TestHost.G("29AAGCB7383J1Z4") };
        var ex = Fails(ServiceError.Invalid, () => host.Companies.Create(host.Owner.Context, p));
        Assert.Equal("GSTIN state does not match the company state.", ex.Message);
        Assert.Empty(host.Companies.List(host.Owner.Context));
        Assert.False(Directory.Exists(host.Data.CompanyFolder) && Directory.EnumerateFiles(host.Data.CompanyFolder).Any());
    }

    [Fact]
    public void Regular_needs_a_gstin_but_composition_may_omit_it()
    {
        var ex = Fails(ServiceError.Invalid, () => host.Companies.Create(host.Owner.Context, TestHost.Profile() with { Gstin = null }));
        Assert.Equal("Enter the GSTIN.", ex.Message);
        host.Companies.Create(host.Owner.Context,
            TestHost.Profile() with { Gstin = null, Registration = GstRegistration.Composition });
    }

    [Fact]
    public void Blank_name_is_refused()
    {
        var ex = Fails(ServiceError.Invalid, () => host.Companies.Create(host.Owner.Context, TestHost.Profile("  ")));
        Assert.Equal("Enter a company name.", ex.Message);
    }

    [Fact]
    public void Viewer_cannot_create()
    {
        var viewer = host.NewUser("ravi", BuiltInRole.Viewer);
        Fails(ServiceError.PermissionDenied, () => host.Companies.Create(viewer.Context, TestHost.Profile()));
    }

    [Fact]
    public async Task Add_year_adds_the_next_one()
    {
        var id = host.NewCompany();
        await host.Companies.AddYearAsync(host.Owner.Context, id);
        Assert.Equal([new FinancialYear(2026), new FinancialYear(2027)], host.Companies.Years(host.Owner.Context, id));
    }

    [Fact]
    public async Task Setting_must_be_json()
    {
        var id = host.NewCompany();
        await host.Companies.SetSettingAsync(host.Owner.Context, id, "invoice.roundOff", "false");
        var ex = await FailsAsync(ServiceError.Invalid, () => host.Companies.SetSettingAsync(host.Owner.Context, id, "invoice.roundOff", "nope"));
        Assert.Equal("Setting invoice.roundOff needs a JSON value.", ex.Message);
    }

    [Fact]
    public async Task Unknown_company_is_not_found() =>
        Fails(ServiceError.NotFound, () => host.Companies.Get(host.Owner.Context, 42));

    [Fact]
    public async Task Writes_to_one_company_run_in_order_and_different_companies_in_parallel()
    {
        var a = host.NewCompany();
        var b = host.NewCompany(TestHost.Profile("B Traders"));
        using var release = new ManualResetEventSlim();
        using var firstInside = new ManualResetEventSlim();
        var first = Task.Run(() => host.Hub.WriteAsync(a, _ => { firstInside.Set(); release.Wait(); return 1; }));
        Assert.True(firstInside.Wait(TimeSpan.FromSeconds(10)));

        var otherCompany = Task.Run(() => host.Hub.WriteAsync(b, _ => 2));
        Assert.Equal(2, await otherCompany.WaitAsync(TimeSpan.FromSeconds(10)));

        var sameCompany = Task.Run(() => host.Hub.WriteAsync(a, _ => 3));
        await Task.Delay(200);
        Assert.False(sameCompany.IsCompleted);
        release.Set();
        Assert.Equal(1, await first);
        Assert.Equal(3, await sameCompany);
    }

    [Fact]
    public async Task After_commit_runs_and_failed_work_rolls_back()
    {
        var id = host.NewCompany();
        var after = 0;
        await host.Hub.WriteAsync(id, tx => { CompanyRepository.SetSetting(tx, "x", "1"); return 5; }, r => after = r);
        Assert.Equal(5, after);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Hub.WriteAsync<int>(id, tx => { CompanyRepository.SetSetting(tx, "y", "1"); throw new InvalidOperationException(); },
                _ => after = 99));
        Assert.Equal(5, after);
        using var c = host.Hub.Open(id);
        Assert.Null(CompanyRepository.GetSetting(c, "y"));
    }

    [Fact]
    public void Creation_skips_an_id_whose_file_already_exists_and_never_deletes_it()
    {
        Directory.CreateDirectory(host.Data.CompanyFolder);
        var existing = host.Data.CompanyPath(1);
        File.WriteAllText(existing, "real books");
        var id = host.NewCompany();
        Assert.NotEqual(1, id);
        Assert.Equal("real books", File.ReadAllText(existing));
        Assert.Equal("A Traders", host.Companies.Get(host.Owner.Context, id).Name);
    }

    [Fact]
    public async Task Known_settings_must_have_the_right_type()
    {
        var id = host.NewCompany();
        var ex = await FailsAsync(ServiceError.Invalid, () => host.Companies.SetSettingAsync(host.Owner.Context, id, "invoice.roundOff", "\"yes\""));
        Assert.Equal("Setting invoice.roundOff must be true or false.", ex.Message);
        await FailsAsync(ServiceError.Invalid, () => host.Companies.SetSettingAsync(host.Owner.Context, id, "invoice.roundOff", "1"));
    }

    [Fact]
    public async Task List_settings_returns_stored_values_and_viewer_cannot_reset()
    {
        var id = host.NewCompany();
        var viewer = host.NewUser("viewer", BuiltInRole.Viewer, id);
        Assert.Empty(host.Companies.ListSettings(viewer.Context, id));
        await host.Companies.SetSettingAsync(host.Owner.Context, id, "invoice.roundOff", "false");
        Assert.Equal(new Dictionary<string, string> { ["invoice.roundOff"] = "false" }, host.Companies.ListSettings(viewer.Context, id));
        await FailsAsync(ServiceError.PermissionDenied, () => host.Companies.ResetSettingAsync(viewer.Context, id, "invoice.roundOff"));
        await host.Companies.ResetSettingAsync(host.Owner.Context, id, "invoice.roundOff");
        Assert.Empty(host.Companies.ListSettings(host.Owner.Context, id));
        var other = host.NewCompany(TestHost.Profile("B Traders"));
        Fails(ServiceError.PermissionDenied, () => host.Companies.ListSettings(viewer.Context, other));
    }

    [Fact]
    public async Task Unknown_and_non_company_settings_are_refused()
    {
        var id = host.NewCompany();
        var ex = await FailsAsync(ServiceError.Invalid, () => host.Companies.SetSettingAsync(host.Owner.Context, id, "invoice.rounding", "true"));
        Assert.Equal("No setting named invoice.rounding.", ex.Message);
        ex = await FailsAsync(ServiceError.Invalid, () => host.Companies.SetSettingAsync(host.Owner.Context, id, "ui.fontSize", "16"));
        Assert.Equal("Setting ui.fontSize cannot be set for a company.", ex.Message);
    }

    [Fact]
    public void A_company_file_from_a_newer_version_gives_the_message()
    {
        var id = host.NewCompany();
        SqliteConnection.ClearAllPools();
        using (var c = new SqliteConnection($"Data Source={host.Data.CompanyPath(id)};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 999";
            cmd.ExecuteNonQuery();
        }
        var fresh = new CompanyHub(host.Data, host.Time);
        var ex = Assert.Throws<ServiceException>(() => fresh.Open(id));
        Assert.Equal("This file needs a newer Open Hisab Kitab.", ex.Message);
    }
    [Fact]
    public async Task Update_changes_name_address_and_gstin_and_renames_in_the_company_list()
    {
        var id = host.NewCompany();
        var owner = host.Owner.Context;
        var next = TestHost.Profile() with { Name = "A Traders LLP", Address = "2 FC Road, Pune", Gstin = TestHost.G("27AAACR5055K1Z7") };
        await host.Companies.UpdateAsync(owner, id, next);
        Assert.Equal(next, host.Companies.Get(owner, id));
        Assert.Equal("A Traders LLP", host.Companies.List(owner).Single(c => c.Id == id).Name);
        using var c = host.Hub.Open(id);
        Assert.Equal(("edit", "company"), AuditLog.Read(c, new AuditFilter(Limit: 1)).Select(e => (e.Entry.Action, e.Entry.Entity)).Single());
        using var h = host.Data.OpenHost();
        Assert.Equal(("edit", "company"), AuditLog.Read(h, new AuditFilter(Limit: 1)).Select(e => (e.Entry.Action, e.Entry.Entity)).Single());
    }

    [Fact]
    public async Task Update_refuses_a_state_change_once_vouchers_exist()
    {
        var id = host.NewCompany();
        var owner = host.Owner.Context;
        var chart = host.Masters.Chart(owner, id);
        var composition = TestHost.Profile() with { Registration = GstRegistration.Composition };
        await host.Companies.UpdateAsync(owner, id, composition);
        await host.Vouchers.SaveJournalAsync(owner, id, new JournalInput(VoucherType.Receipt, new DateOnly(2026, 5, 1), "",
            [new PostingLine(chart.Required(RequiredLedger.Cash), Money.Parse("1.00")), new PostingLine(chart.Required(RequiredLedger.Sales), Money.Parse("-1.00"))]));
        var e = await FailsAsync(ServiceError.Invalid, () => host.Companies.UpdateAsync(owner, id, TestHost.Profile()));
        Assert.Equal("State and registration cannot change after vouchers are entered.", e.Message);
        await host.Companies.UpdateAsync(owner, id, composition with { Name = "Renamed" });
    }

    [Fact]
    public async Task Update_refuses_a_first_year_change()
    {
        var id = host.NewCompany();
        var e = await FailsAsync(ServiceError.Invalid, () => host.Companies.UpdateAsync(host.Owner.Context, id, TestHost.Profile() with { FirstFyStartYear = 2025 }));
        Assert.Equal("The first financial year cannot change.", e.Message);
    }

    [Fact]
    public async Task Update_checks_the_gstin_and_needs_manage_companies()
    {
        var id = host.NewCompany();
        var e = await FailsAsync(ServiceError.Invalid, () => host.Companies.UpdateAsync(host.Owner.Context, id, TestHost.Profile() with { Gstin = null }));
        Assert.Equal("Enter the GSTIN.", e.Message);
        var clerk = host.NewUser("clerk", BuiltInRole.DataEntry, id).Context;
        await FailsAsync(ServiceError.PermissionDenied, () => host.Companies.UpdateAsync(clerk, id, TestHost.Profile()));
    }
}

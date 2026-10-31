namespace OpenHisabKitab.Host.Tests;

public sealed class UserSettingsServiceTests : IDisposable
{
    readonly TestHost h = new();

    public void Dispose() => h.Dispose();

    static ServiceException Fails(ServiceError error, Action act)
    {
        var ex = Assert.Throws<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    [Fact]
    public void Each_user_sees_only_their_own_settings()
    {
        var clerk = h.NewUser("clerk", BuiltInRole.DataEntry, h.NewCompany());
        h.UserSettings.Set(h.Owner.Context, "ui.fontSize", "20");
        h.UserSettings.Set(clerk.Context, "ui.fontSize", "16");
        Assert.Equal("20", h.UserSettings.List(h.Owner.Context)["ui.fontSize"]);
        Assert.Equal("16", h.UserSettings.List(clerk.Context)["ui.fontSize"]);
    }

    [Fact]
    public void Company_setting_is_refused_for_a_user()
    {
        var ex = Fails(ServiceError.Invalid, () => h.UserSettings.Set(h.Owner.Context, "invoice.roundOff", "false"));
        Assert.Equal("Setting invoice.roundOff cannot be set for a user.", ex.Message);
    }

    [Fact]
    public void Bad_keybindings_are_refused_with_their_line()
    {
        var ex = Fails(ServiceError.Invalid, () =>
            h.UserSettings.Set(h.Owner.Context, "keybindings", "[\n {\"key\":\"ctrl+\",\"command\":\"tab.new\"}\n]"));
        Assert.Equal("keybindings.json line 2: Cannot read key: ctrl+.", ex.Message);
    }

    [Fact]
    public void Good_keybindings_are_stored()
    {
        const string json = """[ {"key":"f6","command":"run.PURC"} ]""";
        h.UserSettings.Set(h.Owner.Context, UserSettingsService.KeybindingsKey, json);
        Assert.Equal(json, h.UserSettings.List(h.Owner.Context)["keybindings"]);
    }

    [Fact]
    public void Set_and_reset_are_audited_with_before_and_after()
    {
        h.UserSettings.Set(h.Owner.Context, "ui.fontSize", "20");
        h.UserSettings.Set(h.Owner.Context, "ui.fontSize", "22");
        h.UserSettings.Reset(h.Owner.Context, "ui.fontSize");
        var rows = h.Admin.Audit(h.Owner.Context, null, new AuditFilter(Entity: "user-setting")).OrderBy(r => r.Seq).ToList();
        var key = $"{h.Owner.Context.UserId}:ui.fontSize";
        Assert.Equal([("set", key, null, "20"), ("set", key, "20", "22"), ("reset", key, "22", null)],
            rows.Select(r => (r.Entry.Action, r.Entry.EntityKey, r.Entry.Before, r.Entry.After)).ToList<(string, string, string?, string?)>());
        Assert.Empty(h.UserSettings.List(h.Owner.Context));
    }

    [Fact]
    public void Reset_of_an_unset_key_writes_no_audit()
    {
        h.UserSettings.Reset(h.Owner.Context, "ui.fontSize");
        Assert.Empty(h.Admin.Audit(h.Owner.Context, null, new AuditFilter(Entity: "user-setting")));
    }

    [Fact]
    public void Unknown_key_is_refused_for_set_and_reset()
    {
        Assert.Equal("No setting named ui.zoom.", Fails(ServiceError.Invalid, () => h.UserSettings.Set(h.Owner.Context, "ui.zoom", "1")).Message);
        Assert.Equal("No setting named ui.zoom.", Fails(ServiceError.Invalid, () => h.UserSettings.Reset(h.Owner.Context, "ui.zoom")).Message);
    }

    [Fact]
    public void Rest_caller_is_refused()
    {
        var rest = h.Owner.Context with { Source = ChangeSource.Rest, TokenName = "scripts" };
        var ex = Fails(ServiceError.PermissionDenied, () => h.UserSettings.Set(rest, "ui.fontSize", "20"));
        Assert.Equal("Change settings in Open Hisab Kitab.", ex.Message);
        Fails(ServiceError.PermissionDenied, () => h.UserSettings.List(rest));
    }
}

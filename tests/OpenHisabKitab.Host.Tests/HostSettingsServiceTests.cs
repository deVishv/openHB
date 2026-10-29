namespace OpenHisabKitab.Host.Tests;

public sealed class HostSettingsServiceTests : IDisposable
{
    readonly TestHost h = new();

    public void Dispose() => h.Dispose();

    static ServiceException Fails(ServiceError error, Action act)
    {
        var ex = Assert.Throws<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    bool Set(string key, string json) => h.HostSettings.Set(h.Owner.Context, key, json);

    [Fact]
    public void Defaults_are_listed_with_their_definitions()
    {
        var list = h.HostSettings.List(h.Owner.Context);
        Assert.Equal(["api.enabled", "api.port", "api.allowLan", "api.allowedOrigins", "mcp.enabled", "mcp.allowWrites"],
            list.Select(s => s.Definition.Key));
        Assert.Equal(["false", "47251", "false", "[]", "false", "false"], list.Select(s => s.Json));
        Assert.All(list, s => Assert.Equal(s.Definition.DefaultJson, s.Json));
        Assert.Equal("Turns on the local REST API port.", list[0].Definition.Description);
        Assert.Equal(HostSettings.DefaultApiPort, ApiSettingsFromDb().Port);
    }

    ApiSettings ApiSettingsFromDb()
    {
        using var c = h.Data.OpenHost();
        return ApiSettings.Read(c);
    }

    [Fact]
    public void Stored_values_are_read_back_typed()
    {
        Set("api.enabled", "true");
        Set("api.port", "48000");
        Set("api.allowLan", "true");
        Set("api.allowedOrigins", """["https://ok.example"]""");
        Set("mcp.enabled", "true");
        Set("mcp.allowWrites", "true");
        var s = ApiSettingsFromDb();
        Assert.Equal((true, 48000, true, true, true), (s.Enabled, s.Port, s.AllowLan, s.McpEnabled, s.McpAllowWrites));
        Assert.Equal(["https://ok.example"], s.AllowedOrigins);
        Assert.Equal("48000", h.HostSettings.List(h.Owner.Context).Single(x => x.Definition.Key == "api.port").Json);
    }

    [Fact]
    public void Only_the_owner_sets_host_settings()
    {
        var co = h.NewCompany();
        var accountant = h.NewUser("acc", BuiltInRole.Accountant, co);
        Assert.Equal("You do not have permission for this.",
            Fails(ServiceError.PermissionDenied, () => h.HostSettings.Set(accountant.Context, "api.enabled", "true")).Message);
        Fails(ServiceError.PermissionDenied, () => h.HostSettings.List(accountant.Context));
    }

    [Theory]
    [InlineData("api.port", "80", "api.port must be 1024 to 65535, not the gRPC port.")]
    [InlineData("api.port", "70000", "api.port must be 1024 to 65535, not the gRPC port.")]
    [InlineData("api.port", "\"x\"", "api.port must be 1024 to 65535, not the gRPC port.")]
    [InlineData("api.port", "47250", "api.port must differ from the gRPC port 47250.")]
    [InlineData("api.enabled", "1", "api.enabled must be true, false.")]
    [InlineData("api.enabled", "not json", "api.enabled must be true, false.")]
    [InlineData("mcp.allowWrites", "null", "mcp.allowWrites must be true, false.")]
    [InlineData("api.allowedOrigins", """["https://a.example/path"]""", "api.allowedOrigins entries must look like https://example.com.")]
    [InlineData("api.allowedOrigins", """["ftp://a.example"]""", "api.allowedOrigins entries must look like https://example.com.")]
    [InlineData("api.allowedOrigins", """["https://a.example?q=1"]""", "api.allowedOrigins entries must look like https://example.com.")]
    [InlineData("api.allowedOrigins", "\"https://a.example\"", "api.allowedOrigins must be origins such as https://example.com.")]
    [InlineData("api.nothing", "true", "No host setting named api.nothing.")]
    public void Values_are_validated(string key, string json, string message)
    {
        Assert.Equal(message, Fails(ServiceError.Invalid, () => Set(key, json)).Message);
        Assert.Equal(HostSettings.DefaultApiPort, ApiSettingsFromDb().Port);
    }

    [Fact]
    public void Allowed_origins_accept_a_bare_origin_with_or_without_slash()
    {
        Set("api.allowedOrigins", """["https://a.example","http://localhost:3000/"]""");
        Assert.Equal(2, ApiSettingsFromDb().AllowedOrigins.Count);
    }

    [Fact]
    public void Restart_needed_only_where_the_table_says()
    {
        Assert.True(Set("api.enabled", "true"));
        Assert.False(Set("api.enabled", "true"));
        Assert.False(Set("api.enabled", "false"));
        Assert.True(Set("api.port", "48000"));
        Assert.False(Set("api.port", "48000"));
        Assert.True(Set("api.allowLan", "true"));
        Assert.False(Set("api.allowLan", "false"));
        Assert.False(Set("api.allowedOrigins", """["https://ok.example"]"""));
        Assert.False(Set("mcp.enabled", "true"));
        Assert.False(Set("mcp.allowWrites", "true"));
    }

    [Fact]
    public void Setting_changes_are_audited()
    {
        Set("api.enabled", "true");
        Set("api.enabled", "false");
        var rows = h.Admin.Audit(h.Owner.Context, null, new AuditFilter(Entity: "host-setting")).OrderBy(r => r.Seq).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(("set", "api.enabled", "owner"), (r.Entry.Action, r.Entry.EntityKey, r.Entry.User)));
        Assert.Equal(("false", "true"), (rows[0].Entry.Before, rows[0].Entry.After));
        Assert.Equal(("true", "false"), (rows[1].Entry.Before, rows[1].Entry.After));
    }
}

using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using OpenHisabKitab.Host.Tests.Grpc;

namespace OpenHisabKitab.Host.Tests.Api;

/// <summary>Turning on the API, making tokens, and calling the API port of a HostFactory.</summary>
public static class ApiHelpers
{
    public const string OwnerPassword = "owner-pass-1";

    /// <summary>A client whose requests arrive on the API port, with the bearer token when given.</summary>
    public static HttpClient ApiClient(this HostFactory f, string? token)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Add(HostFactory.ApiPortHeader, "api");
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static T Get<T>(this HostFactory f) where T : notnull => f.Services.GetRequiredService<T>();

    /// <summary>The Owner's session, setting the Owner up on first use.</summary>
    public static Session Owner(this HostFactory f)
    {
        var auth = f.Get<AuthService>();
        return auth.NeedsSetup() ? auth.SetupOwner("owner", OwnerPassword, "HOSTPC", isLoopback: true) : auth.Login("owner", OwnerPassword, "HOSTPC");
    }

    public static void Set(this HostFactory f, string key, string json) => f.Get<HostSettingsService>().Set(f.Owner().Context, key, json);

    public static void EnableApi(this HostFactory f, bool mcp = false, bool mcpWrites = false)
    {
        f.Set("api.enabled", "true");
        if (mcp) f.Set("mcp.enabled", "true");
        if (mcpWrites) f.Set("mcp.allowWrites", "true");
    }

    /// <summary>A token of the Owner.</summary>
    public static string NewToken(this HostFactory f, bool readWrite = false, string name = "test") =>
        f.Get<ApiTokenService>().Create(f.Owner().Context, name, readWrite).Token;

    /// <summary>Creates a user with the role and company access, and returns a token of theirs.</summary>
    public static string NewUserToken(this HostFactory f, string user, BuiltInRole role, bool readWrite, params int[] companies)
    {
        var owner = f.Owner().Context;
        var id = f.Get<UserService>().Create(owner, user, "password-" + user, role);
        if (companies.Length > 0) f.Get<UserService>().SetCompanies(owner, id, companies);
        var session = f.Get<AuthService>().Login(user, "password-" + user, "PC2");
        return f.Get<ApiTokenService>().Create(session.Context, "test", readWrite).Token;
    }
}

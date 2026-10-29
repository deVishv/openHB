using OpenHisabKitab.Host.Application;
using OpenHisabKitab.Host.Grpc;

namespace OpenHisabKitab.Host;

/// <summary>Host settings from the "Host" configuration section.</summary>
public sealed class OpenHisabKitabOptions
{
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenHisabKitab");

    public int GrpcPort { get; set; } = 47250;
}

/// <summary>Registers the application services once, for every transport adapter to share.</summary>
public static class HostSetup
{
    public static OpenHisabKitabOptions AddOpenHisabKitab(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("Host").Get<OpenHisabKitabOptions>() ?? new OpenHisabKitabOptions();
        var s = builder.Services;
        s.AddSingleton(options);
        s.AddSingleton(TimeProvider.System);
        s.AddSingleton(new HostData(options.DataDirectory));
        s.AddSingleton<SessionStore>();
        s.AddSingleton<LoginThrottle>();
        s.AddSingleton<AuthService>();
        s.AddSingleton<UserService>();
        s.AddSingleton<ChangeFeed>();
        s.AddSingleton<CompanyHub>();
        s.AddSingleton<CompanyBooks>();
        s.AddSingleton<IBooksObserver>(p => p.GetRequiredService<CompanyBooks>());
        s.AddSingleton<CompanyService>();
        s.AddSingleton<MasterService>();
        s.AddSingleton<VoucherService>();
        s.AddSingleton<ReportService>();
        s.AddSingleton<AdminService>();
        s.AddSingleton<ApiTokenService>();
        s.AddSingleton<HostSettingsService>();
        s.AddHostedService<BackupScheduler>();
        s.AddHostedService<AnchorFlushOnStop>();
        s.AddSingleton<IRemoteAddressAccessor, HttpRemoteAddress>();
        s.AddGrpc(o =>
        {
            o.Interceptors.Add<ErrorInterceptor>();
            o.Interceptors.Add<VersionInterceptor>();
            o.Interceptors.Add<AuthInterceptor>();
        });
        return options;
    }

    public static WebApplication MapOpenHisabKitab(this WebApplication app)
    {
        app.MapGrpcService<SessionEndpoint>();
        app.MapGrpcService<CompaniesEndpoint>();
        app.MapGrpcService<MastersEndpoint>();
        app.MapGrpcService<VouchersEndpoint>();
        app.MapGrpcService<ReportsEndpoint>();
        app.MapGrpcService<AdminEndpoint>();
        app.MapGrpcService<ChangesEndpoint>();
        return app;
    }
}

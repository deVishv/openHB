using Microsoft.AspNetCore.Server.Kestrel.Core;
using OpenHisabKitab.Host.Api;
using OpenHisabKitab.Host.Application;
using OpenHisabKitab.Host.Security;

namespace OpenHisabKitab.Host;

/// <summary>Builds the host: the Windows service, the gRPC port over TLS, the API port when turned on, and the application services.</summary>
public static class HostApp
{
    public const string ServiceName = "OpenHisabKitabHost";

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseWindowsService(o => o.ServiceName = ServiceName);
        var options = builder.AddOpenHisabKitab();
        // The API port's settings apply at start (spec 4.1). Turning it off applies at once, in ApiGate.
        ApiSettings api;
        using (var hostDb = new HostData(options.DataDirectory).OpenHost()) api = ApiSettings.Read(hostDb);
        builder.Services.AddSingleton<IListenerAccessor>(new KestrelListener(api.Port));

        builder.WebHost.ConfigureKestrel((context, kestrel) =>
        {
            var log = kestrel.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger<CertificateStore>();
            var certificate = new CertificateStore(options.DataDirectory, TimeProvider.System, log).GetOrCreate();
            log.LogInformation("Host certificate fingerprint {Fingerprint}", CertificateStore.Fingerprint(certificate));
            kestrel.ListenAnyIP(options.GrpcPort, listen =>
            {
                listen.Protocols = HttpProtocols.Http2;
                listen.UseHttps(certificate);
            });
            ApiListener.Configure(kestrel, api, certificate);
        });

        var app = builder.Build();
        // host.db is created and migrated before the first call is accepted.
        app.Services.GetRequiredService<HostData>().OpenHost().Dispose();
        app.MapOpenHisabKitab();
        return app;
    }
}

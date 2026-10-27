using OpenHisabKitab.Host;
using OpenHisabKitab.Host.Application;

var builder = WebApplication.CreateBuilder(args);
builder.AddOpenHisabKitab();

var app = builder.Build();

// host.db is created and migrated before the first call is accepted.
app.Services.GetRequiredService<HostData>().OpenHost().Dispose();
app.MapOpenHisabKitab();
app.Run();

public partial class Program;

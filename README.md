# Open Hisab Kitab

Open source accounting for Indian businesses.

Status: in development. Not ready for use.

## Build

Requires the .NET 10 SDK on Windows.

```
dotnet build
dotnet test
```

## Running the host

The host is the service that holds the books. The desktop app connects to it.

```
dotnet run --project src/OpenHisabKitab.Host
```

- Data goes in `%ProgramData%\OpenHisabKitab`. Change it with `--Host:DataDirectory=<folder>`.
- The gRPC port is 47250. Change it with `--Host:GrpcPort=<port>`.
- On first start the host makes a TLS certificate. Its fingerprint is in `host-cert-fingerprint.txt` in the data folder, and in the log. Clients show this fingerprint the first time they connect.

To run it as a Windows service, from an administrator prompt:

```
sc.exe create OpenHisabKitabHost binPath= "C:\Program Files\OpenHisabKitab\OpenHisabKitab.Host.exe" start= auto
netsh advfirewall firewall add rule name="Open Hisab Kitab host" dir=in action=allow protocol=TCP localport=47250
```

The installer will do both.

## Design

See `docs/superpowers/specs/` for the design and `docs/superpowers/plans/` for the build plans.

## Licence

MIT. See `LICENSE`.

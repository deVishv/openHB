using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OpenHisabKitab.Contracts;

/// <summary>How the host's certificate is shown and pinned (spec 4, Transport): SHA-256 as upper case hex pairs separated by colons.</summary>
public static class CertificateFingerprint
{
    public static string Of(X509Certificate cert) =>
        string.Join(':', SHA256.HashData(cert.GetRawCertData()).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
}

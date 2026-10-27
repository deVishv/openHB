using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenHisabKitab.Host.Security;

/// <summary>
/// The host's self signed TLS certificate (spec 4, Transport). Clients trust it on first use by its fingerprint, so it
/// is made once and kept: the PFX in the data folder, its random password protected for this machine by DPAPI.
/// </summary>
public sealed class CertificateStore(string dataDirectory, TimeProvider time, ILogger? log = null)
{
    const string Subject = "CN=Open Hisab Kitab host";
    readonly ILogger logger = log ?? NullLogger.Instance;

    string PfxPath => Path.Combine(dataDirectory, "host-cert.pfx");
    string KeyPath => Path.Combine(dataDirectory, "host-cert.key");
    string FingerprintPath => Path.Combine(dataDirectory, "host-cert-fingerprint.txt");

    public X509Certificate2 GetOrCreate()
    {
        if (File.Exists(PfxPath) && File.Exists(KeyPath))
        {
            try
            {
                var password = System.Text.Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.LocalMachine));
                return X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(PfxPath), password);
            }
            catch (CryptographicException e)
            {
                logger.LogCritical(e, "The host certificate could not be read. A new one is made; clients must accept it again.");
            }
        }
        else if (File.Exists(PfxPath))
        {
            logger.LogCritical("The host certificate key file is missing. A new certificate is made; clients must accept it again.");
        }
        return Create();
    }

    /// <summary>SHA-256 of the certificate, as upper case hex pairs separated by colons.</summary>
    public static string Fingerprint(X509Certificate cert) =>
        string.Join(':', SHA256.HashData(cert.GetRawCertData()).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));

    X509Certificate2 Create()
    {
        Directory.CreateDirectory(dataDirectory);
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(Subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddDnsName(Environment.MachineName);
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        var now = time.GetUtcNow();
        using var made = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10).AddDays(-1));
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var pfx = made.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(PfxPath, pfx);
        File.WriteAllBytes(KeyPath, ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(password), null, DataProtectionScope.LocalMachine));

        // Windows TLS cannot use the ephemeral key CreateSelfSigned returns, so serve the certificate loaded back from the PFX.
        var cert = X509CertificateLoader.LoadPkcs12(pfx, password);
        File.WriteAllText(FingerprintPath, Fingerprint(cert) + Environment.NewLine);
        logger.LogInformation("Made the host certificate. Fingerprint {Fingerprint}", Fingerprint(cert));
        return cert;
    }
}

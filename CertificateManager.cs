using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VramOp;

internal static class CertificateManager
{
    private const string CertificateName = "VRAM Vue Local Telemetry";
    private static readonly string[] CertificateSubjectNames = [CertificateName, "VRAM Op Local Telemetry"];

    public static X509Certificate2 GetOrCreateCertificate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        var existing = CertificateSubjectNames
            .SelectMany(name => store.Certificates
                .Find(X509FindType.FindBySubjectName, name, validOnly: false)
                .OfType<X509Certificate2>())
            .OfType<X509Certificate2>()
            .Where(cert => cert.HasPrivateKey && cert.NotAfter > DateTimeOffset.Now.AddDays(30))
            .OrderByDescending(cert => cert.NotAfter)
            .FirstOrDefault();

        if (existing is not null)
        {
            return existing;
        }

        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            $"CN={CertificateName} ({Environment.MachineName})",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") },
            critical: false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(Environment.MachineName);
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);

        foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()).Where(IsUsableAddress))
        {
            sanBuilder.AddIpAddress(address);
        }

        request.CertificateExtensions.Add(sanBuilder.Build());

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.Now.AddDays(-1),
            DateTimeOffset.Now.AddYears(5));
        certificate.FriendlyName = CertificateName;

        var exportableCertificate = new X509Certificate2(
            certificate.Export(X509ContentType.Pfx),
            string.Empty,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);

        store.Add(exportableCertificate);
        return exportableCertificate;
    }

    public static string GetSha256Thumbprint(X509Certificate2 certificate) =>
        Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));

    private static bool IsUsableAddress(IPAddress address) =>
        address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
        && !IPAddress.IsLoopback(address);
}

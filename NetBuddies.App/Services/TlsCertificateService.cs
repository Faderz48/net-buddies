using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NetBuddies.App.Services;

public static class TlsCertificateService
{
    public static GeneratedTlsCertificate GenerateSelfSignedPfx(string password, string hostName = "netbuddies.local")
    {
        var certificateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetBuddies",
            "Certificates");
        Directory.CreateDirectory(certificateDirectory);

        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={hostName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(hostName);
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(3));
        var path = Path.Combine(certificateDirectory, $"netbuddies-server-{DateTime.Now:yyyyMMdd-HHmmss}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return new GeneratedTlsCertificate(path, NetBuddies.Core.TlsCertificateHelper.GetSha256Fingerprint(certificate));
    }
}

public sealed record GeneratedTlsCertificate(string PfxPath, string Sha256Fingerprint);

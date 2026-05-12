using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NetBuddies.Core;

public static class ServerCertificateService
{
    public static GeneratedServerCertificate Generate(
        string outputDirectory,
        string password,
        string hostName = "netbuddies.local")
    {
        Directory.CreateDirectory(outputDirectory);

        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={hostName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(hostName);
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(3));

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var pfxPath = Path.Combine(outputDirectory, $"netbuddies-server-{stamp}.pfx");
        var pemPath = Path.Combine(outputDirectory, $"netbuddies-stunnel-{stamp}.pem");
        var configPath = Path.Combine(outputDirectory, $"netbuddies-stunnel-{stamp}.conf");

        File.WriteAllBytes(pfxPath, certificate.Export(X509ContentType.Pfx, password));
        File.WriteAllText(pemPath, certificate.ExportCertificatePem() + key.ExportPkcs8PrivateKeyPem());

        return new GeneratedServerCertificate(
            pfxPath,
            pemPath,
            configPath,
            TlsCertificateHelper.GetSha256Fingerprint(certificate));
    }
}

public sealed record GeneratedServerCertificate(
    string PfxPath,
    string StunnelPemPath,
    string SuggestedStunnelConfigPath,
    string Sha256Fingerprint);

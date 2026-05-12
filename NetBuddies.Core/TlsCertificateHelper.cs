using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NetBuddies.Core;

public static class TlsCertificateHelper
{
    public static string GetSha256Fingerprint(X509Certificate certificate)
    {
        var bytes = certificate.GetRawCertData();
        var hash = SHA256.HashData(bytes);
        return string.Join(':', hash.Select(value => value.ToString("X2")));
    }

    public static bool FingerprintMatches(X509Certificate certificate, string expectedFingerprint)
    {
        var expected = NormalizeFingerprint(expectedFingerprint);
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        return string.Equals(
            NormalizeFingerprint(GetSha256Fingerprint(certificate)),
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeFingerprint(string fingerprint)
    {
        return new string((fingerprint ?? "")
            .Where(Uri.IsHexDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }
}

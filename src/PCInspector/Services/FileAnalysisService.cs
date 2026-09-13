using System.ComponentModel;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PCInspector.Services;

internal sealed record FileAnalysis(string Publisher, string Signature, string Sha256);

internal static class FileAnalysisService
{
    public static Task<FileAnalysis?> AnalyzeAsync(string path) => Task.Run(() => Analyze(path));

    private static FileAnalysis? Analyze(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        string hash;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
            hash = Convert.ToHexString(SHA256.HashData(file));
        var publisher = "Unknown publisher";
        var signature = "Unsigned or unavailable";
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
            publisher = certificate.GetNameInfo(X509NameType.SimpleName, false);
            signature = certificate.Verify() ? "Valid signature" : "Invalid signature";
        }
        catch (CryptographicException) { }
        catch (Win32Exception) { }
        return new FileAnalysis(publisher, signature, hash);
    }
}

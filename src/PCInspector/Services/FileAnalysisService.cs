using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PCInspector.Services;

internal enum FileSignatureState { Verified, NoEmbeddedSignature, Invalid, Untrusted, Unavailable }

internal sealed record FileAnalysis(string Publisher, string Signature, string Sha256,
    string? Warning = null, FileSignatureState SignatureState = FileSignatureState.Unavailable);

internal static class FileAnalysisService
{
    private const long MaximumFileBytes = 512L * 1024 * 1024;
    private static readonly Guid VerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static Task<FileAnalysis?> AnalyzeAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Analyze(path, cancellationToken), cancellationToken);

    private static FileAnalysis? Analyze(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            // Hold a read lock so hashing and signature verification inspect the same bytes.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumFileBytes)
                return new("Unknown publisher", "Not checked", "Not computed",
                    "File exceeds the 512 MiB analysis limit.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            var started = System.Diagnostics.Stopwatch.StartNew();
            int count;
            while ((count = file.Read(buffer)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (started.Elapsed > TimeSpan.FromSeconds(10))
                    return new("Unknown publisher", "Not checked", "Not computed", "File analysis timed out while reading.");
                hash.AppendData(buffer.AsSpan(0, count));
            }
            var sha256 = Convert.ToHexString(hash.GetHashAndReset());
            cancellationToken.ThrowIfCancellationRequested();
            file.Position = 0;
            var status = VerifyEmbeddedSignature(path, file.SafeFileHandle.DangerousGetHandle());
            var (state, description) = DescribeSignature(status);
            var publisher = "Unknown publisher";
            if (state is not FileSignatureState.NoEmbeddedSignature and not FileSignatureState.Unavailable)
            {
                try
                {
                    // Extracting a PE signer never establishes trust; WinVerifyTrust checks the file above.
                    // LoadCertificateFromFile cannot extract a certificate from an executable.
#pragma warning disable SYSLIB0057
                    using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
                    using var certificate = X509CertificateLoader.LoadCertificate(signer.Export(X509ContentType.Cert));
                    publisher = certificate.GetNameInfo(X509NameType.SimpleName, false);
                }
                catch (CryptographicException) { }
            }
            return new(publisher, description, sha256,
                "Embedded Authenticode only; catalog signatures and online revocation are not checked.", state);
        }
        catch (FileNotFoundException) { return new("Unknown publisher", "Unavailable", "Unavailable", "File no longer exists."); }
        catch (DirectoryNotFoundException) { return new("Unknown publisher", "Unavailable", "Unavailable", "File no longer exists."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
            or DllNotFoundException or EntryPointNotFoundException or ArgumentException or System.Security.SecurityException)
        {
            return new("Unknown publisher", "Unavailable", "Unavailable", $"File analysis unavailable: {ex.Message}");
        }
    }

    internal static (FileSignatureState State, string Description) DescribeSignature(uint status) => status switch
    {
        0 => (FileSignatureState.Verified, "Valid embedded signature (offline)"),
        0x800B0100 => (FileSignatureState.NoEmbeddedSignature, "No embedded signature (catalog not checked)"),
        0x80096010 => (FileSignatureState.Invalid, "Invalid signature: file digest mismatch"),
        0x800B0111 => (FileSignatureState.Untrusted, "Signer explicitly distrusted"),
        0x800B0109 => (FileSignatureState.Untrusted, "Certificate root is not trusted"),
        0x800B0101 => (FileSignatureState.Untrusted, "Certificate expired or not yet valid"),
        0x800B010C => (FileSignatureState.Untrusted, "Certificate revoked"),
        0x800B0004 => (FileSignatureState.Untrusted, "Signature trust check failed"),
        0x800B0110 => (FileSignatureState.Untrusted, "Certificate is not valid for code signing"),
        _ => (FileSignatureState.Unavailable, $"Signature verification unavailable (0x{status:X8})")
    };

    private static uint VerifyEmbeddedSignature(string path, IntPtr handle)
    {
        var fileInfo = new WinTrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = path, FileHandle = handle
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(fileInfo, pointer, false);
        var data = new WinTrustData
        {
            Size = (uint)Marshal.SizeOf<WinTrustData>(), UiChoice = 2, UnionChoice = 1,
            FileInfo = pointer, StateAction = 1,
            // Offline, cache-only verification does not send the selected file or its hash anywhere.
            ProviderFlags = 0x1000 | 0x10
        };
        var action = VerifyV2;
        try { return WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
        finally
        {
            data.StateAction = 2;
            try { WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
            finally
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(pointer);
                Marshal.FreeHGlobal(pointer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern uint WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);
}

// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Selects a reviewed native HTTP/3 image by trusted absolute path and exact SHA-256.
    /// Directory permissions and release signature verification remain application responsibilities.
    /// </summary>
    public sealed class NativeHttp3Library
    {
        /// <summary>
        /// Creates an explicit asset selection without PATH or current-directory probing.
        /// </summary>
        public NativeHttp3Library(string absolutePath, ByteString sha256)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
            if (!Path.IsPathFullyQualified(absolutePath))
            {
                throw new ArgumentException("An absolute trusted native asset path is required.", nameof(absolutePath));
            }
            if (sha256.Length != 32)
            {
                throw new ArgumentException("The reviewed native SHA-256 is required.", nameof(sha256));
            }
            AbsolutePath = Path.GetFullPath(absolutePath);
            Sha256 = sha256.Copy();
        }

        /// <summary>
        /// Gets the trusted image path.
        /// </summary>
        public string AbsolutePath { get; }

        /// <summary>
        /// Gets the expected image hash.
        /// </summary>
        public ByteString Sha256 { get; }

        /// <summary>
        /// Selects the current RID's native image and caller-provisioned SHA-256 sidecar from a trusted directory.
        /// </summary>
        public static async ValueTask<NativeHttp3Library> FromDirectoryAsync(
            string absoluteDirectory,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(absoluteDirectory);
            if (!Path.IsPathFullyQualified(absoluteDirectory))
            {
                throw new ArgumentException("An absolute trusted asset directory is required.",
                    nameof(absoluteDirectory));
            }
            EnsureSupportedPlatform();
            string file = OperatingSystem.IsWindows() ? "opcua_http3.dll" : "libopcua_http3.so";
            string path = Path.Combine(absoluteDirectory, file);
            string digest = await File.ReadAllTextAsync(path + ".sha256", cancellationToken).ConfigureAwait(false);
            return new NativeHttp3Library(path, new ByteString(Convert.FromHexString(digest.Trim())));
        }

        /// <summary>
        /// Verifies the hash, nine required exports and exact ABI layouts before use.
        /// One immutable image selection is retained for process lifetime.
        /// </summary>
        public async ValueTask ValidateAsync(CancellationToken cancellationToken = default)
        {
            EnsureSupportedPlatform();
            if (AssemblyLoadContext.GetLoadContext(typeof(NativeHttp3Library).Assembly) != AssemblyLoadContext.Default)
            {
                throw new NotSupportedException("The native binding requires the default assembly load context.");
            }
            await s_loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (s_path != null)
                {
                    if (!string.Equals(s_path, AbsolutePath, OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || s_hash != Sha256)
                    {
                        throw new InvalidOperationException("Restart the process to select a different native image.");
                    }
                    if (!s_validated)
                    {
                        throw new InvalidOperationException(
                            "Native HTTP/3 initialization failed; restart is required.");
                    }
                    return;
                }
                using var stream = new FileStream(AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                byte[] actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(actual, Sha256.Span))
                {
                    throw new InvalidDataException("The native HTTP/3 image does not match its reviewed SHA-256.");
                }
                s_path = AbsolutePath;
                s_hash = Sha256;
                nint library = NativeLibrary.Load(AbsolutePath);
                foreach (string export in s_exports)
                {
                    if (!NativeLibrary.TryGetExport(library, export, out _))
                    {
                        throw new EntryPointNotFoundException("The native HTTP/3 export contract is incomplete.");
                    }
                }
                NativeLibrary.SetDllImportResolver(typeof(Http3Native).Assembly,
                    (name, _, _) => name == Http3Native.LibraryName
                        ? library : throw new DllNotFoundException("An unselected native library was requested."));
                Http3Native.ValidateLayout();
                s_validated = true;
            }
            finally
            {
                s_loadGate.Release();
            }
        }

        /// <summary>
        /// Restricts asset loading to the qualified architecture/libc family.
        /// </summary>
        private static void EnsureSupportedPlatform()
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
                RuntimeInformation.OSArchitecture != Architecture.X64 ||
                (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) ||
                RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.Ordinal))
            {
                throw new PlatformNotSupportedException(
                    "Native WebTransport assets target win-x64 and glibc linux-x64 only.");
            }
        }

        private static readonly SemaphoreSlim s_loadGate = new(1, 1);
        private static readonly string[] s_exports =
        [
            "opcua_http3_abi_version", "opcua_http3_api_revision", "opcua_http3_layout",
            "opcua_http3_create", "opcua_http3_command", "opcua_http3_poll",
            "opcua_http3_last_error", "opcua_http3_resource_snapshot", "opcua_http3_destroy"
        ];
        private static string? s_path;
        private static ByteString s_hash;
        private static bool s_validated;
    }
}

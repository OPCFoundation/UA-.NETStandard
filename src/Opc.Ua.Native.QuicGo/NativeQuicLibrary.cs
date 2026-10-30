// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Native.QuicGo
{
    /// <summary>
    /// Selects a reviewed native asset by absolute trusted path and exact SHA256.
    /// The application owns directory permissions and artifact/signature verification.
    /// </summary>
    public sealed class NativeQuicLibrary
    {
        /// <summary>
        /// Creates an explicit asset selection. There is no PATH/current-directory search.
        /// </summary>
        public NativeQuicLibrary(string absolutePath, ByteString sha256)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
            if (!Path.IsPathFullyQualified(absolutePath))
            {
                throw new ArgumentException(
                    "An absolute trusted native library path is required.", nameof(absolutePath));
            }
            if (sha256.Length != 32)
            {
                throw new ArgumentException("The reviewed native asset SHA256 is required.", nameof(sha256));
            }
            AbsolutePath = Path.GetFullPath(absolutePath);
            Sha256 = new ByteString(sha256.Span.ToArray());
        }

        /// <summary>
        /// Gets the trusted absolute image path.
        /// </summary>
        public string AbsolutePath { get; }

        /// <summary>
        /// Gets the reviewed expected binary SHA256, not an advisory suppression.
        /// </summary>
        public ByteString Sha256 { get; }

        /// <summary>
        /// Selects the platform asset and its pack-generated hash sidecar from a trusted directory.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public static async ValueTask<NativeQuicLibrary> FromDirectoryAsync(
            string absoluteDirectory, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(absoluteDirectory);
            if (!Path.IsPathFullyQualified(absoluteDirectory))
            {
                throw new ArgumentException(
                    "A trusted absolute asset directory is required.", nameof(absoluteDirectory));
            }
            EnsureSupportedPlatform();
            string file = OperatingSystem.IsWindows() ? "opcua_quic_go.dll" : "libopcua_quic_go.so";
            string path = Path.Combine(absoluteDirectory, file);
            string digest = await File.ReadAllTextAsync(path + ".sha256", cancellationToken).ConfigureAwait(false);
            return new NativeQuicLibrary(path, new ByteString(Convert.FromHexString(digest.Trim())));
        }

        /// <summary>
        /// Verifies and permanently loads a single compatible Go image into the default load context.
        /// A failed load/ABI check poisons the selection: restart rather than load a second Go runtime.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="InvalidDataException"></exception>
        internal async ValueTask<NativeApi> LoadAsync(CancellationToken cancellationToken)
        {
            EnsureSupportedPlatform();
            if (AssemblyLoadContext.GetLoadContext(typeof(NativeQuicLibrary).Assembly) != AssemblyLoadContext.Default)
            {
                throw new NotSupportedException(
                    "The native QUIC backend must be loaded in the default assembly context.");
            }
            await s_loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (s_selectedPath is not null)
                {
                    if (!string.Equals(s_selectedPath, AbsolutePath, OperatingSystem.IsWindows() ?
                        StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                        !s_selectedHash.Equals(Sha256))
                    {
                        throw new InvalidOperationException(
                            "One native Go image is permitted per process. Restart to upgrade.");
                    }
                    return s_api ??
                        throw new InvalidOperationException("Native image initialization failed; restart is required.");
                }
                using var stream = new FileStream(AbsolutePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                byte[] actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(actual, Sha256.Span))
                {
                    throw new InvalidDataException("Native QUIC asset does not match its reviewed SHA256.");
                }
                s_selectedPath = AbsolutePath;
                s_selectedHash = Sha256;
                s_api = new NativeApi(AbsolutePath);
                return s_api;
            }
            finally
            {
                s_loadGate.Release();
            }
        }

        /// <summary>
        /// Restricts runtime loading to the reviewed x64 Windows/Linux matrix.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException"></exception>
        private static void EnsureSupportedPlatform()
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
                (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            {
                throw new PlatformNotSupportedException("Native QUIC assets support only win-x64 and glibc linux-x64.");
            }
        }

        private static readonly SemaphoreSlim s_loadGate = new(1, 1);
        private static string? s_selectedPath;
        private static ByteString s_selectedHash;
        private static NativeApi? s_api;
    }
}

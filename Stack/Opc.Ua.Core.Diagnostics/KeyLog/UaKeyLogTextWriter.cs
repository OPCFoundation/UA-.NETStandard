/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.KeyLog
{
    /// <summary>
    /// Writes OPC UA channel key material in a Wireshark-style text format.
    /// </summary>
    public sealed class UaKeyLogTextWriter : IKeyLogWriter
    {
        /// <summary>
        /// Default maximum bytes per keylog file.
        /// </summary>
        public const long DefaultMaxBytesPerKeylog = 8L * 1024 * 1024;

        /// <summary>
        /// Default maximum number of retained artifacts per session.
        /// </summary>
        public const int DefaultMaxArtifactsPerSession = 16;

        /// <summary>
        /// Constructs a text key-log writer for the supplied file.
        /// </summary>
        public UaKeyLogTextWriter(string filePath)
            : this(filePath, sessionKey: null, DefaultMaxBytesPerKeylog, DefaultMaxArtifactsPerSession)
        {
        }

        /// <summary>
        /// Constructs a text key-log writer for the supplied file.
        /// </summary>
        public UaKeyLogTextWriter(string filePath, byte[]? sessionKey)
            : this(filePath, sessionKey, DefaultMaxBytesPerKeylog, DefaultMaxArtifactsPerSession)
        {
        }

        /// <summary>
        /// Constructs a text key-log writer for the supplied file.
        /// </summary>
        public UaKeyLogTextWriter(string filePath, long maxBytes, int maxArtifacts)
            : this(filePath, sessionKey: null, maxBytes, maxArtifacts)
        {
        }

        /// <summary>
        /// Constructs a text key-log writer for the supplied file.
        /// </summary>
        public UaKeyLogTextWriter(string filePath, byte[]? sessionKey, long maxBytes, int maxArtifacts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(maxArtifacts);

            FilePath = filePath;
            m_maxBytes = maxBytes;
            m_maxArtifacts = maxArtifacts;
            m_sessionKey = sessionKey is null ? null : (byte[])sessionKey.Clone();
            OpenCurrentFile(FileMode.OpenOrCreate);
        }

        /// <inheritdoc/>
        public string FilePath { get; }

        /// <inheritdoc/>
        public async ValueTask AppendAsync(ChannelKeyMaterial material, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(material);
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_needsHeader)
                {
                    await m_writer.WriteLineAsync("# OPC UA channel key log v1".AsMemory(), ct).ConfigureAwait(false);
                    await m_writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, ct).ConfigureAwait(false);
                    m_needsHeader = false;
                }

                await m_writer.WriteLineAsync(FormatLine(material).AsMemory(), ct).ConfigureAwait(false);
                await m_writer.FlushAsync(ct).ConfigureAwait(false);
                await m_stream.FlushAsync(ct).ConfigureAwait(false);
                m_bytesWritten = m_fileStream.Length;
                if (m_bytesWritten >= m_maxBytes && m_maxBytes > 0)
                {
                    await RotateAsync(ct).ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask FlushAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await FlushCoreAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (m_disposed)
                {
                    return;
                }

                await FlushCoreAsync(CancellationToken.None).ConfigureAwait(false);
                m_disposed = true;
                await m_writer.DisposeAsync().ConfigureAwait(false);
                await m_stream.DisposeAsync().ConfigureAwait(false);
                await m_fileStream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
                m_gate.Dispose();
            }
        }

        internal static string FormatLine(ChannelKeyMaterial material)
        {
            return string.Join(
                ' ',
                "OPCUA_CHANNEL",
                string.Create(CultureInfo.InvariantCulture, $"0x{material.ChannelId:X}"),
                string.Create(CultureInfo.InvariantCulture, $"0x{material.TokenId:X}"),
                material.SecurityPolicyUri,
                material.SecurityMode.ToString(),
                ToHex(material.ClientSigningKey),
                ToHex(material.ClientEncryptingKey),
                ToHex(material.ClientInitializationVector),
                ToHex(material.ServerSigningKey),
                ToHex(material.ServerEncryptingKey),
                ToHex(material.ServerInitializationVector));
        }

        private void OpenCurrentFile(FileMode fileMode)
        {
            string currentPath = GetCurrentFilePath();
            m_fileStream = new FileStream(
                currentPath,
                fileMode,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(currentPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            m_needsHeader = m_fileStream.Length == 0;
            m_fileStream.Seek(0, SeekOrigin.End);
            m_bytesWritten = m_fileStream.Length;
            m_stream = m_sessionKey is null
                ? m_fileStream
                : new EncryptedKeyLogStream(m_fileStream, m_sessionKey, leaveOpen: false);
            m_writer = new StreamWriter(m_stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
        }

        private async ValueTask FlushCoreAsync(CancellationToken ct)
        {
            await m_writer.FlushAsync(ct).ConfigureAwait(false);
            await m_stream.FlushAsync(ct).ConfigureAwait(false);
        }

        private async ValueTask RotateAsync(CancellationToken ct)
        {
            await FlushCoreAsync(ct).ConfigureAwait(false);
            await m_writer.DisposeAsync().ConfigureAwait(false);
            await m_stream.DisposeAsync().ConfigureAwait(false);
            await m_fileStream.DisposeAsync().ConfigureAwait(false);
            m_currentSuffix++;
            OpenCurrentFile(FileMode.Create);
            PruneArtifacts();
        }

        private void PruneArtifacts()
        {
            if (m_maxArtifacts == 0)
            {
                return;
            }

            var artifacts = new List<ArtifactFile>();
            if (File.Exists(FilePath))
            {
                artifacts.Add(new ArtifactFile(0, FilePath));
            }

            string? directory = Path.GetDirectoryName(FilePath);
            string searchDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
            string baseName = Path.GetFileNameWithoutExtension(FilePath);
            string extension = Path.GetExtension(FilePath);
            foreach (string filePath in Directory.GetFiles(searchDirectory, baseName + ".*" + extension))
            {
                if (TryGetSuffix(filePath, out int suffix))
                {
                    artifacts.Add(new ArtifactFile(suffix, filePath));
                }
            }

            artifacts.Sort(static (left, right) => right.Suffix.CompareTo(left.Suffix));
            int retainedArtifacts = 0;
            foreach (ArtifactFile artifact in artifacts)
            {
                if (artifact.Suffix == m_currentSuffix)
                {
                    continue;
                }

                retainedArtifacts++;
                if (retainedArtifacts >= m_maxArtifacts)
                {
                    File.Delete(artifact.FilePath);
                }
            }
        }

        private bool TryGetSuffix(string filePath, out int suffix)
        {
            suffix = 0;
            string fileName = Path.GetFileName(filePath);
            string baseName = Path.GetFileNameWithoutExtension(FilePath);
            string extension = Path.GetExtension(FilePath);
            if (!fileName.StartsWith(baseName + ".", StringComparison.Ordinal) ||
                !fileName.EndsWith(extension, StringComparison.Ordinal))
            {
                return false;
            }

            string suffixText = fileName.Substring(
                baseName.Length + 1,
                fileName.Length - baseName.Length - extension.Length - 1);
            return int.TryParse(suffixText, out suffix);
        }

        private string GetCurrentFilePath()
        {
            if (m_currentSuffix == 0)
            {
                return FilePath;
            }

            string? directory = Path.GetDirectoryName(FilePath);
            string fileName = Path.GetFileNameWithoutExtension(FilePath);
            string extension = Path.GetExtension(FilePath);
            string suffix = m_currentSuffix.ToString("000", CultureInfo.InvariantCulture);
            return Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, fileName + "." + suffix + extension);
        }

        private static string ToHex(byte[]? value)
        {
            return value is { Length: > 0 } ? Convert.ToHexString(value) : "-";
        }

        private readonly struct ArtifactFile
        {
            public ArtifactFile(int suffix, string filePath)
            {
                Suffix = suffix;
                FilePath = filePath;
            }

            public int Suffix { get; }

            public string FilePath { get; }
        }

        private FileStream m_fileStream = null!;
        private Stream m_stream = null!;
        private StreamWriter m_writer = null!;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly byte[]? m_sessionKey;
        private readonly long m_maxBytes;
        private readonly int m_maxArtifacts;
        private int m_currentSuffix;
        private long m_bytesWritten;
        private bool m_disposed;
        private bool m_needsHeader;
    }
}

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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Capture;

namespace Opc.Ua.Pcap.Frame
{
    /// <summary>
    /// Writes little-endian libpcap files.
    /// </summary>
    public sealed class PcapFileWriter : IAsyncDisposable
    {
        /// <summary>
        /// BSD loopback link type.
        /// </summary>
        public const uint LinkTypeNull = 0;

        /// <summary>
        /// Ethernet link type.
        /// </summary>
        public const uint LinkTypeEthernet = 1;

        /// <summary>
        /// Raw IPv4 link type.
        /// </summary>
        public const uint LinkTypeRaw = 101;

        /// <summary>
        /// IPv4 link type.
        /// </summary>
        public const uint LinkTypeIPv4 = 228;

        /// <summary>
        /// Default maximum bytes per pcap capture file.
        /// </summary>
        public const long DefaultMaxBytesPerCapture = 256L * 1024 * 1024;

        /// <summary>
        /// Default maximum number of retained artifacts per session.
        /// </summary>
        public const int DefaultMaxArtifactsPerSession = 16;

        /// <summary>
        /// Constructs a pcap writer and writes the global header.
        /// </summary>
        public PcapFileWriter(string filePath, uint linkType, uint snapLen = 65535)
            : this(filePath, linkType, snapLen, DefaultMaxBytesPerCapture, DefaultMaxArtifactsPerSession)
        {
        }

        /// <summary>
        /// Constructs a pcap writer and writes the global header.
        /// </summary>
        public PcapFileWriter(string filePath, uint linkType, long maxBytes, int maxArtifacts)
            : this(filePath, linkType, 65535, maxBytes, maxArtifacts)
        {
        }

        /// <summary>
        /// Constructs a pcap writer and writes the global header.
        /// </summary>
        public PcapFileWriter(string filePath, uint linkType, uint snapLen, long maxBytes, int maxArtifacts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(maxArtifacts);

            m_filePath = filePath;
            m_linkType = linkType;
            m_snapLen = snapLen;
            m_maxBytes = maxBytes;
            m_maxArtifacts = maxArtifacts;
            m_stream = OpenCurrentFile();
        }

        /// <summary>
        /// Writes one packet record.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException"></exception>
        public async ValueTask WriteAsync(
            DateTimeOffset timestamp,
            ReadOnlyMemory<byte> packetData,
            CancellationToken ct)
        {
            if (packetData.Length > m_snapLen)
            {
                throw new PcapDiagnosticsException("Packet exceeds the configured pcap snap length.");
            }

            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                long micros = (timestamp.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) / 10;
                byte[] header = new byte[16];
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(), checked((uint)(micros / 1_000_000L)));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)(micros % 1_000_000L)));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), checked((uint)packetData.Length));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), checked((uint)packetData.Length));
                await m_stream.WriteAsync(header, ct).ConfigureAwait(false);
                await m_stream.WriteAsync(packetData, ct).ConfigureAwait(false);
                m_bytesWritten += header.Length + packetData.Length;
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
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                m_disposed = true;
                await m_stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                await m_stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
                m_gate.Dispose();
            }
        }

        private static void WriteGlobalHeader(Stream stream, uint linkType, uint snapLen)
        {
            Span<byte> header = stackalloc byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(header, 0xA1B2C3D4U);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 2);
            BinaryPrimitives.WriteUInt16LittleEndian(header[6..], 4);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], snapLen);
            BinaryPrimitives.WriteUInt32LittleEndian(header[20..], linkType);
            stream.Write(header);
        }

        private FileStream OpenCurrentFile()
        {
            string currentPath = GetCurrentFilePath();
            FileStream stream = new(
                currentPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(currentPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            WriteGlobalHeader(stream, m_linkType, m_snapLen);
            m_bytesWritten = PcapGlobalHeaderLength;
            return stream;
        }

        private async ValueTask RotateAsync(CancellationToken ct)
        {
            await m_stream.FlushAsync(ct).ConfigureAwait(false);
            await m_stream.DisposeAsync().ConfigureAwait(false);
            m_currentSuffix++;
            m_stream = OpenCurrentFile();
            PruneArtifacts();
        }

        private void PruneArtifacts()
        {
            if (m_maxArtifacts == 0)
            {
                return;
            }

            var artifacts = new List<ArtifactFile>();
            if (File.Exists(m_filePath))
            {
                artifacts.Add(new ArtifactFile(0, m_filePath));
            }

            string? directory = Path.GetDirectoryName(m_filePath);
            string searchDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
            string baseName = Path.GetFileNameWithoutExtension(m_filePath);
            string extension = Path.GetExtension(m_filePath);
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
            string baseName = Path.GetFileNameWithoutExtension(m_filePath);
            string extension = Path.GetExtension(m_filePath);
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
                return m_filePath;
            }

            string? directory = Path.GetDirectoryName(m_filePath);
            string fileName = Path.GetFileNameWithoutExtension(m_filePath);
            string extension = Path.GetExtension(m_filePath);
            string suffix = m_currentSuffix.ToString("000", CultureInfo.InvariantCulture);
            return Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, fileName + "." + suffix + extension);
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

        private const int PcapGlobalHeaderLength = 24;
        private FileStream m_stream;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly string m_filePath;
        private readonly uint m_linkType;
        private readonly uint m_snapLen;
        private readonly long m_maxBytes;
        private readonly int m_maxArtifacts;
        private int m_currentSuffix;
        private long m_bytesWritten;
        private bool m_disposed;
    }
}

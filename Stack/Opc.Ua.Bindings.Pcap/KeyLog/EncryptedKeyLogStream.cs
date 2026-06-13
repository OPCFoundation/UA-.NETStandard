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
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings.Pcap.KeyLog
{
    /// <summary>
    /// Encrypts newline-delimited key-log payloads as AES-256-GCM records.
    /// </summary>
    internal sealed class EncryptedKeyLogStream : Stream
    {
        /// <summary>
        /// Constructs an AES-256-GCM wrapper around <paramref name="inner"/>
        /// keyed by <paramref name="sessionKey"/>.
        /// </summary>
        public EncryptedKeyLogStream(Stream inner, byte[] sessionKey, bool leaveOpen)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(sessionKey);
            if (sessionKey.Length != SessionKeyManager.KeySizeInBytes)
            {
                throw new ArgumentException("The session key must be 32 bytes.", nameof(sessionKey));
            }

            m_inner = inner;
            m_aesGcm = new AesGcm(sessionKey, TagSizeInBytes);
            m_leaveOpen = leaveOpen;
        }

        /// <inheritdoc/>
        public override bool CanRead => m_inner.CanRead;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => m_inner.CanWrite;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            FlushPendingWrite();
            m_inner.Flush();
        }

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushPendingWrite();
            return m_inner.FlushAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBuffer(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length == 0)
            {
                return 0;
            }

            if (!EnsurePlaintextRecord())
            {
                return 0;
            }

            return CopyPlaintext(buffer);
        }

        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0)
            {
                return 0;
            }

            if (!await EnsurePlaintextRecordAsync(cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }

            return CopyPlaintext(buffer.Span);
        }

        /// <inheritdoc/>
        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ValidateBuffer(buffer, offset, count);
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBuffer(buffer, offset, count);
            Write(buffer.AsSpan(offset, count));
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                int newlineIndex = buffer.IndexOf((byte)'\n');
                int segmentLength = newlineIndex < 0 ? buffer.Length : newlineIndex + 1;
                AppendPendingWrite(buffer[..segmentLength]);
                buffer = buffer[segmentLength..];

                if (newlineIndex >= 0)
                {
                    FlushPendingWrite();
                }
            }
        }

        /// <inheritdoc/>
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ValidateBuffer(buffer, offset, count);
            return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FlushPendingWrite();
                m_aesGcm.Dispose();
                if (!m_leaveOpen)
                {
                    m_inner.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            FlushPendingWrite();
            m_aesGcm.Dispose();
            if (!m_leaveOpen)
            {
                await m_inner.DisposeAsync().ConfigureAwait(false);
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }

        private static void ValidateBuffer(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - offset < count)
            {
                throw new ArgumentException("The buffer range is invalid.", nameof(count));
            }
        }

        private void AppendPendingWrite(ReadOnlySpan<byte> bytes)
        {
            EnsureWriteBuffer(bytes.Length);
            bytes.CopyTo(m_writeBuffer.AsSpan(m_writeCount));
            m_writeCount += bytes.Length;
        }

        private void EnsureWriteBuffer(int additionalBytes)
        {
            int requiredLength = m_writeCount + additionalBytes;
            if (m_writeBuffer.Length >= requiredLength)
            {
                return;
            }

            int newLength = Math.Max(requiredLength, Math.Max(DefaultBufferSize, m_writeBuffer.Length * 2));
            Array.Resize(ref m_writeBuffer, newLength);
        }

        private void FlushPendingWrite()
        {
            if (m_writeCount == 0)
            {
                return;
            }

            WriteEncryptedRecord(m_writeBuffer.AsSpan(0, m_writeCount));
            m_writeCount = 0;
        }

        private void WriteEncryptedRecord(ReadOnlySpan<byte> plaintext)
        {
            int recordLength = NonceSizeInBytes + plaintext.Length + TagSizeInBytes;
            Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, recordLength);
            m_inner.Write(lengthBytes);

            byte[] record = new byte[recordLength];
            Span<byte> nonce = record.AsSpan(0, NonceSizeInBytes);
            RandomNumberGenerator.Fill(nonce);
            Span<byte> ciphertext = record.AsSpan(NonceSizeInBytes, plaintext.Length);
            Span<byte> tag = record.AsSpan(NonceSizeInBytes + plaintext.Length, TagSizeInBytes);
            m_aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
            m_inner.Write(record);
        }

        private bool EnsurePlaintextRecord()
        {
            if (m_readBufferOffset < m_readBuffer.Length)
            {
                return true;
            }

            Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
            int read = ReadAtMost(lengthBytes);
            if (read == 0)
            {
                return false;
            }
            if (read != lengthBytes.Length)
            {
                throw new InvalidDataException("Encrypted key-log record length is incomplete.");
            }

            int recordLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            m_readBuffer = ReadEncryptedRecord(recordLength);
            m_readBufferOffset = 0;
            return true;
        }

        private async ValueTask<bool> EnsurePlaintextRecordAsync(CancellationToken cancellationToken)
        {
            if (m_readBufferOffset < m_readBuffer.Length)
            {
                return true;
            }

            byte[] lengthBytes = new byte[sizeof(int)];
            int read = await ReadAtMostAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }
            if (read != lengthBytes.Length)
            {
                throw new InvalidDataException("Encrypted key-log record length is incomplete.");
            }

            int recordLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            m_readBuffer = await ReadEncryptedRecordAsync(recordLength, cancellationToken).ConfigureAwait(false);
            m_readBufferOffset = 0;
            return true;
        }

        private byte[] ReadEncryptedRecord(int recordLength)
        {
            ValidateRecordLength(recordLength);
            byte[] record = new byte[recordLength];
            ReadExactly(record);
            return DecryptRecord(record);
        }

        private async ValueTask<byte[]> ReadEncryptedRecordAsync(int recordLength, CancellationToken cancellationToken)
        {
            ValidateRecordLength(recordLength);
            byte[] record = new byte[recordLength];
            await ReadExactlyAsync(record, cancellationToken).ConfigureAwait(false);
            return DecryptRecord(record);
        }

        private static void ValidateRecordLength(int recordLength)
        {
            if (recordLength < NonceSizeInBytes + TagSizeInBytes)
            {
                throw new InvalidDataException("Encrypted key-log record length is invalid.");
            }
        }

        private byte[] DecryptRecord(byte[] record)
        {
            ReadOnlySpan<byte> nonce = record.AsSpan(0, NonceSizeInBytes);
            int ciphertextLength = record.Length - NonceSizeInBytes - TagSizeInBytes;
            ReadOnlySpan<byte> ciphertext = record.AsSpan(NonceSizeInBytes, ciphertextLength);
            ReadOnlySpan<byte> tag = record.AsSpan(NonceSizeInBytes + ciphertextLength, TagSizeInBytes);
            byte[] plaintext = new byte[ciphertextLength];
            m_aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }

        private int CopyPlaintext(Span<byte> destination)
        {
            int bytesToCopy = Math.Min(destination.Length, m_readBuffer.Length - m_readBufferOffset);
            m_readBuffer.AsSpan(m_readBufferOffset, bytesToCopy).CopyTo(destination);
            m_readBufferOffset += bytesToCopy;
            return bytesToCopy;
        }

        private int ReadAtMost(Span<byte> buffer)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = m_inner.Read(buffer[totalRead..]);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return totalRead;
        }

        private async ValueTask<int> ReadAtMostAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = await m_inner.ReadAsync(buffer[totalRead..], cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return totalRead;
        }

        private void ReadExactly(byte[] buffer)
        {
            if (ReadAtMost(buffer) != buffer.Length)
            {
                throw new EndOfStreamException("Encrypted key-log record ended unexpectedly.");
            }
        }

        private async ValueTask ReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            if (await ReadAtMostAsync(buffer, cancellationToken).ConfigureAwait(false) != buffer.Length)
            {
                throw new EndOfStreamException("Encrypted key-log record ended unexpectedly.");
            }
        }

        private const int DefaultBufferSize = 1024;
        private const int NonceSizeInBytes = 12;
        private const int TagSizeInBytes = 16;
        private readonly AesGcm m_aesGcm;
        private readonly Stream m_inner;
        private readonly bool m_leaveOpen;
        private byte[] m_readBuffer = Array.Empty<byte>();
        private int m_readBufferOffset;
        private byte[] m_writeBuffer = Array.Empty<byte>();
        private int m_writeCount;
    }
}

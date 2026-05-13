/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// Strongly-typed handle to a single OPC UA <c>FileType</c>
    /// instance. Mirrors <see cref="System.IO.FileInfo"/>.
    /// </summary>
    /// <remarks>
    /// File metadata properties (<see cref="Size"/>,
    /// <see cref="Writable"/>, <see cref="MimeType"/>, …) are populated
    /// lazily by <see cref="RefreshAsync"/>; before the first call they
    /// return their default values. The property
    /// <see cref="Writable"/> / <see cref="UserWritable"/> are advisory
    /// only (TOCTOU-prone): rely on the server's
    /// <see cref="OpenAsync(UaFileMode, CancellationToken)"/> response
    /// to determine whether write access is actually granted.
    /// </remarks>
    public sealed class UaFileInfo : UaFileSystemInfo
    {
        internal UaFileInfo(
            FileSystemClient owner,
            UaDirectoryInfo? parent,
            NodeId nodeId,
            QualifiedName browseName,
            IReadOnlyList<QualifiedName> segments)
            : base(owner, parent, nodeId, browseName, segments)
        {
            Proxy = new FileTypeClient(
                owner.Session,
                nodeId,
                owner.Session.MessageContext.Telemetry);
        }

        /// <inheritdoc/>
        public override bool IsDirectory => false;

        /// <summary>The cached <c>Size</c> property in bytes.</summary>
        public ulong Size { get; private set; }

        /// <summary>The cached <c>Writable</c> property (advisory).</summary>
        public bool Writable { get; private set; }

        /// <summary>The cached <c>UserWritable</c> property (advisory).</summary>
        public bool UserWritable { get; private set; }

        /// <summary>The cached <c>OpenCount</c> property.</summary>
        public ushort OpenCount { get; private set; }

        /// <summary>The cached <c>MimeType</c> property; <c>null</c>
        /// when the server does not expose it.</summary>
        public string? MimeType { get; private set; }

        /// <summary>The cached <c>MaxByteStringLength</c> property;
        /// <c>null</c> when the server does not expose it.</summary>
        public uint? MaxByteStringLength { get; private set; }

        /// <summary>The cached <c>LastModifiedTime</c> property;
        /// <c>null</c> when the server does not expose it.</summary>
        public DateTime? LastModifiedTime { get; private set; }

        /// <summary>
        /// The underlying <see cref="FileTypeClient"/> proxy.
        /// Exposed so advanced callers can reach the raw method
        /// wrappers when the high-level API does not suffice.
        /// </summary>
        public FileTypeClient Proxy { get; }

        /// <inheritdoc/>
        public override async ValueTask RefreshAsync(CancellationToken ct = default)
        {
            FileMetadata metadata = await Owner
                .ReadFileMetadataAsync(NodeId, FullPath, ct)
                .ConfigureAwait(false);

            Size = metadata.Size ?? 0UL;
            Writable = metadata.Writable ?? false;
            UserWritable = metadata.UserWritable ?? false;
            OpenCount = metadata.OpenCount ?? (ushort)0;
            MimeType = metadata.MimeType;
            MaxByteStringLength = metadata.MaxByteStringLength;
            LastModifiedTime = metadata.LastModifiedTime;
        }

        /// <summary>
        /// Opens this file with the supplied <paramref name="mode"/>.
        /// </summary>
        /// <param name="mode">The combined open-mode flags.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A <see cref="UaFileStream"/> wrapping the server
        /// handle.</returns>
        /// <exception cref="ArgumentException"><paramref name="mode"/></exception>
        public async ValueTask<UaFileStream> OpenAsync(
            UaFileMode mode,
            CancellationToken ct = default)
        {
            if (mode == 0)
            {
                throw new ArgumentException(
                    "UaFileMode must include at least one of Read or Write.",
                    nameof(mode));
            }

            uint handle;
            try
            {
                handle = await Proxy.OpenAsync((byte)mode, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                throw FileSystemErrors.Translate(ex, FullPath, targetIsDirectory: false);
            }

            long initialPosition = 0;
            long initialLength = (long)Size;

            try
            {
                if ((mode & UaFileMode.Append) != 0)
                {
                    // Spec leaves this server-defined; query for accuracy.
                    ulong serverPos = await Proxy.GetPositionAsync(handle, ct)
                        .ConfigureAwait(false);
                    initialPosition = checked((long)serverPos);
                    if (initialPosition > initialLength)
                    {
                        initialLength = initialPosition;
                    }
                }
                else if ((mode & UaFileMode.EraseExisting) != 0)
                {
                    initialLength = 0;
                }
            }
            catch
            {
                // If GetPosition fails after a successful Open we still
                // own the handle and must release it.
                try
                {
                    await Proxy.CloseAsync(handle, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup.
                }
                throw;
            }

            int chunk = Owner.Options.ChunkSize;
            if (MaxByteStringLength is uint cap && cap > 0 && cap < int.MaxValue)
            {
                chunk = Math.Min(chunk, (int)cap);
            }

            return new UaFileStream(
                Proxy,
                handle,
                mode,
                initialLength,
                initialPosition,
                chunk);
        }

        /// <summary>
        /// Equivalent to <see cref="OpenAsync"/> with
        /// <see cref="UaFileMode.Read"/>.
        /// </summary>
        public ValueTask<UaFileStream> OpenReadAsync(CancellationToken ct = default)
        {
            return OpenAsync(UaFileMode.Read, ct);
        }

        /// <summary>
        /// Equivalent to <see cref="OpenAsync"/> with
        /// <see cref="UaFileMode.Write"/> | <see cref="UaFileMode.EraseExisting"/>
        /// (truncates).
        /// </summary>
        public ValueTask<UaFileStream> OpenWriteAsync(CancellationToken ct = default)
        {
            return OpenAsync(UaFileMode.Write | UaFileMode.EraseExisting, ct);
        }

        /// <summary>
        /// Equivalent to <see cref="OpenAsync"/> with
        /// <see cref="UaFileMode.Write"/> | <see cref="UaFileMode.Append"/>.
        /// </summary>
        public ValueTask<UaFileStream> OpenAppendAsync(CancellationToken ct = default)
        {
            return OpenAsync(UaFileMode.Write | UaFileMode.Append, ct);
        }

        /// <summary>
        /// Reads the entire file contents into memory. Throws when the
        /// file size exceeds
        /// <see cref="FileSystemClientOptions.MaxBufferedReadSize"/>.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<byte[]> ReadAllBytesAsync(CancellationToken ct = default)
        {
            UaFileStream stream = await OpenReadAsync(ct).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                long length = stream.Length;
                if (length > Owner.Options.MaxBufferedReadSize)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "File '{0}' size ({1}) exceeds MaxBufferedReadSize ({2}).",
                        FullPath,
                        length,
                        Owner.Options.MaxBufferedReadSize);
                }

                using var ms = new MemoryStream(length > 0 ? (int)length : 0);
                int rentSize = length > 0
                    ? (int)Math.Min(length, Owner.Options.ChunkSize)
                    : Owner.Options.ChunkSize;
                byte[] rented = ArrayPool<byte>.Shared.Rent(rentSize);
                try
                {
                    while (true)
                    {
#if NETSTANDARD2_1_OR_GREATER || NET
                        int read = await stream.ReadAsync(rented.AsMemory(), ct)
                            .ConfigureAwait(false);
#else
                        int read = await stream.ReadAsync(rented, 0, rented.Length, ct)
                            .ConfigureAwait(false);
#endif
                        if (read == 0)
                        {
                            break;
                        }
                        ms.Write(rented, 0, read);
                        if (ms.Length > Owner.Options.MaxBufferedReadSize)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadEncodingLimitsExceeded,
                                "File '{0}' size exceeds MaxBufferedReadSize ({1}).",
                                FullPath,
                                Owner.Options.MaxBufferedReadSize);
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Reads the entire file contents and decodes it as text using
        /// the supplied <paramref name="encoding"/> (UTF-8 by default).
        /// </summary>
        public async ValueTask<string> ReadAllTextAsync(
            Encoding? encoding = null,
            CancellationToken ct = default)
        {
            byte[] bytes = await ReadAllBytesAsync(ct).ConfigureAwait(false);
            return (encoding ?? Encoding.UTF8).GetString(bytes);
        }

        /// <summary>
        /// Truncates the file and writes <paramref name="bytes"/> as
        /// the new contents.
        /// </summary>
        public async ValueTask WriteAllBytesAsync(
            ReadOnlyMemory<byte> bytes,
            CancellationToken ct = default)
        {
            UaFileStream stream = await OpenWriteAsync(ct).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                if (!bytes.IsEmpty)
                {
#if NETSTANDARD2_1_OR_GREATER || NET
                    await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
#else
                    byte[] buffer = bytes.ToArray();
                    await stream.WriteAsync(buffer, 0, buffer.Length, ct)
                        .ConfigureAwait(false);
#endif
                }
            }
        }

        /// <summary>
        /// Truncates the file and writes <paramref name="contents"/>
        /// using the supplied <paramref name="encoding"/> (UTF-8 by
        /// default).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="contents"/> is <c>null</c>.</exception>
        public ValueTask WriteAllTextAsync(
            string contents,
            Encoding? encoding = null,
            CancellationToken ct = default)
        {
            if (contents == null)
            {
                throw new ArgumentNullException(nameof(contents));
            }
            byte[] bytes = (encoding ?? Encoding.UTF8).GetBytes(contents);
            return WriteAllBytesAsync(bytes, ct);
        }
    }
}

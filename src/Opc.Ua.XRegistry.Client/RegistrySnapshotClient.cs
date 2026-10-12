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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Client
{
    /// <summary>
    /// A Session-bound native snapshot lease. Paths, child descriptors and values stay native:
    /// this client never reconstructs JSON or parses an owning schema document.
    /// </summary>
    public sealed class RegistrySnapshotClient : IAsyncDisposable
    {
        private RegistrySnapshotClient(
            NativeRegistryAccessTypeClient access,
            RegistrySnapshotOpenResultDataType opened,
            uint maxItems,
            uint maxBytes)
        {
            m_access = access;
            m_opened = opened;
            m_maxItems = maxItems;
            m_maxBytes = maxBytes;
        }

        /// <summary>Gets the pinned entity epoch.</summary>
        public uint TargetEpoch => m_opened.TargetEpoch;

        /// <summary>Gets the registry epoch captured with the selected entity.</summary>
        public uint RegistryEpoch => m_opened.RegistryEpoch;

        /// <summary>
        /// Opens a native lease with bounds obtained from the server's advertised SnapshotLimits.
        /// </summary>
        public static async ValueTask<RegistrySnapshotClient> OpenAsync(
            NativeRegistryAccessTypeClient access,
            RegistrySnapshotOpenRequestDataType request,
            uint maxItems,
            uint maxBytes,
            CancellationToken cancellationToken = default)
        {
            if (access is null)
            {
                throw new ArgumentNullException(nameof(access));
            }
            if (maxItems == 0 || maxBytes == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems));
            }
            RegistrySnapshotOpenResultDataType opened = await access.OpenDocumentAsync(request, cancellationToken)
                .ConfigureAwait(false);
            Check(opened.StatusCode);
#pragma warning disable CA2000 // The caller owns the returned asynchronous snapshot lease.
            return new RegistrySnapshotClient(access, opened, maxItems, maxBytes);
#pragma warning restore CA2000
        }

        /// <summary>
        /// Reads every bounded part of a subtree or leaf. Continuations are consumed once;
        /// the caller traverses Structure and array children using their native descriptors.
        /// </summary>
        public IAsyncEnumerable<RegistrySnapshotReadResultDataType> ReadPartsAsync(
            ArrayOf<RegistryPathElementDataType> path,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref m_closed) != 0)
            {
                throw new ObjectDisposedException(nameof(RegistrySnapshotClient));
            }
            return ReadPartsCoreAsync(path, cancellationToken);
        }

        private async IAsyncEnumerable<RegistrySnapshotReadResultDataType> ReadPartsCoreAsync(
            ArrayOf<RegistryPathElementDataType> path,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ulong offset = 0;
            ulong? total = null;
            ByteString continuation = ByteString.Empty;
            while (true)
            {
                RegistrySnapshotReadResultDataType result = await m_access.ReadDocumentPartAsync(
                    new RegistrySnapshotReadRequestDataType
                    {
                        SnapshotId = m_opened.SnapshotId,
                        Path = path,
                        Offset = offset,
                        MaxItems = m_maxItems,
                        MaxBytes = m_maxBytes,
                        ContinuationPoint = continuation
                    }, cancellationToken).ConfigureAwait(false);
                Check(result.StatusCode);
                if (result.Offset != offset || total.HasValue && total.Value != result.TotalLength)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "The native snapshot range changed during a pinned read.");
                }
                total = result.TotalLength;
                yield return result;
                if (result.Complete)
                {
                    yield break;
                }
                ulong count = Count(result);
                if (count == 0 || result.ContinuationPoint.IsNull || result.ContinuationPoint.Length == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "An incomplete native snapshot made no progress.");
                }
                offset = checked(offset + count);
                continuation = result.ContinuationPoint;
            }
        }

        /// <summary>
        /// Reads one native String leaf, preserving Unicode scalar boundaries.
        /// </summary>
        public async ValueTask<string> ReadStringAsync(
            ArrayOf<RegistryPathElementDataType> path,
            CancellationToken cancellationToken = default)
        {
            var value = new StringBuilder();
            await foreach (RegistrySnapshotReadResultDataType part in ReadPartsAsync(path, cancellationToken)
                .ConfigureAwait(false))
            {
                if (part.Kind != 1 || !part.Value.TryGetValue(out string text))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The selected native leaf is not a String.");
                }
                value.Append(text);
            }
            return value.ToString();
        }

        /// <summary>
        /// Reads one native ByteString leaf. Fingerprints and protocol octets stay binary.
        /// </summary>
        public async ValueTask<ByteString> ReadByteStringAsync(
            ArrayOf<RegistryPathElementDataType> path,
            CancellationToken cancellationToken = default)
        {
            using var value = new MemoryStream();
            await foreach (RegistrySnapshotReadResultDataType part in ReadPartsAsync(path, cancellationToken)
                .ConfigureAwait(false))
            {
                if (part.Kind != 2 || !part.Value.TryGetValue(out ByteString bytes))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "The selected native leaf is not a ByteString.");
                }
                byte[] next = bytes.ToArray();
                value.Write(next, 0, next.Length);
            }
            return ByteString.From(value.ToArray());
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_closed, 1) == 0)
            {
                RegistrySnapshotCloseResultDataType result = await m_access.CloseDocumentAsync(m_opened.SnapshotId)
                    .ConfigureAwait(false);
                Check(result.StatusCode);
            }
        }

        private static ulong Count(RegistrySnapshotReadResultDataType part)
        {
            if (part.Kind is 3 or 4)
            {
                return (ulong)part.Entries.Count;
            }
            if (part.Kind == 2 && part.Value.TryGetValue(out ByteString bytes))
            {
                return (ulong)bytes.Length;
            }
            if (part.Kind == 1 && part.Value.TryGetValue(out string text))
            {
                ulong count = 0;
                for (int index = 0; index < text.Length; index++)
                {
                    if (char.IsHighSurrogate(text[index]))
                    {
                        index++;
                    }
                    count++;
                }
                return count;
            }
            return 1;
        }

        private static void Check(StatusCode status)
        {
            if (StatusCode.IsBad(status))
            {
                throw new ServiceResultException(status);
            }
        }

        private readonly NativeRegistryAccessTypeClient m_access;
        private readonly RegistrySnapshotOpenResultDataType m_opened;
        private readonly uint m_maxItems;
        private readonly uint m_maxBytes;
        private int m_closed;
    }
}

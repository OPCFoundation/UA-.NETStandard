/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Stores the document bytes behind a registry resource. The address-space representation of a
    /// resource is a <c>ResourceType</c>, which is a <c>FileType</c>, so this provider mirrors the
    /// file access model: reads and writes are <b>offset and length based</b> and a document never
    /// has to be materialized as a whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keeping the bytes behind an injectable provider is what lets a registry run in a
    /// high-availability or distributed deployment — the default keeps them in the server process,
    /// but a deployment can substitute a shared store without touching the node managers.
    /// Implementations must be safe for concurrent calls.
    /// </para>
    /// <para>
    /// <b>Random access.</b> <see cref="WriteAsync"/> takes the absolute offset at which the chunk
    /// starts, so a writer may fill a document out of order or overwrite a region it already wrote.
    /// Writing past the current end grows the document, and any gap created that way reads back as
    /// zero bytes. Writing at offset 0 does <b>not</b> truncate what follows — call
    /// <see cref="DeleteAsync"/> first to replace a document wholesale.
    /// </para>
    /// <para>
    /// <b>Reads.</b> <see cref="ReadAsync"/> returns at most <c>count</c> bytes starting at
    /// <c>offset</c> and returns fewer — including an empty <see cref="ByteString"/> — when the
    /// document ends first. A null <see cref="ByteString"/> is returned only when the key itself is
    /// unknown, which lets a caller tell "no such resource" from "resource is empty".
    /// </para>
    /// <para>
    /// <b>Error reporting.</b> Argument faults are signalled with exceptions:
    /// <see cref="ArgumentException"/> for a null or empty key and
    /// <see cref="ArgumentOutOfRangeException"/> for a negative offset or count. Everything a caller
    /// is expected to handle is signalled in the return value instead of by throwing — an unknown key
    /// is a null <see cref="ByteString"/> from <see cref="ReadAsync"/>, a missing length is
    /// <c>-1</c> from <see cref="GetLengthAsync"/>, and a delete that removed nothing is
    /// <c>false</c>. A backing store that fails for an infrastructure reason (an unreachable share, a
    /// permission fault) should throw a <see cref="ServiceResultException"/> carrying the appropriate
    /// status code; the node manager surfaces it as the Method's result rather than faulting the
    /// server.
    /// </para>
    /// </remarks>
    public interface IXRegistryResourceStore
    {
        /// <summary>
        /// Reads up to <paramref name="count"/> bytes of the document stored under
        /// <paramref name="resourceKey"/>, starting at <paramref name="offset"/>.
        /// </summary>
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <param name="offset">The absolute offset to read from.</param>
        /// <param name="count">The maximum number of bytes to return.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// The bytes read, which may be shorter than <paramref name="count"/> at the end of the
        /// document, or a null <see cref="ByteString"/> when the key is unknown.
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="resourceKey"/> is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="offset"/> or <paramref name="count"/> is negative.
        /// </exception>
        ValueTask<ByteString> ReadAsync(
            string resourceKey,
            long offset,
            int count,
            CancellationToken ct = default);

        /// <summary>
        /// Writes <paramref name="data"/> into the document stored under
        /// <paramref name="resourceKey"/> at <paramref name="offset"/>, creating the document when it
        /// does not exist yet and growing it when the chunk extends past the current end.
        /// </summary>
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <param name="offset">The absolute offset to write at.</param>
        /// <param name="data">The bytes to write.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ArgumentException"><paramref name="resourceKey"/> is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative.</exception>
        ValueTask WriteAsync(
            string resourceKey,
            long offset,
            ByteString data,
            CancellationToken ct = default);

        /// <summary>
        /// Gets the length in bytes of the document stored under <paramref name="resourceKey"/>.
        /// </summary>
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The document length, or <c>-1</c> when the key is unknown.</returns>
        /// <exception cref="ArgumentException"><paramref name="resourceKey"/> is null or empty.</exception>
        ValueTask<long> GetLengthAsync(string resourceKey, CancellationToken ct = default);

        /// <summary>
        /// Removes the document stored under <paramref name="resourceKey"/>.
        /// </summary>
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns><c>true</c> when a document was removed.</returns>
        /// <exception cref="ArgumentException"><paramref name="resourceKey"/> is null or empty.</exception>
        ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default);
    }
}

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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Stores the document bytes behind a registry resource. The address-space representation of a
    /// resource is a <c>ResourceType</c> (a <c>FileType</c>); this provider is where the bytes that
    /// the file methods transfer actually live.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider is deliberately document-oriented rather than stream-oriented: a resource's
    /// identity is derived from the whole document, so an upload is committed in one piece once the
    /// writer closes the file and the content id has been computed.
    /// </para>
    /// <para>
    /// Keeping the bytes behind an injectable provider is what lets a registry run in a
    /// high-availability or distributed deployment — the default keeps them in the server process,
    /// but a deployment can substitute a shared store without touching the node managers.
    /// Implementations must be safe for concurrent calls.
    /// </para>
    /// </remarks>
    public interface IXRegistryResourceStore
    {
        /// <summary>
        /// Reads the document stored under <paramref name="resourceKey"/>.
        /// </summary>
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The document, or a null <see cref="ByteString"/> when the key is unknown.</returns>
        ValueTask<ByteString> ReadAsync(string resourceKey, CancellationToken ct = default);

        /// <summary>
        /// Stores <paramref name="document"/> under <paramref name="resourceKey"/>, replacing any
        /// document already held under that key.
        /// </summary>
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <param name="document">The document bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        ValueTask WriteAsync(
            string resourceKey,
            ReadOnlyMemory<byte> document,
            CancellationToken ct = default);

        /// <summary>
        /// Removes the document stored under <paramref name="resourceKey"/>.
        /// </summary>
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns><c>true</c> when a document was removed.</returns>
        ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default);
    }
}

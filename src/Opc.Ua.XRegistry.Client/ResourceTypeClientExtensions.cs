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

namespace Opc.Ua.XRegistry.Client
{
    /// <summary>
    /// Convenience helpers layered over the source-generated <see cref="ResourceTypeClient"/>
    /// proxy. <c>ResourceType</c> is a <c>FileType</c>, so a resource document is transferred with
    /// the standard file methods; these helpers only add chunking and handle management on top of
    /// the auto-generated <c>Write</c>/<c>Read</c>/<c>Close</c> wrappers.
    /// </summary>
    /// <remarks>
    /// Because a domain resource proxy derives from <see cref="ResourceTypeClient"/> (for example
    /// <c>SchemaFileTypeClient</c>), every helper here is directly callable on a domain proxy
    /// without any inheritance in the client layer.
    /// </remarks>
    public static class ResourceTypeClientExtensions
    {
        /// <summary>
        /// Default per-call chunk size used when streaming a resource document.
        /// </summary>
        public const int DefaultChunkSize = 4096;

        /// <summary>
        /// Streams <paramref name="document"/> into an already open resource file and closes the
        /// handle. The handle is the one returned by the group's <c>CreateResource</c> /
        /// <c>GetOrCreateResource</c> with <c>RequestFileOpen</c> set.
        /// </summary>
        /// <param name="resource">The resource proxy.</param>
        /// <param name="fileHandle">The open write handle.</param>
        /// <param name="document">The document bytes.</param>
        /// <param name="chunkSize">Maximum per-write chunk size.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is not positive.</exception>
        public static async ValueTask WriteDocumentAsync(
            this ResourceTypeClient resource,
            uint fileHandle,
            ByteString document,
            int chunkSize = DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (resource is null)
            {
                throw new ArgumentNullException(nameof(resource));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            try
            {
                for (int offset = 0; offset < document.Length; offset += chunkSize)
                {
                    int length = Math.Min(chunkSize, document.Length - offset);
                    ByteString chunk = new(document.Slice(offset, length));
                    await resource.WriteAsync(fileHandle, chunk, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                await resource.CloseAsync(fileHandle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Opens the resource file for reading, streams it to the end and closes the handle.
        /// </summary>
        /// <param name="resource">The resource proxy.</param>
        /// <param name="chunkSize">Maximum per-read chunk size.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The resource document bytes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is not positive.</exception>
        public static async ValueTask<ByteString> ReadDocumentAsync(
            this ResourceTypeClient resource,
            int chunkSize = DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (resource is null)
            {
                throw new ArgumentNullException(nameof(resource));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            uint fileHandle = await resource.OpenAsync(kReadMode, ct).ConfigureAwait(false);
            try
            {
                var document = new System.IO.MemoryStream();
                while (true)
                {
                    ByteString chunk = await resource.ReadAsync(fileHandle, chunkSize, ct)
                        .ConfigureAwait(false);
                    if (chunk.IsNull || chunk.Length == 0)
                    {
                        break;
                    }
#if NETSTANDARD2_0 || NETFRAMEWORK
                    byte[] buffer = chunk.Span.ToArray();
                    document.Write(buffer, 0, buffer.Length);
#else
                    document.Write(chunk.Span);
#endif
                    if (chunk.Length < chunkSize)
                    {
                        break;
                    }
                }
                return ByteString.From(document.ToArray());
            }
            finally
            {
                await resource.CloseAsync(fileHandle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private const byte kReadMode = 1;
    }
}

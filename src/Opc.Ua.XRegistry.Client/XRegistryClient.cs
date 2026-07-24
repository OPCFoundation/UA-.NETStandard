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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.XRegistry.Client
{
    /// <summary>
    /// Generic client for an in-server xRegistry registry. It resolves a resource from its
    /// content-derived id through the Opaque-NodeId fast path (a single <c>Read</c> of the node whose
    /// Identifier is the raw content-id bytes) and registers a resource through the
    /// <c>CreateResource</c>/<c>Write</c>/<c>Close</c> lifecycle. Concrete registries (for example the
    /// PubSub Schema Registry) derive from this type to add domain-specific naming and defaults.
    /// </summary>
    public class XRegistryClient
    {
        private readonly ushort m_namespaceIndex;

        /// <summary>
        /// Initializes a registry client bound to a connected <paramref name="session"/> and the
        /// registry's companion namespace.
        /// </summary>
        /// <param name="session">The connected session whose server hosts the registry.</param>
        /// <param name="registryNamespaceUri">The registry companion namespace URI.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="registryNamespaceUri"/> is null/empty.</exception>
        /// <exception cref="ServiceResultException">The server does not expose the registry namespace.</exception>
        public XRegistryClient(ISession session, string registryNamespaceUri)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrEmpty(registryNamespaceUri))
            {
                throw new ArgumentException("A registry namespace URI is required.", nameof(registryNamespaceUri));
            }

            int index = session.NamespaceUris.GetIndex(registryNamespaceUri);
            if (index <= 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "The server does not expose the registry namespace '{0}'.",
                    registryNamespaceUri);
            }

            m_namespaceIndex = (ushort)index;
        }

        /// <summary>Gets the session the client operates on.</summary>
        public ISession Session { get; }

        /// <summary>Gets the resolved registry companion namespace index on the connected server.</summary>
        public ushort NamespaceIndex => m_namespaceIndex;

        /// <summary>
        /// Resolves a resource document from its content-derived id through the Opaque-NodeId fast
        /// path: the Opaque NodeId is built deterministically from the raw content-id bytes and read
        /// in a single operation. Returns a null <see cref="ByteString"/> when no fast-path node is
        /// registered (the caller then falls back to a Browse or a registry-specific download method).
        /// </summary>
        /// <param name="resourceId">The raw content-derived id bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The resource document bytes, or a null ByteString when not registered.</returns>
        /// <exception cref="ArgumentException"><paramref name="resourceId"/> is null/empty.</exception>
        public async Task<ByteString> ResolveResourceAsync(
            ByteString resourceId,
            CancellationToken ct = default)
        {
            if (resourceId.IsNull || resourceId.Length == 0)
            {
                throw new ArgumentException("A resource id is required.", nameof(resourceId));
            }

            var fastPathNodeId = new NodeId(resourceId, m_namespaceIndex);
            try
            {
                DataValue value = await Session.ReadValueAsync(fastPathNodeId, ct).ConfigureAwait(false);
                if (StatusCode.IsBad(value.StatusCode))
                {
                    return default;
                }

                _ = value.WrappedValue.TryGetValue(out ByteString document);
                return document;
            }
            catch (ServiceResultException sre) when (
                sre.StatusCode == StatusCodes.BadNodeIdUnknown
                || sre.StatusCode == StatusCodes.BadNodeIdInvalid)
            {
                return default;
            }
        }

        /// <summary>
        /// Registers a resource document through the registry write lifecycle: the
        /// <paramref name="createResourceMethodId"/> obtains a write handle on the
        /// <paramref name="resourceGroupObjectId"/>, the document is streamed with one or more
        /// <paramref name="writeMethodId"/> calls, and <paramref name="closeMethodId"/> finalizes it.
        /// On close the server computes and returns the content-derived id and its algorithm and
        /// publishes the Opaque fast-path node. A client obtains the group and method NodeIds by
        /// Browsing the registry's well-known object.
        /// </summary>
        /// <param name="resourceGroupObjectId">The resource-group object NodeId.</param>
        /// <param name="createResourceMethodId">The CreateResource method NodeId.</param>
        /// <param name="writeMethodId">The Write method NodeId.</param>
        /// <param name="closeMethodId">The Close method NodeId.</param>
        /// <param name="document">The resource document bytes.</param>
        /// <param name="format">The resource format (for example <c>avro</c>).</param>
        /// <param name="chunkSize">The maximum Write chunk size in bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The computed content-id and its algorithm name.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is not positive.</exception>
        public async Task<(ByteString ContentId, string? Algorithm)> RegisterResourceAsync(
            NodeId resourceGroupObjectId,
            NodeId createResourceMethodId,
            NodeId writeMethodId,
            NodeId closeMethodId,
            ReadOnlyMemory<byte> document,
            string format = "avro",
            int chunkSize = 4096,
            CancellationToken ct = default)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            ArrayOf<Variant> createOutputs = await Session.CallAsync(
                resourceGroupObjectId,
                createResourceMethodId,
                ct,
                new Variant(string.Empty),
                new Variant(string.Empty)).ConfigureAwait(false);

            _ = createOutputs[0].TryGetValue(out uint handle);

            for (int offset = 0; offset < document.Length; offset += chunkSize)
            {
                int length = Math.Min(chunkSize, document.Length - offset);
                var chunk = ByteString.From(document.Slice(offset, length).ToArray());
                _ = await Session.CallAsync(
                    resourceGroupObjectId,
                    writeMethodId,
                    ct,
                    new Variant(handle),
                    new Variant(chunk)).ConfigureAwait(false);
            }

            ArrayOf<Variant> closeOutputs = await Session.CallAsync(
                resourceGroupObjectId,
                closeMethodId,
                ct,
                new Variant(handle),
                new Variant(format)).ConfigureAwait(false);

            _ = closeOutputs[0].TryGetValue(out ByteString contentId);
            _ = closeOutputs[1].TryGetValue(out string? algorithm);
            return (contentId, algorithm);
        }
    }
}

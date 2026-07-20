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
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.PubSub.SchemaRegistry
{
    /// <summary>
    /// Client for the PubSub Schema Registry — the schema specialization of the generic
    /// <see cref="XRegistryClient"/>. It resolves a schema document from its content-derived on-wire
    /// <c>SchemaId</c> through the Opaque SchemaId-NodeId fast path (§6.4) in a single Read, and
    /// registers a schema document through the <c>CreateResource</c>/<c>Write</c>/<c>Close</c>
    /// lifecycle (§5.2). A consumer that received a SchemaId on the wire never has to Browse or
    /// recompute a fingerprint to obtain the schema.
    /// </summary>
    public sealed class SchemaRegistryClient : XRegistryClient
    {
        /// <summary>The well-known Schema Registry companion namespace URI.</summary>
        public const string SchemaRegistryNamespaceUri =
            SchemaRegistryWellKnown.SchemaRegistryNamespaceUri;

        /// <summary>
        /// Initializes a Schema Registry client bound to a connected <paramref name="session"/>.
        /// </summary>
        /// <param name="session">The connected session whose server hosts the Schema Registry.</param>
        /// <param name="schemaRegistryNamespaceUri">
        /// The Schema Registry companion namespace URI. Defaults to
        /// <see cref="SchemaRegistryNamespaceUri"/>.
        /// </param>
        public SchemaRegistryClient(ISession session, string? schemaRegistryNamespaceUri = null)
            : base(
                session,
                string.IsNullOrEmpty(schemaRegistryNamespaceUri)
                    ? SchemaRegistryNamespaceUri
                    : schemaRegistryNamespaceUri!)
        {
        }

        /// <summary>
        /// Resolves a schema document from its on-wire <c>SchemaId</c> through the Opaque
        /// SchemaId-NodeId fast path (§6.4). Returns a null <see cref="ByteString"/> when no fast-path
        /// node is registered for the SchemaId.
        /// </summary>
        /// <param name="schemaId">The raw on-wire SchemaId bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The schema document bytes, or a null ByteString when not registered.</returns>
        public Task<ByteString> ResolveSchemaAsync(ByteString schemaId, CancellationToken ct = default)
        {
            return ResolveResourceAsync(schemaId, ct);
        }

        /// <summary>
        /// Registers a schema document through the Schema Registry write lifecycle (§5.2). On close
        /// the server computes and returns the content-derived <c>SchemaId</c> and its
        /// <c>SchemaIdAlg</c> and publishes the Opaque fast-path node (§10.1).
        /// </summary>
        /// <param name="schemaGroupObjectId">The SchemaGroup object NodeId.</param>
        /// <param name="createResourceMethodId">The CreateResource method NodeId.</param>
        /// <param name="writeMethodId">The Write method NodeId.</param>
        /// <param name="closeMethodId">The Close method NodeId.</param>
        /// <param name="document">The schema document bytes.</param>
        /// <param name="format">The schema format (for example <c>avro</c>).</param>
        /// <param name="chunkSize">The maximum Write chunk size in bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The computed SchemaId and its SchemaIdAlg name.</returns>
        public Task<(ByteString SchemaId, string? SchemaIdAlg)> RegisterSchemaAsync(
            NodeId schemaGroupObjectId,
            NodeId createResourceMethodId,
            NodeId writeMethodId,
            NodeId closeMethodId,
            ReadOnlyMemory<byte> document,
            string format = "avro",
            int chunkSize = 4096,
            CancellationToken ct = default)
        {
            return RegisterResourceAsync(
                schemaGroupObjectId,
                createResourceMethodId,
                writeMethodId,
                closeMethodId,
                document,
                format,
                chunkSize,
                ct);
        }
    }
}

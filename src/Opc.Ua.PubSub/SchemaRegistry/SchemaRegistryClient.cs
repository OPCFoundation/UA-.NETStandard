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
        /// Initializes a Schema Registry client bound to a connected <paramref name="session"/>,
        /// using the well-known Schema Registry root Object.
        /// </summary>
        /// <param name="session">The connected session whose server hosts the Schema Registry.</param>
        /// <param name="telemetry">Telemetry context used by the generated proxies.</param>
        /// <param name="schemaRegistryNamespaceUri">
        /// The Schema Registry companion namespace URI. Defaults to
        /// <see cref="SchemaRegistryNamespaceUri"/>.
        /// </param>
        public SchemaRegistryClient(
            ISession session,
            ITelemetryContext telemetry,
            string? schemaRegistryNamespaceUri = null)
            : this(session, telemetry, default, schemaRegistryNamespaceUri)
        {
        }

        /// <summary>
        /// Initializes a Schema Registry client bound to a connected <paramref name="session"/> and
        /// an explicit Schema Registry root Object.
        /// <para>
        /// A server does not have to publish the Schema Registry root at the provisional well-known
        /// identifier, so a client that discovered the root by Browse passes it here rather than
        /// relying on the default.
        /// </para>
        /// </summary>
        /// <param name="session">The connected session whose server hosts the Schema Registry.</param>
        /// <param name="telemetry">Telemetry context used by the generated proxies.</param>
        /// <param name="registryNodeId">
        /// The Schema Registry root Object. Pass a null NodeId to use the well-known root in the
        /// resolved Schema Registry namespace.
        /// </param>
        /// <param name="schemaRegistryNamespaceUri">
        /// The Schema Registry companion namespace URI. Defaults to
        /// <see cref="SchemaRegistryNamespaceUri"/>.
        /// </param>
        public SchemaRegistryClient(
            ISession session,
            ITelemetryContext telemetry,
            NodeId registryNodeId,
            string? schemaRegistryNamespaceUri = null)
            : base(
                session,
                string.IsNullOrEmpty(schemaRegistryNamespaceUri)
                    ? SchemaRegistryNamespaceUri
                    : schemaRegistryNamespaceUri!,
                registryNodeId,
                telemetry)
        {
        }

        /// <summary>
        /// Resolves a schema document from its on-wire <c>SchemaId</c> through the Opaque
        /// SchemaId-NodeId fast path (§6.4). Returns a null <see cref="ByteString"/> when no fast-path
        /// node is registered for the SchemaId.
        /// </summary>
        /// <param name="schemaId">The raw on-wire SchemaId bytes.</param>
        /// <param name="maxByteStringLength">
        /// The chunk size for the range-based reads; 0 uses the session's own limit.
        /// </param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The schema document bytes, or a null ByteString when not registered.</returns>
        public Task<ByteString> ResolveSchemaAsync(
            ByteString schemaId,
            int maxByteStringLength = 0,
            CancellationToken ct = default)
        {
            return ResolveResourceAsync(schemaId, maxByteStringLength, ct);
        }

        /// <summary>
        /// Gets the SchemaGroup that owns the schemas of a DataSet, creating it when the registry
        /// does not host it yet.
        /// </summary>
        /// <param name="registryNodeId">The Schema Registry root NodeId.</param>
        /// <param name="schemaGroupId">The SchemaGroup id.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The SchemaGroup NodeId and whether this call created it.</returns>
        public Task<GroupRegistrationResult> GetOrCreateSchemaGroupAsync(
            NodeId registryNodeId,
            string schemaGroupId,
            CancellationToken ct = default)
        {
            return GetOrCreateGroupAsync(registryNodeId, schemaGroupId, ct);
        }

        /// <summary>
        /// Registers a schema document in a SchemaGroup through the model's own lifecycle: the
        /// group's <c>CreateResource</c> creates the schema version and opens it for writing, and the
        /// document is streamed through the inherited <c>FileType</c> Write. On close the server
        /// bootstraps the schema's content-derived <c>SchemaId</c> and publishes the Opaque fast-path
        /// node (§10.1), so the schema becomes resolvable by the SchemaId carried on the wire.
        /// </summary>
        /// <param name="schemaGroupNodeId">The SchemaGroup NodeId that owns the schema.</param>
        /// <param name="schemaId">The schema resource id — the stable identity of the DataSet schema across its versions.</param>
        /// <param name="document">The schema document bytes.</param>
        /// <param name="versionId">The version id; empty lets the server assign the next one.</param>
        /// <param name="chunkSize">The maximum Write chunk size in bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The created schema NodeId and the version id the server assigned.</returns>
        public Task<ResourceRegistrationResult> RegisterSchemaAsync(
            NodeId schemaGroupNodeId,
            string schemaId,
            ByteString document,
            string versionId = "",
            int chunkSize = ResourceTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            return RegisterResourceAsync(schemaGroupNodeId, schemaId, document, versionId, chunkSize, ct);
        }

        /// <summary>
        /// Registers a schema document idempotently: an existing version with the same
        /// <paramref name="schemaId"/> and <paramref name="versionId"/> is reused rather than
        /// rejected, and the document is only streamed when this call created the version. This is
        /// the call a publisher uses when it re-announces a schema it may already have registered.
        /// </summary>
        /// <param name="schemaGroupNodeId">The SchemaGroup NodeId that owns the schema.</param>
        /// <param name="schemaId">The schema resource id — the stable identity of the DataSet schema across its versions.</param>
        /// <param name="document">The schema document bytes.</param>
        /// <param name="versionId">The version id; empty lets the server assign the next one.</param>
        /// <param name="chunkSize">The maximum Write chunk size in bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The schema NodeId, the assigned version id, and whether it was created.</returns>
        public Task<ResourceRegistrationResult> GetOrRegisterSchemaAsync(
            NodeId schemaGroupNodeId,
            string schemaId,
            ByteString document,
            string versionId = "",
            int chunkSize = ResourceTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            return GetOrRegisterResourceAsync(
                schemaGroupNodeId, schemaId, document, versionId, chunkSize, ct);
        }
    }
}

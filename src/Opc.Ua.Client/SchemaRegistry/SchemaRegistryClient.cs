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

namespace Opc.Ua.Client.SchemaRegistry
{
    /// <summary>
    /// Client for the OPC UA in-server Schema Registry (the counterpart of the
    /// <c>Opc.Ua.Server</c> Schema Registry feature). It resolves a schema document from its
    /// content-derived on-wire <c>SchemaId</c> through the Opaque <c>SchemaId</c>-NodeId fast path
    /// (§6.4) in a single <c>Read</c>, and registers a schema document through the
    /// <c>CreateResource</c>/<c>Write</c>/<c>Close</c> lifecycle (§5.2, auto-bootstrap §10.1). A
    /// consumer that received a SchemaId on the wire never has to Browse or recompute a fingerprint
    /// to obtain the schema: it builds the Opaque NodeId directly from the SchemaId bytes.
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public sealed class SchemaRegistryClient
    {
        /// <summary>The well-known Schema Registry companion namespace URI.</summary>
        public const string SchemaRegistryNamespaceUri = "http://opcfoundation.org/UA/SchemaRegistry/";

        private readonly ushort m_namespaceIndex;

        /// <summary>
        /// Initializes a Schema Registry client bound to a connected <paramref name="session"/>.
        /// </summary>
        /// <param name="session">The connected session whose server hosts the Schema Registry.</param>
        /// <param name="schemaRegistryNamespaceUri">
        /// The Schema Registry companion namespace URI. Defaults to
        /// <see cref="SchemaRegistryNamespaceUri"/>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">
        /// The server does not expose the Schema Registry namespace.
        /// </exception>
        public SchemaRegistryClient(ISession session, string? schemaRegistryNamespaceUri = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));

            string uri = string.IsNullOrEmpty(schemaRegistryNamespaceUri)
                ? SchemaRegistryNamespaceUri
                : schemaRegistryNamespaceUri!;

            int index = session.NamespaceUris.GetIndex(uri);
            if (index <= 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "The server does not expose the Schema Registry namespace '{0}'.",
                    uri);
            }

            m_namespaceIndex = (ushort)index;
        }

        /// <summary>Gets the session the client operates on.</summary>
        public ISession Session { get; }

        /// <summary>Gets the resolved Schema Registry namespace index on the connected server.</summary>
        public ushort NamespaceIndex => m_namespaceIndex;

        /// <summary>
        /// Resolves a schema document from its on-wire <c>SchemaId</c> through the Opaque
        /// SchemaId-NodeId fast path (§6.4): the Opaque NodeId is constructed deterministically from
        /// the raw SchemaId bytes and read in a single operation. Returns a null <see cref="ByteString"/>
        /// when no fast-path node is registered for the SchemaId (the caller then falls back to a
        /// Browse or a <c>GetSchema</c> call).
        /// </summary>
        /// <param name="schemaId">The raw on-wire SchemaId bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The schema document bytes, or a null ByteString when not registered.</returns>
        /// <exception cref="ArgumentException"><paramref name="schemaId"/> is null/empty.</exception>
        public async Task<ByteString> ResolveSchemaAsync(
            ByteString schemaId,
            CancellationToken ct = default)
        {
            if (schemaId.IsNull || schemaId.Length == 0)
            {
                throw new ArgumentException("A SchemaId is required.", nameof(schemaId));
            }

            var fastPathNodeId = new NodeId(schemaId, m_namespaceIndex);
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
                // No fast-path node for this SchemaId — the consumer falls back to Browse/GetSchema.
                return default;
            }
        }

        /// <summary>
        /// Registers a schema document through the Schema Registry write lifecycle (§5.2): the
        /// <paramref name="createResourceMethodId"/> obtains a write handle on the
        /// <paramref name="schemaGroupObjectId"/>, the document is streamed with one or more
        /// <paramref name="writeMethodId"/> calls, and <paramref name="closeMethodId"/> finalizes it.
        /// On close the server computes and returns the content-derived <c>SchemaId</c> and its
        /// <c>SchemaIdAlg</c> (§6.6) and publishes the Opaque fast-path node (§10.1). A client obtains
        /// the group and method NodeIds by Browsing the well-known <c>SchemaRegistry</c> object.
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
        public async Task<(ByteString SchemaId, string? SchemaIdAlg)> RegisterSchemaAsync(
            NodeId schemaGroupObjectId,
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

            // 1) CreateResource(ResourceId, VersionId) -> (FileHandle, VersionId).
            ArrayOf<Variant> createOutputs = await Session.CallAsync(
                schemaGroupObjectId,
                createResourceMethodId,
                ct,
                new Variant(string.Empty),
                new Variant(string.Empty)).ConfigureAwait(false);

            _ = createOutputs[0].TryGetValue(out uint handle);

            // 2) Write(FileHandle, Data) — streamed in chunks.
            for (int offset = 0; offset < document.Length; offset += chunkSize)
            {
                int length = Math.Min(chunkSize, document.Length - offset);
                var chunk = ByteString.From(document.Slice(offset, length).ToArray());
                _ = await Session.CallAsync(
                    schemaGroupObjectId,
                    writeMethodId,
                    ct,
                    new Variant(handle),
                    new Variant(chunk)).ConfigureAwait(false);
            }

            // 3) Close(FileHandle, Format) -> (SchemaId, SchemaIdAlg).
            ArrayOf<Variant> closeOutputs = await Session.CallAsync(
                schemaGroupObjectId,
                closeMethodId,
                ct,
                new Variant(handle),
                new Variant(format)).ConfigureAwait(false);

            _ = closeOutputs[0].TryGetValue(out ByteString schemaId);
            _ = closeOutputs[1].TryGetValue(out string? algorithm);
            return (schemaId, algorithm);
        }
    }
}

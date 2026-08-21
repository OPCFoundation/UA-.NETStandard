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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.PubSub.SchemaRegistry
{
    /// <summary>
    /// An <see cref="ISchemaRegistrationSink"/> that publishes a produced schema into the Schema
    /// Registry through a <see cref="SchemaRegistryClient"/> — the registry-publish half of the
    /// schema-change protocol (Avro Part 14 §8.4.5). The schema-lifecycle observer invokes this when
    /// an encoder produces a new per-DataSet schema, so the schema document is registered in addition
    /// to being announced on the wire.
    /// </summary>
    /// <remarks>
    /// The sink registers each DataSet's schema as one registry <i>resource</i> whose id is the
    /// DataSet identity, so successive schema growths of the same DataSet become successive
    /// <i>versions</i> of that resource rather than unrelated entries. Registration is idempotent:
    /// re-announcing a schema the registry already holds reuses the existing version instead of
    /// failing. The owning SchemaGroup NodeId is supplied by the caller, resolved once from the
    /// connected registry topology.
    /// </remarks>
    public sealed class SchemaRegistrySink : ISchemaRegistrationSink
    {
        /// <summary>
        /// Initializes a new <see cref="SchemaRegistrySink"/>.
        /// </summary>
        /// <param name="client">The connected Schema Registry client.</param>
        /// <param name="schemaGroupNodeId">The SchemaGroup NodeId that owns the registered schemas.</param>
        /// <param name="chunkSize">The maximum Write chunk size in bytes.</param>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="schemaGroupNodeId"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is not positive.</exception>
        public SchemaRegistrySink(
            SchemaRegistryClient client,
            NodeId schemaGroupNodeId,
            int chunkSize = ResourceTypeClientExtensions.DefaultChunkSize)
        {
            m_client = client ?? throw new ArgumentNullException(nameof(client));
            if (schemaGroupNodeId.IsNull)
            {
                throw new ArgumentException(
                    "A SchemaGroup NodeId is required.", nameof(schemaGroupNodeId));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            m_schemaGroupNodeId = schemaGroupNodeId;
            m_chunkSize = chunkSize;
        }

        /// <inheritdoc/>
        public async ValueTask RegisterAsync(
            SchemaChangeNotification change,
            CancellationToken cancellationToken = default)
        {
            if (change.Schema.IsNull || change.Schema.Span.Length == 0)
            {
                // No document to register (identity-only notification); nothing to publish.
                return;
            }

            _ = await m_client.GetOrRegisterSchemaAsync(
                m_schemaGroupNodeId,
                BuildSchemaResourceId(change.MetaDataKey),
                change.Schema,
                change.MetaDataKey.MajorVersion.ToString(CultureInfo.InvariantCulture),
                m_chunkSize,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds the resource id that identifies a DataSet's schema across its versions. The
        /// MajorVersion is deliberately excluded — it is carried as the resource <i>version</i> id,
        /// so a schema growth versions the same resource instead of creating a new one.
        /// </summary>
        /// <param name="key">The DataSet the schema was produced for.</param>
        /// <returns>The stable schema resource id.</returns>
        private static string BuildSchemaResourceId(DataSetMetaDataKey key)
        {
            return FormattableString.Invariant(
                $"{key.PublisherId}.{key.WriterGroupId}.{key.DataSetWriterId}");
        }

        private readonly SchemaRegistryClient m_client;
        private readonly NodeId m_schemaGroupNodeId;
        private readonly int m_chunkSize;
    }
}

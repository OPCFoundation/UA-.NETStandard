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
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.SchemaRegistry
{
    /// <summary>
    /// An <see cref="ISchemaRegistrationSink"/> that publishes a produced schema into the Schema
    /// Registry through a <see cref="SchemaRegistryClient"/> — the registry-publish half of the
    /// schema-change protocol (Avro Part 14 §8.4.5). The schema-lifecycle observer invokes this when
    /// an encoder produces a new per-DataSet schema, so the schema document is registered in addition
    /// to being announced on the wire. The SchemaGroup object and CreateResource/Write/Close method
    /// NodeIds are supplied by the caller (resolved once from the connected registry topology).
    /// </summary>
    public sealed class SchemaRegistrySink : ISchemaRegistrationSink
    {
        private readonly SchemaRegistryClient m_client;
        private readonly NodeId m_schemaGroupObjectId;
        private readonly NodeId m_createResourceMethodId;
        private readonly NodeId m_writeMethodId;
        private readonly NodeId m_closeMethodId;
        private readonly int m_chunkSize;

        /// <summary>
        /// Initializes a new <see cref="SchemaRegistrySink"/>.
        /// </summary>
        /// <param name="client">The connected Schema Registry client.</param>
        /// <param name="schemaGroupObjectId">The SchemaGroup object NodeId.</param>
        /// <param name="createResourceMethodId">The CreateResource method NodeId.</param>
        /// <param name="writeMethodId">The Write method NodeId.</param>
        /// <param name="closeMethodId">The Close method NodeId.</param>
        /// <param name="chunkSize">The maximum Write chunk size in bytes.</param>
        public SchemaRegistrySink(
            SchemaRegistryClient client,
            NodeId schemaGroupObjectId,
            NodeId createResourceMethodId,
            NodeId writeMethodId,
            NodeId closeMethodId,
            int chunkSize = 4096)
        {
            m_client = client ?? throw new ArgumentNullException(nameof(client));
            m_schemaGroupObjectId = schemaGroupObjectId;
            m_createResourceMethodId = createResourceMethodId;
            m_writeMethodId = writeMethodId;
            m_closeMethodId = closeMethodId;
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
            _ = await m_client.RegisterSchemaAsync(
                m_schemaGroupObjectId,
                m_createResourceMethodId,
                m_writeMethodId,
                m_closeMethodId,
                change.Schema.Span.ToArray(),
                string.IsNullOrEmpty(change.Format) ? "avro" : change.Format,
                m_chunkSize,
                cancellationToken).ConfigureAwait(false);
        }
    }
}

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

using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.PubSub.SchemaRegistry
{
    /// <summary>
    /// Options that locate the Schema Registry group a <see cref="SchemaRegistrySink"/> publishes
    /// into. The SchemaGroup NodeId is resolved once from the connected registry topology (a Browse
    /// of the registry root, or the client's <c>GetOrCreateSchemaGroupAsync</c>) and then supplied
    /// here, so the sink itself performs no discovery on the publish path.
    /// </summary>
    public sealed class SchemaRegistrySinkOptions
    {
        /// <summary>
        /// Gets or sets the SchemaGroup NodeId that owns the registered schemas.
        /// </summary>
        public NodeId SchemaGroupNodeId { get; set; } = NodeId.Null;

        /// <summary>
        /// Gets or sets the maximum Write chunk size in bytes.
        /// </summary>
        public int ChunkSize { get; set; } = ResourceTypeClientExtensions.DefaultChunkSize;

        /// <summary>
        /// Gets a value indicating whether the sink has everything it needs to publish.
        /// </summary>
        public bool IsComplete => !SchemaGroupNodeId.IsNull && ChunkSize > 0;
    }
}

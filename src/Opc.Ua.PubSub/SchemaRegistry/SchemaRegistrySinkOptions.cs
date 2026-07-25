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

namespace Opc.Ua.PubSub.SchemaRegistry
{
    /// <summary>
    /// Options that locate the Schema Registry write lifecycle used by a
    /// <see cref="SchemaRegistrySink"/>. The NodeIds are resolved once from the connected registry
    /// topology (a Browse of the SchemaGroup and its CreateResource/Write/Close methods) and then
    /// supplied here, so the sink itself performs no discovery on the publish path.
    /// </summary>
    public sealed class SchemaRegistrySinkOptions
    {
        /// <summary>
        /// Gets or sets the SchemaGroup object NodeId that owns the registered schemas.
        /// </summary>
        public NodeId? SchemaGroupObjectId { get; set; }

        /// <summary>
        /// Gets or sets the CreateResource method NodeId.
        /// </summary>
        public NodeId? CreateResourceMethodId { get; set; }

        /// <summary>
        /// Gets or sets the Write method NodeId.
        /// </summary>
        public NodeId? WriteMethodId { get; set; }

        /// <summary>
        /// Gets or sets the Close method NodeId.
        /// </summary>
        public NodeId? CloseMethodId { get; set; }

        /// <summary>
        /// Gets or sets the maximum Write chunk size in bytes.
        /// </summary>
        public int ChunkSize { get; set; } = 4096;

        /// <summary>
        /// Gets a value indicating whether every NodeId required by the write lifecycle is set.
        /// </summary>
        public bool IsComplete
        {
            get
            {
                return SchemaGroupObjectId is not null
                    && CreateResourceMethodId is not null
                    && WriteMethodId is not null
                    && CloseMethodId is not null;
            }
        }
    }
}

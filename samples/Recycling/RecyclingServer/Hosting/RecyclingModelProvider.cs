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
using Opc.Ua.Machinery.Server;
using Opc.Ua.Recycling.Model;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Loads the compiled sample types and exactly the instance appropriate to the selected host role.
    /// </summary>
    public sealed class RecyclingModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates the model contribution for a single owner.
        /// </summary>
        public RecyclingModelProvider(RecyclingHostOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public int Order => 100;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => [RecyclingModelMapping.NamespaceUri];

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(context);
            context.EncodeableFactory.Builder.AddOpcUaRecyclingModel().Commit();
            context.NodeStateFactory.AddOpcUaRecyclingModel();
            var model = new NodeStateCollection();
            model.AddOpcUaRecyclingModel(context);
            uint omitted = m_options.Role == RecyclingHostRole.Coordinator
                ? Model.Objects.ProcessView
                : Model.Objects.Coordinator;
            ushort namespaceIndex = context.NamespaceUris.GetIndexOrAppend(RecyclingModelMapping.NamespaceUri);
            var omittedId = new NodeId(omitted, namespaceIndex);
            foreach (NodeState node in model)
            {
                if (node.NodeId != omittedId)
                {
                    nodes.Add(node);
                }
            }
        }

        private readonly RecyclingHostOptions m_options;
    }
}

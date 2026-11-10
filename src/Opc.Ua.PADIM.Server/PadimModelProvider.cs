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
using System.Collections.Generic;
using System.Linq;
using Opc.Ua.Di.Server;
using Opc.Ua.IRDI;

namespace Opc.Ua.PADIM.Server
{
    /// <summary>
    /// Loads the sole compiled PA-DIM and IRDI models into any DI address-space owner.
    /// </summary>
    public sealed class PadimModelProvider : IDiModelProvider
    {
        /// <inheritdoc/>
        public int Order => 30081;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => [Namespaces.PADIM, IRDI.Namespaces.IRDI];

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            var compiled = new NodeStateCollection();
            compiled.AddOpcUaIRDI(context);
            compiled.AddOpcUaPADIM(context);
            var existing = new HashSet<NodeId>(nodes.Select(node => node.NodeId));
            foreach (NodeState node in compiled)
            {
                if (existing.Add(node.NodeId))
                {
                    nodes.Add(node);
                }
            }
        }
    }
}

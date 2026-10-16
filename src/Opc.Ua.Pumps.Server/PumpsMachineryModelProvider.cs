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
using System.Linq;
using Microsoft.Extensions.Options;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Adds Pumps after the existing Machinery provider's dependency models.
    /// </summary>
    public sealed class PumpsMachineryModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates a provider with optional additional namespace registrations.
        /// </summary>
        public PumpsMachineryModelProvider(IOptions<PumpsServerOptions>? options = null)
        {
            m_options = (options?.Value ?? new PumpsServerOptions()).Validate();
        }

        /// <inheritdoc/>
        public int Order => 200;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
            ((ArrayOf<string>)[Namespaces.Pumps]).AddItems(m_options.AdditionalNamespaceUris);

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
            NodeId machines = ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == machines))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Pumps composition requires MachineryParts.BuildingBlocks on the owner.");
            }
            NodeId pumpType = ExpandedNodeId.ToNodeId(ObjectTypeIds.PumpType, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == pumpType))
            {
                nodes.AddOpcUaPumps(context);
                context.EncodeableFactory.Builder.AddOpcUaPumps().Commit();
            }
        }

        /// <summary>The provider's selected namespace options.</summary>
        private readonly PumpsServerOptions m_options;
    }
}

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
using Opc.Ua.PackML;

namespace Opc.Ua.Scales.Server
{
    /// <summary>
    /// Adds PackML and Scales after the Machinery provider's shared dependency models.
    /// </summary>
    public sealed class ScalesMachineryModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates a provider with optional additional namespace registrations.
        /// </summary>
        public ScalesMachineryModelProvider(IOptions<ScalesServerOptions>? options = null)
        {
            m_options = (options?.Value ?? new ScalesServerOptions()).Validate();
        }

        /// <inheritdoc/>
        public int Order => 200;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
            ((ArrayOf<string>)[PackML.Namespaces.PackML, Namespaces.Scales])
                .AddItems(m_options.AdditionalNamespaceUris);

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
                    "Scales composition requires MachineryParts.BuildingBlocks on the owner.");
            }
            NodeId packMlType = ExpandedNodeId.ToNodeId(
                PackML.ObjectTypeIds.PackMLBaseStateMachineType, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == packMlType))
            {
                nodes.AddOpcUaPackML(context);
            }
            NodeId scaleType = ExpandedNodeId.ToNodeId(ObjectTypeIds.ScaleDeviceType, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == scaleType))
            {
                nodes.AddOpcUaScales(context);
            }
            context.EncodeableFactory.Builder.AddOpcUaPackML().AddOpcUaScales().Commit();
        }

        /// <summary>The selected namespace options.</summary>
        private readonly ScalesServerOptions m_options;
    }
}

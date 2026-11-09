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
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// Adds the licensed GDD model after the existing Machinery/DI owner's dependencies.
    /// Common types are loaded once even when other plastics/rubber families share the owner.
    /// </summary>
    public sealed class GranulateDryingModelProvider : IMachineryModelProvider
    {
        /// <inheritdoc/>
        public int Order => 350;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
            [PlasticsRubber.Namespaces.PlasticsRubber, Namespaces.GranulateDrying];

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
                    "Granulate drying requires MachineryParts.BuildingBlocks on the shared owner.");
            }
            new PlasticsRubberModelProvider().AddPredefinedNodes(nodes, context);
            NodeId type = ExpandedNodeId.ToNodeId(ObjectTypeIds.GranulateDryingDeviceType, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == type))
            {
                nodes.AddOpcUaGranulateDrying(context);
            }
            context.NodeStateFactory.AddOpcUaIA().AddOpcUaMachinery().AddOpcUaGranulateDrying();
            context.EncodeableFactory.Builder.AddOpcUaGranulateDrying().Commit();
        }
    }
}

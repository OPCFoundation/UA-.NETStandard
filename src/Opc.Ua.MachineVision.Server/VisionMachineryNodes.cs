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

using Opc.Ua.Server;
using Amcm = Opc.Ua.Vision.MachineVision.AMCM;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Supplies Part 2 interface add-ins to the shared declared-child materializer.
    /// </summary>
    internal static class VisionMachineryNodes
    {
        /// <summary>
        /// Gets a declared member or the Part 2 interface's typed optional add-in.
        /// </summary>
        /// <typeparam name="T">The expected generated child type.</typeparam>
        public static T Require<T>(AsyncCustomNodeManager manager, NodeState parent, string name)
            where T : BaseInstanceState
        {
            return MachineVisionNodes.Require<T>(manager, parent, name, CreateAddIn);
        }

        private static BaseInstanceState? CreateAddIn(ISystemContext context, NodeState parent, string name)
        {
            var type = ExpandedNodeId.ToNodeId(Amcm.ObjectTypeIds.IVisionInfoType, context.NamespaceUris);
            if (type.IsNull || !parent.ReferenceExists(ReferenceTypeIds.HasInterface, false, type))
            {
                return null;
            }
            return name switch
            {
                "Maintenance" => Amcm.OpcUaVisionMachineVisionAMCMExtensions.CreateInstanceOfVisionMaintenanceInfoType(
                    context, parent, new QualifiedName(name,
                        context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi))),
                "Health" => Amcm.OpcUaVisionMachineVisionAMCMExtensions.CreateInstanceOfVisionHealthInfoType(
                    context, parent, new QualifiedName(name,
                        context.NamespaceUris.GetIndexOrAppend(MachineVisionModels.AssetNamespaceUri))),
                _ => null
            };
        }
    }
}

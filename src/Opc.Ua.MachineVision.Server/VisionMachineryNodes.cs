// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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

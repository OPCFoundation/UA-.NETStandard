// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.GMS;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Adds the real generated GMS model before the shared runtime fixture establishes namespace ownership.
    /// </summary>
    internal sealed class MachineToolEquipmentModelProvider : IMachineryModelProvider
    {
        /// <inheritdoc/>
        public int Order => 200;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => GmsModel.GetNamespaceUris();

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            nodes.AddGmsTypeSystem(context);
            context.EncodeableFactory.Builder.AddGmsTypeSystem().Commit();
        }
    }
}

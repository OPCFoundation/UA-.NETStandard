// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Machinery.Server;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Loads GMS and its exact MachineTool/Machinery Result dependency closure on an existing Machinery owner.
    /// </summary>
    public sealed class GmsModelProvider : IMachineryModelProvider
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

/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Moq;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Builds the stand-in NodeManager the fluent builder tests wire their
    /// <see cref="Opc.Ua.Server.Fluent.NodeManagerBuilder"/> to.
    /// </summary>
    /// <remarks>
    /// The fluent surface mints NodeIds through its owning NodeManager, so a
    /// bare mock would hand every node a null NodeId and the tests would be
    /// exercising nothing. This mock answers with the same
    /// <see cref="DefaultNodeIdFactory"/> a real NodeManager uses, which is
    /// what makes the identifiers these tests assert the identifiers a server
    /// would actually produce.
    /// </remarks>
    internal static class FluentTestNodeManager
    {
        /// <summary>
        /// Returns a NodeManager that mints deterministic NodeIds into the
        /// supplied namespace.
        /// </summary>
        /// <param name="namespaceIndex">The namespace to mint into.</param>
        /// <returns>The stand-in NodeManager.</returns>
        public static IAsyncNodeManager Create(ushort namespaceIndex)
        {
            return Create(new DefaultNodeIdFactory(
                NodeIdAssignmentMode.String,
                namespaceIndex));
        }

        /// <summary>
        /// Returns a NodeManager that mints NodeIds through the supplied
        /// factory.
        /// </summary>
        /// <param name="factory">The factory to mint with.</param>
        /// <returns>The stand-in NodeManager.</returns>
        public static IAsyncNodeManager Create(IRebasableNodeIdFactory factory)
        {
            var nodeManager = new Mock<IAsyncNodeManager>();

            nodeManager
                .Setup(manager => manager.New(
                    It.IsAny<ISystemContext>(),
                    It.IsAny<NodeState>()))
                .Returns((ISystemContext context, NodeState node)
                    => factory.New(context, node));

            return nodeManager.Object;
        }
    }
}

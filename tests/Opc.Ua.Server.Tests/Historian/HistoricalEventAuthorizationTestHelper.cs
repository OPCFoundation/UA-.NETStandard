// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Moq;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Registers known unrestricted event identities for tests of provider behavior.
    /// </summary>
    internal static class HistoricalEventAuthorizationTestHelper
    {
        /// <summary>
        /// Supplies metadata only for the specified nodes without replacing other manager routes.
        /// </summary>
        internal static void RegisterUnrestrictedEventNodes(ServerSystemContext context, ArrayOf<NodeId> nodeIds)
        {
            IMasterNodeManager? existing = context.Server.NodeManager;
            Mock<IMasterNodeManager> master = existing == null
                ? new Mock<IMasterNodeManager>()
                : Mock.Get(existing);
            Mock.Get(context.Server).Setup(server => server.NodeManager).Returns(master.Object);
            var owner = new Mock<IAsyncNodeManager>();
            foreach (NodeId nodeId in nodeIds)
            {
                master.Setup(manager => manager.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<(object? handle, IAsyncNodeManager? nodeManager)>((nodeId, owner.Object)));
                owner.Setup(manager => manager.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(), nodeId, It.IsAny<BrowseResultMask>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeMetadata?>(new NodeMetadata(nodeId, nodeId)));
            }
        }
    }
}

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
                    .Returns(new ValueTask<NodeMetadata>(new NodeMetadata(nodeId, nodeId)));
            }
        }
    }
}

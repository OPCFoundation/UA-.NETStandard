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

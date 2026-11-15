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
 *
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
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Tests the ownership of sources captured for a prepared projection.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotPreparedSourceImageTests
    {
        /// <summary>
        /// Excludes only the selected source owner, not a different owner that compares equal.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExclusionMatchesExactLiveSourceAsync(bool excludeLiveOwner)
        {
            IAsyncNodeManager live = new EqualSourceNodeManager(new Mock<INodeManager>().Object);
            IAsyncNodeManager distinct = new EqualSourceNodeManager(new Mock<INodeManager>().Object);
            var handle = new object();
            var nodeId = new NodeId(42);
            var master = new Mock<IMasterNodeManager>();
            master.Setup(value => value.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<(object?, IAsyncNodeManager?)>((handle, live)));
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.NodeManager).Returns(master.Object);
            var image = new WotPreparedSourceImage();
            image.Exclude(excludeLiveOwner ? live : distinct);
            Assert.That(live, Is.EqualTo(distinct));

            (object? found, IAsyncNodeManager? owner) =
                await image.FindAsync(server.Object, nodeId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(found, excludeLiveOwner ? Is.Null : Is.SameAs(handle));
            Assert.That(owner, excludeLiveOwner ? Is.Null : Is.SameAs(live));
        }

        /// <summary>
        /// Provides equal-valued sources backed by distinct synchronous managers.
        /// </summary>
        private sealed class EqualSourceNodeManager : AsyncNodeManagerAdapter
        {
            /// <summary>
            /// Creates an equal-valued source for the synchronous manager.
            /// </summary>
            public EqualSourceNodeManager(INodeManager manager)
                : base(manager)
            {
            }

            /// <inheritdoc/>
            public override bool Equals(object? other)
            {
                return other is EqualSourceNodeManager;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return 1;
            }
        }
    }
}

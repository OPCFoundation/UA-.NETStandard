// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Exercises intentionally retained synchronous compatibility.
// TODO: Remove the legacy cases when the synchronous API is removed.

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

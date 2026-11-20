// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies registration identity, namespace snapshotting, and null namespace handling
    /// without starting a server.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Category("Server")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerRegistrationTests
    {
        /// <summary>
        /// The internal <see cref="NodeManagerRegistration"/> constructor must record the
        /// exact <c>Id</c>, <c>Generation</c>, and <c>NodeManager</c> passed to it, and must
        /// copy the NodeManager's namespace URIs defensively at construction time: mutating
        /// the source list afterwards must not affect the registration.
        /// </summary>
        [Test]
        public void NodeManagerRegistrationCopiesNamespaceUris()
        {
            var sourceNamespaces = new List<string> { "urn:test:one", "urn:test:two" };
            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock.Setup(m => m.NamespaceUris).Returns(() => sourceNamespaces);
            IAsyncNodeManager nodeManager = nodeManagerMock.Object;

            var id = System.Guid.NewGuid();
            // A fresh manager's published identity is generation 1; the handle must match it.
            const long generation = 1;
            var registration = new NodeManagerRegistration(id, generation, nodeManager);

            // Mutate the source list after construction: the registration must be unaffected.
            sourceNamespaces.Add("urn:test:three");
            sourceNamespaces[0] = "urn:test:mutated";

            Assert.That(registration.Id, Is.EqualTo(id));
            Assert.That(registration.Generation, Is.EqualTo(generation));
            Assert.That(registration.NodeManager, Is.SameAs(nodeManager));
            Assert.That(registration.NamespaceUris.Count, Is.EqualTo(2));
            Assert.That(registration.NamespaceUris[0], Is.EqualTo("urn:test:one"));
            Assert.That(registration.NamespaceUris[1], Is.EqualTo("urn:test:two"));
        }

        /// <summary>
        /// A custom-routed NodeManager may return a null namespace list. The
        /// lifecycle handle must preserve that shape rather than rejecting a
        /// manager the master routing layer accepts.
        /// </summary>
        [Test]
        public void NodeManagerRegistrationAllowsNullNamespaceUris()
        {
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager.Setup(manager => manager.NamespaceUris).Returns((IEnumerable<string>)null!);

            var registration = new NodeManagerRegistration(
                System.Guid.NewGuid(),
                1,
                nodeManager.Object);

            Assert.That(registration.NamespaceUris.IsNull, Is.True);
        }
    }
}

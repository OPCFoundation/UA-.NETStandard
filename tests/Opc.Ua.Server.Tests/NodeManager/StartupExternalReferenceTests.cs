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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises independent reference contributions through actual initial Add publication.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class StartupExternalReferenceTests
    {
        /// <summary>
        /// A later factory cannot rewrite another candidate's private contribution table.
        /// </summary>
        [TestCase("Unchanged")]
        [TestCase("Reordered")]
        [TestCase("Cloned")]
        [TestCase("Removed")]
        public async Task StartupFactoriesOwnIndependentReferenceContributionsAsync(string mutation)
        {
            IReference firstReference = Reference(1);
            var first = new ReferenceFactory(references =>
                references[ObjectIds.ObjectsFolder] = [firstReference, Reference(1), Reference(2)]);
            var second = new ReferenceFactory(references =>
            {
                Assert.That(references, Is.Empty);
                references[ObjectIds.ObjectsFolder] = [Reference(8), Reference(9)];
                switch (mutation)
                {
                    case "Reordered":
                        references[ObjectIds.ObjectsFolder] = [Reference(9), Reference(8)];
                        break;
                    case "Cloned":
                        references[ObjectIds.ObjectsFolder] = [Reference(8), Reference(9)];
                        break;
                    case "Removed":
                        references.Remove(ObjectIds.ObjectsFolder);
                        break;
                    case "Unchanged":
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
            });
            await WithServerAsync([first, second], server =>
            {
                Assert.That(first.Contributions, Is.Not.SameAs(second.Contributions));
                IList<IReference> contributions = first.Contributions[ObjectIds.ObjectsFolder];
                Assert.That(contributions, Has.Count.EqualTo(3));
                Assert.That(contributions[0], Is.SameAs(firstReference));
                Assert.That(contributions[1].TargetId, Is.EqualTo(new ExpandedNodeId(1u, 2)));
                Assert.That(contributions[2].TargetId, Is.EqualTo(new ExpandedNodeId(2u, 2)));
                Assert.That(server.NodeManagerLifecycle.Registrations, Has.Count.EqualTo(2));
                Assert.That(second.Contributions.ContainsKey(ObjectIds.ObjectsFolder),
                    Is.EqualTo(mutation != "Removed"));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Initial publication preserves duplicate contributions and each factory's order and identities.
        /// </summary>
        [Test]
        public async Task StartupPreservesEachFactoryReferenceOrderAndMultiplicityAsync()
        {
            IReference firstReference = Reference(1);
            IReference extraDuplicate = Reference(1);
            IReference otherType = new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(1u, 2));
            IReference otherDirection = new NodeStateReference(ReferenceTypeIds.Organizes, true, new NodeId(1u, 2));
            IReference stringTarget = new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId("1", 2));
            IReference newSource = Reference(3);
            var first = new ReferenceFactory(references =>
                references[ObjectIds.ObjectsFolder] = [firstReference, Reference(1), Reference(2)]);
            var second = new ReferenceFactory(references =>
            {
                references[ObjectIds.ObjectsFolder] = [extraDuplicate, otherType, otherDirection, stringTarget];
                references[ObjectIds.ViewsFolder] = [newSource];
            });
            await WithServerAsync([first, second], _ =>
            {
                Assert.That(first.Contributions[ObjectIds.ObjectsFolder], Has.Count.EqualTo(3));
                Assert.That(first.Contributions[ObjectIds.ObjectsFolder][0], Is.SameAs(firstReference));
                IList<IReference> additions = second.Contributions[ObjectIds.ObjectsFolder];
                Assert.That(additions, Has.Count.EqualTo(4));
                Assert.That(additions[0], Is.SameAs(extraDuplicate));
                Assert.That(additions[1], Is.SameAs(otherType));
                Assert.That(additions[2], Is.SameAs(otherDirection));
                Assert.That(additions[3], Is.SameAs(stringTarget));
                Assert.That(second.Contributions[ObjectIds.ViewsFolder][0], Is.SameAs(newSource));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Empty contributions stay independent and do not prevent canonical registration.
        /// </summary>
        [Test]
        public async Task StartupFactoriesRetainEmptyContributionsAsync()
        {
            var first = new ReferenceFactory(_ => { });
            var second = new ReferenceFactory(_ => { });
            await WithServerAsync([first, second], server =>
            {
                Assert.That(first.Contributions, Is.Empty);
                Assert.That(second.Contributions, Is.Empty.And.Not.SameAs(first.Contributions));
                Assert.That(server.NodeManagerLifecycle.Registrations, Has.Count.EqualTo(2));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects application instances that cannot be constructed against a private candidate server.
        /// </summary>
        [Test]
        public async Task HostedStartupRejectsPreconstructedApplicationManagersAsync()
        {
            PreconstructedServer? rejected = null;
            var fixture = new ServerFixture<PreconstructedServer>(telemetry =>
                rejected = new PreconstructedServer(telemetry));
            try
            {
                ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.StartAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure, Is.Not.Null);
                Assert.That(failure!.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(failure.InnerException!.Message, Does.Contain("InitialFactories"));
                Assert.That(rejected, Is.Not.Null);
                Assert.That(rejected!.NodeManagerLifecycle.Registrations, Is.Empty);
                rejected.VerifyRejectedManagerCleanup();
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Runs assertions after the factories have completed canonical initial publication.
        /// </summary>
        private static async Task WithServerAsync(
            ArrayOf<ReferenceFactory> factories,
            Action<StandardServer> verify)
        {
            var fixture = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                for (int index = 0; index < factories.Count; index++)
                {
                    server.AddNodeManager(factories[index]);
                }
                return server;
            });
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                verify(server);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates an exact contributed reference.
        /// </summary>
        private static NodeStateReference Reference(uint target)
        {
            return new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(target, 2));
        }

        /// <summary>
        /// Captures the reference table provided to one privately constructed application.
        /// </summary>
        private sealed class ReferenceFactory(Action<IDictionary<NodeId, IList<IReference>>> create)
            : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => ["urn:test:startup-reference"];

            /// <summary>
            /// Gets the factory's own reference table, retained for publication assertions.
            /// </summary>
            internal IDictionary<NodeId, IList<IReference>> Contributions { get; private set; } = null!;

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manager = new Mock<IAsyncNodeManager>();
                manager.SetupGet(value => value.NamespaceUris).Returns(["urn:test:startup-reference"]);
                manager.Setup(value => value.CreateAddressSpaceAsync(
                        It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                    .Callback<IDictionary<NodeId, IList<IReference>>, CancellationToken>((references, _) =>
                    {
                        Contributions = references;
                        create(references);
                    })
                    .Returns(default(ValueTask));
                return new ValueTask<IAsyncNodeManager>(manager.Object);
            }
        }

        /// <summary>
        /// Exercises the legacy preconstructed-hosting boundary without a private factory.
        /// </summary>
        private sealed class PreconstructedServer : StandardServer
        {
            /// <summary>
            /// Creates a disposable compatibility manager without initializing its address space.
            /// </summary>
            internal PreconstructedServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
                m_manager.As<IDisposable>();
            }

            /// <summary>
            /// Verifies rejection precedes application initialization and still releases construction ownership.
            /// </summary>
            internal void VerifyRejectedManagerCleanup()
            {
                m_manager.Verify(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Never);
                m_manager.As<IDisposable>().Verify(manager => manager.Dispose(), Times.Once);
            }

            /// <inheritdoc/>
            protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IMasterNodeManager>(new MasterNodeManager(
                    server, configuration, null, [m_manager.Object]));
            }

            /// <summary>The preconstructed instance retained for exact cleanup assertions.</summary>
            private readonly Mock<IAsyncNodeManager> m_manager = new();
        }
    }
}

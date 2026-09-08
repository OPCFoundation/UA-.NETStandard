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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises reference snapshots through real startup and lifecycle adoption.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class StartupExternalReferenceTests
    {
        [TestCase("Unchanged")]
        [TestCase("Reordered")]
        [TestCase("Cloned")]
        [TestCase("Removed")]
        public async Task StartupDoesNotClaimExistingReferencesAsync(string mutation)
        {
            IReference first = Reference(1);
            IReference second = Reference(2);
            IAsyncNodeManager original = Manager(references =>
            {
                references[ObjectIds.ObjectsFolder] = [first, Reference(1), second];
                references[ObjectIds.ViewsFolder] = [Reference(3)];
            });
            IAsyncNodeManager observer = Manager(references =>
            {
                switch (mutation)
                {
                    case "Reordered":
                        references[ObjectIds.ObjectsFolder] =
                            references[ObjectIds.ObjectsFolder].Reverse().ToList();
                        break;
                    case "Cloned":
                        references[ObjectIds.ObjectsFolder] = [Reference(1), Reference(1), Reference(2)];
                        break;
                    case "Removed":
                        references[ObjectIds.ObjectsFolder] = [Reference(1)];
                        references.Remove(ObjectIds.ViewsFolder);
                        break;
                    case "Unchanged":
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
            });
            using MasterNodeManager master = CreateMaster([original, observer]);

            await master.StartupAsync().ConfigureAwait(false);
            ArrayOf<PreparedNodeManager> prepared = await ((IDynamicNodeManagerHost)master)
                .TakeStartupNodeManagersAsync().ConfigureAwait(false);

            Assert.That(prepared, Has.Count.EqualTo(2));
            Assert.That(prepared[0].ExternalReferences[ObjectIds.ObjectsFolder], Has.Count.EqualTo(3));
            Assert.That(prepared[0].ExternalReferences[ObjectIds.ObjectsFolder][0], Is.SameAs(first));
            Assert.That(prepared[1].ExternalReferences, Is.Empty);
        }

        [Test]
        public async Task StartupPreservesAddedMultiplicityOrderAndIdentityAsync()
        {
            IReference extraDuplicate = Reference(1);
            IReference otherType = new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(1u, 2));
            IReference otherDirection = new NodeStateReference(ReferenceTypeIds.Organizes, true, new NodeId(1u, 2));
            IReference stringTarget = new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId("1", 2));
            IReference newSource = Reference(3);
            IAsyncNodeManager first = Manager(references =>
                references[ObjectIds.ObjectsFolder] = [Reference(1), Reference(1), Reference(2)]);
            IAsyncNodeManager second = Manager(references =>
            {
                references[ObjectIds.ObjectsFolder] =
                [
                    Reference(2), Reference(1), Reference(1), extraDuplicate, otherType, otherDirection, stringTarget
                ];
                references[ObjectIds.ViewsFolder] = [newSource];
            });
            IAsyncNodeManager third = Manager(_ => { });
            using MasterNodeManager master = CreateMaster([first, second, third]);

            await master.StartupAsync().ConfigureAwait(false);
            ArrayOf<PreparedNodeManager> prepared = await ((IDynamicNodeManagerHost)master)
                .TakeStartupNodeManagersAsync().ConfigureAwait(false);

            IList<IReference> additions = prepared[1].ExternalReferences[ObjectIds.ObjectsFolder];
            Assert.Multiple(() =>
            {
                Assert.That(additions, Has.Count.EqualTo(4));
                Assert.That(additions[0], Is.SameAs(extraDuplicate));
                Assert.That(additions[1], Is.SameAs(otherType));
                Assert.That(additions[2], Is.SameAs(otherDirection));
                Assert.That(additions[3], Is.SameAs(stringTarget));
                Assert.That(prepared[1].ExternalReferences[ObjectIds.ViewsFolder][0], Is.SameAs(newSource));
                Assert.That(prepared[2].ExternalReferences, Is.Empty);
            });
        }

        [TestCase("Type")]
        [TestCase("Direction")]
        [TestCase("Target")]
        public async Task StartupSnapshotsMutableReferenceValuesAsync(string changedField)
        {
            var mutable = new MutableReference(Reference(1));
            IAsyncNodeManager first = Manager(references =>
                references[ObjectIds.ObjectsFolder] = [mutable]);
            IAsyncNodeManager second = Manager(references =>
            {
                switch (changedField)
                {
                    case "Type":
                        mutable.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                        break;
                    case "Direction":
                        mutable.IsInverse = true;
                        break;
                    case "Target":
                        mutable.TargetId = new ExpandedNodeId("remote", "urn:test:remote", 1);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(changedField));
                }
                references[ObjectIds.ObjectsFolder].Add(Reference(1));
            });
            using MasterNodeManager master = CreateMaster([first, second]);

            await master.StartupAsync().ConfigureAwait(false);
            ArrayOf<PreparedNodeManager> prepared = await ((IDynamicNodeManagerHost)master)
                .TakeStartupNodeManagersAsync().ConfigureAwait(false);

            Assert.That(prepared[1].ExternalReferences[ObjectIds.ObjectsFolder], Has.Count.EqualTo(1));
            Assert.That(prepared[1].ExternalReferences[ObjectIds.ObjectsFolder][0], Is.SameAs(mutable));
        }

        [Test]
        public async Task StartupCapturesEmptyReferenceTablesAsync()
        {
            using MasterNodeManager master = CreateMaster([Manager(_ => { }), Manager(_ => { })]);

            await master.StartupAsync().ConfigureAwait(false);
            ArrayOf<PreparedNodeManager> prepared = await ((IDynamicNodeManagerHost)master)
                .TakeStartupNodeManagersAsync().ConfigureAwait(false);

            Assert.That(prepared, Has.Count.EqualTo(2));
            Assert.That(prepared[0].ExternalReferences, Is.Empty);
            Assert.That(prepared[1].ExternalReferences, Is.Empty);
        }

        private static NodeStateReference Reference(uint target)
        {
            return new NodeStateReference(ReferenceTypeIds.Organizes, false, new NodeId(target, 2));
        }

        private static IAsyncNodeManager Manager(Action<IDictionary<NodeId, IList<IReference>>> create)
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.SetupGet(value => value.NamespaceUris).Returns(["urn:test:startup-reference"]);
            manager.Setup(value => value.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Callback<IDictionary<NodeId, IList<IReference>>, CancellationToken>((references, _) =>
                    create(references))
                .Returns(default(ValueTask));
            return manager.Object;
        }

        private static MasterNodeManager CreateMaster(ArrayOf<IAsyncNodeManager> managers)
        {
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(Mock.Of<IConfigurationNodeManager>());
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(Mock.Of<ICoreNodeManager>());
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:test:server");
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
            return new MasterNodeManager(
                server.Object,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                null,
                managers.ToArray(),
                null);
        }

        private sealed class MutableReference : IReference
        {
            public MutableReference(IReference reference)
            {
                ReferenceTypeId = reference.ReferenceTypeId;
                IsInverse = reference.IsInverse;
                TargetId = reference.TargetId;
            }

            public NodeId ReferenceTypeId { get; set; }

            public bool IsInverse { get; set; }

            public ExpandedNodeId TargetId { get; set; }
        }
    }
}

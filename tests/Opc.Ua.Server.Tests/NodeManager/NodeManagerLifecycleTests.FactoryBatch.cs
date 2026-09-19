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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Server.Tests.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [Test]
        public async Task PreparedBatchRejectsUnsupportedFactoryBeforeCandidateEffectsAsync()
        {
            var fixture = new ServerFixture<LifecycleTestServer>(telemetry =>
                new LifecycleTestServer(telemetry)
                {
                    PrivateEncodeableFactory = new ForeignBatchFactory(EncodeableFactory.Create())
                })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            try
            {
                LifecycleTestServer server = await fixture.StartAsync(Path.Combine(m_pkiRoot, "foreign-factory"))
                    .ConfigureAwait(false);
                var candidate = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
                int namespaces = server.CurrentInstance.NamespaceUris.Count;
                var lifecycle = (INodeManagerBatchLifecycle)server.NodeManagerLifecycle;
                await Assert.ThatAsync(() => lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(candidate.Object)]).AsTask(),
                    Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
                candidate.VerifyNoOtherCalls();
                Assert.That(lifecycle.Registrations.IsEmpty, Is.True);
                Assert.That(server.CurrentInstance.NamespaceUris.Count, Is.EqualTo(namespaces));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PreparedBatchFactoryOwnershipEndsWithServerLifetimeAsync()
        {
            IEncodeableFactory factory = EncodeableFactory.Create();
            var fixture = new ServerFixture<LifecycleTestServer>(telemetry =>
                new LifecycleTestServer(telemetry) { PrivateEncodeableFactory = factory })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            string pki = Path.Combine(m_pkiRoot, "factory-lifetime");
            ExpandedNodeId alias = new(8308, "urn:opcfoundation.org:Tests:FactoryLifetime");
            try
            {
                LifecycleTestServer first = await fixture.StartAsync(pki).ConfigureAwait(false);
                Assert.That(first.CurrentInstance.Factory.TryGetEncodeableType(
                    DataTypeIds.Range, out IEncodeableType type), Is.True);
                first.CurrentInstance.Factory.Builder.AddEncodeableType(alias, type).Commit();
                await fixture.StopAsync().ConfigureAwait(false);
                Assert.That(factory.TryGetEncodeableType(alias, out IEncodeableType retained), Is.True,
                    "A supplied private factory must retain committed registrations when its server stops.");
                Assert.That(retained, Is.SameAs(type));
                LifecycleTestServer second = await fixture.StartAsync(pki).ConfigureAwait(false);
                Assert.That(second.CurrentInstance.Factory, Is.SameAs(factory));
                Assert.That(second.CurrentInstance.Factory.TryGetEncodeableType(alias, out _), Is.True);
                var lifecycle = (INodeManagerBatchLifecycle)second.NodeManagerLifecycle;
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateBatchComplexTypeOptions(false)))]).ConfigureAwait(false);
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PreparedBatchAliasIsPrivateWhenComplexTypeLoadingIsDisabledAsync()
        {
            NodeManagerRegistration current = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateBatchComplexTypeOptions(false), null).ConfigureAwait(false);
            m_server.LoadComplexTypes = false;
            ExpandedNodeId alias = new(kBatchAliasId, RuntimeNodeSetTestServer.NamespaceUri);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(current,
                    new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(true)))])
                .ConfigureAwait(false))
            {
                Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(alias, out _), Is.False);
                await prepared.CommitAsync(_ => default).ConfigureAwait(false);
            }
            Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(alias, out _), Is.True);
        }

        [Test]
        public async Task PreparedBatchFailureDoesNotRetainFactoryRegistrationsAsync()
        {
            var failed = new Mock<IAsyncNodeManagerFactory>();
            failed.Setup(factory => factory.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Later candidate preparation failed."));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await Assert.ThatAsync(() => lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false))),
                    NodeManagerBatchChange.Add(failed.Object)
                ]).AsTask(), Throws.TypeOf<IOException>()).ConfigureAwait(false);
            Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(
                new ExpandedNodeId(RuntimeNodeSetTestServer.TestPointDataType, RuntimeNodeSetTestServer.NamespaceUri),
                out _), Is.False);
            Assert.That(lifecycle.Registrations.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchSchemaResolverChangesOnlyOnPublicationAsync(bool publish)
        {
            const string markerNamespace = "urn:opcfoundation.org:Tests:FactoryResolverMarker";
            ExpandedNodeId marker = new(1, markerNamespace);
            var original = new DataTypeDefinitionRegistry();
            original.Add(new UaTypeDescription(marker, new QualifiedName("Marker"),
                new EnumDefinition { Fields = [] }, markerNamespace));
            var holder = new ServerDataTypeDefinitionResolver();
            holder.SetResolver(original);
            m_server.ComplexTypeResolverHolder = holder;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            ExpandedNodeId structure = new(RuntimeNodeSetTestServer.TestPointDataType,
                RuntimeNodeSetTestServer.NamespaceUri);
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))])
                .ConfigureAwait(false))
            {
                Assert.That(holder.TryResolve(marker, out _), Is.True);
                Assert.That(holder.TryResolve(structure, out _), Is.False);
                if (publish)
                {
                    await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                }
            }
            Assert.That(holder.TryResolve(marker, out _), Is.EqualTo(!publish));
            Assert.That(holder.TryResolve(structure, out _), Is.EqualTo(publish));
        }

        [Test]
        public async Task PreparedBatchRejectsChangedFactoryBeforeDecisionAsync()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))])
                .ConfigureAwait(false);
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType type), Is.True);
            ExpandedNodeId alias = new(8305, "urn:opcfoundation.org:Tests:ConcurrentFactory");
            factory.Builder.AddEncodeableType(alias, type).Commit();
            int decisions = 0;
            await Assert.ThatAsync(() => prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }).AsTask(), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(factory.TryGetEncodeableType(alias, out IEncodeableType retained), Is.True);
            Assert.That(retained, Is.SameAs(type));
            Assert.That(prepared.IsCommitted, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchDoesNotLoseFactoryWritesDuringDecisionAsync(bool rejectDecision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType type), Is.True);
            ExpandedNodeId alias = new(8306, "urn:opcfoundation.org:Tests:ConcurrentFactory");
            IEncodeableFactoryBuilder writer = factory.Builder.AddEncodeableType(alias, type);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))],
                timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (rejectDecision)
                {
                    throw new IOException("Confirmed noncommit.");
                }
            }, timeout.Token).AsTask();
            bool accepted = false;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    writer.Commit();
                    accepted = true;
                }
                catch (InvalidOperationException)
                {
                    Assert.That(factory.TryGetEncodeableType(alias, out _), Is.False);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            if (rejectDecision)
            {
                await Assert.ThatAsync(() => commit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                writer.Commit();
                accepted = true;
            }
            else
            {
                NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
            }
            Assert.That(factory.TryGetEncodeableType(alias, out IEncodeableType retained), Is.EqualTo(accepted),
                "A factory write must survive publication or be rejected before effects.");
            if (accepted)
            {
                Assert.That(retained, Is.SameAs(type));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchFactoryTypesRemainPrivateUntilPublicationAsync(bool publish)
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            ExpandedNodeId structure = new(RuntimeNodeSetTestServer.TestPointDataType,
                RuntimeNodeSetTestServer.NamespaceUri);
            ExpandedNodeId encoding = new(RuntimeNodeSetTestServer.TestPointBinaryEncoding,
                RuntimeNodeSetTestServer.NamespaceUri);
            ExpandedNodeId enumeration = new(RuntimeNodeSetTestServer.TestColorDataType,
                RuntimeNodeSetTestServer.NamespaceUri);
            Assert.That(factory.TryGetEncodeableType(structure, out _), Is.False);
            Assert.That(factory.TryGetEnumeratedType(enumeration, out _), Is.False);
            bool preparedStructure;
            bool preparedEncoding;
            bool preparedEnumeration;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))])
                .ConfigureAwait(false))
            {
                preparedStructure = factory.TryGetEncodeableType(structure, out _);
                preparedEncoding = factory.TryGetEncodeableType(encoding, out _);
                preparedEnumeration = factory.TryGetEnumeratedType(enumeration, out _);
                if (publish)
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(preparedStructure, Is.False, "A prepared structure must not enter the serving factory.");
                Assert.That(preparedEncoding, Is.False, "A prepared encoding must not enter the serving factory.");
                Assert.That(preparedEnumeration, Is.False, "A prepared enum must not enter the serving factory.");
                Assert.That(factory.TryGetEncodeableType(structure, out IEncodeableType structureType),
                    Is.EqualTo(publish));
                Assert.That(factory.TryGetEncodeableType(encoding, out IEncodeableType encodingType),
                    Is.EqualTo(publish));
                Assert.That(factory.TryGetEnumeratedType(enumeration, out _), Is.EqualTo(publish));
                if (publish)
                {
                    Assert.That(encodingType, Is.SameAs(structureType));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchEncodingAliasRemainsPrivateUntilPublicationAsync(bool publish)
        {
            NodeManagerRegistration current = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateBatchComplexTypeOptions(false), null).ConfigureAwait(false);
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            ExpandedNodeId structure = new(RuntimeNodeSetTestServer.TestPointDataType,
                RuntimeNodeSetTestServer.NamespaceUri);
            ExpandedNodeId alias = new(kBatchAliasId, RuntimeNodeSetTestServer.NamespaceUri);
            Assert.That(factory.TryGetEncodeableType(structure, out IEncodeableType original), Is.True);
            Assert.That(factory.TryGetEncodeableType(alias, out _), Is.False);
            bool visibleWhilePrepared;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(current,
                    new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(true)))])
                .ConfigureAwait(false))
            {
                visibleWhilePrepared = factory.TryGetEncodeableType(alias, out _);
                if (publish)
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(visibleWhilePrepared, Is.False, "An alias must stay private until its owner is published.");
                Assert.That(factory.TryGetEncodeableType(alias, out IEncodeableType resolved), Is.EqualTo(publish));
                Assert.That(factory.TryGetEncodeableType(structure, out IEncodeableType retained), Is.True);
                Assert.That(retained, Is.SameAs(original));
                if (publish)
                {
                    Assert.That(resolved, Is.SameAs(original));
                }
            }
        }

        private static RuntimeNodeSetOptions CreateBatchComplexTypeOptions(bool includeAlias)
        {
            using Stream source = RuntimeNodeSetTestServer.OpenTestStream();
            XDocument document = XDocument.Load(source);
            XNamespace ns = document.Root.Name.Namespace;
            if (includeAlias)
            {
                XElement structure = document.Root.Elements(ns + "UADataType").Single(element =>
                    element.Attribute("NodeId").Value == $"ns=1;i={RuntimeNodeSetTestServer.TestPointDataType}");
                structure.Element(ns + "References").Add(new XElement(ns + "Reference",
                    new XAttribute("ReferenceType", "HasEncoding"), $"ns=1;i={kBatchAliasId}"));
                document.Root.Add(new XElement(ns + "UAObject",
                    new XAttribute("NodeId", $"ns=1;i={kBatchAliasId}"),
                    new XAttribute("BrowseName", "Default XML"),
                    new XElement(ns + "DisplayName", "Default XML"),
                    new XElement(ns + "References",
                        new XElement(ns + "Reference", new XAttribute("ReferenceType", "HasEncoding"),
                            new XAttribute("IsForward", "false"),
                            $"ns=1;i={RuntimeNodeSetTestServer.TestPointDataType}"),
                        new XElement(ns + "Reference", new XAttribute("ReferenceType", "HasTypeDefinition"), "i=76"))));
            }
            string xml = document.ToString();
            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream("batch-complex-types",
                        _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [RuntimeNodeSetTestServer.NamespaceUri])
                ],
                DefaultNamespaceUri = RuntimeNodeSetTestServer.NamespaceUri
            };
        }

        private sealed class ForeignBatchFactory(IEncodeableFactory factory) : IEncodeableFactory
        {
            public IEnumerable<ExpandedNodeId> KnownTypeIds => factory.KnownTypeIds;
            public IEncodeableFactoryBuilder Builder => factory.Builder;

            public bool TryGetEncodeableType(ExpandedNodeId typeId, out IEncodeableType type)
            {
                return factory.TryGetEncodeableType(typeId, out type);
            }

            public bool TryGetEnumeratedType(ExpandedNodeId typeId, out IEnumeratedType type)
            {
                return factory.TryGetEnumeratedType(typeId, out type);
            }

            public bool TryGetType(XmlQualifiedName xmlName, out IType type)
            {
                return factory.TryGetType(xmlName, out type);
            }
        }

        private const uint kBatchAliasId = 15012;
    }
}

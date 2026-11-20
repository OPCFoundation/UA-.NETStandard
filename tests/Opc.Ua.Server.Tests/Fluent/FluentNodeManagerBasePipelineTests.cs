/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Covers the configure pipeline that
    /// <see cref="FluentNodeManagerBase.CreateAddressSpaceAsync"/> runs for
    /// hand-written managers that only override
    /// <c>ConfigureAsync</c>.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public sealed class FluentNodeManagerBasePipelineTests
    {
        private const string kNamespaceUri = "http://opcfoundation.org/UA/Pipeline/";

        [Test]
        public async Task CreateAddressSpaceRegistersNodesAuthoredInConfigureAsync()
        {
            using var manager = new PipelineTestManager(builder =>
                builder.AddFolder("Devices"));
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();

            await manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

            NodeState? folder = manager.FindByBrowseName("Devices");
            Assert.Multiple(() =>
            {
                Assert.That(manager.ConfigureCount, Is.EqualTo(1));
                Assert.That(folder, Is.InstanceOf<FolderState>());
                Assert.That(
                    folder!.NodeId.NamespaceIndex,
                    Is.EqualTo(manager.NamespaceIndex),
                    "the builder defaults to the manager's own namespace");
                Assert.That(
                    externalReferences.ContainsKey(ObjectIds.ObjectsFolder),
                    Is.True,
                    "the Organizes reference to the Objects folder is mirrored");
            });
        }

        [Test]
        public async Task CreateAddressSpaceRejectsNullExternalReferences()
        {
            using var manager = new PipelineTestManager(_ => { });

            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await manager.CreateAddressSpaceAsync(null!).ConfigureAwait(false));
            Assert.That(manager.ConfigureCount, Is.Zero);
        }

        [Test]
        public async Task CreateAddressSpaceSealsTheBuilderAndReplaysNodeAddedAsync()
        {
            INodeManagerBuilder? retained = null;
            int added = 0;
            using var manager = new PipelineTestManager(builder =>
            {
                retained = builder;
                builder.AddFolder("Devices").OnNodeAdded((_, _) => added++);
            });

            await manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(added, Is.EqualTo(1), "OnNodeAdded fires once after sealing");
                ServiceResultException sealedBuilder = Assert.Throws<ServiceResultException>(
                    () => retained!.AddFolder("Late"))!;
                Assert.That(sealedBuilder.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            });
        }

        private sealed class PipelineTestManager : FluentNodeManagerBase
        {
            public PipelineTestManager(Action<INodeManagerBuilder> configure)
                : base(CreateMockServer(), kNamespaceUri)
            {
                m_configure = configure;
            }

            public int ConfigureCount { get; private set; }

            public NodeState? FindByBrowseName(string name)
            {
                foreach (NodeState node in PredefinedNodes.Values)
                {
                    if (node.BrowseName.Name == name)
                    {
                        return node;
                    }
                }
                return null;
            }

            protected override ValueTask ConfigureAsync(
                INodeManagerBuilder builder,
                CancellationToken cancellationToken)
            {
                ConfigureCount++;
                m_configure(builder);
                return default;
            }

            private static IServerInternal CreateMockServer()
            {
                var namespaceUris = new NamespaceTable();
                namespaceUris.Append(Ua.Namespaces.OpcUa);

                var telemetry = new Mock<ITelemetryContext>();
                telemetry
                    .SetupGet(context => context.LoggerFactory)
                    .Returns(NullLoggerFactory.Instance);

                var server = new Mock<IServerInternal>();
                server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
                server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
                server.SetupGet(value => value.MessageContext)
                    .Returns(ServiceMessageContext.Create(telemetry.Object));
                server.SetupGet(value => value.DefaultSystemContext)
                    .Returns(new ServerSystemContext(server.Object));
                return server.Object;
            }

            private readonly Action<INodeManagerBuilder> m_configure;
        }
    }
}

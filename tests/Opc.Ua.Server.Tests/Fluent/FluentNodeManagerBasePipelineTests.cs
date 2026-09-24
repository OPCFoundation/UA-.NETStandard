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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable

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

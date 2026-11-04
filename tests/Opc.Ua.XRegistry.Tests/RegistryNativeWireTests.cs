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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    [NonParallelizable]
    public sealed class RegistryNativeWireTests
    {
        [Test]
        public async Task GeneratedNativeMethodCarriesExactSubtypesThroughASecuredTcpSession()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string pki = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                nameof(RegistryNativeWireTests), Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<WireServer>(context => new WireServer(context))
            {
                AutoAccept = true
            };
            var client = new ClientFixture(telemetry);
            try
            {
                await fixture.StartAsync(Path.Combine(pki, "server")).ConfigureAwait(false);
                await client.LoadClientConfigurationAsync(Path.Combine(pki, "client")).ConfigureAwait(false);
                using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                    new Uri($"opc.tcp://localhost:{fixture.Port}/WireServer"),
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                session.MessageContext.Factory.Builder.AddOpcUaXRegistry().Commit();
                Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                int index = session.NamespaceUris.GetIndex(XRegistryWellKnown.XRegistryNamespaceUri);
                Assert.That(index, Is.GreaterThan(0));
                var proxy = new NativeRegistryAccessTypeClient(
                    session, new NodeId(900001u, (ushort)index), telemetry);
                RegistryReadResultDataType result = await proxy.ReadDocumentAsync(new RegistryReadRequestDataType
                {
                    TargetXid = "/",
                    DocumentKind = "metadata",
                    View = 0,
                    MaxItems = 10,
                    ContinuationPoint = ByteString.Empty
                }).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(result.Epoch, Is.EqualTo(3));
                    Assert.That(result.Document.TryGetValue(out RegistryNumberValueDataType? number), Is.True);
                    Assert.That(number!.Kind, Is.EqualTo(3));
                    Assert.That(number.Coefficient, Is.EqualTo(ByteString.FromHexString("64")));
                    Assert.That(number.Exponent, Is.EqualTo(-2));
                    Assert.That(number.IsInteger, Is.False);
                });
                RegistrySnapshotOpenResultDataType opened = await proxy.OpenDocumentAsync(
                    new RegistrySnapshotOpenRequestDataType
                    {
                        TargetXid = "/large",
                        DocumentKind = "metadata",
                        View = 0,
                        ExpectedEpoch = 3
                    }).ConfigureAwait(false);
                Assert.That(opened.TargetEpoch, Is.EqualTo(3));
                Assert.That(opened.RegistryEpoch, Is.EqualTo(7));
                RegistrySnapshotReadResultDataType part = await proxy.ReadDocumentPartAsync(
                    new RegistrySnapshotReadRequestDataType
                    {
                        SnapshotId = opened.SnapshotId,
                        Path = [new RegistryPathElementDataType { Kind = 0, Name = "Value" }],
                        MaxItems = 128,
                        MaxBytes = 256
                    }).ConfigureAwait(false);
                Assert.That(part.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(part.TotalLength, Is.EqualTo(8192));
                Assert.That(part.Complete, Is.False);
                Assert.That(part.Value.TryGetValue(out string text), Is.True);
                Assert.That(text, Has.Length.GreaterThan(0).And.Length.LessThanOrEqualTo(128));
                RegistrySnapshotCloseResultDataType closed = await proxy.CloseDocumentAsync(opened.SnapshotId)
                    .ConfigureAwait(false);
                Assert.That(closed.StatusCode, Is.EqualTo(StatusCodes.Good));
                await session.CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
                await fixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(pki))
                {
                    Directory.Delete(pki, recursive: true);
                }
            }
        }

        private sealed class WireServer : ReferenceServer
        {
            public WireServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
                AddNodeManager(new WireFactory());
            }
        }

        private sealed class WireFactory : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => [XRegistryWellKnown.XRegistryNamespaceUri];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<IAsyncNodeManager>(new WireNodeManager(server, configuration));
            }
        }

        private sealed class WireNodeManager : AsyncCustomNodeManager
        {
            public WireNodeManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, server.Telemetry.CreateLogger<WireNodeManager>(),
                    XRegistryWellKnown.XRegistryNamespaceUri)
            {
                server.Factory.Builder.AddOpcUaXRegistry().Commit();
            }

            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<NodeStateCollection>(new NodeStateCollection().AddOpcUaXRegistry(context));
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                NativeRegistryAccessState access = SystemContext.CreateInstanceOfNativeRegistryAccessType(
                    null!, new QualifiedName("WireAccess", NamespaceIndexes[0]));
                access.NodeId = new NodeId(900001u, NamespaceIndexes[0]);
                access.AddReadDocument(SystemContext);
                access.ReadDocument!.OnCallAsync = ReadAsync;
                var messageContext = new ServiceMessageContext(Server.Telemetry, Server.Factory)
                {
                    NamespaceUris = Server.NamespaceUris,
                    ServerUris = Server.ServerUris
                };
                m_snapshots = new RegistryNativeSnapshots(messageContext);
                m_binding = new RegistrySnapshotBinding(access, SystemContext, m_snapshots, _ => "wire-reader",
                    (_, request, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (request.TargetXid != "/large")
                        {
                            throw new ServiceResultException(StatusCodes.BadNotFound);
                        }
                        return new ValueTask<RegistrySnapshotSource>(new RegistrySnapshotSource(
                            new RegistryStringValueDataType { Kind = 2, Value = new string('x', 8192) }, 3, 7));
                    });
                await AddPredefinedNodeAsync(SystemContext, access, cancellationToken).ConfigureAwait(false);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_binding?.Dispose();
                    m_snapshots?.Dispose();
                }
                base.Dispose(disposing);
            }

            private static ValueTask<ReadDocumentMethodStateResult> ReadAsync(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                RegistryReadRequestDataType request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.That(request.TargetXid, Is.EqualTo("/"));
                return new ValueTask<ReadDocumentMethodStateResult>(new ReadDocumentMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Result = new RegistryReadResultDataType
                    {
                        StatusCode = StatusCodes.Good,
                        Epoch = 3,
                        Document = new ExtensionObject(new RegistryNumberValueDataType
                        {
                            Kind = 3,
                            Coefficient = ByteString.FromHexString("64"),
                            Exponent = -2,
                            IsInteger = false,
                            NegativeZero = false
                        })
                    }
                });
            }

            private RegistryNativeSnapshots? m_snapshots;
            private RegistrySnapshotBinding? m_binding;
        }
    }
}

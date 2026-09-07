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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests.Client
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class WotConnectivityClientTests
    {
        [Test]
        public void ConstructorRejectsInvalidArguments()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            var managementId = new NodeId("management", 2);

            Assert.That(
                () => new WotConnectivityClient(null!, managementId, telemetry),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
            Assert.That(
                () => new WotConnectivityClient(session.Object, NodeId.Null, telemetry),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("managementObjectId"));
            Assert.That(
                () => new WotConnectivityClient(session.Object, managementId, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public async Task ForServerAsyncResolvesManagementObjectAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            var managementId = new NodeId("management", 2);
            SetupTranslate(session, StatusCodes.Good, managementId);

            WotConnectivityClient client = await WotConnectivityClient
                .ForServerAsync(session.Object, telemetry)
                .ConfigureAwait(false);

            Assert.That(client.Session, Is.SameAs(session.Object));
            Assert.That(client.ManagementObjectId, Is.EqualTo(managementId));
            Assert.That(client.Telemetry, Is.SameAs(telemetry));
        }

        [Test]
        public void ForServerAsyncRejectsMissingEntryPoint()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            SetupTranslate(session, StatusCodes.BadNoMatch, NodeId.Null);

            Assert.That(
                () => WotConnectivityClient
                    .ForServerAsync(session.Object, telemetry)
                    .AsTask(),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task OpenAssetAsyncReturnsClientWithDisplayNameAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            var assetId = new NodeId("asset", 2);
            var fileId = new NodeId("file", 2);
            SetupTranslate(session, StatusCodes.Good, fileId);
            SetupRead(session, new DataValue(new LocalizedText("Boiler")));
            var client = new WotConnectivityClient(
                session.Object,
                new NodeId("management", 2),
                telemetry);

            WotAssetClient asset = await client.OpenAssetAsync(assetId).ConfigureAwait(false);

            Assert.That(asset.AssetId, Is.EqualTo(assetId));
            Assert.That(asset.Name, Is.EqualTo("Boiler"));
            Assert.That(asset.File.ObjectId, Is.EqualTo(fileId));
        }

        [Test]
        public async Task OpenAssetAsyncUsesEmptyNameWhenDisplayNameReadFailsAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            var assetId = new NodeId("asset", 2);
            SetupTranslate(session, StatusCodes.Good, new NodeId("file", 2));
            SetupRead(session, DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown));
            var client = new WotConnectivityClient(
                session.Object,
                new NodeId("management", 2),
                telemetry);

            WotAssetClient asset = await client.OpenAssetAsync(assetId).ConfigureAwait(false);

            Assert.That(asset.Name, Is.Empty);
        }

        [Test]
        public void OpenAssetAsyncRejectsMissingAssetAndFile()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            var client = new WotConnectivityClient(
                session.Object,
                new NodeId("management", 2),
                telemetry);

            Assert.That(
                () => client.OpenAssetAsync(NodeId.Null).AsTask(),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("assetId"));

            SetupTranslate(session, StatusCodes.BadNoMatch, NodeId.Null);
            Assert.That(
                () => client.OpenAssetAsync(new NodeId("asset", 2)).AsTask(),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNoMatch));
        }

        [Test]
        public async Task EnumerateAssetsAsyncReturnsBrowseSnapshotAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            var managementId = new NodeId("management", 2);
            var assetOne = new NodeId("asset-1", 2);
            var assetTwo = new NodeId("asset-2", 2);
            session
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = new[]
                    {
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            References = new[]
                            {
                                new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId(assetOne),
                                    DisplayName = new LocalizedText("Asset One")
                                },
                                new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId(assetTwo),
                                    DisplayName = LocalizedText.Null
                                }
                            }.ToArrayOf()
                        }
                    }.ToArrayOf(),
                    DiagnosticInfos = default
                });
            var client = new WotConnectivityClient(session.Object, managementId, telemetry);
            var entries = new List<WotAssetEntry>();

            await foreach (WotAssetEntry entry in client.EnumerateAssetsAsync()
                .ConfigureAwait(false))
            {
                entries.Add(entry);
            }

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0], Is.EqualTo(new WotAssetEntry(assetOne, "Asset One")));
            Assert.That(entries[1], Is.EqualTo(new WotAssetEntry(assetTwo, string.Empty)));
        }

        [Test]
        public async Task AssetClientEnumeratesPropertiesAndActionsAsync()
        {
            (Mock<ISession> session, ITelemetryContext telemetry) = CreateSession();
            var propertyId = new NodeId("temperature", 2);
            var actionId = new NodeId("reset", 2);
            session
                .SetupSequence(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateBrowseResponse(propertyId, "Temperature"))
                .ReturnsAsync(CreateBrowseResponse(actionId, "Reset"));
            var file = new WoTAssetFileTypeClient(
                session.Object,
                new NodeId("file", 2),
                telemetry);
            var asset = new WotAssetClient(
                session.Object,
                new NodeId("asset", 2),
                "Boiler",
                file,
                telemetry);
            var properties = new List<WotAssetVariableEntry>();
            var actions = new List<WotAssetVariableEntry>();

            await foreach (WotAssetVariableEntry entry in asset.EnumeratePropertiesAsync()
                .ConfigureAwait(false))
            {
                properties.Add(entry);
            }
            await foreach (WotAssetVariableEntry entry in asset.EnumerateActionsAsync()
                .ConfigureAwait(false))
            {
                actions.Add(entry);
            }

            Assert.That(
                properties,
                Is.EqualTo([new WotAssetVariableEntry(propertyId, "Temperature")]));
            Assert.That(
                actions,
                Is.EqualTo([new WotAssetVariableEntry(actionId, "Reset")]));
        }

        private static (Mock<ISession> Session, ITelemetryContext Telemetry) CreateSession()
        {
            ITelemetryContext telemetry = Mock.Of<ITelemetryContext>();
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session.SetupGet(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            return (session, telemetry);
        }

        private static void SetupTranslate(
            Mock<ISession> session,
            StatusCode statusCode,
            NodeId targetId)
        {
            ArrayOf<BrowsePathTarget> targets = targetId.IsNull
                ? []
                : new[]
                {
                    new BrowsePathTarget
                    {
                        TargetId = new ExpandedNodeId(targetId),
                        RemainingPathIndex = uint.MaxValue
                    }
                }.ToArrayOf();
            session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = new[]
                    {
                        new BrowsePathResult
                        {
                            StatusCode = statusCode,
                            Targets = targets
                        }
                    }.ToArrayOf(),
                    DiagnosticInfos = default
                });
        }

        private static void SetupRead(Mock<ISession> session, DataValue result)
        {
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = new[] { result }.ToArrayOf(),
                    DiagnosticInfos = default
                });
        }

        private static BrowseResponse CreateBrowseResponse(NodeId nodeId, string browseName)
        {
            return new BrowseResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = new[]
                {
                    new BrowseResult
                    {
                        StatusCode = StatusCodes.Good,
                        References = new[]
                        {
                            new ReferenceDescription
                            {
                                NodeId = new ExpandedNodeId(nodeId),
                                BrowseName = new QualifiedName(browseName, nodeId.NamespaceIndex)
                            }
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                DiagnosticInfos = default
            };
        }
    }
}

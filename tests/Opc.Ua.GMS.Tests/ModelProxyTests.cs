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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Machinery.Result;
using Opc.Ua.MachineTool;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Tests generated GMS proxies through the session transport boundary without a client/server runtime dependency.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class ModelProxyTests
    {
        /// <summary>
        /// GMS child access uses each declaration's namespace and specialized proxy, not the root model namespace.
        /// </summary>
        [TestCase("Identification", "http://opcfoundation.org/UA/DI/", typeof(GMSIdentificationTypeClient))]
        [TestCase("Equipment", "http://opcfoundation.org/UA/MachineTool/", typeof(GMSEquipmentTypeClient))]
        [TestCase("Monitoring", "http://opcfoundation.org/UA/MachineTool/", typeof(GMSMonitoringTypeClient))]
        [TestCase("Notification", "http://opcfoundation.org/UA/MachineTool/", typeof(NotificationTypeClient))]
        [TestCase("Production", "http://opcfoundation.org/UA/MachineTool/", typeof(ProductionTypeClient))]
        [TestCase("ResultManagement", "http://opcfoundation.org/UA/GMS/", typeof(GMSResultManagementTypeClient))]
        public async Task GmsChildProxiesPreserveQualifiedPathsAndCacheTheResolvedInstance(
            string childName,
            string namespaceUri,
            Type proxyType)
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(reverseNamespaces: true);
            var objectId = new NodeId("gms", 1);
            var childId = new NodeId("gms/" + childName, 1);
            var expectedName = new QualifiedName(childName, context.NamespaceUris.GetIndexOrAppend(namespaceUri));
            using var cancellation = new CancellationTokenSource();
            Mock<ISessionClient> session = CreateBrowsingSession(
                context, objectId, expectedName, childId, cancellation.Token);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var proxy = new GMSTypeClient(session.Object, objectId, telemetry);

            BaseObjectTypeClient? first =
                await GetChildAsync(proxy, childName, telemetry, cancellation.Token).ConfigureAwait(false);
            BaseObjectTypeClient? second =
                await GetChildAsync(proxy, childName, telemetry, cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.TypeOf(proxyType));
                Assert.That(first!.ObjectId, Is.EqualTo(childId));
                Assert.That(first.Session, Is.SameAs(session.Object));
                Assert.That(second, Is.SameAs(first));
                Assert.That(proxy, Is.InstanceOf<MachineToolTypeClient>());
                Assert.That(context.NamespaceUris.GetIndex(MachineToolModels.WmtpNamespaceUri), Is.EqualTo(-1));
            });
            session.Verify(client => client.TranslateBrowsePathsToNodeIdsAsync(
                null, It.IsAny<ArrayOf<BrowsePath>>(), cancellation.Token), Times.Once);
        }

        /// <summary>
        /// A missing namespace does not create a fake optional child or issue a service call.
        /// </summary>
        [Test]
        public async Task MissingGmsNamespaceLeavesOptionalCorrectionsAbsent()
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(registerModel: false);
            context.NamespaceUris.GetIndexOrAppend(Machinery.Result.Namespaces.MachineryResult);
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            session.SetupGet(client => client.MessageContext).Returns(context);
            var proxy = new GMSResultManagementTypeClient(
                session.Object, new NodeId("results", 1), NUnitTelemetryContext.Create());

            FolderTypeClient? folder =
                await proxy.GetCorrectionsFolderAsync(NUnitTelemetryContext.Create()).ConfigureAwait(false);

            Assert.That(folder, Is.Null);
            Assert.That(context.NamespaceUris.GetIndex(GmsModel.NamespaceUri), Is.EqualTo(-1));
            session.VerifyGet(client => client.MessageContext, Times.Once);
            session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The result method uses its inherited declaration identity and preserves typed results and domain codes.
        /// </summary>
        [TestCase(0)]
        [TestCase(-17)]
        public async Task ResultProxyUsesInheritedDeclarationAndPreservesDomainErrors(int error)
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(reverseNamespaces: true);
            var result = new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "inspection-17" },
                ResultContent = [new Variant(25.75)]
            };
            Mock<ISessionClient> session = CreateCallSession(
                context, [new Variant(42u), Variant.FromStructure(result), new Variant(error)]);
            var objectId = new NodeId("gms/results", 1);
            var proxy = new GMSResultManagementTypeClient(session.Object, objectId, NUnitTelemetryContext.Create());
            using var cancellation = new CancellationTokenSource();

            (uint handle, ResultDataType actual, int actualError) =
                await proxy.GetLatestResultAsync(1500, cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(handle, Is.EqualTo(42));
                Assert.That(actual.IsEqual(result), Is.True);
                Assert.That(actual.ResultMetaData!.ResultId, Is.EqualTo("inspection-17"));
                Assert.That(actualError, Is.EqualTo(error));
            });
            var methodId = NodeId.Create(7008, Machinery.Result.Namespaces.MachineryResult, context.NamespaceUris);
            session.Verify(client => client.CallAsync(
                null,
                It.Is<ArrayOf<CallMethodRequest>>(requests =>
                    requests.Count == 1 &&
                    requests[0].ObjectId == objectId &&
                    requests[0].MethodId == methodId &&
                    requests[0].InputArguments.Count == 1 &&
                    requests[0].InputArguments[0] == new Variant(1500)),
                cancellation.Token), Times.Once);
        }

        /// <summary>
        /// Releasing a result handle preserves the UInt32 value and inherited method identity.
        /// </summary>
        [TestCase(0)]
        [TestCase(-17)]
        public async Task ResultProxyReleasesOnlyTheSuppliedHandle(int error)
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(reverseNamespaces: true);
            Mock<ISessionClient> session = CreateCallSession(context, [new Variant(error)]);
            var objectId = new NodeId("gms/results", 1);
            var proxy = new GMSResultManagementTypeClient(session.Object, objectId, NUnitTelemetryContext.Create());

            int actualError = await proxy.ReleaseResultHandleAsync(4000000001u).ConfigureAwait(false);

            Assert.That(actualError, Is.EqualTo(error));
            var methodId = NodeId.Create(7007, Machinery.Result.Namespaces.MachineryResult, context.NamespaceUris);
            session.Verify(client => client.CallAsync(
                null,
                It.Is<ArrayOf<CallMethodRequest>>(requests =>
                    requests.Count == 1 &&
                    requests[0].ObjectId == objectId &&
                    requests[0].MethodId == methodId &&
                    requests[0].InputArguments.Count == 1 &&
                    requests[0].InputArguments[0] == new Variant(4000000001u)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Missing or mistyped output arguments are bad responses rather than successful empty measurements.
        /// </summary>
        [TestCase("Missing")]
        [TestCase("Handle")]
        [TestCase("Result")]
        [TestCase("Error")]
        public void ResultProxyRejectsMalformedOutput(string malformed)
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext();
            ArrayOf<Variant> output = malformed switch
            {
                "Missing" => [],
                "Handle" => [new Variant("not a handle"), Variant.FromStructure(new ResultDataType()), new Variant(0)],
                "Result" => [new Variant(42u), new Variant("not a result"), new Variant(0)],
                "Error" => [new Variant(42u), Variant.FromStructure(new ResultDataType()), new Variant("not an error")],
                _ => throw new ArgumentOutOfRangeException(nameof(malformed))
            };
            Mock<ISessionClient> session = CreateCallSession(context, output);
            var proxy = new GMSResultManagementTypeClient(
                session.Object, new NodeId("results", 1), NUnitTelemetryContext.Create());

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await proxy.GetLatestResultAsync(0).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        /// <summary>
        /// A method-level service failure is not converted into a domain error or a successful result.
        /// </summary>
        [Test]
        public void ResultProxyPreservesBadServiceStatus()
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext();
            Mock<ISessionClient> session = CreateCallSession(context, [], StatusCodes.BadUserAccessDenied);
            var proxy = new GMSResultManagementTypeClient(
                session.Object, new NodeId("results", 1), NUnitTelemetryContext.Create());

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await proxy.GetLatestResultAsync(0).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        private static Mock<ISessionClient> CreateCallSession(
            ServiceMessageContext context,
            ArrayOf<Variant> outputArguments,
            StatusCode status = default)
        {
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            session.SetupGet(client => client.MessageContext).Returns(context);
            session.Setup(client => client.CallAsync(
                null, It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult { StatusCode = status, OutputArguments = outputArguments }],
                    DiagnosticInfos = []
                }));
            return session;
        }

        private static Mock<ISessionClient> CreateBrowsingSession(
            ServiceMessageContext context,
            NodeId objectId,
            QualifiedName childName,
            NodeId childId,
            CancellationToken cancellationToken)
        {
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            session.SetupGet(client => client.MessageContext).Returns(context);
            session.Setup(client => client.TranslateBrowsePathsToNodeIdsAsync(
                null,
                It.Is<ArrayOf<BrowsePath>>(paths =>
                    paths.Count == 1 &&
                    paths[0].StartingNode == objectId &&
                    paths[0].RelativePath.Elements.Count == 1 &&
                    paths[0].RelativePath.Elements[0].TargetName == childName &&
                    paths[0].RelativePath.Elements[0].ReferenceTypeId == ReferenceTypeIds.HasComponent &&
                    paths[0].RelativePath.Elements[0].IncludeSubtypes &&
                    !paths[0].RelativePath.Elements[0].IsInverse),
                cancellationToken))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = NodeId.ToExpandedNodeId(childId, context.NamespaceUris),
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            }
                        ],
                        DiagnosticInfos = []
                    }));
            return session;
        }

        private static async ValueTask<BaseObjectTypeClient?> GetChildAsync(
            GMSTypeClient proxy,
            string child,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            return child switch
            {
                "Identification" =>
                    await proxy.GetIdentificationAsync(telemetry, cancellationToken).ConfigureAwait(false),
                "Equipment" =>
                    await proxy.GetEquipmentAsync(telemetry, cancellationToken).ConfigureAwait(false),
                "Monitoring" =>
                    await proxy.GetMonitoringAsync(telemetry, cancellationToken).ConfigureAwait(false),
                "Notification" =>
                    await proxy.GetNotificationAsync(telemetry, cancellationToken).ConfigureAwait(false),
                "Production" =>
                    await proxy.GetProductionAsync(telemetry, cancellationToken).ConfigureAwait(false),
                "ResultManagement" =>
                    await proxy.GetResultManagementAsync(telemetry, cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(child))
            };
        }
    }
}

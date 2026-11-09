/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Encoders;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// Preserves the service's method status independently of model validation/refresh outcomes.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable]
    public sealed class WotStatusPreservingMethodTests
    {
        [SetUp]
        public void SetUp()
        {
            m_calls.Clear();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
            m_context.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            ushort instanceNamespace = m_context.NamespaceUris.GetIndexOrAppend("urn:test:wot:instances");
            m_registryId = new NodeId("registry/root", instanceNamespace);
            m_versionId = new NodeId("registry/things/sensor/versions/v7", instanceNamespace);
            m_response = new CallResponse { ResponseHeader = new ResponseHeader(), Results = [] };
            m_session = new Mock<ISession>(MockBehavior.Strict);
            m_session.SetupGet(s => s.MessageContext).Returns(m_context);
            m_session.SetupGet(s => s.NamespaceUris).Returns(m_context.NamespaceUris);
            m_session.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, token) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    m_calls.Add((requests[0], token));
                    return new ValueTask<CallResponse>(m_response);
                });
            m_registry = new WotRegistryClient(m_session.Object, m_registryId, telemetry);
            m_document = new ThingDescriptionFileTypeClient(m_session.Object, m_versionId, telemetry);
        }

        [TestCaseSource(nameof(s_nonBadStatuses))]
        public async Task RefreshAsyncPreservesMethodStatusAndPartialOutcomesAsync(StatusCode methodStatus)
        {
            SetResponse(methodStatus, RefreshOutputs());
            using var cancellation = new CancellationTokenSource();

            WotRegistryRefreshResult result = await m_registry.RefreshAsync(
                [], new WoTRefreshOptionsDataType(), expectedGeneration: 17,
                requestId: "refresh-17", ct: cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.MethodStatus, Is.EqualTo(methodStatus));
                Assert.That(result.NewGeneration, Is.EqualTo(18u));
                Assert.That(result.Summary.RequestId, Is.EqualTo("refresh-17"));
                Assert.That(result.Summary.Generation, Is.EqualTo(18u));
                Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(result.Summary.Total, Is.EqualTo(2u));
                Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
                Assert.That(result.Summary.Failed, Is.EqualTo(1u));
                Assert.That(result.Results.ConvertAll(static r => r.ResourceId), Is.EqualTo(s_resourceIds));
                Assert.That(result.Results.ConvertAll(static r => r.VersionId), Is.EqualTo(s_versionIds));
                Assert.That(result.Results.ConvertAll(static r => r.Outcome), Is.EqualTo(s_resourceOutcomes));
                Assert.That(result.HasFailures, Is.True,
                    "A successful method status must not conceal failed model-level outcomes.");
            });
            AssertWireCall(m_registryId, MethodIds.WoTRegistryType_Refresh, inputCount: 4, token: cancellation.Token);
            Assert.That(m_calls[0].Request.InputArguments[2], Is.EqualTo(Variant.From(17u)));
            Assert.That(m_calls[0].Request.InputArguments[3], Is.EqualTo(Variant.From("refresh-17")));
        }

        [TestCaseSource(nameof(s_badStatuses))]
        public async Task RefreshAsyncRejectsBadMethodStatusWithoutRetry(StatusCode methodStatus)
        {
            SetResponse(methodStatus, RefreshOutputs());

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_registry.RefreshAsync([], new WoTRefreshOptionsDataType(), expectedGeneration: 17)
                    .ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(methodStatus));
            AssertWireCall(
                m_registryId, MethodIds.WoTRegistryType_Refresh, inputCount: 4, token: CancellationToken.None);
        }

        [TestCaseSource(nameof(s_nonBadStatuses))]
        public async Task ValidateWithResultAsyncPreservesStatusAndTypedOutcomeOnExactVersionAsync(
            StatusCode methodStatus)
        {
            SetResponse(methodStatus, [Variant.FromStructure(new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Failed
            })]);
            using var cancellation = new CancellationTokenSource();

            (StatusCode status, WoTValidationOutcomeDataType outcome) = await m_document
                .ValidateWithResultAsync(cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo(methodStatus));
                Assert.That(outcome.FormatValidated, Is.True);
                Assert.That(outcome.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(m_document.ObjectId, Is.EqualTo(new NodeId("registry/things/sensor/versions/v7", 2)));
            });
            AssertWireCall(m_versionId, MethodIds.WoTDocumentType_Validate, inputCount: 0, token: cancellation.Token);
            Assert.That(m_calls[0].Request.MethodId.NamespaceIndex, Is.EqualTo(1),
                "The declaration namespace is not the exact instance's namespace.");
        }

        [Test]
        public async Task ValidateWithResultAsyncDecodesOpaqueBinaryOutcomeAndPreservesStatusAsync()
        {
            var wireOutcome = new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Rejected
            };
            using var encoder = new BinaryEncoder(m_context);
            wireOutcome.Encode(encoder);
            var opaque = new ExtensionObject(
                s_unregisteredEncodingId, ByteString.From(encoder.CloseAndReturnBuffer()!));
            SetResponse(StatusCodes.GoodResultsMayBeIncomplete, [new Variant(opaque)]);

            (StatusCode status, WoTValidationOutcomeDataType outcome) = await m_document.ValidateWithResultAsync()
                .ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete));
            Assert.That(outcome.FormatValidated, Is.True);
            Assert.That(outcome.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            AssertWireCall(
                m_versionId, MethodIds.WoTDocumentType_Validate, inputCount: 0, token: CancellationToken.None);
        }

        [TestCaseSource(nameof(s_badStatuses))]
        public async Task ValidateWithResultAsyncRejectsBadMethodStatusWithoutRetry(StatusCode methodStatus)
        {
            SetResponse(methodStatus, [Variant.FromStructure(new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Success
            })]);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_document.ValidateWithResultAsync().ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(methodStatus));
            AssertWireCall(
                m_versionId, MethodIds.WoTDocumentType_Validate, inputCount: 0, token: CancellationToken.None);
        }

        [TestCase("missing")]
        [TestCase("extra")]
        [TestCase("wrong-type")]
        [TestCase("null-binary")]
        public async Task ValidateWithResultAsyncRejectsInvalidOutcomeShape(string shape)
        {
            Variant valid = Variant.FromStructure(new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Success
            });
            ArrayOf<Variant> output = shape switch
            {
                "missing" => [],
                "extra" => [valid, valid],
                "wrong-type" => [Variant.From(42)],
                "null-binary" => [new Variant(new ExtensionObject(s_unregisteredEncodingId, default(ByteString)))],
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
            SetResponse(StatusCodes.Good, output);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_document.ValidateWithResultAsync().ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            AssertWireCall(
                m_versionId, MethodIds.WoTDocumentType_Validate, inputCount: 0, token: CancellationToken.None);
        }

        [Test]
        public async Task ValidateWithResultAsyncPreservesBadServiceStatus()
        {
            SetResponse(StatusCodes.Good, [Variant.FromStructure(new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Success
            })], serviceStatus: StatusCodes.BadSessionClosed);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_document.ValidateWithResultAsync().ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            AssertWireCall(
                m_versionId, MethodIds.WoTDocumentType_Validate, inputCount: 0, token: CancellationToken.None);
        }

        [Test]
        public async Task ValidateWithResultAsyncRejectsNullProxy()
        {
            ArgumentNullException exception = (await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await WoTDocumentTypeClientExtensions.ValidateWithResultAsync(null!).ConfigureAwait(false)))!;

            Assert.That(exception.ParamName, Is.EqualTo("document"));
            Assert.That(m_calls, Is.Empty);
        }

        private void SetResponse(StatusCode methodStatus, ArrayOf<Variant> outputs, StatusCode serviceStatus = default)
        {
            m_response = new CallResponse
            {
                ResponseHeader = new ResponseHeader { ServiceResult = serviceStatus },
                Results = [new CallMethodResult { StatusCode = methodStatus, OutputArguments = outputs }]
            };
        }

        private static ArrayOf<Variant> RefreshOutputs()
        {
            var summary = new WoTRefreshSummaryDataType
            {
                RequestId = "refresh-17",
                Generation = 18,
                Outcome = WoTOutcomeEnum.Failed,
                Total = 2,
                Succeeded = 1,
                Failed = 1
            };
            ArrayOf<WoTResourceLoadResultDataType> results =
            [
                new WoTResourceLoadResultDataType
                {
                    GroupId = "things", ResourceId = "healthy", VersionId = "v7",
                    Outcome = WoTOutcomeEnum.Success
                },
                new WoTResourceLoadResultDataType
                {
                    GroupId = "things", ResourceId = "broken", VersionId = "v8",
                    Outcome = WoTOutcomeEnum.Rejected
                }
            ];
            return [Variant.FromStructure(summary), Variant.FromStructure(results), Variant.From(18u)];
        }

        private void AssertWireCall(
            NodeId objectId, ExpandedNodeId methodId, int inputCount, CancellationToken token)
        {
            Assert.That(m_calls, Has.Count.EqualTo(1));
            Assert.That(m_calls[0].Request.ObjectId, Is.EqualTo(objectId));
            Assert.That(m_calls[0].Request.MethodId,
                Is.EqualTo(ExpandedNodeId.ToNodeId(methodId, m_context.NamespaceUris)));
            Assert.That(m_calls[0].Request.InputArguments.Count, Is.EqualTo(inputCount));
            Assert.That(m_calls[0].Token, Is.EqualTo(token));
            m_session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            m_session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            m_session.Verify(s => s.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static readonly ExpandedNodeId s_unregisteredEncodingId = new NodeId("OpaqueValidationEncoding", 0);
        private static readonly StatusCode[] s_nonBadStatuses =
        [
            StatusCodes.Good, StatusCodes.GoodResultsMayBeIncomplete, StatusCodes.Uncertain
        ];
        private static readonly StatusCode[] s_badStatuses =
        [
            StatusCodes.BadMethodInvalid, StatusCodes.BadUserAccessDenied
        ];
        private static readonly string[] s_resourceIds = ["healthy", "broken"];
        private static readonly string[] s_versionIds = ["v7", "v8"];
        private static readonly WoTOutcomeEnum[] s_resourceOutcomes = [WoTOutcomeEnum.Success, WoTOutcomeEnum.Rejected];
        private readonly List<(CallMethodRequest Request, CancellationToken Token)> m_calls = [];
        private Mock<ISession> m_session = null!;
        private ServiceMessageContext m_context = null!;
        private CallResponse m_response = null!;
        private WotRegistryClient m_registry = null!;
        private ThingDescriptionFileTypeClient m_document = null!;
        private NodeId m_registryId;
        private NodeId m_versionId;
    }
}

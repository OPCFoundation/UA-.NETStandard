/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// The result-bearing method path must not inherit the retrying proxy fallback.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Parallelizable]
    public sealed class ObjectTypeClientResultTests
    {
        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
            m_context.NamespaceUris.GetIndexOrAppend(kNamespace);
            m_session = new Mock<ISessionClient>(MockBehavior.Strict);
            m_session.SetupGet(s => s.MessageContext).Returns(m_context);
            m_client = new ResultProxy(m_session.Object, new NodeId("object", 1), telemetry);
        }

        [TestCaseSource(nameof(s_methodStatuses))]
        public async Task CallWithResultAsyncPreservesMethodAndArgumentResultsWithoutRetryAsync(StatusCode status)
        {
            using var cancellation = new CancellationTokenSource();
            var argumentDiagnostic = new DiagnosticInfo { AdditionalInfo = "argument detail" };
            var expected = new CallMethodResult
            {
                StatusCode = status,
                InputArgumentResults = [StatusCodes.Good, StatusCodes.BadOutOfRange],
                InputArgumentDiagnosticInfos = [new DiagnosticInfo(), argumentDiagnostic],
                OutputArguments = [Variant.From(42), Variant.From("assigned-version")]
            };
            CallMethodRequest? captured = null;
            CancellationToken receivedToken = default;
            m_session.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, token) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    captured = requests[0];
                    receivedToken = token;
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [expected]
                    });
                });

            CallMethodResult actual = await m_client.CallWithResultAsync(
                new ExpandedNodeId(91u, kNamespace), [Variant.From("input"), Variant.From(7u)],
                cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(actual.StatusCode, Is.EqualTo(status));
                Assert.That(actual.InputArgumentResults,
                    Is.EqualTo(new StatusCode[] { StatusCodes.Good, StatusCodes.BadOutOfRange }));
                Assert.That(actual.InputArgumentDiagnosticInfos[1].AdditionalInfo, Is.EqualTo("argument detail"));
                Assert.That(actual.OutputArguments,
                    Is.EqualTo(new[] { Variant.From(42), Variant.From("assigned-version") }));
                Assert.That(captured!.ObjectId, Is.EqualTo(new NodeId("object", 1)));
                Assert.That(captured.MethodId, Is.EqualTo(new NodeId(91u, 1)));
                Assert.That(captured.InputArguments, Is.EqualTo(new[] { Variant.From("input"), Variant.From(7u) }));
                Assert.That(receivedToken, Is.EqualTo(cancellation.Token));
            });
            VerifyOneCallAndNoFallback();
        }

        [TestCase("missing-result")]
        [TestCase("extra-result")]
        [TestCase("extra-diagnostic")]
        public async Task CallWithResultAsyncRejectsInvalidResponseCardinality(string shape)
        {
            var response = new CallResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
            };
            if (shape == "missing-result")
            {
                response.Results = [];
            }
            else if (shape == "extra-result")
            {
                response.Results = [new CallMethodResult(), new CallMethodResult()];
            }
            else
            {
                response.DiagnosticInfos = [new DiagnosticInfo(), new DiagnosticInfo()];
            }
            SetupResponse(response);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.CallWithResultAsync(
                    new ExpandedNodeId(91u, kNamespace), []).ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            VerifyOneCallAndNoFallback();
        }

        [Test]
        public async Task CallWithResultAsyncRejectsBadServiceStatusEvenWithGoodMethodResult()
        {
            SetupResponse(new CallResponse
            {
                ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionClosed },
                Results =
                    [new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = [Variant.From(123)] }]
            });

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.CallWithResultAsync(
                    new ExpandedNodeId(91u, kNamespace), []).ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            VerifyOneCallAndNoFallback();
        }

        [Test]
        public async Task CallWithResultAsyncPropagatesServiceExceptionWithoutRetry()
        {
            var primary = new ServiceResultException(StatusCodes.BadTimeout);
            m_session.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(primary);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.CallWithResultAsync(
                    new ExpandedNodeId(91u, kNamespace), []).ConfigureAwait(false)))!;

            Assert.That(exception, Is.SameAs(primary));
            VerifyOneCallAndNoFallback();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CallWithResultAsyncRejectsNonLocalMethodWithoutDispatch(bool remote)
        {
            ExpandedNodeId method = remote
                ? new ExpandedNodeId(new NodeId(91u, 1), null, 3)
                : new ExpandedNodeId(91u, "urn:missing:method");
            int namespaceCount = m_context.NamespaceUris.Count;

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.CallWithResultAsync(method, []).ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(m_context.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            m_session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task CallWithResultAsyncRejectsNullProxy()
        {
            ArgumentNullException exception = (await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await ObjectTypeClientExtensions.CallWithResultAsync(
                    null!, new ExpandedNodeId(91u, kNamespace), []).ConfigureAwait(false)))!;
            Assert.That(exception.ParamName, Is.EqualTo("client"));
        }

        private void SetupResponse(CallResponse response)
        {
            m_session.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(response));
        }

        private void VerifyOneCallAndNoFallback()
        {
            m_session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            m_session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private sealed class ResultProxy : ObjectTypeClient
        {
            public ResultProxy(ISessionClient session, NodeId id, ITelemetryContext telemetry)
                : base(session, id, telemetry)
            {
            }
        }

        private const string kNamespace = "urn:test:result-method";
        private static readonly StatusCode[] s_methodStatuses =
        [
            StatusCodes.Good, StatusCodes.GoodResultsMayBeIncomplete, StatusCodes.Uncertain,
            StatusCodes.BadMethodInvalid, StatusCodes.BadInvalidArgument
        ];
        private ServiceMessageContext m_context = null!;
        private Mock<ISessionClient> m_session = null!;
        private ResultProxy m_client = null!;
    }
}

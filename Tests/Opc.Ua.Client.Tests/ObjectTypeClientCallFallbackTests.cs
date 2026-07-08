/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Tests for the <see cref="ObjectTypeClient"/> method-call
    /// interoperability fallback (issue #3914). Calling a method with the
    /// type-declaration MethodId is spec-conformant (OPC UA Part 4
    /// §5.12.2.2) and is the zero-cost happy path. When a non-conformant
    /// server rejects it with <see cref="StatusCodes.BadMethodInvalid"/>,
    /// the instance MethodId is resolved via a HasComponent browse path,
    /// cached, and the call is retried.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Parallelizable]
    public sealed class ObjectTypeClientCallFallbackTests
    {
        private const string kMethodNamespaceUri = "http://test.org/UA/fallback/";
        private const string kMethodBrowseName = "DoIt";

        private Mock<ISessionClient> m_sessionMock = null!;
        private NodeId m_objectId;
        private NodeId m_typeMethodId;
        private NodeId m_instanceMethodId;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            ushort nsIndex = messageContext.NamespaceUris.GetIndexOrAppend(kMethodNamespaceUri);

            m_objectId = new NodeId(100u, nsIndex);
            m_typeMethodId = new NodeId(26u, nsIndex);
            m_instanceMethodId = new NodeId(32u, nsIndex);

            m_sessionMock = new Mock<ISessionClient>(MockBehavior.Strict);
            m_sessionMock.SetupGet(s => s.MessageContext).Returns(messageContext);
        }

        [Test]
        public async Task ConformantServerCallsOnceWithTypeMethodIdAndDoesNotResolveAsync()
        {
            SetupCall(_ => Good(new Variant(42)));
            TestObjectTypeClient client = CreateClient();

            ArrayOf<Variant> outputs = await client
                .InvokeAsync(m_typeMethodId, kMethodNamespaceUri, kMethodBrowseName, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(outputs.Count, Is.EqualTo(1));
            VerifyCallCount(Times.Once());
            VerifyTranslateCount(Times.Never());
        }

        [Test]
        public async Task NonConformantServerResolvesInstanceMethodIdAndRetriesAsync()
        {
            SetupCall(req => req.MethodId.Equals(m_typeMethodId) ? BadMethodInvalid() : Good(new Variant(7)));
            SetupTranslate(m_instanceMethodId);
            TestObjectTypeClient client = CreateClient();

            ArrayOf<Variant> outputs = await client
                .InvokeAsync(m_typeMethodId, kMethodNamespaceUri, kMethodBrowseName, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(outputs.Count, Is.EqualTo(1));
            VerifyTranslateCount(Times.Once());
            VerifyCallCount(Times.Exactly(2));
        }

        [Test]
        public async Task ResolvedInstanceMethodIdIsCachedAcrossCallsAsync()
        {
            SetupCall(req => req.MethodId.Equals(m_typeMethodId) ? BadMethodInvalid() : Good(new Variant(1)));
            SetupTranslate(m_instanceMethodId);
            TestObjectTypeClient client = CreateClient();

            _ = await client
                .InvokeAsync(m_typeMethodId, kMethodNamespaceUri, kMethodBrowseName, CancellationToken.None)
                .ConfigureAwait(false);
            _ = await client
                .InvokeAsync(m_typeMethodId, kMethodNamespaceUri, kMethodBrowseName, CancellationToken.None)
                .ConfigureAwait(false);

            // Exactly one resolution total. The first invocation tries the
            // type MethodId then the instance MethodId (2 calls); the second
            // invocation uses the cached instance MethodId directly (1 call).
            VerifyTranslateCount(Times.Once());
            VerifyCallCount(Times.Exactly(3));
        }

        [Test]
        public void UnresolvedInstanceMethodIdSurfacesOriginalBadMethodInvalid()
        {
            SetupCall(_ => BadMethodInvalid());
            SetupTranslate(NodeId.Null);
            TestObjectTypeClient client = CreateClient();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client
                    .InvokeAsync(m_typeMethodId, kMethodNamespaceUri, kMethodBrowseName, CancellationToken.None)
                    .ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadMethodInvalid));
            // Only the initial attempt is made; the missed resolution means no retry.
            VerifyCallCount(Times.Once());
            VerifyTranslateCount(Times.Once());
        }

        private TestObjectTypeClient CreateClient()
        {
            return new TestObjectTypeClient(
                m_sessionMock.Object,
                m_objectId,
                NUnitTelemetryContext.Create());
        }

        private void SetupCall(Func<CallMethodRequest, CallMethodResult> handler)
        {
            m_sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        CallMethodResult result = handler(requests[0]);
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = ArrayOf.Wrapped([result]),
                            DiagnosticInfos = default
                        });
                    });
        }

        private void SetupTranslate(NodeId resolved)
        {
            BrowsePathResult result = resolved.IsNull
                ? new BrowsePathResult { StatusCode = StatusCodes.Good, Targets = default }
                : new BrowsePathResult
                {
                    StatusCode = StatusCodes.Good,
                    Targets = ArrayOf.Wrapped(
                    [
                        new BrowsePathTarget
                        {
                            TargetId = new ExpandedNodeId(resolved),
                            RemainingPathIndex = uint.MaxValue
                        }
                    ])
                };

            m_sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped([result]),
                        DiagnosticInfos = default
                    }));
        }

        private void VerifyCallCount(Times times)
        {
            m_sessionMock.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), times);
        }

        private void VerifyTranslateCount(Times times)
        {
            m_sessionMock.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), times);
        }

        private static CallMethodResult Good(params Variant[] outputs)
        {
            return new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = ArrayOf.Wrapped(outputs)
            };
        }

        private static CallMethodResult BadMethodInvalid()
        {
            return new CallMethodResult
            {
                StatusCode = StatusCodes.BadMethodInvalid,
                OutputArguments = default
            };
        }

        /// <summary>
        /// Minimal concrete proxy exposing the protected fallback overload
        /// of <see cref="ObjectTypeClient.CallMethodAsync(NodeId, string, string, CancellationToken, Variant[])"/>.
        /// </summary>
        private sealed class TestObjectTypeClient : ObjectTypeClient
        {
            public TestObjectTypeClient(
                ISessionClient session,
                NodeId objectId,
                ITelemetryContext telemetry)
                : base(session, objectId, telemetry)
            {
            }

            public ValueTask<ArrayOf<Variant>> InvokeAsync(
                NodeId methodId,
                string methodNamespaceUri,
                string methodBrowseName,
                CancellationToken ct,
                params Variant[] args)
            {
                return CallMethodAsync(methodId, methodNamespaceUri, methodBrowseName, ct, args);
            }
        }
    }
}

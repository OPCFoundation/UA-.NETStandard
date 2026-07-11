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
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    [TestFixture]
    [Category("Client")]
    [Parallelizable]
    public sealed class ObjectTypeClientTests
    {
        [Test]
        public void ConstructorValidatesRequiredArguments()
        {
            Mock<ISessionClient> session = CreateSession();
            NodeId objectId = new(1000);

            Assert.That(
                () => _ = new TestObjectTypeClient(null, objectId, NUnitTelemetryContext.Create()),
                Throws.ArgumentNullException);
            Assert.That(
                () => _ = new TestObjectTypeClient(session.Object, objectId, null),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task CallMethodAsyncReturnsOutputArguments()
        {
            Mock<ISessionClient> session = CreateSession();
            NodeId methodId = new(2000);
            var expected = new ArrayOf<Variant>(new[] { new Variant(42) });
            session.Setup(s => s.CallAsync(
                    null,
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(CreateCallResponse(StatusCodes.Good, expected)));
            var client = new TestObjectTypeClient(session.Object, new NodeId(1000), NUnitTelemetryContext.Create());

            ArrayOf<Variant> output = await client.CallAsync(methodId, CancellationToken.None, new Variant("input"))
                .ConfigureAwait(false);

            Assert.That(output, Is.EqualTo(expected));
            session.Verify(s => s.CallAsync(
                null,
                It.Is<ArrayOf<CallMethodRequest>>(requests =>
                    requests.Count == 1 &&
                    requests[0].ObjectId == client.ObjectId &&
                    requests[0].MethodId == methodId &&
                    requests[0].InputArguments.Count == 1),
                It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public void CallMethodAsyncThrowsForBadResultStatus()
        {
            Mock<ISessionClient> session = CreateSession();
            session.Setup(s => s.CallAsync(
                    null,
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(CreateCallResponse(StatusCodes.BadUserAccessDenied, [])));
            var client = new TestObjectTypeClient(session.Object, new NodeId(1000), NUnitTelemetryContext.Create());

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.CallAsync(new NodeId(2000), CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task CallMethodAsyncFallsBackToResolvedInstanceMethodAndCachesIt()
        {
            Mock<ISessionClient> session = CreateSession();
            NodeId typeMethodId = new(2000);
            NodeId instanceMethodId = new(3000);
            var expected = new ArrayOf<Variant>(new[] { new Variant("ok") });
            var callMethodIds = new List<NodeId>();
            session.Setup(s => s.CallAsync(
                    null,
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        NodeId methodId = requests[0].MethodId;
                        callMethodIds.Add(methodId);
                        if (methodId == typeMethodId)
                        {
                            return new ValueTask<CallResponse>(
                                CreateCallResponse(StatusCodes.BadMethodInvalid, []));
                        }
                        if (methodId == instanceMethodId)
                        {
                            return new ValueTask<CallResponse>(
                                CreateCallResponse(StatusCodes.Good, expected));
                        }
                        throw new InvalidOperationException($"Unexpected method id {methodId}.");
                    });
            session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    CreateTranslateResponse(instanceMethodId)));
            var client = new TestObjectTypeClient(session.Object, new NodeId(1000), NUnitTelemetryContext.Create());

            ArrayOf<Variant> first = await client
                .CallWithFallbackAsync(typeMethodId, "urn:test", "Method", CancellationToken.None)
                .ConfigureAwait(false);
            ArrayOf<Variant> second = await client
                .CallWithFallbackAsync(typeMethodId, "urn:test", "Method", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(first, Is.EqualTo(expected));
            Assert.That(second, Is.EqualTo(expected));
            Assert.That(
                callMethodIds,
                Is.EqualTo(new[] { typeMethodId, instanceMethodId, instanceMethodId }));
            session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                null,
                It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public async Task ResolveChildNodeIdAsyncReturnsNullWhenNamespaceOrTargetMissing()
        {
            Mock<ISessionClient> session = CreateSession();
            session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = new ArrayOf<BrowsePathResult>(new[]
                        {
                            new BrowsePathResult { StatusCode = StatusCodes.BadNotFound }
                        }),
                        DiagnosticInfos = []
                    }));
            var client = new TestObjectTypeClient(session.Object, new NodeId(1000), NUnitTelemetryContext.Create());

            NodeId unknownNamespace = await client.ResolveAsync("urn:missing", "Child", CancellationToken.None)
                .ConfigureAwait(false);
            NodeId missingTarget = await client.ResolveAsync("urn:test", "Child", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(unknownNamespace.IsNull, Is.True);
            Assert.That(missingTarget.IsNull, Is.True);
        }

        private static Mock<ISessionClient> CreateSession()
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append("urn:test");
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            session.SetupGet(s => s.MessageContext).Returns(context);
            return session;
        }

        private static CallResponse CreateCallResponse(StatusCode statusCode, ArrayOf<Variant> outputArguments)
        {
            return new CallResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = new ArrayOf<CallMethodResult>(new[]
                {
                    new CallMethodResult
                    {
                        StatusCode = statusCode,
                        OutputArguments = outputArguments
                    }
                }),
                DiagnosticInfos = []
            };
        }

        private static TranslateBrowsePathsToNodeIdsResponse CreateTranslateResponse(NodeId targetId)
        {
            return new TranslateBrowsePathsToNodeIdsResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = new ArrayOf<BrowsePathResult>(new[]
                {
                    new BrowsePathResult
                    {
                        StatusCode = StatusCodes.Good,
                        Targets = new ArrayOf<BrowsePathTarget>(new[]
                        {
                            new BrowsePathTarget { TargetId = new ExpandedNodeId(targetId) }
                        })
                    }
                }),
                DiagnosticInfos = []
            };
        }

        private sealed class TestObjectTypeClient : ObjectTypeClient
        {
            public TestObjectTypeClient(
                ISessionClient session,
                NodeId objectId,
                ITelemetryContext telemetry)
                : base(session, objectId, telemetry)
            {
            }

            public ValueTask<ArrayOf<Variant>> CallAsync(
                NodeId methodId,
                CancellationToken ct,
                params Variant[] args)
            {
                return CallMethodAsync(methodId, ct, args);
            }

            public ValueTask<ArrayOf<Variant>> CallWithFallbackAsync(
                NodeId methodId,
                string methodNamespaceUri,
                string methodBrowseName,
                CancellationToken ct)
            {
                return CallMethodAsync(methodId, methodNamespaceUri, methodBrowseName, ct);
            }

            public ValueTask<NodeId> ResolveAsync(
                string namespaceUri,
                string browseName,
                CancellationToken ct)
            {
                return ResolveChildNodeIdAsync(namespaceUri, browseName, ct);
            }
        }
    }
}

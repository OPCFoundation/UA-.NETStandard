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
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Verifies caller-owned metadata cleanup and status handling at the session transport boundary.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class GmsResultClientTests
    {
        /// <summary>
        /// A result call owns only its returned handle and releases it even when the domain result is unsuccessful.
        /// </summary>
        [TestCase(0)]
        [TestCase(-17)]
        public async Task MetadataHandlesAreReleasedAfterSuccessAndDomainFailure(int domainError)
        {
            var calls = new List<CallMethodRequest>();
            Mock<ISession> session = CreateSession(calls, domainError: domainError);
            var client = new GmsResultClient(session.Object, new NodeId("gms", 1), NUnitTelemetryContext.Create());

            if (domainError == 0)
            {
                ResultDataType result = await client.GetLatestAsync();
                Assert.That(result.ResultMetaData!.ResultId, Is.EqualTo("inspection-17"));
            }
            else
            {
                GmsResultDomainException exception = (await Assert.ThrowsAsync<GmsResultDomainException>(
                    async () => await client.GetLatestAsync()))!;
                Assert.That(exception.Error, Is.EqualTo(-17));
                Assert.That(exception.Operation, Is.EqualTo("GetLatestResult"));
            }
            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(calls[1].InputArguments[0].TryGetValue(out uint released), Is.True);
            Assert.That(released, Is.EqualTo(42u));
        }

        /// <summary>
        /// Oversized metadata fails explicitly and the acquired handle is still released.
        /// </summary>
        [Test]
        public async Task OversizedMetadataIsNotTruncatedAndStillReleasesItsHandle()
        {
            var calls = new List<CallMethodRequest>();
            Mock<ISession> session = CreateSession(calls);
            var client = new GmsResultClient(session.Object, new NodeId("gms", 1), NUnitTelemetryContext.Create(),
                new GmsResultLimits { MaximumMetadataBytes = 8 });

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.GetLatestAsync()))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(calls, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// A bad method service status never triggers release of an unacquired handle.
        /// </summary>
        [Test]
        public async Task ServiceFailureIsNotADomainStatusOrAnEmptyGoodResult()
        {
            var calls = new List<CallMethodRequest>();
            Mock<ISession> session = CreateSession(calls, serviceStatus: StatusCodes.BadUserAccessDenied);
            var client = new GmsResultClient(session.Object, new NodeId("gms", 1), NUnitTelemetryContext.Create());

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.GetLatestAsync()))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(calls, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Cancellation after a successful service response still releases its acquired handle with an independent token.
        /// </summary>
        [Test]
        public async Task CancellationAfterTheReplyDoesNotCancelOwnedMetadataCleanup()
        {
            var calls = new List<CallMethodRequest>();
            var tokens = new List<CancellationToken>();
            using var cancellation = new CancellationTokenSource();
            Mock<ISession> session = CreateSession(calls, cancelAfterGet: cancellation, tokens: tokens);
            var client = new GmsResultClient(session.Object, new NodeId("gms", 1), NUnitTelemetryContext.Create());

            await Assert.ThrowsAsync<OperationCanceledException>(async () => await client.GetLatestAsync(cancellation.Token));

            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(tokens[0].IsCancellationRequested, Is.True);
            Assert.That(tokens[1].IsCancellationRequested, Is.False);
        }

        /// <summary>
        /// Both domain failures remain visible when the operation and release fail independently.
        /// </summary>
        [Test]
        public async Task MetadataReleaseFailureRetainsTheOriginalDomainFailure()
        {
            var calls = new List<CallMethodRequest>();
            Mock<ISession> session = CreateSession(calls, domainError: -17, releaseError: -23);
            var client = new GmsResultClient(session.Object, new NodeId("gms", 1), NUnitTelemetryContext.Create());
            AggregateException error = (await Assert.ThrowsAsync<AggregateException>(
                async () => await client.GetLatestAsync()))!;
            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(((GmsResultDomainException)error.InnerExceptions[0]).Error, Is.EqualTo(-17));
            Assert.That(((GmsResultDomainException)error.InnerExceptions[1]).Error, Is.EqualTo(-23));
        }

        /// <summary>
        /// A zero handle is not a resource the workflow may release.
        /// </summary>
        [Test]
        public async Task ZeroHandleDoesNotReleaseAnUnownedResource()
        {
            var calls = new List<CallMethodRequest>();
            Mock<ISession> session = CreateSession(calls, handle: 0);
            var client = new GmsResultClient(session.Object, new NodeId("gms", 1), NUnitTelemetryContext.Create());
            Assert.That((await client.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("inspection-17"));
            Assert.That(calls, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Invalid limits, identities and empty request batches fail without a service call.
        /// </summary>
        [Test]
        public async Task InvalidClientRequestsDoNotReachTheSession()
        {
            var calls = new List<CallMethodRequest>();
            Mock<ISession> session = CreateSession(calls);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var client = new GmsResultClient(session.Object, new NodeId("gms", 1), telemetry,
                new GmsResultLimits { MaximumResults = 2, MaximumCharacteristics = 1 });
            Assert.Throws<ArgumentNullException>(() => _ = new GmsResultClient(null!, new NodeId(1), telemetry));
            Assert.Throws<ArgumentNullException>(() => _ = new GmsResultClient(session.Object, new NodeId(1), null!));
            Assert.Throws<ArgumentException>(() => _ = new GmsResultClient(session.Object, NodeId.Null, telemetry));
            await Assert.ThrowsAsync<ArgumentException>(async () => await client.GetByIdAsync(string.Empty));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await client.QueryAsync(0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await client.QueryAsync(3));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await client.AcknowledgeAsync([]));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await client.AcknowledgeAsync(["a", "b", "c"]));
            Assert.Throws<ArgumentException>(() => client.CreateIntermediateResultFilter([QualifiedName.Null]));
            Assert.Throws<ArgumentOutOfRangeException>(() => client.CreateIntermediateResultFilter(
                [new QualifiedName("A"), new QualifiedName("B")]));
            Assert.That(calls, Is.Empty);
        }

        /// <summary>
        /// Standard exception constructors do not manufacture a received domain code.
        /// </summary>
        [Test]
        public void DomainExceptionPreservesOnlyActuallyReceivedCodes()
        {
            var empty = new GmsResultDomainException();
            var message = new GmsResultDomainException("message");
            var inner = new InvalidOperationException("source");
            var wrapped = new GmsResultDomainException("message", inner);
            Assert.Multiple(() =>
            {
                Assert.That(empty.Error, Is.Null);
                Assert.That(empty.Operation, Is.Empty);
                Assert.That(message.Message, Is.EqualTo("message"));
                Assert.That(wrapped.InnerException, Is.SameAs(inner));
            });
            Assert.Throws<ArgumentNullException>(() => _ = new GmsResultDomainException(null!, -1));
        }

        /// <summary>
        /// Provides a session boundary that resolves the real GMS-qualified child and inherited method declarations.
        /// </summary>
        internal static Mock<ISession> CreateSession(
            List<CallMethodRequest> calls,
            int domainError = 0,
            StatusCode serviceStatus = default,
            int releaseError = 0,
            uint handle = 42,
            CancellationTokenSource? cancelAfterGet = null,
            List<CancellationToken>? tokens = null)
        {
            ServiceMessageContext message = ModelTestContext.CreateMessageContext();
            var management = new NodeId("gms/results", 1);
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MessageContext).Returns(message);
            session.SetupGet(value => value.NamespaceUris).Returns(message.NamespaceUris);
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                null, It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new BrowsePathResult
                            {
                                Targets =
                                [
                                    new BrowsePathTarget { TargetId = management, RemainingPathIndex = uint.MaxValue }
                                ]
                            }
                        ],
                        DiagnosticInfos = []
                    }));
            NodeId release = NodeId.Create(7007, Machinery.Result.Namespaces.MachineryResult, message.NamespaceUris);
            session.Setup(value => value.CallAsync(
                null, It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<CallMethodRequest> requests, CancellationToken cancellationToken) =>
                {
                    calls.Add(requests[0]);
                    tokens?.Add(cancellationToken);
                    bool releasing = requests[0].MethodId == release;
                    if (!releasing)
                    {
                        cancelAfterGet?.Cancel();
                    }
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = releasing ? StatusCodes.Good : serviceStatus,
                                OutputArguments = releasing
                                    ? [new Variant(releaseError)]
                                    :
                                    [
                                        new Variant(handle),
                                        Variant.FromStructure(GmsResultStoreTests.Result("inspection-17").Data),
                                        new Variant(domainError)
                                    ]
                            }
                        ],
                        DiagnosticInfos = []
                    });
                });
            return session;
        }
    }
}

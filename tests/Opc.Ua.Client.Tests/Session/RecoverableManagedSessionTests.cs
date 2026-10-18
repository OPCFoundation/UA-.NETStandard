/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

namespace Opc.Ua.Client.Tests.SessionContexts
{
    /// <summary>
    /// Exercises prefer-recoverable execution through unchanged ISession and generated proxy APIs.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RecoverableManagedSessionTests
    {
        [Test]
        public async Task DefaultOffReadUsesOrdinaryServiceAndRejectsReceiptScope()
        {
            Assert.That(new ManagedSessionOptions().RecoverableExecution, Is.Null);
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(enabled: false)
                .ConfigureAwait(false);

            ReadResponse response = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);

            AssertRead(response, 42);
            Assert.That(peer.OrdinaryRequests, Has.Count.EqualTo(1));
            Assert.That(peer.DiscoveryRequests, Is.Empty);
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                peer.Session.BeginAcknowledgementScope())!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void CustomSessionCannotAcquireAnOptInAdapterThroughReceiptScope()
        {
            var custom = new Mock<ISession>(MockBehavior.Strict);
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                custom.Object.BeginAcknowledgementScope())!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            custom.VerifyNoOtherCalls();
        }

        [TestCase("options")]
        [TestCase("builder")]
        [TestCase("di")]
        public async Task ConstructionForwardsExactRecoverableOptionsAndAcceptance(string construction)
        {
            int accepted = 0;
            var options = new RecoverableSessionOptions
            {
                ExecutionAllowance = 14001.5,
                ResultRetentionTime = 7200.75,
                AcceptAsync = (context, _) =>
                {
                    Assert.That(context.TryGetResult(out ReadResponse? result), Is.True);
                    AssertRead(result!, 42);
                    Assert.That(context.Receipt!.IsAcknowledged, Is.False);
                    Assert.That(context.Failure, Is.Null);
                    Interlocked.Increment(ref accepted);
                    return default;
                }
            };
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                options, construction: construction).ConfigureAwait(false);

            ReadResponse response = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            AssertRead(response, 42);
            Assert.That(accepted, Is.EqualTo(1));
            SubmitServiceRequest submit = peer.Submissions.Single();
            Assert.That(submit.ExecutionAllowance, Is.EqualTo(14001.5));
            Assert.That(submit.ResultRetentionTime, Is.EqualTo(7200.75));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task BuilderWithoutArgumentsEnablesAdvertisedDefaults()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                construction: "builder").ConfigureAwait(false);
            ReadResponse response = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            AssertRead(response, 42);
            Assert.That(peer.Submissions.Single().ExecutionAllowance, Is.EqualTo(12345.5));
            Assert.That(peer.Submissions.Single().ResultRetentionTime, Is.EqualTo(6789.25));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task SupportedReadPreservesOriginalHeaderAndWholeBatch()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            var header = new RequestHeader
            {
                RequestHandle = 137,
                TimeoutHint = 17,
                ReturnDiagnostics = 3,
                AuditEntryId = "synthetic-audit"
            };
            ArrayOf<ReadValueId> nodes =
            [
                new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value },
                new ReadValueId { NodeId = new NodeId(101u), AttributeId = Attributes.Value, IndexRange = "1:2" }
            ];

            ReadResponse response = await peer.Session.ReadAsync(
                header, 2.5, TimestampsToReturn.Both, nodes, peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            AssertRead(response, 42, 43);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(137u));
            Assert.That(response.ResponseHeader.StringTable.ToArray(), Is.EqualTo(s_originalStrings));
            Assert.That(response.ResponseHeader.Timestamp,
                Is.EqualTo(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)));
            var original = (ReadRequest)RecoverableSessionPeer.Original(peer.Submissions.Single());
            Assert.That(original.NodesToRead.ToList().Select(node => node.NodeId),
                Is.EqualTo([new NodeId(100u), new NodeId(101u)]));
            Assert.That(original.NodesToRead[1].IndexRange, Is.EqualTo("1:2"));
            Assert.That(original.NodesToRead.ToList().Select(node => node.AttributeId),
                Is.All.EqualTo(Attributes.Value));
            Assert.That(original.MaxAge, Is.EqualTo(2.5));
            Assert.That(original.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Both));
            Assert.That(original.RequestHeader.TimeoutHint, Is.EqualTo(17u));
            Assert.That(original.RequestHeader.ReturnDiagnostics, Is.EqualTo(3u));
            Assert.That(original.RequestHeader.AuditEntryId, Is.EqualTo("synthetic-audit"));
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(3));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task SupportedWritePreservesWholeBatch()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            WriteResponse response = await peer.Session.WriteAsync(new RequestHeader { RequestHandle = 138 },
            [
                new WriteValue
                {
                    NodeId = new NodeId(100u), AttributeId = Attributes.Value, Value = new DataValue(Variant.From(7))
                },
                new WriteValue
                {
                    NodeId = new NodeId(101u), AttributeId = Attributes.Value, Value = new DataValue(Variant.From(9))
                }
            ], peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(response.Results, Is.EqualTo([StatusCodes.Good, StatusCodes.BadOutOfRange]));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(138u));
            var original = (WriteRequest)RecoverableSessionPeer.Original(peer.Submissions.Single());
            Assert.That(original.NodesToWrite.ToList().Select(node => node.NodeId),
                Is.EqualTo([new NodeId(100u), new NodeId(101u)]));
            Assert.That(original.NodesToWrite.ToList().Select(node => Scalar(node.Value)), Is.EqualTo(s_writeValues));
            Assert.That(original.NodesToWrite.ToList().Select(node => node.AttributeId),
                Is.All.EqualTo(Attributes.Value));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task SupportedCallPreservesWholeBatch()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.MutateResponse = (_, response) =>
            {
                if (response is SubmitServiceResponse submit && submit.Result.TryGetValue(out CallResponse? call))
                {
                    call.Results[1].StatusCode = StatusCodes.GoodClamped;
                    call.Results[1].OutputArguments = [Variant.From(ByteString.From("synthetic-second-csr"u8))];
                }
            };
            CallResponse response = await peer.Session.CallAsync(new RequestHeader { RequestHandle = 139 },
            [
                new CallMethodRequest { ObjectId = new NodeId(200u), MethodId = new NodeId(300u),
                    InputArguments = [Variant.From(7)] },
                new CallMethodRequest { ObjectId = new NodeId(201u), MethodId = new NodeId(301u),
                    InputArguments = [Variant.From(9)] }
            ], peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(response.Results, Has.Count.EqualTo(2));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(response.Results[0].OutputArguments[0].TryGetValue(out ByteString firstCsr), Is.True);
            Assert.That(firstCsr, Is.EqualTo(ByteString.From("synthetic-csr"u8)));
            Assert.That(response.Results[1].OutputArguments[0].TryGetValue(out ByteString secondCsr), Is.True);
            Assert.That(secondCsr, Is.EqualTo(ByteString.From("synthetic-second-csr"u8)));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(139u));
            var original = (CallRequest)RecoverableSessionPeer.Original(peer.Submissions.Single());
            Assert.That(original.MethodsToCall.ToList().Select(method => method.MethodId),
                Is.EqualTo([new NodeId(300u), new NodeId(301u)]));
            Assert.That(original.MethodsToCall.ToList().Select(method => method.ObjectId),
                Is.EqualTo([new NodeId(200u), new NodeId(201u)]));
            Assert.That(original.MethodsToCall.ToList().Select(method =>
                Scalar(new DataValue(method.InputArguments[0]))), Is.EqualTo(s_writeValues));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task TypedReadValidatesBeforeAcceptance()
        {
            int accepted = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (context, _) =>
                    {
                        Assert.That(context.TryGetResult(out int result), Is.True);
                        Assert.That(result, Is.EqualTo(42));
                        Assert.That(context.TryGetResult(out ReadResponse? _), Is.False);
                        Interlocked.Increment(ref accepted);
                        return default;
                    }
                }).ConfigureAwait(false);

            int value = await peer.Session.ReadValueAsync<int>(new NodeId(100u), peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(value, Is.EqualTo(42));
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task GeneratedProxyValidatesBeforeAcceptance()
        {
            int accepted = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (context, _) =>
                    {
                        Assert.That(context.TryGetResult(out ByteString csr), Is.True);
                        Assert.That(csr, Is.EqualTo(ByteString.From("synthetic-csr"u8)));
                        Assert.That(context.TryGetResult(out CallResponse? _), Is.False);
                        Interlocked.Increment(ref accepted);
                        return default;
                    }
                }).ConfigureAwait(false);

            ByteString result = await CreateSigningRequestAsync(peer).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(ByteString.From("synthetic-csr"u8)));
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ProvedUnsupportedRecoveryFallsBackOnceWithoutManufacturingReceipt(bool outerSubmitFault)
        {
            int accepted = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.CapabilityAvailable = outerSubmitFault;
            peer.SubmitFault = outerSubmitFault ? StatusCodes.BadServiceUnsupported : StatusCodes.Good;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                acceptAsync: (context, _) =>
                {
                    Assert.That(context.Receipt, Is.Null);
                    Assert.That(context.TryGetResult(out ReadResponse? value), Is.True);
                    AssertRead(value!, 42);
                    Interlocked.Increment(ref accepted);
                    return default;
                });

            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);

            AssertRead(result, 42);
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(scope.Receipt, Is.Null);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(peer.OrdinaryRequests, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests.Single(), Is.TypeOf<ReadRequest>());
            Assert.That(peer.Submissions, Has.Count.EqualTo(outerSubmitFault ? 1 : 0));
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(outerSubmitFault ? 3 : 1));
            Assert.That(peer.Controls, Is.Empty);
        }

        [TestCase("resource")]
        [TestCase("lifetime")]
        [TestCase("validation")]
        [TestCase("authorization")]
        [TestCase("security")]
        [TestCase("decode")]
        public async Task SubmitRejectionOtherThanUnsupportedNeverFallsBack(string rejection)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.SubmitFault = RejectionStatus(rejection);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(RejectionStatus(rejection)));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task ThrownUnsupportedWithoutDecodedSubmitFaultNeverFallsBack()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            int attempts = 0;
            peer.BeforeRespondAsync = (request, _) =>
            {
                if (request is SubmitServiceRequest)
                {
                    Interlocked.Increment(ref attempts);
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
                }
                return default;
            };

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
        }

        [TestCase("connection")]
        [TestCase("timeout")]
        [TestCase("request-timeout")]
        public async Task LostSubmitReplyCollectsByClientIdWithoutResubmission(string lostReply)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.LoseSubmitReply = true;
            peer.LostSubmitStatus = lostReply switch
            {
                "timeout" => StatusCodes.BadTimeout,
                "request-timeout" => StatusCodes.BadRequestTimeout,
                _ => StatusCodes.BadConnectionClosed
            };

            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            AssertRead(result, 42);
            SubmitServiceRequest original = peer.Submissions.Single();
            GetServiceResultRequest collection = peer.Collections.Single();
            Assert.That(collection.ClientOperationId, Is.EqualTo(original.ClientOperationId));
            Assert.That(collection.ServerToken.IsNull, Is.True);
            Assert.That(original.ExecutionAllowance, Is.EqualTo(12345.5));
            Assert.That(original.ResultRetentionTime, Is.EqualTo(6789.25));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnknownCollectionNeverResubmitsOrFallsBack(bool lostSubmit)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.FastResult = false;
            peer.LoseSubmitReply = lostSubmit;
            peer.UnknownCollection = true;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Has.Count.EqualTo(1));
            Assert.That(peer.Collections.Single().ClientOperationId != Uuid.Empty, Is.EqualTo(lostSubmit));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
        }

        [TestCase("unsupported")]
        [TestCase("resource")]
        [TestCase("authorization")]
        [TestCase("decode")]
        public async Task CollectionFailureNeverFallsBack(string failure)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.FastResult = false;
            peer.CollectionFault = RejectionStatus(failure);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(RejectionStatus(failure)));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
        }

        [TestCase(0d, true)]
        [TestCase(-1d, true)]
        [TestCase(double.NaN, true)]
        [TestCase(double.PositiveInfinity, true)]
        [TestCase(double.NegativeInfinity, true)]
        [TestCase(0d, false)]
        [TestCase(-1d, false)]
        [TestCase(double.NaN, false)]
        [TestCase(double.PositiveInfinity, false)]
        [TestCase(double.NegativeInfinity, false)]
        public async Task InvalidAdvertisedBudgetFailsBeforeOriginalDispatch(double budget, bool execution)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            SetDefaultBudget(peer, execution, new DataValue(Variant.From(budget)));

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(3));
            AssertNoApplicationDispatch(peer);
        }

        [TestCase("missing", true)]
        [TestCase("wrong-type", true)]
        [TestCase("null", true)]
        [TestCase("uncertain", true)]
        [TestCase("missing", false)]
        [TestCase("wrong-type", false)]
        [TestCase("null", false)]
        [TestCase("uncertain", false)]
        public async Task MissingOrMalformedRequiredDefaultNeverFallsBack(string invalid, bool execution)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            DataValue value = invalid switch
            {
                "missing" => new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                "wrong-type" => new DataValue(Variant.From(1000)),
                "uncertain" => new DataValue(Variant.From(1000d), StatusCodes.Uncertain),
                _ => new DataValue(Variant.Null)
            };
            SetDefaultBudget(peer, execution, value);
            StatusCode expected = invalid switch
            {
                "missing" => StatusCodes.BadNodeIdUnknown,
                "uncertain" => StatusCodes.Uncertain,
                _ => StatusCodes.BadDecodingError
            };

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(expected));
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(3));
            AssertNoApplicationDispatch(peer);
        }

        [TestCase(double.Epsilon, double.MaxValue)]
        [TestCase(double.MaxValue, double.Epsilon)]
        [TestCase(14001.5, 7200.75)]
        public async Task ExplicitPositiveFiniteBudgetsOverrideInvalidDefaultsExactly(double execution, double retention)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    ExecutionAllowance = execution,
                    ResultRetentionTime = retention
                }).ConfigureAwait(false);
            peer.ExecutionDefault = new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown);
            peer.RetentionDefault = new DataValue(Variant.From("not-a-duration"));

            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            AssertRead(result, 42);
            Assert.That(peer.Submissions.Single().ExecutionAllowance, Is.EqualTo(execution));
            Assert.That(peer.Submissions.Single().ResultRetentionTime, Is.EqualTo(retention));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task OneBudgetOverrideDoesNotRelaxOtherRequiredDefault(bool overrideExecution)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    ExecutionAllowance = overrideExecution ? 123d : null,
                    ResultRetentionTime = overrideExecution ? null : 456d
                }).ConfigureAwait(false);
            peer.ExecutionDefault = new DataValue(Variant.From(-1d));
            peer.RetentionDefault = new DataValue(Variant.From(-1d));

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            AssertNoApplicationDispatch(peer);
        }

        [TestCase(0d, true)]
        [TestCase(-1d, true)]
        [TestCase(double.NaN, true)]
        [TestCase(double.PositiveInfinity, true)]
        [TestCase(double.NegativeInfinity, true)]
        [TestCase(0d, false)]
        [TestCase(-1d, false)]
        [TestCase(double.NaN, false)]
        [TestCase(double.PositiveInfinity, false)]
        [TestCase(double.NegativeInfinity, false)]
        public async Task InvalidOverrideIsRejectedBeforeConnection(double budget, bool execution)
        {
            await using var channels = new SessionChannelHarness();
            var options = new ManagedSessionOptions
            {
                Endpoint = SessionChannelHarness.CreateEndpoint(),
                RecoverableExecution = new RecoverableSessionOptions
                {
                    ExecutionAllowance = execution ? budget : null,
                    ResultRetentionTime = execution ? null : budget
                }
            };

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Client.ManagedSession.CreateAsync(options, channels.Configuration,
                    new DefaultSessionFactory(channels.Telemetry), telemetry: channels.Telemetry,
                    channelManager: channels.Manager).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(channels.CreatedChannels, Is.Empty);
        }

        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(uint.MaxValue)]
        public async Task AdvertisedBudgetsAreIndependentOfOriginalTimeoutHint(uint timeoutHint)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            ReadResponse result = await peer.Session.ReadAsync(new RequestHeader { TimeoutHint = timeoutHint },
                0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value }],
                peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            AssertRead(result, 42);
            Assert.That(peer.Submissions.Single().ExecutionAllowance, Is.EqualTo(12345.5));
            Assert.That(peer.Submissions.Single().ResultRetentionTime, Is.EqualTo(6789.25));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task TypedReadMismatchNeverAcceptsAcknowledgesOrFallsBack()
        {
            int accepted = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (_, _) =>
                    {
                        Interlocked.Increment(ref accepted);
                        return default;
                    }
                }).ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peer.Session.ReadValueAsync<string>(new NodeId(100u), peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(accepted, Is.Zero);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MalformedProxyOutputNeverAcceptsAcknowledgesOrFallsBack(bool missingOutput)
        {
            int accepted = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (_, _) =>
                    {
                        Interlocked.Increment(ref accepted);
                        return default;
                    }
                }).ConfigureAwait(false);
            peer.InvalidMethodOutput = !missingOutput;
            if (missingOutput)
            {
                peer.MutateResponse = (_, response) =>
                {
                    if (response is SubmitServiceResponse submit && submit.Result.TryGetValue(out CallResponse? call))
                    {
                        call.Results[0].OutputArguments = [];
                    }
                };
            }

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateSigningRequestAsync(peer).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(accepted, Is.Zero);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(3));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase("wrong-handle")]
        [TestCase("wrong-response-type")]
        [TestCase("wrong-client")]
        [TestCase("revised-budget")]
        [TestCase("missing-result")]
        public async Task MalformedRetainedOutcomeNeverAcceptsAcknowledgesOrFallsBack(string malformed)
        {
            int accepted = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (_, _) =>
                    {
                        Interlocked.Increment(ref accepted);
                        return default;
                    }
                }).ConfigureAwait(false);
            peer.MutateResponse = (_, response) =>
            {
                if (response is not SubmitServiceResponse submit)
                {
                    return;
                }
                Assert.That(submit.Result.TryGetValue(out ReadResponse? read), Is.True);
                switch (malformed)
                {
                    case "wrong-handle":
                        read!.ResponseHeader.RequestHandle++;
                        break;
                    case "wrong-response-type":
                        submit.Result = new ExtensionObject(new WriteResponse { ResponseHeader = read!.ResponseHeader });
                        break;
                    case "wrong-client":
                        submit.Operation.ClientOperationId = Uuid.NewUuid();
                        break;
                    case "revised-budget":
                        submit.Operation.ExecutionAllowance++;
                        break;
                    default:
                        read!.Results = [];
                        break;
                }
            };

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(malformed == "missing-result"
                ? StatusCodes.BadUnexpectedError : StatusCodes.BadDecodingError));
            Assert.That(accepted, Is.Zero);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase("unsupported")]
        [TestCase("authorization")]
        public async Task OriginalServiceFailureIsAcceptedAsFailureWithoutFallback(string failure)
        {
            int accepted = 0;
            StatusCode expected = RejectionStatus(failure);
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (context, _) =>
                    {
                        Assert.That(context.Failure!.StatusCode, Is.EqualTo(expected));
                        Assert.That(context.TryGetResult(out ReadResponse? _), Is.False);
                        Assert.That(context.Receipt!.IsFullyRecoverable, Is.True);
                        Assert.That(context.Receipt.IsAcknowledged, Is.False);
                        Interlocked.Increment(ref accepted);
                        return default;
                    }
                }).ConfigureAwait(false);
            peer.OriginalFailure = expected;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(expected));
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task OriginalMethodFailureIsAcceptedWithoutMethodIdFallback()
        {
            int accepted = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (context, _) =>
                    {
                        Assert.That(context.Failure!.StatusCode, Is.EqualTo(StatusCodes.BadMethodInvalid));
                        Assert.That(context.TryGetResult(out ByteString _), Is.False);
                        Interlocked.Increment(ref accepted);
                        return default;
                    }
                }).ConfigureAwait(false);
            peer.MethodStatus = StatusCodes.BadMethodInvalid;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateSigningRequestAsync(peer).ConfigureAwait(false))!;
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadMethodInvalid));
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(3), "No method-ID browse fallback is permitted.");
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitReceiptWaitsForApplicationAcknowledgement(bool useReceipt)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true);

            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);

            AssertRead(result, 42);
            RecoverableReceipt receipt = scope.Receipt!;
            Assert.That(receipt.SessionId, Is.EqualTo(peer.Session.SessionId));
            Assert.That(receipt.IsFullyRecoverable, Is.True);
            Assert.That(receipt.IsAcknowledged, Is.False);
            Assert.That(peer.Controls, Is.Empty, "Returning the ordinary result must not acknowledge an explicit scope.");
            if (useReceipt)
            {
                await receipt.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            }
            else
            {
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            }

            Assert.That(receipt.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.Controls.Single().Action, Is.EqualTo(ServiceOperationControlAction.Acknowledge));
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(peer.Controls, Has.Count.EqualTo(1), "A confirmed receipt must not send another ACK.");
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            AssertRead(result, 42);
        }

        [Test]
        public async Task EmptyScopeCannotManufactureAcknowledgementAndDisposalIsLocal()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();

            Assert.That(scope.Receipt, Is.Null);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            await scope.DisposeAsync().ConfigureAwait(false);
            await scope.DisposeAsync().ConfigureAwait(false);
            AssertNoApplicationDispatch(peer);
            Assert.That(peer.DiscoveryRequests, Is.Empty);
        }

        [Test]
        public async Task DisposingAcceptedScopeNeverAcknowledgesAndRestoresOrdinaryCallerFlow()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            ReadResponse retained = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            RecoverableReceipt receipt = scope.Receipt!;

            await scope.DisposeAsync().ConfigureAwait(false);

            AssertRead(retained, 42);
            Assert.That(receipt.IsAcknowledged, Is.False);
            Assert.That(peer.Controls, Is.Empty);
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await receipt.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false));
            ReadResponse next = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
            AssertRead(next, 42);
            Assert.That(peer.Submissions, Has.Count.EqualTo(2));
            Assert.That(peer.Controls, Has.Count.EqualTo(1), "Only the later unscoped operation may be acknowledged.");
            Assert.That(receipt.IsAcknowledged, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RequiredReceiptRejectsUnsupportedBeforeOrdinaryDispatch(bool outerSubmitFault)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.CapabilityAvailable = outerSubmitFault;
            peer.SubmitFault = outerSubmitFault ? StatusCodes.BadServiceUnsupported : StatusCodes.Good;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            Assert.That(scope.Receipt, Is.Null);
            Assert.That(peer.Submissions, Has.Count.EqualTo(outerSubmitFault ? 1 : 0));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.Collections, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BrowseStaysOrdinaryAndRequiredReceiptRejectsBeforeDispatch(bool requireReceipt)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            BrowseResponse ordinary = await BrowseAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            Assert.That(ordinary.Results[0].References[0].NodeId, Is.EqualTo(new ExpandedNodeId(101u)));
            Assert.That(ordinary.Results[0].References[0].BrowseName.Name, Is.EqualTo("SyntheticChild"));
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(requireReceipt);
            if (requireReceipt)
            {
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await BrowseAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            }
            else
            {
                BrowseResponse result = await BrowseAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
                Assert.That(result.Results[0].References[0].NodeId, Is.EqualTo(new ExpandedNodeId(101u)));
            }

            Assert.That(scope.Receipt, Is.Null);
            ServiceResultException noReceipt = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false))!;
            Assert.That(noReceipt.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(peer.OrdinaryRequests, Has.Count.EqualTo(requireReceipt ? 1 : 2));
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.DiscoveryRequests, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OldUninstrumentedProxyOnlyRejectsOptedInSessions(bool recoverable)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(enabled: recoverable)
                .ConfigureAwait(false);
            var proxy = new UninstrumentedProxy(peer.Session, peer.Channels.Telemetry);
            if (recoverable)
            {
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await proxy.InvokeAsync(peer.Timeout).ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            else
            {
                ArrayOf<Variant> output = await proxy.InvokeAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(output[0].TryGetValue(out ByteString csr), Is.True);
                Assert.That(csr, Is.EqualTo(ByteString.From("synthetic-csr"u8)));
                Assert.That(peer.OrdinaryRequests, Has.Count.EqualTo(1));
            }
            Assert.That(peer.DiscoveryRequests, Is.Empty);
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
        }

        [TestCase("read")]
        [TestCase("typed-read")]
        [TestCase("proxy")]
        [TestCase("void")]
        public async Task AcceptanceWaitsForActualCallerCompletionBeforeAcknowledgement(string api)
        {
            TaskCompletionSource<bool> entered = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> release = RecoverableSessionPeer.NewSignal();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = async (context, ct) =>
                    {
                        switch (api)
                        {
                            case "read":
                                Assert.That(context.TryGetResult(out ReadResponse? read), Is.True);
                                AssertRead(read!, 42);
                                break;
                            case "typed-read":
                                Assert.That(context.TryGetResult(out int value), Is.True);
                                Assert.That(value, Is.EqualTo(42));
                                break;
                            case "proxy":
                                Assert.That(context.TryGetResult(out ByteString csr), Is.True);
                                Assert.That(csr, Is.EqualTo(ByteString.From("synthetic-csr"u8)));
                                break;
                            default:
                                Assert.That(context.TryGetResult(out StatusCode status), Is.True);
                                Assert.That(status, Is.EqualTo(StatusCodes.Good));
                                break;
                        }
                        Assert.That(context.Receipt!.IsAcknowledged, Is.False);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            Task caller = api switch
            {
                "read" => ReadAsync(peer.Session, peer.Timeout).AsTask(),
                "typed-read" => peer.Session.ReadValueAsync<int>(new NodeId(100u), peer.Timeout),
                "proxy" => CreateSigningRequestAsync(peer).AsTask(),
                _ => ClientOperationContext.ExecuteAsync(peer.Session, async token =>
                {
                    ReadResponse response = await ReadAsync(peer.Session, token).ConfigureAwait(false);
                    AssertRead(response, 42);
                }, peer.Timeout).AsTask()
            };
            peer.CallerHasCompleted = () => caller.IsCompletedSuccessfully;
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(caller.IsCompleted, Is.False);
                Assert.That(peer.Controls, Is.Empty);
                release.TrySetResult(true);
                await caller.WaitAsync(peer.Timeout).ConfigureAwait(false);
                await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(peer.AckObservedCallerCompletion, Is.True,
                    "The actual public API task, not an internal operation task, must be completed before ACK.");
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(peer.Controls, Has.Count.EqualTo(1));
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task ScopeAcceptanceOverridesSessionCallback()
        {
            int sessionAcceptances = 0;
            int scopeAcceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (_, _) =>
                    {
                        Interlocked.Increment(ref sessionAcceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                acceptAsync: (context, _) =>
                {
                    Assert.That(context.TryGetResult(out ReadResponse? read), Is.True);
                    AssertRead(read!, 42);
                    Interlocked.Increment(ref scopeAcceptances);
                    return default;
                });

            ReadResponse response = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);

            AssertRead(response, 42);
            Assert.That(scopeAcceptances, Is.EqualTo(1));
            Assert.That(sessionAcceptances, Is.Zero);
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CallbackFailureNeverAcknowledgesEvenLegitimateOriginalFailure(bool originalFailure)
        {
            int acceptances = 0;
            var rejected = new InvalidOperationException("synthetic application rejection");
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.OriginalFailure = originalFailure ? StatusCodes.BadServiceUnsupported : StatusCodes.Good;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                acceptAsync: (context, _) =>
                {
                    Assert.That(context.Failure?.StatusCode,
                        Is.EqualTo(originalFailure ? StatusCodes.BadServiceUnsupported : (StatusCode?)null));
                    Interlocked.Increment(ref acceptances);
                    throw rejected;
                });

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error, Is.SameAs(rejected));
            Assert.That(acceptances, Is.EqualTo(1));
            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false));
            await scope.DisposeAsync().ConfigureAwait(false);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CallbackCancellationNeverAcknowledgesEvenLegitimateOriginalFailure(bool originalFailure)
        {
            TaskCompletionSource<bool> entered = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> neverReleased = RecoverableSessionPeer.NewSignal();
            using var cancellation = new CancellationTokenSource();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.OriginalFailure = originalFailure ? StatusCodes.BadServiceUnsupported : StatusCodes.Good;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                acceptAsync: async (context, ct) =>
                {
                    Assert.That(context.Failure?.StatusCode,
                        Is.EqualTo(originalFailure ? StatusCodes.BadServiceUnsupported : (StatusCode?)null));
                    entered.TrySetResult(true);
                    await neverReleased.Task.WaitAsync(ct).ConfigureAwait(false);
                });
            Task<ReadResponse> call = ReadAsync(peer.Session, cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(call.IsCompleted, Is.False);
                Assert.That(peer.Controls, Is.Empty);
                await cancellation.CancelAsync().ConfigureAwait(false);
                Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await call.WaitAsync(peer.Timeout).ConfigureAwait(false));
                Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(peer.Controls, Is.Empty);
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                neverReleased.TrySetResult(true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CallbackCannotAcknowledgePrematurelyOrDeadlock(bool throughScope)
        {
            int rejected = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            RecoverableAcknowledgementScope? scope = null;
            scope = peer.Session.BeginAcknowledgementScope(acceptAsync: async (context, ct) =>
            {
                Assert.That(context.TryGetResult(out ReadResponse? result), Is.True);
                AssertRead(result!, 42);
                try
                {
                    Task acknowledgement = throughScope
                        ? scope!.AcknowledgeAsync(ct).AsTask() : context.Receipt!.AcknowledgeAsync(ct).AsTask();
                    await acknowledgement.WaitAsync(peer.Timeout).ConfigureAwait(false);
                    Assert.Fail("Premature acknowledgement must throw before any control dispatch.");
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref rejected);
                }
            });
            await using (scope.ConfigureAwait(false))
            {
                ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
                AssertRead(result, 42);
                Assert.That(rejected, Is.EqualTo(1));
                Assert.That(peer.Controls, Is.Empty);
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(scope.Receipt!.IsAcknowledged, Is.True);
                Assert.That(peer.Controls, Has.Count.EqualTo(1));
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            }
        }

        [TestCase("read")]
        [TestCase("browse")]
        [TestCase("scope")]
        [TestCase("proxy")]
        public async Task AcceptanceCallbackRejectsSameSessionReentryBeforeDispatch(string reentry)
        {
            int rejected = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                acceptAsync: async (context, ct) =>
                {
                    Assert.That(context.Failure, Is.Null);
                    try
                    {
                        switch (reentry)
                        {
                            case "read":
                                _ = await ReadAsync(peer.Session, ct).ConfigureAwait(false);
                                break;
                            case "browse":
                                _ = await BrowseAsync(peer.Session, ct).ConfigureAwait(false);
                                break;
                            case "proxy":
                                _ = await CreateSigningRequestAsync(peer).ConfigureAwait(false);
                                break;
                            default:
                                await using (peer.Session.BeginAcknowledgementScope().ConfigureAwait(false))
                                {
                                    Assert.Fail("Acceptance must not open a same-session nested scope.");
                                }
                                break;
                        }
                        Assert.Fail("Acceptance must not start another operation on its own session.");
                    }
                    catch (InvalidOperationException)
                    {
                        Interlocked.Increment(ref rejected);
                    }
                });

            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);

            AssertRead(result, 42);
            Assert.That(rejected, Is.EqualTo(1));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(3));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.True);
        }

        [Test]
        public async Task DisposingScopeWhileCallbackIsPendingNeverAcknowledges()
        {
            TaskCompletionSource<bool> entered = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> release = RecoverableSessionPeer.NewSignal();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                acceptAsync: async (context, _) =>
                {
                    Assert.That(context.TryGetResult(out ReadResponse? result), Is.True);
                    AssertRead(result!, 42);
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                });
            Task<ReadResponse> call = ReadAsync(peer.Session, peer.Timeout).AsTask();
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(call.IsCompleted, Is.False);
                await scope.DisposeAsync().AsTask().WaitAsync(peer.Timeout).ConfigureAwait(false);
                release.TrySetResult(true);
                Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await call.WaitAsync(peer.Timeout).ConfigureAwait(false));
                Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
                Assert.That(peer.Controls, Is.Empty);
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task ConcurrentScopesKeepIndependentReceiptsAndAcknowledgements()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            var firstReady = new TaskCompletionSource<RecoverableReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondReady = new TaskCompletionSource<RecoverableReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> allowFirstAck = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> allowSecondAck = RecoverableSessionPeer.NewSignal();
            Task first = InvokeAsync(new NodeId(100u), 42, firstReady, allowFirstAck);
            Task second = InvokeAsync(new NodeId(101u), 43, secondReady, allowSecondAck);
            try
            {
                RecoverableReceipt firstReceipt = await firstReady.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                RecoverableReceipt secondReceipt = await secondReady.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(firstReceipt, Is.Not.SameAs(secondReceipt));
                Assert.That(firstReceipt.SessionId, Is.EqualTo(peer.Session.SessionId));
                Assert.That(secondReceipt.SessionId, Is.EqualTo(peer.Session.SessionId));
                Assert.That(peer.Submissions, Has.Count.EqualTo(2));
                Assert.That(peer.Submissions.Select(submit => submit.ClientOperationId).Distinct().Count(), Is.EqualTo(2));
                Assert.That(peer.Controls, Is.Empty);

                allowSecondAck.TrySetResult(true);
                await second.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(secondReceipt.IsAcknowledged, Is.True);
                Assert.That(firstReceipt.IsAcknowledged, Is.False);
                Assert.That(peer.Controls, Has.Count.EqualTo(1));

                allowFirstAck.TrySetResult(true);
                await first.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(firstReceipt.IsAcknowledged, Is.True);
                Assert.That(peer.Controls, Has.Count.EqualTo(2));
                Assert.That(peer.Controls.Select(control => control.ServerToken).Distinct().Count(), Is.EqualTo(2));
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            finally
            {
                allowFirstAck.TrySetResult(true);
                allowSecondAck.TrySetResult(true);
                await Task.WhenAll(first, second).WaitAsync(peer.Timeout).ConfigureAwait(false);
            }

            async Task InvokeAsync(NodeId node, int expected, TaskCompletionSource<RecoverableReceipt> ready,
                TaskCompletionSource<bool> acknowledge)
            {
                await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                    requireRecoverableReceipt: true);
                int result = await peer.Session.ReadValueAsync<int>(node, peer.Timeout).ConfigureAwait(false);
                Assert.That(result, Is.EqualTo(expected));
                ready.TrySetResult(scope.Receipt!);
                await acknowledge.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ScopeRejectsConcurrentAndSequentialReuseBeforeSecondDispatch()
        {
            TaskCompletionSource<bool> entered = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> release = RecoverableSessionPeer.NewSignal();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                acceptAsync: async (_, ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                });
            Task<ReadResponse> first = ReadAsync(peer.Session, peer.Timeout).AsTask();
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false));
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(peer.Controls, Is.Empty);
                release.TrySetResult(true);
                AssertRead(await first.WaitAsync(peer.Timeout).ConfigureAwait(false), 42);
                RecoverableReceipt receipt = scope.Receipt!;

                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false));
                Assert.That(scope.Receipt, Is.SameAs(receipt));
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(3));
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(peer.Controls, Has.Count.EqualTo(1));
                Assert.That(receipt.IsAcknowledged, Is.True);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task NestedScopesRequireLifoDisposalAndRestoreOuterScope()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope outer = peer.Session.BeginAcknowledgementScope();
            await using RecoverableAcknowledgementScope inner = peer.Session.BeginAcknowledgementScope();
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await outer.DisposeAsync().ConfigureAwait(false));
            int innerResult = await peer.Session.ReadValueAsync<int>(new NodeId(101u), peer.Timeout).ConfigureAwait(false);
            Assert.That(innerResult, Is.EqualTo(43));
            RecoverableReceipt innerReceipt = inner.Receipt!;
            Assert.That(outer.Receipt, Is.Null);
            await inner.DisposeAsync().ConfigureAwait(false);

            int outerResult = await peer.Session.ReadValueAsync<int>(new NodeId(100u), peer.Timeout).ConfigureAwait(false);

            Assert.That(outerResult, Is.EqualTo(42));
            Assert.That(outer.Receipt, Is.Not.SameAs(innerReceipt));
            Assert.That(innerReceipt.IsAcknowledged, Is.False);
            Assert.That(peer.Controls, Is.Empty);
            await outer.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(outer.Receipt!.IsAcknowledged, Is.True);
            Assert.That(innerReceipt.IsAcknowledged, Is.False);
            Assert.That(peer.Submissions, Has.Count.EqualTo(2));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task NestedScopeInsideTypedOperationDoesNotJoinOuterReceipt()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope outer = peer.Session.BeginAcknowledgementScope();
            RecoverableReceipt? innerReceipt = null;

            int sum = await ClientOperationContext.ExecuteAsync(peer.Session, async token =>
            {
                int first = await peer.Session.ReadValueAsync<int>(new NodeId(100u), token).ConfigureAwait(false);
                await using RecoverableAcknowledgementScope inner = peer.Session.BeginAcknowledgementScope();
                int second = await peer.Session.ReadValueAsync<int>(new NodeId(101u), token).ConfigureAwait(false);
                innerReceipt = inner.Receipt;
                Assert.That(innerReceipt, Is.Not.SameAs(outer.Receipt));
                await inner.AcknowledgeAsync(token).ConfigureAwait(false);
                Assert.That(innerReceipt!.IsAcknowledged, Is.True);
                Assert.That(outer.Receipt!.IsAcknowledged, Is.False);
                return first + second;
            }, peer.Timeout).ConfigureAwait(false);

            Assert.That(sum, Is.EqualTo(85));
            Assert.That(peer.Submissions, Has.Count.EqualTo(2));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            await outer.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(outer.Receipt!.IsAcknowledged, Is.True);
            Assert.That(innerReceipt!.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task ExplicitLostAcknowledgementRetriesOnlyAcknowledgement()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.LoseAckReply = true;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            NodeId originalSession = peer.Session.SessionId;

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            AssertRead(result, 42);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt.IsAcknowledged, Is.True);
            Assert.That(peer.Session.SessionId, Is.EqualTo(originalSession));
            Assert.That(peer.Controls, Has.Count.EqualTo(2));
            Assert.That(peer.Controls.Select(control => control.ServerToken).Distinct().Count(), Is.EqualTo(1));
            Assert.That(peer.Controls.Select(control => control.Action), Is.All.EqualTo(ServiceOperationControlAction.Acknowledge));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            AssertRead(result, 42);
        }

        [TestCase("unsupported")]
        [TestCase("resource")]
        [TestCase("security")]
        public async Task RejectedAcknowledgementDoesNotReplaceResultOrPermitFallback(string failure)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.ControlFault = RejectionStatus(failure);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(RejectionStatus(failure)));
            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            AssertRead(result, 42);
            peer.ControlFault = StatusCodes.Good;
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(2));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task AutomaticLostAcknowledgementCannotReplaceCompletedCallerResult()
        {
            TaskCompletionSource<bool> entered = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> release = RecoverableSessionPeer.NewSignal();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = async (context, ct) =>
                    {
                        Assert.That(context.TryGetResult(out ReadResponse? read), Is.True);
                        AssertRead(read!, 42);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            peer.LoseAckReply = true;
            Task<ReadResponse> call = ReadAsync(peer.Session, peer.Timeout).AsTask();
            peer.CallerHasCompleted = () => call.IsCompletedSuccessfully;
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                release.TrySetResult(true);
                ReadResponse accepted = await call.WaitAsync(peer.Timeout).ConfigureAwait(false);
                await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(peer.AckObservedCallerCompletion, Is.True);
                AssertRead(accepted, 42);
                AssertRead(await call.ConfigureAwait(false), 42);
                Assert.That(call.IsCompletedSuccessfully, Is.True);
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(peer.Controls, Has.Count.EqualTo(1));
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task LostCollectionReplyRetriesCollectionWithoutResubmissionOrBudgetRenewal()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.FastResult = false;
            peer.LoseCollectionReply = true;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();

            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);

            AssertRead(result, 42);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Submissions.Single().ExecutionAllowance, Is.EqualTo(12345.5));
            Assert.That(peer.Submissions.Single().ResultRetentionTime, Is.EqualTo(6789.25));
            Assert.That(peer.Collections, Has.Count.EqualTo(2));
            Assert.That(peer.Collections.Select(get => get.ClientOperationId), Is.All.EqualTo(Uuid.Empty));
            Assert.That(peer.Collections.Select(get => get.ServerToken).Distinct().Count(), Is.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task PrecancelledCallNeverDispatchesOrConsumesItsScope()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await ReadAsync(peer.Session, cancellation.Token).ConfigureAwait(false));

            Assert.That(scope.Receipt, Is.Null);
            Assert.That(peer.DiscoveryRequests, Is.Empty);
            AssertNoApplicationDispatch(peer);
            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            AssertRead(result, 42);
            Assert.That(scope.Receipt!.IsFullyRecoverable, Is.True);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
        }

        [Test]
        public async Task CancellationDuringCollectionNeverCancelsAbandonsOrResubmitsRemoteWork()
        {
            using var cancellation = new CancellationTokenSource();
            TaskCompletionSource<bool> entered = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> release = RecoverableSessionPeer.NewSignal();
            int collectionAttempts = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.FastResult = false;
            peer.BeforeRespondAsync = async (request, ct) =>
            {
                if (request is GetServiceResultRequest)
                {
                    Interlocked.Increment(ref collectionAttempts);
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
            };
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            Task<ReadResponse> call = ReadAsync(peer.Session, cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);
                Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await call.WaitAsync(peer.Timeout).ConfigureAwait(false));
                await scope.DisposeAsync().ConfigureAwait(false);
                Assert.That(collectionAttempts, Is.EqualTo(1));
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(peer.Submissions.Single().ExecutionAllowance, Is.EqualTo(12345.5));
                Assert.That(peer.Submissions.Single().ResultRetentionTime, Is.EqualTo(6789.25));
                Assert.That(peer.Controls, Is.Empty);
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task CancellationAfterAcceptanceDoesNotCancelReceiptLifetime()
        {
            using var cancellation = new CancellationTokenSource();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            ReadResponse result = await ReadAsync(peer.Session, cancellation.Token).ConfigureAwait(false);

            await cancellation.CancelAsync().ConfigureAwait(false);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);

            AssertRead(result, 42);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.True);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.Controls.Single().Action, Is.EqualTo(ServiceOperationControlAction.Acknowledge));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task IdentityChangeRejectsReceiptBeforeAcknowledgementDispatch(bool activationFails)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                configureEndpoint: endpoint => endpoint.Description.UserIdentityTokens =
                [
                    endpoint.Description.UserIdentityTokens[0],
                    new UserTokenPolicy
                    {
                        PolicyId = "username", TokenType = UserTokenType.UserName, SecurityPolicyUri = SecurityPolicies.None
                    }
                ]).ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            peer.BeforeRespondAsync = (request, _) =>
            {
                if (activationFails && request is ActivateSessionRequest)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                return default;
            };
            var identity = new UserIdentity("synthetic-user", []);
            if (activationFails)
            {
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await peer.Owner.UpdateSessionAsync(identity, [], peer.Timeout).ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            else
            {
                await peer.Owner.UpdateSessionAsync(identity, [], peer.Timeout).ConfigureAwait(false);
            }

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            AssertRead(result, 42);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ChangedNamespaceMappingRejectsReceiptBeforeAcknowledgementDispatch()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            peer.Session.NamespaceUris.Append("urn:synthetic:changed-mapping");

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            AssertRead(result, 42);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task BinaryPooledResponsesRemainValidAfterEnvelopeReuseAndLaterOperations()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.BinaryRoundTripResponses = true;
            await using RecoverableAcknowledgementScope firstScope = peer.Session.BeginAcknowledgementScope();
            ReadResponse first = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            await firstScope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            await firstScope.DisposeAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope secondScope = peer.Session.BeginAcknowledgementScope();
            ReadResponse second = await peer.Session.ReadAsync(new RequestHeader { RequestHandle = 219 },
                0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = new NodeId(101u), AttributeId = Attributes.Value }],
                peer.Timeout).ConfigureAwait(false);
            await secondScope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            try
            {
                AssertRead(first, 42);
                AssertRead(second, 43);
                Assert.That(first.ResponseHeader.RequestHandle, Is.EqualTo(137u));
                Assert.That(second.ResponseHeader.RequestHandle, Is.EqualTo(219u));
                Assert.That(first.ResponseHeader.StringTable.ToArray(), Is.EqualTo(s_originalStrings));
                Assert.That(peer.DeliveredResponses.OfType<SubmitServiceResponse>().Count(), Is.EqualTo(2));
                Assert.That(peer.DeliveredResponses, Is.All.InstanceOf<IPooledEncodeable>());
                Assert.That(peer.Submissions, Has.Count.EqualTo(2));
                Assert.That(peer.Controls, Has.Count.EqualTo(2));
            }
            finally
            {
                first.Reuse();
                second.Reuse();
            }
        }

        [TestCase("root-denied")]
        [TestCase("root-shape")]
        [TestCase("translate-denied")]
        [TestCase("partial-target")]
        [TestCase("remote-target")]
        [TestCase("version-type")]
        [TestCase("service-list-type")]
        public async Task MalformedOrDeniedCapabilityDiscoveryNeverFallsBack(string invalid)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.MutateResponse = (request, response) =>
            {
                if (request is ReadRequest { NodesToRead.Count: 1 } read &&
                    read.NodesToRead[0].NodeId == new NodeId(1200100u) &&
                    response is ReadResponse root)
                {
                    if (invalid == "root-denied")
                    {
                        root.Results = [new DataValue(Variant.From((int)NodeClass.Object), StatusCodes.BadUserAccessDenied)];
                    }
                    else if (invalid == "root-shape")
                    {
                        root.Results = [];
                    }
                }
                if (response is TranslateBrowsePathsToNodeIdsResponse translated)
                {
                    switch (invalid)
                    {
                        case "translate-denied":
                            translated.Results[2].StatusCode = StatusCodes.BadNodeIdUnknown;
                            break;
                        case "partial-target":
                            translated.Results[0].Targets[0].RemainingPathIndex = 0;
                            break;
                        case "remote-target":
                            translated.Results[0].Targets[0].TargetId = ExpandedNodeId.Parse("svr=1;i=93001");
                            break;
                    }
                }
                if (request is ReadRequest { NodesToRead.Count: 4 } && response is ReadResponse values)
                {
                    if (invalid == "version-type")
                    {
                        values.Results =
                        [
                            new DataValue(Variant.From(2)), values.Results[1], values.Results[2], values.Results[3]
                        ];
                    }
                    else if (invalid == "service-list-type")
                    {
                        values.Results =
                        [
                            values.Results[0], new DataValue(Variant.From("Read")), values.Results[2], values.Results[3]
                        ];
                    }
                }
            };

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(invalid switch
            {
                "root-denied" => StatusCodes.BadUserAccessDenied,
                "translate-denied" => StatusCodes.BadNodeIdUnknown,
                _ => StatusCodes.BadDecodingError
            }));
            AssertNoApplicationDispatch(peer);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task SingleBudgetOverridePreservesTheOtherAdvertisedDefault(bool execution)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    ExecutionAllowance = execution ? 777.5 : null,
                    ResultRetentionTime = execution ? null : 444.25
                }).ConfigureAwait(false);
            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            AssertRead(result, 42);
            Assert.That(peer.Submissions.Single().ExecutionAllowance, Is.EqualTo(execution ? 777.5 : 12345.5));
            Assert.That(peer.Submissions.Single().ResultRetentionTime, Is.EqualTo(execution ? 6789.25 : 444.25));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task CompoundCallerOperationKeepsCapturedBudgetsAndAcknowledgesEveryOriginal()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            int result = await ClientOperationContext.ExecuteAsync(peer.Session, async token =>
            {
                int first = await peer.Session.ReadValueAsync<int>(new NodeId(100u), token).ConfigureAwait(false);
                peer.ExecutionDefault = new DataValue(Variant.From(99d));
                peer.RetentionDefault = new DataValue(Variant.From(88d));
                int second = await peer.Session.ReadValueAsync<int>(new NodeId(101u), token).ConfigureAwait(false);
                return first + second;
            }, peer.Timeout).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(85));
            Assert.That(scope.Receipt!.IsFullyRecoverable, Is.True);
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(3));
            Assert.That(peer.Submissions, Has.Count.EqualTo(2));
            Assert.That(peer.Submissions.Select(submit => submit.ExecutionAllowance), Is.All.EqualTo(12345.5));
            Assert.That(peer.Submissions.Select(submit => submit.ResultRetentionTime), Is.All.EqualTo(6789.25));
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(2));
            Assert.That(peer.Controls.Select(control => control.ServerToken).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public async Task MixedCompoundOutcomeMarksReceiptPartialAndAcknowledgesOnlyRetainedOriginal()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            int result = await ClientOperationContext.ExecuteAsync(peer.Session, async token =>
            {
                int first = await peer.Session.ReadValueAsync<int>(new NodeId(100u), token).ConfigureAwait(false);
                peer.SubmitFault = StatusCodes.BadServiceUnsupported;
                int second = await peer.Session.ReadValueAsync<int>(new NodeId(101u), token).ConfigureAwait(false);
                int third = await peer.Session.ReadValueAsync<int>(new NodeId(100u), token).ConfigureAwait(false);
                return first + second + third;
            }, peer.Timeout).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(127));
            Assert.That(scope.Receipt!.IsFullyRecoverable, Is.False);
            Assert.That(scope.Receipt.IsAcknowledged, Is.False);
            Assert.That(peer.Submissions, Has.Count.EqualTo(2), "An ordinary fallback is not retried for the third read.");
            Assert.That(peer.OrdinaryRequests, Has.Count.EqualTo(2));
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task FallbackScopeCannotBeReusedEvenWithoutReceipt()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.CapabilityAvailable = false;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            AssertRead(result, 42);
            Assert.That(scope.Receipt, Is.Null);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false));

            Assert.That(peer.OrdinaryRequests, Has.Count.EqualTo(1));
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(1));
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
        }

        [Test]
        public async Task AcceptanceCallbackCanUseADifferentSessionWithoutSharingReceipts()
        {
            await using RecoverableSessionPeer first = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableSessionPeer second = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = first.Session.BeginAcknowledgementScope(
                acceptAsync: async (context, ct) =>
                {
                    Assert.That(context.TryGetResult(out ReadResponse? original), Is.True);
                    AssertRead(original!, 42);
                    int related = await second.Session.ReadValueAsync<int>(new NodeId(101u), ct).ConfigureAwait(false);
                    Assert.That(related, Is.EqualTo(43));
                });

            ReadResponse response = await ReadAsync(first.Session, first.Timeout).ConfigureAwait(false);
            await second.AckEntered.Task.WaitAsync(second.Timeout).ConfigureAwait(false);

            AssertRead(response, 42);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            Assert.That(first.Controls, Is.Empty);
            Assert.That(second.Controls, Has.Count.EqualTo(1));
            await scope.AcknowledgeAsync(first.Timeout).ConfigureAwait(false);
            Assert.That(first.Submissions, Has.Count.EqualTo(1));
            Assert.That(second.Submissions, Has.Count.EqualTo(1));
            Assert.That(first.Controls, Has.Count.EqualTo(1));
            Assert.That(scope.Receipt.IsAcknowledged, Is.True);
        }

        [Test]
        public async Task CancelledAcknowledgementLeavesReceiptRetryableWithoutOriginalReplay()
        {
            using var cancellation = new CancellationTokenSource();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            ReadResponse response = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await scope.AcknowledgeAsync(cancellation.Token).ConfigureAwait(false));

            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            AssertRead(response, 42);
            Assert.That(scope.Receipt.IsAcknowledged, Is.True);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task ConcurrentAcknowledgementsSendOneConfirmedControl()
        {
            TaskCompletionSource<bool> entered = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> release = RecoverableSessionPeer.NewSignal();
            int controlAttempts = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            ReadResponse response = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            peer.BeforeRespondAsync = async (request, ct) =>
            {
                if (request is ControlServiceExecutionRequest)
                {
                    Interlocked.Increment(ref controlAttempts);
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
            };
            Task first = scope.AcknowledgeAsync(peer.Timeout).AsTask();
            Task? second = null;
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                second = scope.Receipt!.AcknowledgeAsync(peer.Timeout).AsTask();
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(controlAttempts, Is.EqualTo(1));
                release.TrySetResult(true);
                await Task.WhenAll(first, second).WaitAsync(peer.Timeout).ConfigureAwait(false);
                AssertRead(response, 42);
                Assert.That(scope.Receipt.IsAcknowledged, Is.True);
                Assert.That(peer.Controls, Has.Count.EqualTo(1));
                Assert.That(controlAttempts, Is.EqualTo(1));
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await first.WaitAsync(peer.Timeout).ConfigureAwait(false);
                if (second != null)
                {
                    await second.WaitAsync(peer.Timeout).ConfigureAwait(false);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BadReadValueIsAnOriginalOutcomeNotMalformedOutput(bool typed)
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (context, _) =>
                    {
                        if (typed)
                        {
                            Assert.That(context.Failure!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                            Assert.That(context.TryGetResult(out int _), Is.False);
                        }
                        else
                        {
                            Assert.That(context.Failure, Is.Null);
                            Assert.That(context.TryGetResult(out ReadResponse? read), Is.True);
                            Assert.That(read!.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                        }
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            peer.MutateResponse = (_, response) =>
            {
                if (response is SubmitServiceResponse submit && submit.Result.TryGetValue(out ReadResponse? read))
                {
                    read.Results = [new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown)];
                }
            };
            if (typed)
            {
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await peer.Session.ReadValueAsync<int>(new NodeId(100u), peer.Timeout).ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            else
            {
                ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
                Assert.That(result.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(result.Results[0].WrappedValue.IsNull, Is.True);
            }
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(acceptances, Is.EqualTo(1));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task FailedOriginalOutcomeCompletesCallerBeforeAutomaticAcknowledgement()
        {
            TaskCompletionSource<bool> entered = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> release = RecoverableSessionPeer.NewSignal();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = async (context, ct) =>
                    {
                        Assert.That(context.Failure!.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
                        Assert.That(context.TryGetResult(out ReadResponse? _), Is.False);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            peer.OriginalFailure = StatusCodes.BadServiceUnsupported;
            Task<ReadResponse> call = ReadAsync(peer.Session, peer.Timeout).AsTask();
            peer.CallerHasCompleted = () => call.IsFaulted;
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(call.IsCompleted, Is.False);
                Assert.That(peer.Controls, Is.Empty);
                release.TrySetResult(true);
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await call.WaitAsync(peer.Timeout).ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
                await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(peer.AckObservedCallerCompletion, Is.True);
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(peer.Controls, Has.Count.EqualTo(1));
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacySessionSyncAndApmRemainOrdinary(bool apm)
        {
            await using var channels = new SessionChannelHarness();
            await using Session session = await channels.CreateSessionAsync(
                SessionChannelHarness.CreateEndpoint()).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            ScriptedChannel transport = channels.CreatedChannels[0];
            int originalReads = 0;
            int submissions = 0;
            transport.RequestHandler = (request, _) =>
            {
                if (request is SubmitServiceRequest)
                {
                    Interlocked.Increment(ref submissions);
                }
                if (request is ReadRequest { NodesToRead.Count: 1 } read &&
                    read.NodesToRead[0].NodeId == new NodeId(100u))
                {
                    Interlocked.Increment(ref originalReads);
                    Assert.That(read.RequestHeader.RequestHandle, Is.EqualTo(193u));
                    Assert.That(read.NodesToRead[0].AttributeId, Is.EqualTo(Attributes.Value));
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader { RequestHandle = 193 },
                        Results = [new DataValue(Variant.From(42))]
                    });
                }
                return new ValueTask<IServiceResponse>(transport.CreateResponse(request));
            };
            ArrayOf<ReadValueId> nodes =
                [new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value }];
            ArrayOf<DataValue> values;
            ArrayOf<DiagnosticInfo> diagnostics;
            ResponseHeader? header;
#pragma warning disable CS0618 // Exercise the actual public compatibility APIs; no production suppression is introduced.
            if (apm)
            {
                var completed = new TaskCompletionSource<IAsyncResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                object state = new();
                IAsyncResult pending = session.BeginRead(new RequestHeader { RequestHandle = 193 },
                    0, TimestampsToReturn.Neither, nodes, result => completed.TrySetResult(result), state);
                IAsyncResult callbackResult = await completed.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(callbackResult, Is.SameAs(pending));
                Assert.That(callbackResult.AsyncState, Is.SameAs(state));
                header = session.EndRead(callbackResult, out values, out diagnostics);
            }
            else
            {
                header = session.Read(new RequestHeader { RequestHandle = 193 }, 0, TimestampsToReturn.Neither,
                    nodes, out values, out diagnostics);
            }
#pragma warning restore CS0618
            Assert.That(header!.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(header.RequestHandle, Is.EqualTo(193u));
            Assert.That(values, Has.Count.EqualTo(1));
            Assert.That(Scalar(values[0]), Is.EqualTo(42));
            Assert.That(diagnostics, Is.Empty);
            Assert.That(originalReads, Is.EqualTo(1));
            Assert.That(submissions, Is.Zero);
            ServiceResultException scopeError = Assert.Throws<ServiceResultException>(() =>
                session.BeginAcknowledgementScope(requireRecoverableReceipt: true))!;
            Assert.That(scopeError.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(originalReads, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TerminalLifetimeOrRetentionFailureNeverFallsBack(bool deadline)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            StatusCode expected = deadline ? StatusCodes.BadTimeout : StatusCodes.BadResponseTooLarge;
            peer.MutateResponse = (_, response) =>
            {
                if (response is SubmitServiceResponse submit)
                {
                    submit.Operation.OutcomeKind = deadline
                        ? ServiceOperationOutcome.ExecutionDeadlineExceeded : ServiceOperationOutcome.ResultUnavailable;
                    submit.Operation.OutcomeStatus = expected;
                    submit.Result = default;
                }
            };
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(expected));
            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task ReadValuesConvenienceAcceptsCompleteTypedTuple()
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (context, _) =>
                    {
                        Assert.That(context.TryGetResult(
                            out (ArrayOf<DataValue> Values, ArrayOf<ServiceResult> Errors) result), Is.True);
                        Assert.That(result.Values.ToList().Select(Scalar), Is.EqualTo(s_readValues));
                        Assert.That(result.Errors, Has.Count.EqualTo(2));
                        Assert.That(result.Errors.ToList().Select(error => error.StatusCode), Is.All.EqualTo(StatusCodes.Good));
                        Assert.That(context.Failure, Is.Null);
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);

            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) = await peer.Session.ReadValuesAsync(
                [new NodeId(100u), new NodeId(101u)], peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(values.ToList().Select(Scalar), Is.EqualTo(s_readValues));
            Assert.That(errors.ToList().Select(error => error.StatusCode), Is.All.EqualTo(StatusCodes.Good));
            Assert.That(acceptances, Is.EqualTo(1));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CallConvenienceAcceptsTypedArgumentsOrLegitimateMethodFailure(bool failed)
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (context, _) =>
                    {
                        if (failed)
                        {
                            Assert.That(context.Failure!.StatusCode, Is.EqualTo(StatusCodes.BadMethodInvalid));
                            Assert.That(context.TryGetResult(out ArrayOf<Variant> _), Is.False);
                        }
                        else
                        {
                            Assert.That(context.TryGetResult(out ArrayOf<Variant> output), Is.True);
                            Assert.That(output[0].TryGetValue(out ByteString csr), Is.True);
                            Assert.That(csr, Is.EqualTo(ByteString.From("synthetic-csr"u8)));
                            Assert.That(context.Failure, Is.Null);
                        }
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            peer.MethodStatus = failed ? StatusCodes.BadMethodInvalid : StatusCodes.Good;
            if (failed)
            {
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await peer.Session.CallAsync(new NodeId(200u), new NodeId(300u), peer.Timeout, Variant.From(7))
                        .ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadMethodInvalid));
            }
            else
            {
                ArrayOf<Variant> output = await peer.Session.CallAsync(
                    new NodeId(200u), new NodeId(300u), peer.Timeout, Variant.From(7)).ConfigureAwait(false);
                Assert.That(output[0].TryGetValue(out ByteString csr), Is.True);
                Assert.That(csr, Is.EqualTo(ByteString.From("synthetic-csr"u8)));
            }
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(acceptances, Is.EqualTo(1));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task ResendDataConvenienceAcceptsCompletePerSubscriptionStatuses()
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (context, _) =>
                    {
                        Assert.That(context.TryGetResult(out ArrayOf<ServiceResult> errors), Is.True);
                        Assert.That(errors, Has.Count.EqualTo(2));
                        Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(errors[1].StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
                        Assert.That(context.Failure, Is.Null);
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            peer.MutateResponse = (_, response) =>
            {
                if (response is SubmitServiceResponse submit && submit.Result.TryGetValue(out CallResponse? call))
                {
                    call.Results[1].StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                }
            };

            ArrayOf<ServiceResult> result = await peer.Session.ResendDataAsync([5u, 7u], peer.Timeout)
                .ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(result[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result[1].StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            Assert.That(acceptances, Is.EqualTo(1));
            var original = (CallRequest)RecoverableSessionPeer.Original(peer.Submissions.Single());
            Assert.That(original.MethodsToCall, Has.Count.EqualTo(2));
            Assert.That(original.MethodsToCall.ToList().Select(method => method.MethodId), Is.All.EqualTo(MethodIds.Server_ResendData));
            Assert.That(original.MethodsToCall[0].InputArguments[0].TryGetValue(out uint first), Is.True);
            Assert.That(first, Is.EqualTo(5u));
            Assert.That(original.MethodsToCall[1].InputArguments[0].TryGetValue(out uint second), Is.True);
            Assert.That(second, Is.EqualTo(7u));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MalformedLocalOutputCannotBorrowAnotherOriginalFailureStatus(bool proxy)
        {
            int acceptances = 0;
            int submissions = 0;
            StatusCode expected = proxy ? StatusCodes.BadUnexpectedError : StatusCodes.BadTypeMismatch;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (_, _) =>
                    {
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            peer.MutateResponse = (_, response) =>
            {
                if (response is not SubmitServiceResponse submit || Interlocked.Increment(ref submissions) != 1)
                {
                    return;
                }
                if (proxy && submit.Result.TryGetValue(out CallResponse? call))
                {
                    call.Results[0].StatusCode = expected;
                }
                else if (!proxy && submit.Result.TryGetValue(out ReadResponse? read))
                {
                    read.Results = [new DataValue(Variant.Null, expected)];
                }
            };
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ClientOperationContext.ExecuteAsync(peer.Session, async token =>
                {
                    if (proxy)
                    {
                        CallResponse first = await peer.Session.CallAsync(null,
                            [new CallMethodRequest { ObjectId = new NodeId(200u), MethodId = new NodeId(300u) }],
                            token).ConfigureAwait(false);
                        Assert.That(first.Results[0].StatusCode, Is.EqualTo(expected));
                        peer.InvalidMethodOutput = true;
                        _ = await CreateSigningRequestAsync(peer).ConfigureAwait(false);
                    }
                    else
                    {
                        ReadResponse first = await ReadAsync(peer.Session, token).ConfigureAwait(false);
                        Assert.That(first.Results[0].StatusCode, Is.EqualTo(expected));
                        _ = await peer.Session.ReadValueAsync<string>(new NodeId(101u), token).ConfigureAwait(false);
                    }
                    return 0;
                }, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(expected));
            Assert.That(acceptances, Is.Zero, "A local validation failure is not an accepted original outcome.");
            Assert.That(peer.Submissions, Has.Count.EqualTo(2));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task PendingAdmissionDoesNotExposeReceiptUntilARealOutcomeArrives()
        {
            TaskCompletionSource<bool> collecting = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> release = RecoverableSessionPeer.NewSignal();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.FastResult = false;
            peer.BeforeRespondAsync = async (request, ct) =>
            {
                if (request is GetServiceResultRequest)
                {
                    collecting.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
            };
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            Task<ReadResponse> call = ReadAsync(peer.Session, peer.Timeout).AsTask();
            try
            {
                await collecting.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(call.IsCompleted, Is.False);
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(scope.Receipt, Is.Null, "Admission and possession of a token are not receipt of an outcome.");
                ServiceResultException noReceipt = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false))!;
                Assert.That(noReceipt.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(peer.Controls, Is.Empty);

                release.TrySetResult(true);
                ReadResponse result = await call.WaitAsync(peer.Timeout).ConfigureAwait(false);
                AssertRead(result, 42);
                Assert.That(scope.Receipt!.SessionId, Is.EqualTo(peer.Session.SessionId));
                Assert.That(scope.Receipt.IsAcknowledged, Is.False);
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(scope.Receipt.IsAcknowledged, Is.True);
                Assert.That(peer.Controls, Has.Count.EqualTo(1));
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [TestCase("resource")]
        [TestCase("lifetime")]
        [TestCase("validation")]
        [TestCase("authorization")]
        [TestCase("security")]
        [TestCase("decode")]
        public async Task RejectedAdmissionCannotExposeReceiptOrSuccessfulAcknowledgement(string rejected)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.SubmitFault = RejectionStatus(rejected);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(RejectionStatus(rejected)));
            Assert.That(scope.Receipt, Is.Null);
            ServiceResultException noReceipt = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false))!;
            Assert.That(noReceipt.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnadvertisedServiceOnlyFallsBackWhenReceiptIsNotRequired(bool required)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.SupportedServices = [DataTypeIds.WriteRequest, DataTypeIds.CallRequest];
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(required);
            if (required)
            {
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            }
            else
            {
                AssertRead(await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false), 42);
            }
            Assert.That(scope.Receipt, Is.Null);
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(3));
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Has.Count.EqualTo(required ? 0 : 1));
            Assert.That(peer.Controls, Is.Empty);
        }

        [Test]
        public async Task UnknownAcknowledgementDispositionCannotConfirmReceipt()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();
            ReadResponse result = await ReadAsync(peer.Session, peer.Timeout).ConfigureAwait(false);
            peer.MutateResponse = (_, response) =>
            {
                if (response is ControlServiceExecutionResponse control)
                {
                    control.Result = ServiceOperationControlResult.UnknownOperation;
                    control.Operation = RecoverableSessionPeer.UnknownInfo(Uuid.Empty);
                }
            };

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            AssertRead(result, 42);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            peer.MutateResponse = null;
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(2));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MatchingStatusReplacementExceptionCannotBorrowOriginalProvenance(bool rethrowOriginal)
        {
            int acceptances = 0;
            ServiceResultException? observedOriginal = null;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (context, _) =>
                    {
                        Assert.That(context.Failure!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                        Assert.That(context.TryGetResult(out ReadResponse? _), Is.False);
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            peer.OriginalFailure = StatusCodes.BadUserAccessDenied;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope();

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ClientOperationContext.ExecuteAsync(peer.Session, async token =>
                {
                    try
                    {
                        return await ReadAsync(peer.Session, token).ConfigureAwait(false);
                    }
                    catch (ServiceResultException original)
                    {
                        observedOriginal = original;
                        if (rethrowOriginal)
                        {
                            throw;
                        }
                        throw new ServiceResultException(original.StatusCode, "synthetic local replacement");
                    }
                }, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(acceptances, Is.EqualTo(rethrowOriginal ? 1 : 0));
            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            if (rethrowOriginal)
            {
                Assert.That(failure, Is.SameAs(observedOriginal));
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(scope.Receipt.IsAcknowledged, Is.True);
            }
            else
            {
                Assert.That(failure, Is.Not.SameAs(observedOriginal));
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false));
            }
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(rethrowOriginal ? 1 : 0));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase("good")]
        [TestCase("bad-quality")]
        [TestCase("bad-quality-decodable")]
        [TestCase("null")]
        public async Task ConvenienceVariableReadAcceptsObservationAndPermitsOnlyLibraryPreparation(string valueKind)
        {
            int acceptances = 0;
            var sourceTime = new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);
            DateTime serverTime = sourceTime.AddSeconds(1);
            bool hasValue = valueKind is "good" or "bad-quality-decodable";
            StatusCode quality = valueKind is "bad-quality" or "bad-quality-decodable"
                ? StatusCodes.BadOutOfRange : StatusCodes.Good;
            Variant wireValue = valueKind switch
            {
                "good" or "bad-quality-decodable" => Variant.From(42),
                "bad-quality" => Variant.From("not-an-integer"),
                _ => Variant.Null
            };
            var observation = new DataValue(wireValue, quality, sourceTime, serverTime);
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            ClientVariable<int> variable = CreateConvenienceVariable(peer);
            peer.ReadValueFactory = request =>
            {
                Assert.That(request.NodeId, Is.EqualTo(new NodeId(100u)));
                return observation;
            };
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true, acceptAsync: (context, _) =>
                {
                    Assert.That(context.TryGetResult(out ClientRead<int> typed), Is.True);
                    Assert.That(typed.Value, Is.EqualTo(hasValue ? 42 : 0));
                    Assert.That(typed.HasValue, Is.EqualTo(hasValue));
                    Assert.That(typed.DataValue.StatusCode, Is.EqualTo(quality));
                    Assert.That(typed.DataValue.WrappedValue, Is.EqualTo(wireValue));
                    Assert.That(context.Failure, Is.Null);
                    Interlocked.Increment(ref acceptances);
                    return default;
                });

            ClientRead<int> result = await variable.ReadAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(result.Value, Is.EqualTo(hasValue ? 42 : 0));
            Assert.That(result.HasValue, Is.EqualTo(hasValue));
            Assert.That(result.DataValue.SourceTimestamp, Is.EqualTo(sourceTime));
            Assert.That(result.DataValue.ServerTimestamp, Is.EqualTo(serverTime));
            Assert.That(result.DataValue.StatusCode, Is.EqualTo(quality));
            Assert.That(result.DataValue.WrappedValue, Is.EqualTo(wireValue));
            Assert.That(acceptances, Is.EqualTo(1));
            AssertOnlyPreparationBrowses(peer, 1);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(scope.Receipt!.IsFullyRecoverable, Is.True);
            Assert.That(peer.Controls, Is.Empty);
            ServiceResultException excluded = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await BrowseAsync(peer.Session, peer.Timeout).ConfigureAwait(false))!;
            Assert.That(excluded.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            AssertOnlyPreparationBrowses(peer, 1);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConvenienceVariableMalformedValueOrUnitsNeverAcceptsOrAcknowledges(bool units)
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            ClientVariable<int> variable = CreateConvenienceVariable(peer);
            peer.ReadValueFactory = request =>
            {
                Assert.That(request.NodeId, Is.EqualTo(new NodeId(units ? 101u : 100u)));
                return new DataValue(Variant.From("malformed-model-value"));
            };
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true, acceptAsync: (_, _) =>
                {
                    Interlocked.Increment(ref acceptances);
                    return default;
                });

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (units)
                {
                    _ = await variable.ReadEngineeringUnitsAsync(peer.Timeout).ConfigureAwait(false);
                }
                else
                {
                    _ = await variable.ReadAsync(peer.Timeout).ConfigureAwait(false);
                }
            })!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(acceptances, Is.Zero);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            AssertOnlyPreparationBrowses(peer, units ? 2 : 1);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false));
            Assert.That(peer.Controls, Is.Empty);
        }

        [Test]
        public async Task ConvenienceVariableEngineeringUnitsAcceptsTheNativeStructure()
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            ClientVariable<int> variable = CreateConvenienceVariable(peer);
            peer.ReadValueFactory = request =>
            {
                Assert.That(request.NodeId, Is.EqualTo(new NodeId(101u)));
                return new DataValue(Variant.From(new ExtensionObject(new EUInformation
                {
                    NamespaceUri = "urn:synthetic:units",
                    UnitId = 7,
                    DisplayName = new LocalizedText("en", "kPa"),
                    Description = new LocalizedText("en", "kilopascal")
                })));
            };
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true, acceptAsync: (context, _) =>
                {
                    Assert.That(context.TryGetResult(out EUInformation? units), Is.True);
                    Assert.That(units!.UnitId, Is.EqualTo(7));
                    Assert.That(units.NamespaceUri, Is.EqualTo("urn:synthetic:units"));
                    Assert.That(units.DisplayName.Text, Is.EqualTo("kPa"));
                    Assert.That(context.Failure, Is.Null);
                    Interlocked.Increment(ref acceptances);
                    return default;
                });

            EUInformation result = await variable.ReadEngineeringUnitsAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(result.UnitId, Is.EqualTo(7));
            Assert.That(result.NamespaceUri, Is.EqualTo("urn:synthetic:units"));
            Assert.That(result.DisplayName.Text, Is.EqualTo("kPa"));
            Assert.That(result.Description.Text, Is.EqualTo("kilopascal"));
            Assert.That(acceptances, Is.EqualTo(1));
            AssertOnlyPreparationBrowses(peer, 2);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(scope.Receipt!.IsFullyRecoverable, Is.True);
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConvenienceMissingVariableOrUnitsNeverAcceptsPreparationAsAnOutcome(bool units)
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            ClientVariable<int> variable = CreateConvenienceVariable(peer);
            Func<BrowseDescription, ArrayOf<ReferenceDescription>> resolve = peer.BrowseReferenceFactory!;
            peer.BrowseReferenceFactory = request =>
                request.NodeId == new NodeId(units ? 100u : 200u) ? [] : resolve(request);
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true, acceptAsync: (_, _) =>
                {
                    Interlocked.Increment(ref acceptances);
                    return default;
                });

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (units)
                {
                    _ = await variable.ReadEngineeringUnitsAsync(peer.Timeout).ConfigureAwait(false);
                }
                else
                {
                    _ = await variable.ReadAsync(peer.Timeout).ConfigureAwait(false);
                }
            })!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(acceptances, Is.Zero);
            AssertOnlyPreparationBrowses(peer, units ? 2 : 1);
            Assert.That(scope.Receipt, Is.Null);
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.DiscoveryRequests, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
        }

        [Test]
        public async Task ConvenienceEngineeringUnitsBadQualityRemainsAnOriginalFailedOutcome()
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            ClientVariable<int> variable = CreateConvenienceVariable(peer);
            peer.ReadValueFactory = request =>
            {
                Assert.That(request.NodeId, Is.EqualTo(new NodeId(101u)));
                return DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied);
            };
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true, acceptAsync: (context, _) =>
                {
                    Assert.That(context.Failure!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(context.TryGetResult(out EUInformation? _), Is.False);
                    Interlocked.Increment(ref acceptances);
                    return default;
                });

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await variable.ReadEngineeringUnitsAsync(peer.Timeout).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(acceptances, Is.EqualTo(1));
            AssertOnlyPreparationBrowses(peer, 2);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConvenienceVariableWriteAcceptsGoodOrOriginalBadResult(bool bad)
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            ClientVariable<int> variable = CreateConvenienceVariable(peer);
            peer.MutateResponse = (_, response) =>
            {
                if (bad && response is SubmitServiceResponse submit && submit.Result.TryGetValue(out WriteResponse? write))
                {
                    write.Results = [StatusCodes.BadUserAccessDenied];
                }
            };
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true, acceptAsync: (context, _) =>
                {
                    if (bad)
                    {
                        Assert.That(context.Failure!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                        Assert.That(context.TryGetResult(out StatusCode _), Is.False);
                    }
                    else
                    {
                        Assert.That(context.TryGetResult(out StatusCode status), Is.True);
                        Assert.That(status, Is.EqualTo(StatusCodes.Good));
                        Assert.That(context.Failure, Is.Null);
                    }
                    Interlocked.Increment(ref acceptances);
                    return default;
                });

            if (bad)
            {
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await variable.WriteAsync(87, peer.Timeout).ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            else
            {
                await variable.WriteAsync(87, peer.Timeout).ConfigureAwait(false);
            }
            Assert.That(acceptances, Is.EqualTo(1));
            var original = (WriteRequest)RecoverableSessionPeer.Original(peer.Submissions.Single());
            Assert.That(original.NodesToWrite, Has.Count.EqualTo(1));
            Assert.That(original.NodesToWrite[0].NodeId, Is.EqualTo(new NodeId(100u)));
            Assert.That(original.NodesToWrite[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(Scalar(original.NodesToWrite[0].Value), Is.EqualTo(87));
            AssertOnlyPreparationBrowses(peer, 1);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.False);
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ConvenienceReadOnlyVariableRejectsWriteBeforePreparationOrAcceptance()
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (_, _) =>
                    {
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            ClientVariable<int> variable = CreateConvenienceVariable(peer, writable: false);

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await variable.WriteAsync(87, peer.Timeout).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(acceptances, Is.Zero);
            Assert.That(peer.DiscoveryRequests, Is.Empty);
            AssertNoApplicationDispatch(peer);
        }

        [TestCase("bad")]
        [TestCase("good-qualified")]
        [TestCase("uncertain")]
        public async Task ConvenienceCallWithResultKeepsPerItemStatusAndArgumentDiagnostics(string quality)
        {
            int acceptances = 0;
            StatusCode status = quality switch
            {
                "bad" => StatusCodes.BadInvalidArgument,
                "good-qualified" => StatusCodes.GoodClamped,
                _ => StatusCodes.Uncertain
            };
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            var proxy = new UninstrumentedProxy(peer.Session, peer.Channels.Telemetry);
            peer.MutateResponse = (_, response) =>
            {
                if (response is SubmitServiceResponse submit && submit.Result.TryGetValue(out CallResponse? call))
                {
                    call.Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = status,
                            InputArgumentResults = [StatusCodes.Good, StatusCodes.BadOutOfRange],
                            InputArgumentDiagnosticInfos =
                                [new DiagnosticInfo(), new DiagnosticInfo { AdditionalInfo = "argument-two" }],
                            OutputArguments = [Variant.From(27), Variant.From("v2")]
                        }
                    ];
                }
            };
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true, acceptAsync: (context, _) =>
                {
                    Assert.That(context.TryGetResult(out CallMethodResult? result), Is.True);
                    Assert.That(result!.StatusCode, Is.EqualTo(status));
                    Assert.That(result.InputArgumentResults[1], Is.EqualTo(StatusCodes.BadOutOfRange));
                    Assert.That(result.InputArgumentDiagnosticInfos[1].AdditionalInfo, Is.EqualTo("argument-two"));
                    Assert.That(context.Failure, Is.Null, "A returned bad method status is not a thrown service failure.");
                    Interlocked.Increment(ref acceptances);
                    return default;
                });

            CallMethodResult actual = await proxy.CallWithResultAsync(
                new ExpandedNodeId(300u), [Variant.From(5), Variant.From("request")], peer.Timeout).ConfigureAwait(false);

            Assert.That(actual.StatusCode, Is.EqualTo(status));
            Assert.That(actual.InputArgumentResults[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(actual.InputArgumentResults[1], Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(actual.InputArgumentDiagnosticInfos[1].AdditionalInfo, Is.EqualTo("argument-two"));
            Assert.That(actual.OutputArguments[0].TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(27));
            Assert.That(actual.OutputArguments[1].TryGetValue(out string? version), Is.True);
            Assert.That(version, Is.EqualTo("v2"));
            Assert.That(acceptances, Is.EqualTo(1));
            var original = (CallRequest)RecoverableSessionPeer.Original(peer.Submissions.Single());
            Assert.That(original.MethodsToCall[0].ObjectId, Is.EqualTo(new NodeId(200u)));
            Assert.That(original.MethodsToCall[0].MethodId, Is.EqualTo(new NodeId(300u)));
            Assert.That(original.MethodsToCall[0].InputArguments[0].TryGetValue(out int argument), Is.True);
            Assert.That(argument, Is.EqualTo(5));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.True);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConvenienceCallWithResultMalformedShapeNeverAcceptsOrAcknowledges(bool diagnostics)
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (_, _) =>
                    {
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            var proxy = new UninstrumentedProxy(peer.Session, peer.Channels.Telemetry);
            peer.MutateResponse = (_, response) =>
            {
                if (response is SubmitServiceResponse submit && submit.Result.TryGetValue(out CallResponse? call))
                {
                    if (diagnostics)
                    {
                        call.DiagnosticInfos = [new DiagnosticInfo(), new DiagnosticInfo()];
                    }
                    else
                    {
                        call.Results = [];
                    }
                }
            };

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await proxy.CallWithResultAsync(new ExpandedNodeId(300u), [], peer.Timeout).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(acceptances, Is.Zero);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConvenienceReadBytesToAcceptsOneCompletedCopyAndAcknowledgesEveryOriginal(bool noData)
        {
            int acceptances = 0;
            TaskCompletionSource<bool> entered = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> release = RecoverableSessionPeer.NewSignal();
            using var destination = new MemoryStream();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = async (context, ct) =>
                    {
                        Assert.That(context.TryGetResult(out long count), Is.True);
                        Assert.That(count, Is.EqualTo(5L));
                        Assert.That(destination.ToArray(), Is.EqualTo(ByteString.From("abcde"u8).ToArray()));
                        Assert.That(context.Failure, Is.Null);
                        Interlocked.Increment(ref acceptances);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            ScriptConvenienceReads(peer,
                new DataValue(Variant.From(ByteString.From("ab"u8))),
                new DataValue(Variant.From(ByteString.From("c"u8))),
                new DataValue(Variant.From(ByteString.From("de"u8))),
                noData ? DataValue.FromStatusCode(StatusCodes.BadIndexRangeNoData)
                    : new DataValue(Variant.From(ByteString.Empty)));
            Task acknowledgements = ObserveConvenienceAcknowledgements(peer, 4);
            Task<long> call = peer.Session.ReadBytesToAsync(new NodeId(100u), destination,
                maxBytes: 5, chunkSize: 4, peer.Timeout).AsTask();
            peer.CallerHasCompleted = () => call.IsCompletedSuccessfully;
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(call.IsCompleted, Is.False);
                Assert.That(peer.Submissions, Has.Count.EqualTo(4));
                Assert.That(peer.Controls, Is.Empty);
                Assert.That(ConvenienceReadRanges(peer), Is.EqualTo(s_convenienceStreamRanges));
                release.TrySetResult(true);
                Assert.That(await call.WaitAsync(peer.Timeout).ConfigureAwait(false), Is.EqualTo(5L));
                await acknowledgements.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(acceptances, Is.EqualTo(1));
                Assert.That(destination.ToArray(), Is.EqualTo(ByteString.From("abcde"u8).ToArray()));
                Assert.That(destination.CanWrite, Is.True);
                Assert.That(peer.AckObservedCallerCompletion, Is.True);
                Assert.That(peer.Controls, Has.Count.EqualTo(4));
                Assert.That(peer.AckCallerCompletions, Is.All.True);
                Assert.That(peer.Controls.Select(control => control.ServerToken).Distinct().Count(), Is.EqualTo(4));
                Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(3));
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConvenienceReadBytesToEmptyInputAcceptsZeroWithoutChangingDestination(bool noData)
        {
            int acceptances = 0;
            using var destination = new MemoryStream();
            destination.WriteByte(99);
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (context, _) =>
                    {
                        Assert.That(context.TryGetResult(out long count), Is.True);
                        Assert.That(count, Is.Zero);
                        Assert.That(destination.Length, Is.EqualTo(1L));
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            ScriptConvenienceReads(peer, noData
                ? DataValue.FromStatusCode(StatusCodes.BadIndexRangeNoData)
                : new DataValue(Variant.From(ByteString.Empty)));

            long copied = await peer.Session.ReadBytesToAsync(
                new NodeId(100u), destination, 4, 4, peer.Timeout).ConfigureAwait(false);
            await peer.AckEntered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);

            Assert.That(copied, Is.Zero);
            Assert.That(destination.ToArray(), Is.EqualTo(ByteString.From("c"u8).ToArray()));
            Assert.That(destination.Position, Is.EqualTo(1L));
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(acceptances, Is.EqualTo(1));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
            Assert.That(ConvenienceReadRanges(peer), Is.EqualTo(s_convenienceFirstRange));
        }

        [TestCase("destination")]
        [TestCase("type")]
        [TestCase("range")]
        [TestCase("limit")]
        public async Task ConvenienceReadBytesToLocalFailureNeverAcknowledgesFragments(string failureKind)
        {
            int acceptances = 0;
            using MemoryStream destination = failureKind == "destination"
                ? new FailingConvenienceDestination() : new MemoryStream();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (_, _) =>
                    {
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            if (failureKind == "range")
            {
                ScriptConvenienceReads(peer, new DataValue(Variant.From(ByteString.From("abcde"u8))));
            }
            else
            {
                ScriptConvenienceReads(peer,
                    new DataValue(Variant.From(ByteString.From("ab"u8))),
                    failureKind == "type" ? new DataValue(Variant.From("not-a-byte-string"))
                        : new DataValue(Variant.From(ByteString.From("cd"u8))));
            }
            if (failureKind == "type")
            {
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await peer.Session.ReadBytesToAsync(new NodeId(100u), destination,
                        8, 4, peer.Timeout).ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            }
            else
            {
                IOException failure = Assert.ThrowsAsync<IOException>(async () =>
                    await peer.Session.ReadBytesToAsync(new NodeId(100u), destination,
                        failureKind == "limit" ? 3 : 8, 4, peer.Timeout).ConfigureAwait(false))!;
                if (destination is FailingConvenienceDestination scripted)
                {
                    Assert.That(failure, Is.SameAs(scripted.Failure));
                    Assert.That(scripted.WriteAttempts, Is.EqualTo(2));
                }
            }

            Assert.That(destination.ToArray(), Is.EqualTo(failureKind == "range"
                ? ByteString.Empty.ToArray() : ByteString.From("ab"u8).ToArray()));
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(acceptances, Is.Zero);
            Assert.That(peer.Submissions, Has.Count.EqualTo(failureKind == "range" ? 1 : 2));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(ConvenienceReadRanges(peer), Is.EqualTo(
                s_convenienceStreamRanges.Take(failureKind == "range" ? 1 : 2)));
        }

        [Test]
        public async Task ConvenienceReadBytesToCancelledDestinationWriteNeverAcknowledges()
        {
            int acceptances = 0;
            using var cancellation = new CancellationTokenSource();
            using var destination = new PendingConvenienceDestination();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (_, _) =>
                    {
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            ScriptConvenienceReads(peer, new DataValue(Variant.From(ByteString.From("ab"u8))));
            Task<long> call = peer.Session.ReadBytesToAsync(
                new NodeId(100u), destination, 8, 4, cancellation.Token).AsTask();
            try
            {
                await destination.Entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(call.IsCompleted, Is.False);
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(peer.Controls, Is.Empty);
                await cancellation.CancelAsync().ConfigureAwait(false);
                Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await call.WaitAsync(peer.Timeout).ConfigureAwait(false));
                Assert.That(acceptances, Is.Zero);
                Assert.That(destination.Length, Is.Zero);
                Assert.That(destination.CanWrite, Is.True);
                Assert.That(peer.Controls, Is.Empty);
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                destination.Release.TrySetResult(true);
            }
        }

        [TestCase("empty")]
        [TestCase("null")]
        [TestCase("short")]
        [TestCase("exact")]
        [TestCase("exact-empty")]
        [TestCase("exact-null")]
        [TestCase("multiple")]
        public async Task ConvenienceChunkedByteStringAcceptsOnlyTheCompleteValue(string shape)
        {
            int acceptances = 0;
            int reads = shape is "empty" or "null" or "short" ? 1 : 2;
            ByteString expected = shape switch
            {
                "empty" or "null" => ByteString.Empty,
                "short" => ByteString.From("ab"u8),
                "exact" or "exact-empty" or "exact-null" => ByteString.From("abcd"u8),
                _ => ByteString.From("abcde"u8)
            };
            TaskCompletionSource<bool> entered = RecoverableSessionPeer.NewSignal();
            TaskCompletionSource<bool> release = RecoverableSessionPeer.NewSignal();
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = async (context, ct) =>
                    {
                        Assert.That(context.TryGetResult(out ByteString value), Is.True);
                        Assert.That(value, Is.EqualTo(expected));
                        Assert.That(context.Failure, Is.Null);
                        Interlocked.Increment(ref acceptances);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            peer.Session.ServerCapabilities.MaxByteStringLength = 4;
            switch (shape)
            {
                case "empty":
                    ScriptConvenienceReads(peer, new DataValue(Variant.From(ByteString.Empty)));
                    break;
                case "null":
                    ScriptConvenienceReads(peer, new DataValue(Variant.Null));
                    break;
                case "short":
                    ScriptConvenienceReads(peer, new DataValue(Variant.From(ByteString.From("ab"u8))));
                    break;
                case "exact":
                    ScriptConvenienceReads(peer, new DataValue(Variant.From(ByteString.From("abcd"u8))),
                        DataValue.FromStatusCode(StatusCodes.BadIndexRangeNoData));
                    break;
                case "exact-empty":
                case "exact-null":
                    ScriptConvenienceReads(peer, new DataValue(Variant.From(ByteString.From("abcd"u8))),
                        shape == "exact-empty" ? new DataValue(Variant.From(ByteString.Empty)) : new DataValue(Variant.Null));
                    break;
                default:
                    ScriptConvenienceReads(peer, new DataValue(Variant.From(ByteString.From("abcd"u8))),
                        new DataValue(Variant.From(ByteString.From("e"u8))));
                    break;
            }
            Task acknowledgements = ObserveConvenienceAcknowledgements(peer, reads);
            Task<ByteString> call = peer.Session.ReadByteStringInChunksAsync(new NodeId(100u), peer.Timeout);
            peer.CallerHasCompleted = () => call.IsCompletedSuccessfully;
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(call.IsCompleted, Is.False);
                Assert.That(peer.Submissions, Has.Count.EqualTo(reads));
                Assert.That(peer.Controls, Is.Empty);
                release.TrySetResult(true);
                ByteString result = await call.WaitAsync(peer.Timeout).ConfigureAwait(false);
                await acknowledgements.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(result, Is.EqualTo(expected));
                Assert.That(acceptances, Is.EqualTo(1));
                Assert.That(peer.Controls, Has.Count.EqualTo(reads));
                Assert.That(peer.AckCallerCompletions, Is.All.True);
                Assert.That(peer.Controls.Select(control => control.ServerToken).Distinct().Count(), Is.EqualTo(reads));
                Assert.That(peer.AckObservedCallerCompletion, Is.True);
                Assert.That(ConvenienceReadRanges(peer), Is.EqualTo(s_convenienceChunkRanges.Take(reads)));
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConvenienceChunkedByteStringMalformedTailNeverAcknowledgesPrefix(bool oversized)
        {
            int acceptances = 0;
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                new RecoverableSessionOptions
                {
                    AcceptAsync = (_, _) =>
                    {
                        Interlocked.Increment(ref acceptances);
                        return default;
                    }
                }).ConfigureAwait(false);
            peer.Session.ServerCapabilities.MaxByteStringLength = 4;
            ScriptConvenienceReads(peer, new DataValue(Variant.From(ByteString.From("abcd"u8))),
                oversized ? new DataValue(Variant.From(ByteString.From("efghi"u8)))
                    : new DataValue(Variant.From(42)));

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peer.Session.ReadByteStringInChunksAsync(new NodeId(100u), peer.Timeout).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(oversized ? StatusCodes.BadUnknownResponse : StatusCodes.BadTypeMismatch));
            Assert.That(acceptances, Is.Zero);
            Assert.That(peer.Submissions, Has.Count.EqualTo(2));
            Assert.That(ConvenienceReadRanges(peer), Is.EqualTo(s_convenienceChunkRanges));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        private static void ScriptConvenienceReads(RecoverableSessionPeer peer, params DataValue[] values)
        {
            int index = -1;
            peer.ReadValueFactory = request =>
            {
                Assert.That(request.NodeId, Is.EqualTo(new NodeId(100u)));
                Assert.That(request.AttributeId, Is.EqualTo(Attributes.Value));
                int current = Interlocked.Increment(ref index);
                Assert.That(current, Is.LessThan(values.Length), "The API must stop after the scripted terminal result.");
                return values[current];
            };
        }

        private static string?[] ConvenienceReadRanges(RecoverableSessionPeer peer)
        {
            return [.. peer.Submissions.Select(submit =>
                ((ReadRequest)RecoverableSessionPeer.Original(submit)).NodesToRead[0].IndexRange)];
        }

        private static Task<bool> ObserveConvenienceAcknowledgements(RecoverableSessionPeer peer, int expected)
        {
            TaskCompletionSource<bool> completed = RecoverableSessionPeer.NewSignal();
            int received = 0;
            peer.MutateResponse = (_, response) =>
            {
                if (response is ControlServiceExecutionResponse && Interlocked.Increment(ref received) == expected)
                {
                    completed.TrySetResult(true);
                }
            };
            return completed.Task;
        }

        private sealed class FailingConvenienceDestination : MemoryStream
        {
            public IOException Failure { get; } = new("synthetic destination failure");
            public int WriteAttempts { get; private set; }

#if NET6_0_OR_GREATER
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (++WriteAttempts == 2)
                {
                    throw Failure;
                }
                return base.WriteAsync(buffer, cancellationToken);
            }
#else
            public override Task WriteAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
            {
                if (++WriteAttempts == 2)
                {
                    throw Failure;
                }
                return base.WriteAsync(buffer, offset, count, cancellationToken);
            }
#endif
        }

        private sealed class PendingConvenienceDestination : MemoryStream
        {
            public TaskCompletionSource<bool> Entered { get; } = RecoverableSessionPeer.NewSignal();
            public TaskCompletionSource<bool> Release { get; } = RecoverableSessionPeer.NewSignal();

#if NET6_0_OR_GREATER
            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
#else
            public override async Task WriteAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
            {
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                await base.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            }
#endif
        }

        private static ClientVariable<int> CreateConvenienceVariable(RecoverableSessionPeer peer, bool writable = true)
        {
            peer.BrowseReferenceFactory = request =>
            {
                Assert.That(request.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
                Assert.That(request.BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
                bool units = request.NodeId == new NodeId(100u);
                Assert.That(request.NodeId == new NodeId(200u) || units, Is.True);
                return
                [
                    new ReferenceDescription
                    {
                        NodeId = new ExpandedNodeId(units ? 101u : 100u),
                        BrowseName = new QualifiedName(units ? BrowseNames.EngineeringUnits : "SyntheticVariable"),
                        NodeClass = NodeClass.Variable,
                        ReferenceTypeId = units ? ReferenceTypeIds.HasProperty : ReferenceTypeIds.HasComponent,
                        IsForward = true
                    }
                ];
            };
            return new ClientVariable<int>(new UninstrumentedProxy(peer.Session, peer.Channels.Telemetry),
                Namespaces.OpcUa, "SyntheticVariable", writable, static (value, _) => value.TryGetValue(out int number)
                        ? number : throw new ServiceResultException(StatusCodes.BadTypeMismatch), static value => Variant.From(value));
        }

        private static void AssertOnlyPreparationBrowses(RecoverableSessionPeer peer, int count)
        {
            Assert.That(peer.OrdinaryRequests, Has.Count.EqualTo(count));
            Assert.That(peer.OrdinaryRequests, Is.All.InstanceOf<BrowseRequest>());
        }

        private sealed class UninstrumentedProxy(ISessionClient session, ITelemetryContext telemetry)
            : ObjectTypeClient(session, new NodeId(200u), telemetry)
        {
            public ValueTask<ArrayOf<Variant>> InvokeAsync(CancellationToken ct)
            {
                return CallMethodAsync(new NodeId(300u), ct, Variant.From(7));
            }
        }

        private static ValueTask<BrowseResponse> BrowseAsync(ISession session, CancellationToken ct)
        {
            return session.BrowseAsync(null, null, 0,
                [new BrowseDescription { NodeId = new NodeId(100u), BrowseDirection = BrowseDirection.Forward }], ct);
        }

        private static StatusCode RejectionStatus(string rejection)
        {
            return rejection switch
            {
                "unsupported" => StatusCodes.BadServiceUnsupported,
                "resource" => StatusCodes.BadResourceUnavailable,
                "lifetime" => StatusCodes.BadOutOfRange,
                "validation" => StatusCodes.BadInvalidArgument,
                "authorization" => StatusCodes.BadUserAccessDenied,
                "security" => StatusCodes.BadSecurityChecksFailed,
                "decode" => StatusCodes.BadDecodingError,
                _ => throw new ArgumentOutOfRangeException(nameof(rejection))
            };
        }

        private static void SetDefaultBudget(RecoverableSessionPeer peer, bool execution, DataValue value)
        {
            if (execution)
            {
                peer.ExecutionDefault = value;
            }
            else
            {
                peer.RetentionDefault = value;
            }
        }

        private static void AssertNoApplicationDispatch(RecoverableSessionPeer peer)
        {
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        private static ValueTask<ReadResponse> ReadAsync(ISession session, CancellationToken ct)
        {
            return session.ReadAsync(new RequestHeader { RequestHandle = 137 }, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value }], ct);
        }

        private static ValueTask<ByteString> CreateSigningRequestAsync(RecoverableSessionPeer peer)
        {
            return new ServerConfigurationTypeClient(
                peer.Session, ObjectIds.ServerConfiguration, peer.Channels.Telemetry)
                .CreateSigningRequestAsync(ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType, "CN=Synthetic", false, default, peer.Timeout);
        }

        private static int Scalar(DataValue value)
        {
            Assert.That(value.WrappedValue.TryGetValue(out int scalar), Is.True);
            return scalar;
        }

        private static void AssertRead(ReadResponse response, params int[] values)
        {
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results.ToList().Select(Scalar), Is.EqualTo(values));
            Assert.That(response.Results.ToList().Select(value => value.StatusCode), Is.All.EqualTo(StatusCodes.Good));
        }

        private static readonly string[] s_originalStrings = ["synthetic-original"];
        private static readonly string[] s_convenienceStreamRanges = ["0:3", "2:5", "3:6", "5:8"];
        private static readonly string[] s_convenienceChunkRanges = ["0:3", "4:7"];
        private static readonly string[] s_convenienceFirstRange = ["0:3"];
        private static readonly int[] s_readValues = [42, 43];
        private static readonly int[] s_writeValues = [7, 9];
    }
}

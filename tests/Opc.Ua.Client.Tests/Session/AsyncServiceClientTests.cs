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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

namespace Opc.Ua.Client.Tests.SessionContexts
{
    /// <summary>
    /// Exercises typed handle-first acceptance through a real managed Session and scripted transport.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class AsyncServiceClientTests
    {
        /// <summary>
        /// The injectable factory is explicit and repeat registration does not replace its singleton.
        /// </summary>
        [Test]
        public async Task ClientFactoryRegistrationIsExplicitAndIdempotent()
        {
            var services = new ServiceCollection();
            Assert.That(services.AddExperimentalAsyncServiceClient(), Is.SameAs(services));
            services.AddExperimentalAsyncServiceClient();
            await using ServiceProvider provider = services.BuildServiceProvider();
            AsyncServiceClient first = provider.GetRequiredService<AsyncServiceClient>();
            Assert.That(provider.GetRequiredService<AsyncServiceClient>(), Is.SameAs(first));
            _ = Assert.Throws<ArgumentNullException>(() =>
                AsyncServiceClientServiceCollectionExtensions.AddExperimentalAsyncServiceClient(null!));
        }

        /// <summary>
        /// Factory argument failures occur before binding or preparing any original request.
        /// </summary>
        [TestCase("read")]
        [TestCase("write")]
        [TestCase("call")]
        [TestCase("method")]
        [TestCase("void-method")]
        [TestCase("session")]
        public void FactoryRejectsMissingArgumentsBeforeBinding(string missing)
        {
            var client = new AsyncServiceClient();
            _ = Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                switch (missing)
                {
                    case "read":
                        _ = await client.CreateReadAsync(null!, null!).ConfigureAwait(false);
                        break;
                    case "write":
                        _ = await client.CreateWriteAsync(null!, null!).ConfigureAwait(false);
                        break;
                    case "call":
                        _ = await client.CreateCallAsync(null!, null!).ConfigureAwait(false);
                        break;
                    case "method":
                        _ = await client.CreateMethodAsync<ByteString>(null!, null!).ConfigureAwait(false);
                        break;
                    case "void-method":
                        _ = await client.CreateMethodAsync(null!, null!).ConfigureAwait(false);
                        break;
                    case "session":
                        _ = await client.CreateReadAsync(null!, CreateReadRequest()).ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentException("Unknown missing argument.", nameof(missing));
                }
            });
        }

        /// <summary>
        /// Low-level generated wrappers preserve their original values and use an explicit receipt exchange.
        /// </summary>
        [Test]
        public async Task LowLevelManagedServicesUseSeparateExchanges()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            Uuid id = Uuid.NewUuid();
            SubmitServiceResponse admission = await peer.Owner.SubmitServiceAsync(
                null, id, new ExtensionObject(CreateReadRequest()), 10_000, 5000, 0, peer.Timeout)
                .ConfigureAwait(false);
            ByteString token = admission.Operation.ServerToken;
            admission.Reuse();
            GetServiceResultResponse collection = await peer.Owner.GetServiceResultAsync(
                null, Uuid.Empty, token, 0, peer.Timeout).ConfigureAwait(false);
            Assert.That(collection.Operation.ClientOperationId, Is.EqualTo(id));
            Assert.That(collection.Result.TryGetValue(out ReadResponse? original), Is.True);
            Assert.That(original!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            collection.Reuse();
            ControlServiceExecutionResponse receipt = await peer.Owner.ControlServiceExecutionAsync(
                null, token, ServiceOperationControlAction.Acknowledge, peer.Timeout).ConfigureAwait(false);
            Assert.That(receipt.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            receipt.Reuse();
            Assert.That(peer.Submissions, Is.EqualTo(1));
            Assert.That(peer.Acknowledgements, Is.EqualTo(1));
        }

        /// <summary>
        /// Submit is not on the shared transport's idempotent retry allowlist, even without the typed handle.
        /// </summary>
        [Test]
        public async Task LowLevelManagedSubmissionIsNeverReplayedAfterReplyLoss()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            peer.LoseSubmission = true;
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peer.Owner.SubmitServiceAsync(
                    null, Uuid.NewUuid(), new ExtensionObject(CreateReadRequest()), 10_000, 5000, 0, peer.Timeout)
                    .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            Assert.That(peer.Submissions, Is.EqualTo(1));
        }

        /// <summary>
        /// Losing the submission reply recovers the same Client ID after verified same-Session reactivation.
        /// </summary>
        [Test]
        public async Task LostSubmissionReplyCollectsByClientIdWithoutResubmission()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            peer.LoseSubmission = true;
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false));
            Assert.That(operation.ServerToken.IsNull, Is.True);
            await peer.RecoverAsync().ConfigureAwait(false);
            _ = await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ReadResponse> result = await operation.Completion.WaitAsync(peer.Timeout)
                .ConfigureAwait(false);
            Assert.That(result.GetValueOrThrow().Results[0].WrappedValue.TryGetValue(out int scalar), Is.True);
            Assert.That(scalar, Is.EqualTo(42));
            Assert.That(peer.LastLookupUsedClientId, Is.True);
            Assert.That(peer.Submissions, Is.EqualTo(1));
            Assert.That(peer.Acknowledgements, Is.EqualTo(1));
            Assert.That(operation.Invalidated.IsCompleted, Is.False);
            _ = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false));
            Assert.That(peer.Submissions, Is.EqualTo(1));
        }

        /// <summary>
        /// An ACK transport failure leaves Completion successful and only the explicit ACK is retried.
        /// </summary>
        [Test]
        public async Task LostAcknowledgementDoesNotReplaceAcceptedOutcome()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            peer.LoseAcknowledgement = true;
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            peer.IsAccepted = () => operation.Completion.IsCompletedSuccessfully;
            _ = await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false);
            _ = await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ReadResponse> result = await operation.Completion.WaitAsync(peer.Timeout)
                .ConfigureAwait(false);
            Assert.That(result.HasValue, Is.True);
            Assert.That(peer.AckObservedAcceptance, Is.True);
            Assert.That(operation.AcknowledgementFailure, Is.Not.Null);
            Assert.That(operation.LastAcknowledgement, Is.Null);
            await peer.RecoverAsync().ConfigureAwait(false);
            ControlServiceExecutionResponse retry =
                await operation.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(retry.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            retry.Reuse();
            Assert.That(operation.AcknowledgementFailure, Is.Null);
            Assert.That(peer.Submissions, Is.EqualTo(1));
            Assert.That(peer.Acknowledgements, Is.EqualTo(2));
            Assert.That(result.GetValueOrThrow().Results[0].WrappedValue.TryGetValue(out int scalar), Is.True);
            Assert.That(scalar, Is.EqualTo(42));
        }

        /// <summary>
        /// Explicit application receipt uses the same acknowledged control after the caller has accepted the value.
        /// </summary>
        [Test]
        public async Task ManualAcceptanceDoesNotSendAutomaticAcknowledgement()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            peer.FastResult = true;
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            operation.RequireExplicitAcknowledgement();
            _ = await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ReadResponse> result = await operation.Completion.WaitAsync(peer.Timeout)
                .ConfigureAwait(false);
            Assert.That(result.HasValue, Is.True);
            Assert.That(peer.Acknowledgements, Is.Zero);
            ControlServiceExecutionResponse acknowledged = await operation.AcknowledgeAsync(peer.Timeout)
                .ConfigureAwait(false);
            Assert.That(acknowledged.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            acknowledged.Reuse();
            Assert.That(peer.Acknowledgements, Is.EqualTo(1));
        }

        /// <summary>
        /// Failed application storage is not acceptance and leaves the retained outcome unacknowledged.
        /// </summary>
        [Test]
        public async Task FailedAcceptanceCanBeRetriedWithoutAcknowledgingOrRepeatingWork()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            int acceptanceAttempts = 0;
            operation.WithAcceptancePolicy((_, _) =>
            {
                if (Interlocked.Increment(ref acceptanceAttempts) == 1)
                {
                    throw new IOException("Synthetic storage failure.");
                }
                return default;
            });
            _ = await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<IOException>(async () =>
                await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false));
            Assert.That(operation.Completion.IsCompleted, Is.False);
            Assert.That(peer.Acknowledgements, Is.Zero);
            _ = await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            Assert.That(operation.Completion.IsCompletedSuccessfully, Is.True);
            Assert.That(acceptanceAttempts, Is.EqualTo(2));
            Assert.That(peer.Acknowledgements, Is.EqualTo(1));
            Assert.That(peer.Submissions, Is.EqualTo(1));
        }

        /// <summary>
        /// Cancelled storage acceptance leaves the decoded candidate unacknowledged and available for a later retry.
        /// </summary>
        [Test]
        public async Task CancelledAcceptanceCanBeRetriedWithoutAcknowledgement()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempts = 0;
            operation.WithAcceptancePolicy(async (_, ct) =>
            {
                Interlocked.Increment(ref attempts);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            });
            _ = await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(peer.Timeout);
            Task<ServiceOperationInfoDataType> collecting = operation.CollectAsync(ct: cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(() => collecting,
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(operation.Completion.IsCompleted, Is.False);
                Assert.That(peer.Acknowledgements, Is.Zero);
                release.TrySetResult(true);
                _ = await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
                Assert.That(operation.Completion.IsCompletedSuccessfully, Is.True);
                Assert.That(attempts, Is.EqualTo(2));
                Assert.That(peer.Acknowledgements, Is.EqualTo(1));
                Assert.That(peer.Submissions, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Decoding alone is not acceptance: the application completion/storage gate must finish before ACK.
        /// </summary>
        [Test]
        public async Task DelayedAcceptanceIsNotAcknowledgedAfterDecoding()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.WithAcceptancePolicy(async (result, ct) =>
            {
                Assert.That(result.GetValueOrThrow().Results[0].WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(42));
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            });
            _ = await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false);
            Task<ServiceOperationInfoDataType> collecting = operation.CollectAsync(ct: peer.Timeout).AsTask();
            try
            {
                await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(operation.Completion.IsCompleted, Is.False);
                Assert.That(peer.Acknowledgements, Is.Zero);
                release.TrySetResult(true);
                _ = await collecting.ConfigureAwait(false);
                Assert.That(operation.Completion.IsCompletedSuccessfully, Is.True);
                Assert.That(peer.Acknowledgements, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// A caller completion context closed during acceptance must not trigger even an attempted automatic ACK.
        /// </summary>
        [Test]
        public async Task RejectedCallerCompletionContextDoesNotAcknowledge()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.WithAcceptancePolicy(async (_, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            });
            _ = await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false);
            Task<ServiceOperationInfoDataType> collecting = operation.CollectAsync(ct: peer.Timeout).AsTask();
            await entered.Task.WaitAsync(peer.Timeout).ConfigureAwait(false);
            Task disposing = operation.DisposeAsync().AsTask();
            try
            {
                _ = Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await operation.Completion.WaitAsync(peer.Timeout).ConfigureAwait(false));
                release.TrySetResult(true);
                _ = await collecting.ConfigureAwait(false);
                await disposing.ConfigureAwait(false);
                Assert.That(peer.Acknowledgements, Is.Zero);
                Assert.That(operation.AcknowledgementFailure, Is.Null);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// A generated proxy validates its output type before the SDK accepts or acknowledges a Call outcome.
        /// </summary>
        [Test]
        public async Task GeneratedProxyValidatesTypedOutputBeforeAcknowledgement()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            peer.InvalidMethodOutput = true;
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ByteString> operation = await client.CreateMethodAsync(peer.Owner,
                (session, ct) => new ServerConfigurationTypeClient(
                    session, ObjectIds.ServerConfiguration, peer.Telemetry)
                    .CreateSigningRequestAsync(ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType, "CN=Synthetic", false, default, ct),
                peer.Timeout).ConfigureAwait(false);
            _ = await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false));
            Assert.That(operation.Completion.IsCompleted, Is.False);
            Assert.That(peer.Acknowledgements, Is.Zero);
            Assert.That(peer.Submissions, Is.EqualTo(1));
        }

        /// <summary>
        /// The proxy's compatibility fallback cannot issue a second original Call inside one retained operation.
        /// </summary>
        [Test]
        public async Task GeneratedProxyDoesNotRetryAnOriginalMethodId()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            peer.MethodStatus = StatusCodes.BadMethodInvalid;
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ByteString> operation = await client.CreateMethodAsync(peer.Owner,
                (session, ct) => new ServerConfigurationTypeClient(
                    session, ObjectIds.ServerConfiguration, peer.Telemetry)
                    .CreateSigningRequestAsync(ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType, "CN=Synthetic", false, default, ct),
                peer.Timeout).ConfigureAwait(false);
            _ = await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false);
            _ = await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ByteString> result = await operation.Completion.WaitAsync(peer.Timeout)
                .ConfigureAwait(false);
            ServiceResultException failure = Assert.Throws<ServiceResultException>(
                () => result.GetValueOrThrow())!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadMethodInvalid));
            Assert.That(peer.Submissions, Is.EqualTo(1));
            Assert.That(peer.PreparationBrowses, Is.Zero);
            Assert.That(peer.Acknowledgements, Is.EqualTo(1));
        }

        /// <summary>
        /// Invalid remote metadata or an incompatible original response is never accepted or acknowledged.
        /// </summary>
        [TestCase("unknown-metadata")]
        [TestCase("wrong-client")]
        [TestCase("wrong-service")]
        [TestCase("revised-allowance")]
        [TestCase("changed-token")]
        [TestCase("wrong-original-handle")]
        [TestCase("wrong-response-type")]
        [TestCase("pending-with-payload")]
        [TestCase("retry-after-retention")]
        public async Task MalformedEnvelopeIsNotAcknowledged(string fault)
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            _ = await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false);
            peer.MutateResponse = response =>
            {
                if (response is not GetServiceResultResponse result)
                {
                    return;
                }
                switch (fault)
                {
                    case "unknown-metadata":
                        result.Operation.State = ServiceOperationState.Unknown;
                        break;
                    case "wrong-client":
                        result.Operation.ClientOperationId = Uuid.NewUuid();
                        break;
                    case "wrong-service":
                        result.Operation.ServiceId = DataTypeIds.WriteRequest;
                        break;
                    case "revised-allowance":
                        result.Operation.ExecutionAllowance++;
                        break;
                    case "changed-token":
                        result.Operation.ServerToken = ByteString.From("different-opaque-token"u8);
                        break;
                    case "wrong-original-handle":
                        Assert.That(result.Result.TryGetValue(out ReadResponse? read), Is.True);
                        read!.ResponseHeader.RequestHandle++;
                        break;
                    case "wrong-response-type":
                        result.Result = new ExtensionObject(new WriteResponse
                        {
                            ResponseHeader = new ResponseHeader { RequestHandle = 13 },
                            Results = [StatusCodes.Good]
                        });
                        break;
                    case "pending-with-payload":
                    case "retry-after-retention":
                        result.Operation.State = ServiceOperationState.Pending;
                        result.Operation.OutcomeKind = ServiceOperationOutcome.None;
                        result.Operation.ExecutionRemaining = 9000;
                        result.Operation.ResultRemaining = 0;
                        if (fault == "retry-after-retention")
                        {
                            result.Result = default;
                            result.Operation.RetryAfter = result.Operation.ResultRetentionTime;
                        }
                        break;
                    default:
                        throw new ArgumentException("Unknown malformed envelope.", nameof(fault));
                }
            };
            _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false));
            Assert.That(operation.Completion.IsCompleted, Is.False);
            Assert.That(peer.Acknowledgements, Is.Zero);
            Assert.That(peer.Submissions, Is.EqualTo(1));
        }

        /// <summary>
        /// The SDK accepts an original ServiceFault rather than misclassifying it as a failed wrapper exchange.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OriginalServiceFaultOrDeadlineIsAcceptedAndAcknowledged(bool protocolDeadline)
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            peer.OriginalFailure = protocolDeadline ? StatusCodes.Good : StatusCodes.BadTimeout;
            peer.ProtocolDeadline = protocolDeadline;
            peer.FastResult = true;
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ReadResponse> result = await operation.ExecuteAsync(
                10_000, 5000, ct: peer.Timeout).ConfigureAwait(false);
            if (protocolDeadline)
            {
                Assert.That(result.OriginalResponse, Is.Null);
                Assert.That(
                    result.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            }
            else
            {
                Assert.That(result.OriginalResponse, Is.InstanceOf<ServiceFault>());
            }
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() => result.GetValueOrThrow())!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(peer.Submissions, Is.EqualTo(1));
            Assert.That(peer.Acknowledgements, Is.EqualTo(1));
        }

        /// <summary>
        /// Client identity exists even when the cancellable submission is stopped before any dispatch.
        /// </summary>
        [Test]
        public async Task CancelledSubmissionKeepsItsPreexistingHandleIdentity()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            Uuid id = operation.ClientOperationId;
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () => await operation.SubmitAsync(10_000, 5000, ct: cancelled.Token)
                .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(id, Is.Not.EqualTo(Uuid.Empty));
            Assert.That(operation.ClientOperationId, Is.EqualTo(id));
            Assert.That(peer.Submissions, Is.Zero);
            Assert.That(peer.Acknowledgements, Is.Zero);
        }

        /// <summary>
        /// Identity exists before submission; the default ACK follows validated acceptance into Completion.
        /// </summary>
        [Test]
        public async Task HandlePrecedesSubmissionAndAcceptedReadIsAcknowledged()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            int submissions = 0;
            int acknowledgements = 0;
            Uuid clientId = default;
            uint originalHandle = 0;
            var token = ByteString.From(new byte[32]);
            await using var channels = new SessionChannelHarness(
                configureChannel: channel => channel.RequestHandler = (request, _) =>
                {
                    switch (request)
                    {
                        case SubmitServiceRequest submit:
                            submissions++;
                            clientId = submit.ClientOperationId;
                            Assert.That(submit.Request.TryGetValue(out ReadRequest? original), Is.True);
                            originalHandle = original!.RequestHeader.RequestHandle;
                            return new ValueTask<IServiceResponse>(new SubmitServiceResponse
                            {
                                ResponseHeader = channel.CreateGoodHeader(),
                                Operation = Info(ServiceOperationState.Pending)
                            });
                        case GetServiceResultRequest:
                            return new ValueTask<IServiceResponse>(new GetServiceResultResponse
                            {
                                ResponseHeader = channel.CreateGoodHeader(),
                                Operation = Info(ServiceOperationState.OutcomeAvailable),
                                Result = new ExtensionObject(new ReadResponse
                                {
                                    ResponseHeader = new ResponseHeader { RequestHandle = originalHandle },
                                    Results = [new DataValue(Variant.From(42))]
                                })
                            });
                        case ControlServiceExecutionRequest control:
                            Assert.That(control.Action, Is.EqualTo(ServiceOperationControlAction.Acknowledge));
                            acknowledgements++;
                            return new ValueTask<IServiceResponse>(new ControlServiceExecutionResponse
                            {
                                ResponseHeader = channel.CreateGoodHeader(),
                                Result = ServiceOperationControlResult.Applied,
                                Operation = Info(ServiceOperationState.Acknowledged)
                            });
                        default:
                            return new ValueTask<IServiceResponse>(channel.CreateResponse(request));
                    }
                });
            await using Client.ManagedSession owner = await Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions { Endpoint = SessionChannelHarness.CreateEndpoint() },
                channels.Configuration,
                new DefaultSessionFactory(channels.Telemetry),
                telemetry: channels.Telemetry,
                channelManager: channels.Manager,
                updateBeforeConnect: false,
                ct: timeout.Token).ConfigureAwait(false);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(owner,
                new ReadRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 13 },
                    TimestampsToReturn = TimestampsToReturn.Neither,
                    NodesToRead = [new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value }]
                }, timeout.Token).ConfigureAwait(false);
            Assert.That(operation.ClientOperationId, Is.Not.EqualTo(Uuid.Empty));
            Assert.That(operation.ServerToken.IsNull, Is.True);
            Assert.That(submissions, Is.Zero);
            Assert.That(operation.Completion.IsCompleted, Is.False);

            ServiceOperationInfoDataType pending = await operation.SubmitAsync(10_000, 5000, ct: timeout.Token)
                .ConfigureAwait(false);
            Assert.That(pending.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(submissions, Is.EqualTo(1));
            Assert.That(acknowledgements, Is.Zero);
            _ = await operation.CollectAsync(ct: timeout.Token).ConfigureAwait(false);
            AsyncServiceResult<ReadResponse> accepted = await operation.Completion.WaitAsync(timeout.Token)
                .ConfigureAwait(false);
            ReadResponse value = accepted.GetValueOrThrow();
            Assert.That(value.Results[0].WrappedValue.TryGetValue(out int scalar), Is.True);
            Assert.That(scalar, Is.EqualTo(42));
            Assert.That(value.ResponseHeader.RequestHandle, Is.EqualTo(13u));
            Assert.That(submissions, Is.EqualTo(1));
            Assert.That(acknowledgements, Is.EqualTo(1));
            Assert.That(operation.LastAcknowledgement!.Result, Is.EqualTo(ServiceOperationControlResult.Applied));

            ServiceOperationInfoDataType Info(ServiceOperationState state)
            {
                return new ServiceOperationInfoDataType
                {
                    ClientOperationId = clientId,
                    ServerToken = token,
                    ServiceId = DataTypeIds.ReadRequest,
                    State = state,
                    OutcomeKind = state == ServiceOperationState.Pending
                        ? ServiceOperationOutcome.None : ServiceOperationOutcome.ServiceResponse,
                    ExecutionAllowance = 10_000,
                    ResultRetentionTime = 5000,
                    ExecutionRemaining = state == ServiceOperationState.Pending ? 9999 : 0,
                    ResultRemaining = state == ServiceOperationState.OutcomeAvailable ? 4999 : 0,
                    HistoryRemaining = state == ServiceOperationState.Acknowledged ? 4999 : 0
                };
            }
        }

        /// <summary>
        /// Creates a complete original request with a distinct original requestHandle.
        /// </summary>
        private static ReadRequest CreateReadRequest()
        {
            return new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 13 },
                TimestampsToReturn = TimestampsToReturn.Neither,
                NodesToRead = [new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value }]
            };
        }

        /// <summary>
        /// Scripts the remote protocol at the transport seam while retaining real Client/recovery/codec plumbing.
        /// </summary>
        private sealed class ScriptedOperationServer : IAsyncDisposable
        {
            private ScriptedOperationServer()
            {
                m_channels = new SessionChannelHarness(configureChannel: channel =>
                {
                    channel.Channel.MessageContext.ServerUris.Update(["urn:localhost:server"]);
                    channel.RequestHandler = (request, _) => Dispatch(channel, request);
                });
            }

            public Client.ManagedSession Owner { get; private set; } = null!;
            public ITelemetryContext Telemetry => m_channels.Telemetry;
            public CancellationToken Timeout => m_timeout.Token;
            public bool LoseSubmission { get; set; }
            public bool LoseAcknowledgement { get; set; }
            public bool FastResult { get; set; }
            public bool InvalidMethodOutput { get; set; }
            public StatusCode MethodStatus { get; set; }
            public StatusCode OriginalFailure { get; set; }
            public bool ProtocolDeadline { get; set; }
            public Action<IServiceResponse>? MutateResponse { get; set; }
            public Func<bool>? IsAccepted { get; set; }
            public bool AckObservedAcceptance { get; private set; }
            public bool LastLookupUsedClientId { get; private set; }
            public int Submissions { get; private set; }
            public int Acknowledgements { get; private set; }
            public int PreparationBrowses { get; private set; }

            public static async Task<ScriptedOperationServer> CreateAsync()
            {
                var peer = new ScriptedOperationServer();
                try
                {
                    peer.Owner = await Client.ManagedSession.CreateAsync(
                        new ManagedSessionOptions { Endpoint = SessionChannelHarness.CreateEndpoint() },
                        peer.m_channels.Configuration,
                        new DefaultSessionFactory(peer.Telemetry),
                        telemetry: peer.Telemetry,
                        channelManager: peer.m_channels.Manager,
                        updateBeforeConnect: false,
                        ct: peer.Timeout).ConfigureAwait(false);
                    return peer;
                }
                catch
                {
                    await peer.m_channels.DisposeAsync().ConfigureAwait(false);
                    peer.m_timeout.Dispose();
                    throw;
                }
            }

            public ValueTask RecoverAsync()
            {
                return m_channels.Manager.ReconnectAsync((IManagedTransportChannel)Owner.TransportChannel, Timeout);
            }

            public async ValueTask DisposeAsync()
            {
                await Owner.DisposeAsync().ConfigureAwait(false);
                await m_channels.DisposeAsync().ConfigureAwait(false);
                m_timeout.Dispose();
            }

            private ValueTask<IServiceResponse> Dispatch(ScriptedChannel channel, IServiceRequest request)
            {
                IServiceResponse response;
                switch (request)
                {
                    case SubmitServiceRequest submit:
                        Submissions++;
                        m_clientId = submit.ClientOperationId;
                        m_execution = submit.ExecutionAllowance;
                        m_retention = submit.ResultRetentionTime;
                        Assert.That(submit.Request.TryGetValue(out IServiceRequest? original), Is.True);
                        m_serviceId = ExpandedNodeId.ToNodeId(
                            original!.TypeId, channel.Channel.MessageContext.NamespaceUris);
                        m_original = original switch
                        {
                            ReadRequest => new ReadResponse
                            {
                                ResponseHeader = new ResponseHeader
                                {
                                    RequestHandle = original.RequestHeader.RequestHandle
                                },
                                Results = [new DataValue(Variant.From(42))]
                            },
                            CallRequest => new CallResponse
                            {
                                ResponseHeader = new ResponseHeader
                                {
                                    RequestHandle = original.RequestHeader.RequestHandle
                                },
                                Results =
                                [
                                    new CallMethodResult
                                    {
                                        StatusCode = MethodStatus,
                                        OutputArguments = InvalidMethodOutput
                                            ? [Variant.From(42)] : [Variant.From(ByteString.From("synthetic-csr"u8))]
                                    }
                                ]
                            },
                            _ => throw new InvalidOperationException("Unexpected original Service.")
                        };
                        if (StatusCode.IsBad(OriginalFailure))
                        {
                            m_original = new ServiceFault
                            {
                                ResponseHeader = new ResponseHeader
                                {
                                    RequestHandle = original.RequestHeader.RequestHandle,
                                    ServiceResult = OriginalFailure
                                }
                            };
                        }
                        m_state = FastResult ? ServiceOperationState.OutcomeAvailable : ServiceOperationState.Pending;
                        if (LoseSubmission)
                        {
                            LoseSubmission = false;
                            throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                        }
                        response = new SubmitServiceResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            Operation = Info(),
                            Result = m_state == ServiceOperationState.OutcomeAvailable && !ProtocolDeadline
                                ? new ExtensionObject(CoreUtils.Clone(m_original)!) : default
                        };
                        break;
                    case GetServiceResultRequest get:
                        LastLookupUsedClientId = get.ClientOperationId != Uuid.Empty;
                        if (LastLookupUsedClientId)
                        {
                            Assert.That(get.ClientOperationId, Is.EqualTo(m_clientId));
                            Assert.That(get.ServerToken.IsNull, Is.True);
                        }
                        else
                        {
                            Assert.That(get.ServerToken, Is.EqualTo(m_token));
                        }
                        if (m_state == ServiceOperationState.Pending)
                        {
                            m_state = ServiceOperationState.OutcomeAvailable;
                        }
                        response = new GetServiceResultResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            Operation = Info(),
                            Result = m_state == ServiceOperationState.OutcomeAvailable && !ProtocolDeadline
                                ? new ExtensionObject(CoreUtils.Clone(m_original)!) : default
                        };
                        break;
                    case ControlServiceExecutionRequest control:
                        Assert.That(control.ServerToken, Is.EqualTo(m_token));
                        Assert.That(control.Action, Is.EqualTo(ServiceOperationControlAction.Acknowledge));
                        Acknowledgements++;
                        AckObservedAcceptance = IsAccepted?.Invoke() ?? true;
                        ServiceOperationControlResult disposition = m_state == ServiceOperationState.Acknowledged
                            ? ServiceOperationControlResult.AlreadyApplied : ServiceOperationControlResult.Applied;
                        m_state = ServiceOperationState.Acknowledged;
                        if (LoseAcknowledgement)
                        {
                            LoseAcknowledgement = false;
                            throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                        }
                        response = new ControlServiceExecutionResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            Result = disposition,
                            Operation = Info()
                        };
                        break;
                    case TranslateBrowsePathsToNodeIdsRequest:
                        PreparationBrowses++;
                        throw new InvalidOperationException(
                            "A captured method must not fall back to another method ID.");
                    default:
                        response = channel.CreateResponse(request);
                        break;
                }
                response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
                MutateResponse?.Invoke(response);
                return new ValueTask<IServiceResponse>(response);
            }

            private ServiceOperationInfoDataType Info()
            {
                return new ServiceOperationInfoDataType
                {
                    ClientOperationId = m_clientId,
                    ServerToken = m_token,
                    ServiceId = m_serviceId,
                    State = m_state,
                    OutcomeKind = m_state == ServiceOperationState.Pending
                        ? ServiceOperationOutcome.None : ProtocolDeadline
                            ? ServiceOperationOutcome.ExecutionDeadlineExceeded
                            : ServiceOperationOutcome.ServiceResponse,
                    OutcomeStatus = m_state == ServiceOperationState.Pending ? StatusCodes.Good :
                        ProtocolDeadline ? StatusCodes.BadTimeout :
                            m_original?.ResponseHeader.ServiceResult ?? StatusCodes.Good,
                    ExecutionAllowance = m_execution,
                    ResultRetentionTime = m_retention,
                    ExecutionRemaining = m_state == ServiceOperationState.Pending ? m_execution - 1 : 0,
                    ResultRemaining = m_state == ServiceOperationState.OutcomeAvailable ? m_retention - 1 : 0,
                    HistoryRemaining = m_state == ServiceOperationState.Acknowledged ? m_retention - 1 : 0
                };
            }

            private readonly SessionChannelHarness m_channels;
            private readonly CancellationTokenSource m_timeout = new(TimeSpan.FromSeconds(15));
            private readonly ByteString m_token = ByteString.From(new byte[32]);
            private Uuid m_clientId;
            private NodeId m_serviceId;
            private double m_execution;
            private double m_retention;
            private IServiceResponse? m_original;
            private ServiceOperationState m_state;
        }
    }
}

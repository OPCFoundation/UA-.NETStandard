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
using System.Linq;
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
        public async Task FactoryRejectsMissingArgumentsBeforeBinding(string missing)
        {
            var client = new AsyncServiceClient();
            _ = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
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
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Low-level generated wrappers preserve their original values and use an explicit receipt exchange.
        /// </summary>
        [Test]
        public async Task LowLevelManagedServicesUseSeparateExchanges()
        {
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            var id = Uuid.NewUuid();
            await using RecoverableExecutionBinding binding = await RecoverableExecutionBinding.CaptureAsync(
                peer.Owner, new RecoverableSessionOptions(), peer.Timeout).ConfigureAwait(false);
            RecoverableManagerClient manager = await binding.GetManagerAsync(peer.Timeout).ConfigureAwait(false);
            SubmitResponseDataType admission = await manager.SubmitAsync(new SubmitRequestDataType
            {
                ClientOperationId = id,
                RecoveryMode = ServiceOperationRecoveryMode.Session,
                Request = new ExtensionObject(CreateReadRequest()),
                ExecutionAllowance = 10_000,
                ResultRetentionTime = 5000
            }, peer.Timeout).ConfigureAwait(false);
            ByteString token = admission.Operation.ServerToken;
            GetResultResponseDataType collection = await manager.GetResultAsync(new GetResultRequestDataType
            {
                RecoveryMode = ServiceOperationRecoveryMode.Session,
                ServerToken = token
            }, peer.Timeout).ConfigureAwait(false);
            Assert.That(collection.Operation.ClientOperationId, Is.EqualTo(id));
            Assert.That(collection.Result.TryGetValue(out ReadResponse? original), Is.True);
            Assert.That(original!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            ControlResponseDataType receipt = await manager.AcknowledgeAsync(
                new OperationControlRequestDataType { ServerToken = token }, peer.Timeout).ConfigureAwait(false);
            Assert.That(receipt.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
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
            await using RecoverableExecutionBinding binding = await RecoverableExecutionBinding.CaptureAsync(
                peer.Owner, new RecoverableSessionOptions(), peer.Timeout).ConfigureAwait(false);
            RecoverableManagerClient manager = await binding.GetManagerAsync(peer.Timeout).ConfigureAwait(false);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await manager.SubmitAsync(new SubmitRequestDataType
                {
                    ClientOperationId = Uuid.NewUuid(),
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    Request = new ExtensionObject(CreateReadRequest()),
                    ExecutionAllowance = 10_000,
                    ResultRetentionTime = 5000
                }, peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false))!;
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
            _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false);
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
            _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false);
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
            ControlResponseDataType retry =
                await operation.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(retry.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
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
            ControlResponseDataType acknowledged = await operation.AcknowledgeAsync(peer.Timeout)
                .ConfigureAwait(false);
            Assert.That(acknowledged.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
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
            _ = await Assert.ThrowsAsync<IOException>(async () =>
                await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);
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
                _ = await Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await operation.Completion.WaitAsync(peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);
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
            _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);
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
                if (response is not GetResultResponseDataType result)
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
            _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);
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
            await using ScriptedOperationServer peer =
                await ScriptedOperationServer.CreateAsync().ConfigureAwait(false);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                peer.Owner, CreateReadRequest(), peer.Timeout).ConfigureAwait(false);
            Assert.That(operation.ClientOperationId, Is.Not.EqualTo(Uuid.Empty));
            Assert.That(operation.ServerToken.IsNull, Is.True);
            Assert.That(peer.Submissions, Is.Zero);
            Assert.That(operation.Completion.IsCompleted, Is.False);

            ServiceOperationInfoDataType pending = await operation.SubmitAsync(10_000, 5000, ct: peer.Timeout)
                .ConfigureAwait(false);
            Assert.That(pending.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(peer.Submissions, Is.EqualTo(1));
            Assert.That(peer.Acknowledgements, Is.Zero);
            _ = await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ReadResponse> accepted = await operation.Completion.WaitAsync(peer.Timeout)
                .ConfigureAwait(false);
            ReadResponse value = accepted.GetValueOrThrow();
            Assert.That(value.Results[0].WrappedValue.TryGetValue(out int scalar), Is.True);
            Assert.That(scalar, Is.EqualTo(42));
            Assert.That(value.ResponseHeader.RequestHandle, Is.EqualTo(13u));
            Assert.That(peer.Submissions, Is.EqualTo(1));
            Assert.That(peer.Acknowledgements, Is.EqualTo(1));
            Assert.That(operation.LastAcknowledgement!.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
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
            private ScriptedOperationServer(RecoverableSessionPeer peer)
            {
                m_peer = peer;
                m_peer.FastResult = false;
                m_peer.MutateResponse = (_, response) =>
                {
                    if (ProtocolDeadline)
                    {
                        switch (response)
                        {
                            case SubmitResponseDataType submit:
                                SetDeadline(submit.Operation);
                                submit.Result = default;
                                break;
                            case GetResultResponseDataType get:
                                SetDeadline(get.Operation);
                                get.Result = default;
                                break;
                            case ControlResponseDataType control:
                                SetDeadline(control.Operation);
                                break;
                        }
                    }
                    MutateResponse?.Invoke(response);
                };
            }

            public Client.ManagedSession Owner => m_peer.Owner;
            public ITelemetryContext Telemetry => m_peer.Channels.Telemetry;
            public CancellationToken Timeout => m_peer.Timeout;

            public bool LoseSubmission
            {
                get => m_peer.LoseSubmitReply;
                set => m_peer.LoseSubmitReply = value;
            }

            public bool LoseAcknowledgement
            {
                get => m_peer.LoseAckReply;
                set => m_peer.LoseAckReply = value;
            }

            public bool FastResult
            {
                get => m_peer.FastResult;
                set => m_peer.FastResult = value;
            }

            public bool InvalidMethodOutput
            {
                get => m_peer.InvalidMethodOutput;
                set => m_peer.InvalidMethodOutput = value;
            }

            public StatusCode MethodStatus
            {
                get => m_peer.MethodStatus;
                set => m_peer.MethodStatus = value;
            }

            public StatusCode OriginalFailure
            {
                get => m_peer.OriginalFailure;
                set => m_peer.OriginalFailure = value;
            }

            public bool ProtocolDeadline { get; set; }
            public Action<IEncodeable>? MutateResponse { get; set; }

            public Func<bool>? IsAccepted
            {
                get => m_peer.CallerHasCompleted;
                set => m_peer.CallerHasCompleted = value;
            }

            public bool AckObservedAcceptance => m_peer.AckObservedCallerCompletion;

            public bool LastLookupUsedClientId => m_peer.Collections.TryPeek(out GetResultRequestDataType? get) &&
                get.ClientOperationId != Uuid.Empty;

            public int Submissions => m_peer.Submissions.Count;
            public int Acknowledgements => m_peer.Controls.Count;

            public int PreparationBrowses =>
                m_peer.OrdinaryRequests.OfType<TranslateBrowsePathsToNodeIdsRequest>().Count();

            public static async Task<ScriptedOperationServer> CreateAsync()
            {
                return new ScriptedOperationServer(
                    await RecoverableSessionPeer.CreateAsync(enabled: false).ConfigureAwait(false));
            }

            public ValueTask RecoverAsync()
            {
                return m_peer.RecoverAsync();
            }

            public ValueTask DisposeAsync()
            {
                return m_peer.DisposeAsync();
            }

            private static void SetDeadline(ServiceOperationInfoDataType info)
            {
                info.OutcomeKind = ServiceOperationOutcome.ExecutionDeadlineExceeded;
                info.OutcomeStatus = StatusCodes.BadTimeout;
            }

            private readonly RecoverableSessionPeer m_peer;
        }
    }
}

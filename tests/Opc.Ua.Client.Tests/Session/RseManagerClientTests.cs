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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

namespace Opc.Ua.Client.Tests.SessionContexts
{
    /// <summary>
    /// Verifies the five-Method Client binding independently of any production Server implementation.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RseManagerClientTests
    {
        [TestCase((ushort)2)]
        [TestCase((ushort)4)]
        [TestCase((ushort)9)]
        public async Task DynamicNamespaceUsesActualMethodInstancesAndFreshOuterHandles(ushort namespaceIndex)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(
                namespaceIndex: namespaceIndex).ConfigureAwait(false);
            peer.FastResult = false;
            var factory = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await factory.CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            operation.RequireExplicitAcknowledgement();
            _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            _ = await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            _ = await operation.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);

            CallRequest[] calls = [.. peer.ManagerCalls];
            Assert.That(calls, Has.Length.EqualTo(3));
            Assert.That(calls.Select(call => call.MethodsToCall[0].ObjectId), Is.All.EqualTo(peer.ManagerId));
            Assert.That(calls.Select(call => call.MethodsToCall[0].MethodId),
                Is.EqualTo(peer.MethodIds.ToList().Take(3)));
            Assert.That(calls.Select(call => call.RequestHeader.RequestHandle).Distinct().Count(), Is.EqualTo(3));
            Assert.That(calls.Select(call => call.RequestHeader.RequestHandle), Has.None.EqualTo(37u));
            Assert.That(calls[0].RequestHeader, Is.Not.SameAs(calls[1].RequestHeader));
            Assert.That(peer.Session.NamespaceUris.GetIndex(RseNamespaces.Rse), Is.EqualTo(namespaceIndex));
            Assert.That(peer.Submissions.Single().RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Session));
            Assert.That(peer.Collections.Single().RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Session));
            Assert.That(RecoverableSessionPeer.Original(peer.Submissions.Single()).RequestHeader.RequestHandle,
                Is.EqualTo(37u));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase("Read")]
        [TestCase("Write")]
        [TestCase("Call")]
        [TestCase("HistoryRead")]
        [TestCase("HistoryUpdate")]
        [TestCase("Browse")]
        [TestCase("BrowseNext")]
        [TestCase("TranslateBrowsePathsToNodeIds")]
        [TestCase("RegisterNodes")]
        [TestCase("UnregisterNodes")]
        [TestCase("AddNodes")]
        [TestCase("AddReferences")]
        [TestCase("DeleteNodes")]
        [TestCase("DeleteReferences")]
        public async Task AdvertisedOriginalPairUsesOneSubmissionAndOneReceipt(string service)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            await using RecoverableAcknowledgementScope scope =
                peer.Session.BeginAcknowledgementScope(requireRecoverableReceipt: true);
            IServiceResponse response = await InvokeOriginalAsync(peer.Session, service, peer.Timeout)
                .ConfigureAwait(false);

            Assert.That(response.GetType().Name, Is.EqualTo(service + "Response"));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(37u));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            IServiceRequest original = RecoverableSessionPeer.Original(peer.Submissions.Single());
            Assert.That(original.GetType().Name, Is.EqualTo(service + "Request"));
            Assert.That(original.RequestHeader.RequestHandle, Is.EqualTo(37u));
            Assert.That(scope.Receipt?.IsFullyRecoverable, Is.True);
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.True);
            Assert.That(peer.Controls.Single().MethodName, Is.EqualTo("Acknowledge"));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase("Read")]
        [TestCase("Write")]
        [TestCase("Call")]
        [TestCase("HistoryRead")]
        [TestCase("HistoryUpdate")]
        [TestCase("Browse")]
        [TestCase("BrowseNext")]
        [TestCase("TranslateBrowsePathsToNodeIds")]
        [TestCase("RegisterNodes")]
        [TestCase("AddNodes")]
        [TestCase("AddReferences")]
        [TestCase("DeleteNodes")]
        [TestCase("DeleteReferences")]
        public async Task MalformedOriginalResultCountNeverAcceptsOrAcknowledges(string service)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.MutateResponse = (_, response) =>
            {
                if (response is SubmitResponseDataType submit &&
                    submit.Result.TryGetValue(out IServiceResponse? original))
                {
                    RemoveOriginalResults(original);
                }
            };
            int accepted = 0;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                acceptAsync: (_, _) =>
                {
                    accepted++;
                    return default;
                });
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeOriginalAsync(peer.Session, service, peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(accepted, Is.Zero);
            Assert.That(scope.Receipt, Is.Null);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        /// <summary>
        /// Continuation release retains the empty original arrays required by the ordinary BrowseNext Service.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BrowseNextReleaseAcceptsEmptyOutcomeWithoutReplay(bool fast)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.FastResult = fast;
            ArrayOf<ByteString> points = [ByteString.From("first"u8), ByteString.From("second"u8)];
            await using RecoverableAcknowledgementScope scope =
                peer.Session.BeginAcknowledgementScope(requireRecoverableReceipt: true);

            BrowseNextResponse response = await peer.Session.BrowseNextAsync(
                new RequestHeader { RequestHandle = 37 }, true, points, peer.Timeout).ConfigureAwait(false);

            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(37u));
            Assert.That(response.Results, Is.Empty);
            Assert.That(response.DiagnosticInfos, Is.Empty);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            var original = (BrowseNextRequest)RecoverableSessionPeer.Original(peer.Submissions.Single());
            Assert.That(original.ReleaseContinuationPoints, Is.True);
            Assert.That(original.ContinuationPoints, Is.EqualTo(points));
            Assert.That(peer.Collections, Has.Count.EqualTo(fast ? 0 : 1));
            Assert.That(scope.Receipt?.IsFullyRecoverable, Is.True);
            Assert.That(peer.Controls, Is.Empty);
            await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(scope.Receipt!.IsAcknowledged, Is.True);
            Assert.That(peer.Controls.Single().MethodName, Is.EqualTo("Acknowledge"));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        /// <summary>
        /// Neither per-point results nor diagnostics are valid in a successful BrowseNext release response.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task BrowseNextReleaseRejectsUnexpectedResultsOrDiagnostics(bool results, bool diagnostics)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            peer.MutateResponse = (_, response) =>
            {
                if (response is SubmitResponseDataType submit &&
                    submit.Result.TryGetValue(out BrowseNextResponse? original))
                {
                    original.Results = results ? [new BrowseResult()] : [];
                    original.DiagnosticInfos = diagnostics ? [new DiagnosticInfo()] : [];
                }
            };
            int accepted = 0;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true, acceptAsync: (_, _) =>
                {
                    accepted++;
                    return default;
                });

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peer.Session.BrowseNextAsync(new RequestHeader { RequestHandle = 37 },
                    true, [ByteString.From("cursor"u8)], peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(accepted, Is.Zero);
            Assert.That(scope.Receipt, Is.Null);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase("QueryFirst")]
        [TestCase("QueryNext")]
        public async Task UnadvertisedQueryKeepsOrdinaryUnsupportedBehaviorWithoutSubmitting(string service)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeOriginalAsync(peer.Session, service, peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests.Single().GetType().Name, Is.EqualTo(service + "Request"));
        }

        [TestCase("CreateSession")]
        [TestCase("ActivateSession")]
        [TestCase("CloseSession")]
        [TestCase("Cancel")]
        [TestCase("CreateSubscription")]
        [TestCase("ModifySubscription")]
        [TestCase("SetPublishingMode")]
        [TestCase("TransferSubscriptions")]
        [TestCase("DeleteSubscriptions")]
        [TestCase("CreateMonitoredItems")]
        [TestCase("ModifyMonitoredItems")]
        [TestCase("SetMonitoringMode")]
        [TestCase("SetTriggering")]
        [TestCase("DeleteMonitoredItems")]
        [TestCase("Publish")]
        [TestCase("Republish")]
        public async Task ExcludedServicesKeepTheirOrdinaryDispatchPath(string service)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync().ConfigureAwait(false);
            ScriptedChannel channel = peer.Channels.CreatedChannels[0];
            Func<IServiceRequest, CancellationToken, ValueTask<IServiceResponse>> handler = channel.RequestHandler!;
            int ordinary = 0;
            channel.RequestHandler = (request, ct) =>
            {
                if (request.GetType().Name != service + "Request")
                {
                    return handler(request, ct);
                }
                ordinary++;
                return new ValueTask<IServiceResponse>(new ServiceFault
                {
                    ResponseHeader = new ResponseHeader
                    {
                        RequestHandle = request.RequestHeader.RequestHandle,
                        ServiceResult = StatusCodes.BadServiceUnsupported
                    }
                });
            };
            try
            {
                ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await InvokeExcludedAsync(peer.Session, service, peer.Timeout).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
                Assert.That(ordinary, Is.EqualTo(1));
                Assert.That(peer.Submissions, Is.Empty);
                Assert.That(peer.ManagerCalls, Is.Empty);
                Assert.That(peer.DiscoveryRequests, Is.Empty);
                Assert.That(peer.Controls, Is.Empty);
            }
            finally
            {
                channel.RequestHandler = handler;
            }
        }

        [TestCase("null-response")]
        [TestCase("null-header")]
        [TestCase("wrong-handle")]
        [TestCase("outer-fault")]
        [TestCase("uncertain-outer")]
        [TestCase("missing-results")]
        [TestCase("extra-results")]
        [TestCase("null-result")]
        [TestCase("method-fault")]
        [TestCase("method-id-invalid")]
        [TestCase("uncertain-method")]
        [TestCase("missing-output")]
        [TestCase("extra-output")]
        [TestCase("null-output")]
        [TestCase("wrong-scalar")]
        [TestCase("array-output")]
        [TestCase("wrong-response-type")]
        [TestCase("null-metadata")]
        [TestCase("bad-input-status")]
        [TestCase("extra-input-status")]
        public async Task FailedOrMalformedSubmitNeverReplaysAndRemainsCollectable(string fault)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(enabled: false)
                .ConfigureAwait(false);
            var factory = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await factory.CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            operation.RequireExplicitAcknowledgement();
            peer.NullManagerResponse = fault == "null-response";
            peer.MutateManagerResponse = (_, response) => MutateCall(response, fault);

            _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(operation.Completion.IsCompleted, Is.False);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.ManagerCalls, Has.Count.EqualTo(1));
            Assert.That(peer.DiscoveryRequests, Has.Count.EqualTo(4), "No MethodId fallback may run after Submit.");
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);

            peer.NullManagerResponse = false;
            peer.MutateManagerResponse = null;
            _ = await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ReadResponse> recovered = await operation.Completion.WaitAsync(peer.Timeout)
                .ConfigureAwait(false);
            Assert.That(recovered.GetValueOrThrow().Results[0].WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Has.Count.EqualTo(1));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task TypedProjectionReplacementExceptionCannotBorrowOriginalFailureProvenance()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(enabled: false)
                .ConfigureAwait(false);
            peer.MethodStatus = StatusCodes.BadMethodInvalid;
            var replacement = new ServiceResultException(StatusCodes.BadMethodInvalid, "local projection failure");
            var factory = new AsyncServiceClient();
            await using AsyncServiceOperation<ByteString> operation = await factory.CreateMethodAsync(
                peer.Session, async (client, ct) =>
                {
                    try
                    {
                        return await new ServerConfigurationTypeClient(
                            client, ObjectIds.ServerConfiguration, peer.Channels.Telemetry).CreateSigningRequestAsync(
                                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                                ObjectTypeIds.RsaSha256ApplicationCertificateType, "CN=Synthetic", false, default, ct)
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        throw replacement;
                    }
                }, peer.Timeout).ConfigureAwait(false);

            ServiceResultException actual = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(actual, Is.SameAs(replacement));
            Assert.That(operation.Completion.IsCompleted, Is.False);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task AcceptedOriginalSnapshotCannotBeChangedByItsCaller()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(enabled: false)
                .ConfigureAwait(false);
            var factory = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await factory.CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            operation.RequireExplicitAcknowledgement();
            _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ReadResponse> accepted = await operation.Completion.WaitAsync(peer.Timeout)
                .ConfigureAwait(false);
            accepted.GetValueOrThrow().Results = [new DataValue(Variant.From(999))];
            Assert.That(accepted.OriginalResponse, Is.InstanceOf<ReadResponse>());
            var snapshot = (ReadResponse)accepted.OriginalResponse!;
            snapshot.Results = [];
            _ = await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            var unchanged = (ReadResponse)accepted.OriginalResponse!;
            Assert.That(unchanged.Results[0].WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task AcceptanceTimeoutExceptionIsNotTreatedAsAnExchangeFailure()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(enabled: false)
                .ConfigureAwait(false);
            var failure = new ServiceResultException(
                StatusCodes.BadTimeout, "Synthetic application acceptance timeout.");
            await using AsyncServiceOperation<ReadResponse> operation = await new AsyncServiceClient().CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            operation.WithAcceptancePolicy((_, _) => throw failure);

            ServiceResultException actual = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.ExecuteAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(operation.Completion.IsCompleted, Is.False);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task CancelAndAbandonUseDistinctTokenOnlyMethodsWithoutAcceptingAnOutcome()
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(enabled: false)
                .ConfigureAwait(false);
            peer.FastResult = false;
            await using AsyncServiceOperation<ReadResponse> operation = await new AsyncServiceClient().CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            ControlResponseDataType cancelled = await operation.CancelAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(cancelled.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(cancelled.Operation.CancellationRequested, Is.True);
            Assert.That(operation.Completion.IsCompleted, Is.False);
            ControlResponseDataType cancelledAgain = await operation.CancelAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(cancelledAgain.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            ControlResponseDataType abandoned = await operation.AbandonAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(abandoned.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(abandoned.Operation.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(operation.Completion.IsCompleted, Is.False);
            Assert.That(peer.Controls.Select(control => control.MethodName),
                Is.EqualTo(s_controlMethods));
            Assert.That(peer.Controls.Select(control => control.Request.ServerToken),
                Is.All.EqualTo(operation.ServerToken));
            Assert.That(peer.ManagerCalls.Select(call => call.MethodsToCall[0].MethodId),
                Is.EqualTo([peer.MethodIds[0], peer.MethodIds[3], peer.MethodIds[3], peer.MethodIds[4]]));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(64, true)]
        [TestCase(65, false)]
        public async Task ServerTokenLengthIsValidatedBeforeAcceptance(int length, bool valid)
        {
            await using RecoverableSessionPeer peer = await RecoverableSessionPeer.CreateAsync(enabled: false)
                .ConfigureAwait(false);
            peer.MutateResponse = (_, response) =>
            {
                if (response is SubmitResponseDataType submit)
                {
                    submit.Operation.ServerToken = ByteString.From(new byte[length]);
                }
            };
            await using AsyncServiceOperation<ReadResponse> operation = await new AsyncServiceClient().CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            operation.RequireExplicitAcknowledgement();
            if (valid)
            {
                _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
                Assert.That(operation.ServerToken.Length, Is.EqualTo(length));
                Assert.That(operation.Completion.IsCompletedSuccessfully, Is.True);
            }
            else
            {
                ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                Assert.That(operation.Completion.IsCompleted, Is.False);
            }
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(ServiceOperationRecoveryMode.Unspecified)]
        [TestCase((ServiceOperationRecoveryMode)9)]
        public void InvalidRecoveryModeFailsBeforeAcquisition(ServiceOperationRecoveryMode mode)
        {
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                new AsyncServiceClient(new RecoverableSessionOptions { RecoveryMode = mode }))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase(ServiceOperationRecoveryMode.Owner)]
        [TestCase(ServiceOperationRecoveryMode.Durable)]
        public void ExplicitOwnerModeRequiresIdentityMappingInsteadOfDowngrade(ServiceOperationRecoveryMode mode)
        {
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                new AsyncServiceClient(new RecoverableSessionOptions { RecoveryMode = mode }))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        private static void MutateCall(CallResponse response, string fault)
        {
            CallMethodResult item = response.Results[0];
            switch (fault)
            {
                case "null-header":
                    response.ResponseHeader = null!;
                    break;
                case "wrong-handle":
                    response.ResponseHeader.RequestHandle++;
                    break;
                case "outer-fault":
                    response.ResponseHeader.ServiceResult = StatusCodes.BadUserAccessDenied;
                    break;
                case "uncertain-outer":
                    response.ResponseHeader.ServiceResult = StatusCodes.Uncertain;
                    break;
                case "missing-results":
                    response.Results = [];
                    break;
                case "extra-results":
                    response.Results = [item, CoreUtils.Clone(item)!];
                    break;
                case "null-result":
                    response.Results = [null!];
                    break;
                case "method-fault":
                    item.StatusCode = StatusCodes.BadServiceUnsupported;
                    break;
                case "method-id-invalid":
                    item.StatusCode = StatusCodes.BadMethodInvalid;
                    break;
                case "uncertain-method":
                    item.StatusCode = StatusCodes.Uncertain;
                    break;
                case "missing-output":
                    item.OutputArguments = [];
                    break;
                case "extra-output":
                    item.OutputArguments = [item.OutputArguments[0], item.OutputArguments[0]];
                    break;
                case "null-output":
                    item.OutputArguments = [Variant.Null];
                    break;
                case "wrong-scalar":
                    item.OutputArguments = [Variant.From(3)];
                    break;
                case "array-output":
                    item.OutputArguments =
                        [Variant.From(ArrayOf.Wrapped(new ExtensionObject(new SubmitResponseDataType())))];
                    break;
                case "wrong-response-type":
                    item.OutputArguments = [Variant.From(new ExtensionObject(new GetResultResponseDataType()))];
                    break;
                case "null-metadata":
                    item.OutputArguments =
                        [Variant.From(new ExtensionObject(new SubmitResponseDataType { Operation = null! }))];
                    break;
                case "bad-input-status":
                    item.InputArgumentResults = [StatusCodes.BadTypeMismatch];
                    break;
                case "extra-input-status":
                    item.InputArgumentResults = [StatusCodes.Good, StatusCodes.Good];
                    break;
            }
        }

        private static void RemoveOriginalResults(IServiceResponse response)
        {
            switch (response)
            {
                case ReadResponse value:
                    value.Results = [];
                    break;
                case WriteResponse value:
                    value.Results = [];
                    break;
                case CallResponse value:
                    value.Results = [];
                    break;
                case HistoryReadResponse value:
                    value.Results = [];
                    break;
                case HistoryUpdateResponse value:
                    value.Results = [];
                    break;
                case BrowseResponse value:
                    value.Results = [];
                    break;
                case BrowseNextResponse value:
                    value.Results = [];
                    break;
                case TranslateBrowsePathsToNodeIdsResponse value:
                    value.Results = [];
                    break;
                case RegisterNodesResponse value:
                    value.RegisteredNodeIds = [];
                    break;
                case AddNodesResponse value:
                    value.Results = [];
                    break;
                case AddReferencesResponse value:
                    value.Results = [];
                    break;
                case DeleteNodesResponse value:
                    value.Results = [];
                    break;
                case DeleteReferencesResponse value:
                    value.Results = [];
                    break;
                default:
                    throw new ArgumentException(
                        "The selected response has no per-request result array.", nameof(response));
            }
        }

        private static ReadRequest ReadRequest()
        {
            return new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 37 },
                TimestampsToReturn = TimestampsToReturn.Neither,
                NodesToRead = [new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value }]
            };
        }

        private static async ValueTask<IServiceResponse> InvokeOriginalAsync(
            ISession session, string service, CancellationToken ct)
        {
            var header = new RequestHeader { RequestHandle = 37 };
            return service switch
            {
                "Read" => await session.ReadAsync(header, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value }], ct)
                    .ConfigureAwait(false),
                "Write" => await session.WriteAsync(header,
                    [new WriteValue
                    {
                        NodeId = new NodeId(100u), AttributeId = Attributes.Value, Value = new(Variant.From(3))
                    }],
                    ct).ConfigureAwait(false),
                "Call" => await session.CallAsync(header,
                    [new CallMethodRequest { ObjectId = new NodeId(100u), MethodId = new NodeId(200u) }], ct)
                    .ConfigureAwait(false),
                "HistoryRead" => await session.HistoryReadAsync(
                    header, new ExtensionObject(new ReadRawModifiedDetails()), TimestampsToReturn.Neither, false,
                    [new HistoryReadValueId { NodeId = new NodeId(100u) }], ct)
                    .ConfigureAwait(false),
                "HistoryUpdate" => await session.HistoryUpdateAsync(header,
                    [new ExtensionObject(new UpdateDataDetails { NodeId = new NodeId(100u) })], ct)
                    .ConfigureAwait(false),
                "Browse" => await session.BrowseAsync(header, null, 10,
                    [new BrowseDescription { NodeId = new NodeId(100u) }], ct).ConfigureAwait(false),
                "BrowseNext" => await session.BrowseNextAsync(header, false, [ByteString.From("cursor"u8)], ct)
                    .ConfigureAwait(false),
                "TranslateBrowsePathsToNodeIds" => await session.TranslateBrowsePathsToNodeIdsAsync(header,
                    [new BrowsePath { StartingNode = new NodeId(100u) }], ct).ConfigureAwait(false),
                "RegisterNodes" => await session.RegisterNodesAsync(header, [new NodeId(100u)], ct)
                    .ConfigureAwait(false),
                "UnregisterNodes" => await session.UnregisterNodesAsync(header, [new NodeId(100u)], ct)
                    .ConfigureAwait(false),
                "QueryFirst" => await session.QueryFirstAsync(header, null, [], null, 10, 10, ct).ConfigureAwait(false),
                "QueryNext" => await session.QueryNextAsync(header, false, ByteString.From("cursor"u8), ct)
                    .ConfigureAwait(false),
                "AddNodes" => await session.AddNodesAsync(header, [new AddNodesItem()], ct).ConfigureAwait(false),
                "AddReferences" => await session.AddReferencesAsync(header, [new AddReferencesItem()], ct)
                    .ConfigureAwait(false),
                "DeleteNodes" => await session.DeleteNodesAsync(header, [new DeleteNodesItem()], ct)
                    .ConfigureAwait(false),
                "DeleteReferences" => await session.DeleteReferencesAsync(header, [new DeleteReferencesItem()], ct)
                    .ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(service))
            };
        }

        private static async ValueTask<IServiceResponse> InvokeExcludedAsync(
            ISession session, string service, CancellationToken ct)
        {
            return service switch
            {
                "CreateSession" => await session.CreateSessionAsync(null, new ApplicationDescription(), null,
                    "opc.tcp://synthetic.invalid:4840", "synthetic", default, default, 60000, 1_048_576, ct)
                    .ConfigureAwait(false),
                "ActivateSession" => await session.ActivateSessionAsync(null, new SignatureData(), [], [],
                    new ExtensionObject(new AnonymousIdentityToken()), new SignatureData(), ct).ConfigureAwait(false),
                "CloseSession" => await session.CloseSessionAsync(null, true, ct).ConfigureAwait(false),
                "Cancel" => await session.CancelAsync(null, 37, ct).ConfigureAwait(false),
                "CreateSubscription" => await session.CreateSubscriptionAsync(null, 1000, 10, 3, 0, true, 0, ct)
                    .ConfigureAwait(false),
                "ModifySubscription" => await session.ModifySubscriptionAsync(null, 1, 1000, 10, 3, 0, 0, ct)
                    .ConfigureAwait(false),
                "SetPublishingMode" => await session.SetPublishingModeAsync(null, true, [1u], ct).ConfigureAwait(false),
                "TransferSubscriptions" => await session.TransferSubscriptionsAsync(null, [1u], false, ct)
                    .ConfigureAwait(false),
                "DeleteSubscriptions" => await session.DeleteSubscriptionsAsync(null, [1u], ct).ConfigureAwait(false),
                "CreateMonitoredItems" => await session.CreateMonitoredItemsAsync(
                    null, 1, TimestampsToReturn.Neither, [], ct).ConfigureAwait(false),
                "ModifyMonitoredItems" => await session.ModifyMonitoredItemsAsync(
                    null, 1, TimestampsToReturn.Neither, [], ct).ConfigureAwait(false),
                "SetMonitoringMode" => await session.SetMonitoringModeAsync(null, 1, MonitoringMode.Reporting, [1u], ct)
                    .ConfigureAwait(false),
                "SetTriggering" => await session.SetTriggeringAsync(null, 1, 1, [2u], [], ct).ConfigureAwait(false),
                "DeleteMonitoredItems" => await session.DeleteMonitoredItemsAsync(null, 1, [1u], ct)
                    .ConfigureAwait(false),
                "Publish" => await session.PublishAsync(null, [], ct).ConfigureAwait(false),
                "Republish" => await session.RepublishAsync(null, 1, 1, ct).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(service))
            };
        }

        private static readonly string[] s_controlMethods = ["Cancel", "Cancel", "Abandon"];
    }
}

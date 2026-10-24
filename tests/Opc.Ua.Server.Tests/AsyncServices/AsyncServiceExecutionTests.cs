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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.TestModels.AsyncExecution;
using Model = Opc.Ua.TestModels.AsyncExecution;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Exercises the generated endpoint and ordinary Server handlers with independently owned recoverable work.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class AsyncServiceExecutionTests
    {
        /// <summary>
        /// Hosting remains default-off until explicit activation and resolves the existing provider policy through DI.
        /// </summary>
        [Test]
        public async Task HostingActivationIsExplicitAndInjectable()
        {
            Assert.That(new AsyncServiceExecutionOptions().Enabled, Is.False);
            var services = new ServiceCollection();
            services.AddSingleton<IAsyncServiceAuthorizationPolicy>(new RevocablePolicy());
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
                options.AsyncServices.Enabled = false);
            Assert.That(builder.WithExperimentalAsyncServices(options => options.MaxExecutionAllowance = 60_000),
                Is.SameAs(builder));
            await using ServiceProvider provider = services.BuildServiceProvider();
            AsyncServiceExecutionOptions configured = provider.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.AsyncServices;
            Assert.That(configured.Enabled, Is.True);
            Assert.That(configured.MaxExecutionAllowance, Is.EqualTo(60_000));
            Assert.That(provider.GetRequiredService<IAsyncServiceExecutionProviderFactory>(),
                Is.InstanceOf<DefaultAsyncServiceExecutionProviderFactory>());
            _ = Assert.Throws<ArgumentNullException>(() =>
                OpcUaServerBuilderExtensions.WithExperimentalAsyncServices(null!));
        }

        /// <summary>
        /// A fast Read retains its complete original response until an explicit acknowledged release.
        /// </summary>
        [Test]
        public async Task FastReadRetainsOriginalOutcomeUntilAcknowledged()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            SubmitRequestDataType request = ExecutionTestContext.ReadRequest(wait: 5000);
            Assert.That(request.Request.TryGetValue(out ReadRequest? read), Is.True);
            read!.RequestHeader.RequestHandle = 13;
            RequestHeader header = test.NewHeader();
            header.RequestHandle = 29;
            using ManagerReply<SubmitResponseDataType> submission =
                await test.SubmitAsync(request, header).ConfigureAwait(false);
            Assert.That(submission.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(submission.Output.Operation.RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Session));
            Assert.That(submission.Output.Operation.ServerToken.Length, Is.EqualTo(32));
            Assert.That(submission.Response.ResponseHeader.RequestHandle, Is.EqualTo(29u));
            Assert.That(submission.Output.Result.TryGetValue(
                out ReadResponse? original, test.Server.MessageContext), Is.True);
            Assert.That(original!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(original.ResponseHeader.RequestHandle, Is.EqualTo(13u));
            Assert.That(original.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            ByteString token = submission.Output.Operation.ServerToken;
            using ManagerReply<ControlResponseDataType> acknowledgement = await test.ControlAsync(
                token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(acknowledgement.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(acknowledgement.Output.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
            using ManagerReply<GetResultResponseDataType> released = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(released.Output.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
            Assert.That(released.Output.Result.IsNull, Is.True);
            Assert.That(original.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good),
                "Another collector's ACK must not invalidate an in-flight response.");
        }

        /// <summary>
        /// A deadline is immutable; acknowledged/forgotten work remains charged until its actual invocation ends.
        /// </summary>
        [Test]
        public async Task DeadlineAndHistoryDoNotReleaseStillRunningWork()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            SubmitRequestDataType request = ExecutionTestContext.ReadRequest(execution: 100, retention: 200);
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(request).ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            Assert.That(admitted.Output.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            admitted.Dispose();
            DateTime deadline = await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            Assert.That(deadline, Is.EqualTo(DateTime.MaxValue),
                "The original timeoutHint must not impose a second owned-work deadline.");

            test.Clock.Advance(TimeSpan.FromMilliseconds(100));
            await test.Server.CancellationRequested.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            using ManagerReply<GetResultResponseDataType> expiredExecution =
                await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(expiredExecution.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(expiredExecution.Output.Operation.OutcomeKind,
                Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(expiredExecution.Output.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(expiredExecution.Output.Result.IsNull, Is.True);
            expiredExecution.Dispose();

            using ManagerReply<ControlResponseDataType> acknowledged = await test.ControlAsync(
                token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(acknowledged.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            acknowledged.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(200));
            using ManagerReply<GetResultResponseDataType> forgotten = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(forgotten.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            forgotten.Dispose();
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.GreaterThanOrEqualTo(1));

            SubmitRequestDataType duplicate = ExecutionTestContext.ReadRequest();
            duplicate.ClientOperationId = request.ClientOperationId;
            await test.RejectAsync(RseBrowseNames.Submit, duplicate, StatusCodes.BadInvalidState).ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));

            await test.ReleaseWorkAsync().ConfigureAwait(false);
            using ManagerReply<GetResultResponseDataType> afterLateCompletion =
                await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(afterLateCompletion.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            Assert.That(afterLateCompletion.Output.Result.IsNull, Is.True);
        }

        /// <summary>
        /// A late handler completion cannot replace a deadline outcome that is still available for collection.
        /// </summary>
        [Test]
        public async Task LateCompletionCannotReplaceAvailableDeadlineOutcome()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admission = await test.SubmitAsync(
                ExecutionTestContext.ReadRequest(execution: 100)).ConfigureAwait(false);
            ByteString token = admission.Output.Operation.ServerToken;
            admission.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(100));
            using ManagerReply<GetResultResponseDataType> deadline = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(deadline.Output.Operation.OutcomeKind,
                Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            deadline.Dispose();
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(1000));
            using ManagerReply<GetResultResponseDataType> late = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(late.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(late.Output.Operation.OutcomeKind,
                Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(late.Output.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(late.Output.Operation.ResultRemaining, Is.EqualTo(4000));
            Assert.That(late.Output.Result.IsNull, Is.True);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().ExecutionDeadlineCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Existing per-owner ceilings bound retained work while completed exchange capacity remains usable for
        /// recovery.
        /// </summary>
        [TestCase(ResourceIsolationStage.RequestExecution, false)]
        [TestCase(ResourceIsolationStage.ParkedRequest, false)]
        [TestCase(ResourceIsolationStage.RequestQueueBytes, false)]
        [TestCase(ResourceIsolationStage.RequestExecution, true)]
        [TestCase(ResourceIsolationStage.ParkedRequest, true)]
        [TestCase(ResourceIsolationStage.RequestQueueBytes, true)]
        public async Task FullOperationQuotaStillPermitsRecoveryAndCleanup(
            ResourceIsolationStage stage, bool aggregate)
        {
            await using ExecutionTestContext test = await ExecutionTestContext.CreateAsync(
                delayRead: true, ownerBoundStage: stage, aggregateBound: aggregate)
                .ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admission =
                await test.SubmitAsync(ExecutionTestContext.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = admission.Output.Operation.ServerToken;
            admission.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            // Original input + captured permission record + outcome + the 1024-byte history reservation.
            long retained = stage == ResourceIsolationStage.RequestQueueBytes ? (3 * 65_536) + 1024 : 1;
            await test.WaitForUsageAsync(stage, retained).ConfigureAwait(false);

            await test.RejectAsync(
                RseBrowseNames.Submit, ExecutionTestContext.ReadRequest(), StatusCodes.BadResourceUnavailable)
                .ConfigureAwait(false);
            await test.WaitForUsageAsync(stage, retained).ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            using ManagerReply<GetResultResponseDataType> pending = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(pending.Output.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            pending.Dispose();
            await test.WaitForUsageAsync(stage, retained).ConfigureAwait(false);
            using ManagerReply<ControlResponseDataType> cancelled = await test.ControlAsync(
                token, RseBrowseNames.Cancel).ConfigureAwait(false);
            Assert.That(cancelled.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            cancelled.Dispose();
            await test.WaitForUsageAsync(stage, retained).ConfigureAwait(false);
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.GreaterThanOrEqualTo(1));
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            using ManagerReply<GetResultResponseDataType> result = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(result.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            result.Dispose();
            await test.WaitForUsageAsync(stage, stage == ResourceIsolationStage.RequestExecution ? 0 : retained)
                .ConfigureAwait(false);
            using ManagerReply<ControlResponseDataType> acknowledged = await test.ControlAsync(
                token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(acknowledged.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            acknowledged.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(5000));
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
            await test.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 0).ConfigureAwait(false);
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
            AsyncServiceExecutionStatistics statistics = test.Server.ExecutionFactory.Provider!.GetStatistics();
            Assert.That(statistics.ActiveOperationCount, Is.Zero);
            Assert.That(statistics.RetainedOutcomeBytes, Is.Zero);
            Assert.That(statistics.TerminalRecordCount, Is.Zero);
        }

        /// <summary>
        /// Pending collectors consume the existing parked-request quota and share one retained outcome.
        /// </summary>
        [Test]
        public async Task PendingCollectorsAreBoundedByExistingRequestQuota()
        {
            var policy = new RevocablePolicy();
            await using ExecutionTestContext test = await ExecutionTestContext.CreateAsync(
                delayRead: true, policy: policy, ownerBoundStage: ResourceIsolationStage.ParkedRequest)
                .ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(ExecutionTestContext.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            await test.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 1).ConfigureAwait(false);
            policy.CollectionEntered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ManagerReply<GetResultResponseDataType>> waiting = test.GetAsync(
                new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ServerToken = token,
                    WaitTime = 5000
                }).AsTask();
            ManagerReply<GetResultResponseDataType>? held = null;
            try
            {
                await policy.CollectionEntered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
                Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.EqualTo(2));
                IServiceResponse refused = await test.SendAsync<IServiceResponse>(
                    test.Call(RseBrowseNames.GetResult, new GetResultRequestDataType
                    {
                        RecoveryMode = ServiceOperationRecoveryMode.Session,
                        ServerToken = token,
                        WaitTime = 5000
                    })).ConfigureAwait(false);
                Assert.That(refused, Is.InstanceOf<ServiceFault>());
                Assert.That(refused.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServerTooBusy));
                (refused as IPooledEncodeable)?.Reuse();
                Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.EqualTo(2));
                test.Server.Release();
                held = await waiting.ConfigureAwait(false);
                Assert.That(held.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                using ManagerReply<ControlResponseDataType> receipt = await test.ControlAsync(
                    token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
                Assert.That(receipt.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                receipt.Dispose();
                Assert.That(held.Output.Result.TryGetValue(out ReadResponse? original), Is.True);
                Assert.That(original!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            }
            finally
            {
                test.Server.Release();
                held ??= await waiting.ConfigureAwait(false);
                held.Dispose();
            }
        }

        /// <summary>
        /// Ordinary Cancel affects the submission wait, even when the enclosed request has the same requestHandle.
        /// </summary>
        [Test]
        public async Task ExchangeCancellationDoesNotCancelAdmittedWork()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            SubmitRequestDataType request = ExecutionTestContext.ReadRequest(wait: 5000);
            Assert.That(request.Request.TryGetValue(out ReadRequest? original), Is.True);
            CallRequest call = test.Call(RseBrowseNames.Submit, request);
            original!.RequestHeader.RequestHandle = call.RequestHeader.RequestHandle;
            Task<IServiceResponse> submission = test.SendAsync<IServiceResponse>(call).AsTask();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(1));
            CancelResponse cancelled = await test.SendAsync<CancelResponse>(new CancelRequest
            {
                RequestHeader = test.NewHeader(),
                RequestHandle = call.RequestHeader.RequestHandle
            }).ConfigureAwait(false);
            Assert.That(cancelled.CancelCount, Is.EqualTo(1));
            cancelled.Reuse();
            IServiceResponse wait = await submission.ConfigureAwait(false);
            Assert.That(wait, Is.InstanceOf<ServiceFault>());
            Assert.That(wait.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            (wait as IPooledEncodeable)?.Reuse();
            Assert.That(test.Server.CancellationRequested.Task.IsCompleted, Is.False);

            test.Server.Release();
            using ManagerReply<GetResultResponseDataType> result = await test.GetAsync(new GetResultRequestDataType
            {
                RecoveryMode = ServiceOperationRecoveryMode.Session,
                ClientOperationId = request.ClientOperationId,
                ServerToken = default,
                WaitTime = 5000
            }).ConfigureAwait(false);
            Assert.That(result.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(result.Output.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(result.Output.Result.TryGetValue(out ReadResponse? response), Is.True);
            Assert.That(response!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Abandon releases recovery only; a token Cancel independently requests stopping the owned invocation.
        /// </summary>
        [Test]
        public async Task AbandonAndCancelHaveIndependentEffects()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admission =
                await test.SubmitAsync(ExecutionTestContext.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = admission.Output.Operation.ServerToken;
            admission.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            using ManagerReply<ControlResponseDataType> abandoned = await test.ControlAsync(
                token, RseBrowseNames.Abandon).ConfigureAwait(false);
            Assert.That(abandoned.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(abandoned.Output.Operation.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(abandoned.Output.Operation.CancellationRequested, Is.False);
            abandoned.Dispose();
            Assert.That(test.Server.CancellationRequested.Task.IsCompleted, Is.False);

            using ManagerReply<ControlResponseDataType> cancelled = await test.ControlAsync(
                token, RseBrowseNames.Cancel).ConfigureAwait(false);
            Assert.That(cancelled.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(cancelled.Output.Operation.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(cancelled.Output.Operation.CancellationRequested, Is.True);
            cancelled.Dispose();
            await test.Server.CancellationRequested.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            using ManagerReply<ControlResponseDataType> repeated = await test.ControlAsync(
                token, RseBrowseNames.Cancel).ConfigureAwait(false);
            Assert.That(repeated.Output.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            repeated.Dispose();
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            using ManagerReply<ControlResponseDataType> stopped = await test.ControlAsync(
                token, RseBrowseNames.Cancel).ConfigureAwait(false);
            Assert.That(stopped.Output.Result, Is.EqualTo(ServiceOperationControlResult.NotApplicable));
        }

        /// <summary>
        /// The existing auditing gate controls metadata-only events, without credentials, tokens or payload fields.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OperationAuditingUsesExistingGateAndMetadataOnly(bool auditing)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false, auditing: auditing)
                .ConfigureAwait(false);
            var events = new ConcurrentQueue<AuditServiceOperationEventState>();
            ServerObjectState serverObject = test.Server.CurrentInstance.DiagnosticsNodeManager
                .FindPredefinedNode<ServerObjectState>(ObjectIds.Server);
            void Observe(ISystemContext context, NodeState sender, IFilterTarget value)
            {
                if (value is AuditServiceOperationEventState operation)
                {
                    events.Enqueue(operation);
                }
            }
            serverObject.EventReported += Observe;
            try
            {
                SubmitRequestDataType request = ExecutionTestContext.ReadRequest(wait: 5000);
                using ManagerReply<SubmitResponseDataType> response = await test.SubmitAsync(request)
                    .ConfigureAwait(false);
                ByteString token = response.Output.Operation.ServerToken;
                response.Dispose();
                using ManagerReply<ControlResponseDataType> acknowledged = await test.ControlAsync(
                    token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
                acknowledged.Dispose();
                Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AcknowledgedCount, Is.EqualTo(1));
                if (!auditing)
                {
                    Assert.That(events, Is.Empty);
                    return;
                }
                AuditServiceOperationEventState[] related =
                    [.. events.Where(value => value.ClientOperationId!.Value == request.ClientOperationId)];
                ServiceOperationAuditAction[] actions = [.. related.Select(value => value.Action!.Value)];
                Assert.That(actions, Does.Contain(ServiceOperationAuditAction.Admitted));
                Assert.That(actions, Does.Contain(ServiceOperationAuditAction.OutcomeCommitted));
                Assert.That(actions, Does.Contain(ServiceOperationAuditAction.Collected));
                Assert.That(actions, Does.Contain(ServiceOperationAuditAction.Acknowledged));
                foreach (AuditServiceOperationEventState entry in related)
                {
                    Assert.That(entry, Is.InstanceOf<AuditSessionEventState>());
                    Assert.That(entry.SessionId!.Value, Is.EqualTo(test.Server.CurrentInstance.SessionManager
                        .GetSession(test.NewHeader().AuthenticationToken)!.Id));
                    var children = new List<BaseInstanceState>();
                    entry.GetChildren(test.Server.CurrentInstance.DefaultSystemContext, children);
                    string?[] names = [.. children.Select(child => child.BrowseName.Name)];
                    Assert.That(names, Does.Not.Contain("ServerToken"));
                    Assert.That(names, Does.Not.Contain("AuthenticationToken"));
                    Assert.That(names, Does.Not.Contain("Request"));
                    Assert.That(names, Does.Not.Contain("Result"));
                    Assert.That(entry.ServiceId!.Value, Is.EqualTo(DataTypeIds.ReadRequest));
                }
            }
            finally
            {
                serverObject.EventReported -= Observe;
            }
        }

        /// <summary>
        /// A default Server neither installs the proposal model nor dispatches a submitted underlying request.
        /// </summary>
        [Test]
        public async Task DisabledFeatureDoesNotAdvertiseOrExecute()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false, enabled: false)
                .ConfigureAwait(false);
            NamespaceTable namespaces = test.Server.MessageContext.NamespaceUris;
            // Assign a Client-side namespace coordinate without installing any Server node.
            ushort modelNamespace = namespaces.GetIndexOrAppend(RseNamespaces.Rse);
            ushort applicationNamespace = namespaces.GetIndexOrAppend("urn:opcfoundation:tests:disabled-rse");
            var managerId = ExpandedNodeId.ToNodeId(RseObjectIds.RecoverableExecutionManager, namespaces);
            CallResponse response = await test.SendAsync<CallResponse>(new CallRequest
            {
                RequestHeader = test.NewHeader(),
                MethodsToCall =
                [
                    new CallMethodRequest
                    {
                        ObjectId = managerId,
                        MethodId = new NodeId(Guid.NewGuid(), applicationNamespace),
                        InputArguments = [Variant.From(new ExtensionObject(ExecutionTestContext.ReadRequest()))]
                    }
                ]
            }).ConfigureAwait(false);
            AssertMethodFailure(response, StatusCodes.BadNodeIdUnknown);
            response.Reuse();
            Assert.That(test.Server.InvocationCount, Is.Zero);
            Assert.That(test.Server.ExecutionFactory.Provider, Is.Null);
            ReadResponse metadata = await test.SendAsync<ReadResponse>(new ReadRequest
            {
                RequestHeader = test.NewHeader(),
                NodesToRead =
                [
                    new ReadValueId { NodeId = managerId, AttributeId = Attributes.NodeClass },
                    new ReadValueId
                    {
                        NodeId = ExpandedNodeId.ToNodeId(
                            RseObjectIds.RecoverableExecutionManager_Capabilities, namespaces),
                        AttributeId = Attributes.NodeClass
                    }
                ]
            }).ConfigureAwait(false);
            Assert.That(modelNamespace, Is.GreaterThan(0));
            Assert.That(metadata.Results.Count, Is.EqualTo(2));
            Assert.That(metadata.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(metadata.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            metadata.Reuse();
        }

        /// <summary>
        /// Enabled roots use RSE IDs; live Methods have application IDs and RSE-qualified scalar metadata.
        /// </summary>
        [Test]
        public async Task EnabledModelUsesApplicationIdsAndPrivilegedDiagnostics()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            NamespaceTable namespaces = test.Server.MessageContext.NamespaceUris;
            ushort modelNamespace = namespaces.GetIndexOrAppend(RseNamespaces.Rse);
            RecoverableExecutionManagerState manager = test.Manager;
            RecoverableExecutionCapabilitiesState capabilities = manager.Capabilities!;
            Assert.That(capabilities, Is.Not.Null);
            Assert.That(capabilities.NodeId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                RseObjectIds.RecoverableExecutionManager_Capabilities, namespaces)));
            Assert.That(capabilities.ContractVersion!.Value, Is.EqualTo("0.4.0"));
            Assert.That(capabilities.SupportedServices!.Value,
                Is.EqualTo(new ArrayOf<NodeId>(
                [
                    DataTypeIds.ReadRequest, DataTypeIds.WriteRequest, DataTypeIds.CallRequest,
                    DataTypeIds.HistoryReadRequest, DataTypeIds.HistoryUpdateRequest,
                    DataTypeIds.BrowseRequest, DataTypeIds.BrowseNextRequest,
                    DataTypeIds.TranslateBrowsePathsToNodeIdsRequest,
                    DataTypeIds.RegisterNodesRequest, DataTypeIds.UnregisterNodesRequest,
                    DataTypeIds.AddNodesRequest, DataTypeIds.AddReferencesRequest,
                    DataTypeIds.DeleteNodesRequest, DataTypeIds.DeleteReferencesRequest
                ])));
            Assert.That(capabilities.SupportedServices.NodeId.NamespaceIndex, Is.GreaterThan(0));
            Assert.That(capabilities.SupportedServices.BrowseName.NamespaceIndex, Is.EqualTo(modelNamespace));
            Assert.That(capabilities.SupportedRecoveryModes!.Value,
                Is.EqualTo(new ArrayOf<ServiceOperationRecoveryMode>([ServiceOperationRecoveryMode.Session])));
            Assert.That(capabilities.SupportedServices.Value.ToArray(),
                Does.Not.Contain(DataTypeIds.QueryFirstRequest));
            Assert.That(capabilities.SupportedServices.Value.ToArray(),
                Does.Not.Contain(DataTypeIds.QueryNextRequest));
            Assert.That(manager.Submit, Is.InstanceOf<SubmitMethodState>());
            Assert.That(manager.GetResult, Is.InstanceOf<GetResultMethodState>());
            Assert.That(manager.Acknowledge, Is.InstanceOf<AcknowledgeMethodState>());
            Assert.That(manager.Cancel, Is.InstanceOf<CancelMethodState>());
            Assert.That(manager.Abandon, Is.InstanceOf<AbandonMethodState>());
            MethodState[] methods =
                [manager.Submit!, manager.GetResult!, manager.Acknowledge!, manager.Cancel!, manager.Abandon!];
            Assert.That(methods.Select(static method => method.NodeId), Is.Unique);
            foreach (MethodState method in methods)
            {
                Assert.That(method.NodeId.NamespaceIndex, Is.GreaterThan(0).And.Not.EqualTo(modelNamespace));
                Assert.That(method.NodeId.IdType, Is.EqualTo(IdType.Guid));
                Assert.That(method.BrowseName.NamespaceIndex, Is.EqualTo(modelNamespace));
                Assert.That(method.InputArguments!.Value.Count, Is.EqualTo(1));
                Assert.That(method.InputArguments.Value[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(method.OutputArguments!.Value.Count, Is.EqualTo(1));
                Assert.That(method.OutputArguments.Value[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
            }
            RecoverableExecutionDiagnosticsState diagnostics = test.Server.CurrentInstance.DiagnosticsNodeManager
                .FindPredefinedNode<RecoverableExecutionDiagnosticsState>(ExpandedNodeId.ToNodeId(
                    RseObjectIds.RecoverableExecutionDiagnostics, namespaces));
            Assert.That(diagnostics, Is.Not.Null);
            ReadResponse read = await test.SendAsync<ReadResponse>(new ReadRequest
            {
                RequestHeader = test.NewHeader(),
                NodesToRead =
                [
                    new ReadValueId { NodeId = capabilities.ContractVersion.NodeId, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = diagnostics.AdmittedCount!.NodeId, AttributeId = Attributes.Value }
                ]
            }).ConfigureAwait(false);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out string? revision), Is.True);
            Assert.That(revision, Is.EqualTo("0.4.0"));
            Assert.That(read.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            read.Reuse();
        }

        /// <summary>
        /// The public scalar definition exposes the admission budget without a second RequestHeader.
        /// </summary>
        [Test]
        public async Task RuntimeSubmissionDefinitionIncludesAdmissionBudgetDescription()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            ReadResponse read = await test.SendAsync<ReadResponse>(new ReadRequest
            {
                RequestHeader = test.NewHeader(),
                NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = ExpandedNodeId.ToNodeId(
                            RseDataTypeIds.SubmitRequestDataType, test.Server.MessageContext.NamespaceUris),
                        AttributeId = Attributes.DataTypeDefinition
                    }
                ]
            }).ConfigureAwait(false);
            try
            {
                Assert.That(read.Results.Count, Is.EqualTo(1));
                Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[0].WrappedValue.TryGetValue(out ExtensionObject body), Is.True);
                Assert.That(body.TryGetValue(out StructureDefinition? definition), Is.True);
                Assert.That(definition!.Fields.Count, Is.EqualTo(6));
                StructureField allowance = definition.Fields[3];
                Assert.That(definition.Fields.ToArray()!.Select(static field => field.Name),
                    Is.EqualTo(s_submitFields));
                Assert.That(allowance.Name, Is.EqualTo(nameof(SubmitRequestDataType.ExecutionAllowance)));
                Assert.That(allowance.DataType, Is.EqualTo(DataTypeIds.Duration));
                Assert.That(allowance.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(allowance.IsOptional, Is.False);
                Assert.That(allowance.Description.Text,
                    Is.EqualTo("Requested positive finite execution budget in milliseconds, starting at admission."));
            }
            finally
            {
                read.Reuse();
            }
        }

        /// <summary>
        /// Unknown lookup metadata never claims non-execution or fabricates a Server token.
        /// </summary>
        [Test]
        public async Task UnknownClientIdLookupUsesCanonicalDefaults()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            var id = Uuid.NewUuid();
            using ManagerReply<GetResultResponseDataType> unknown = await test.GetAsync(
                new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ClientOperationId = id,
                    ServerToken = default
                }).ConfigureAwait(false);
            Assert.That(unknown.Output.Operation.ClientOperationId, Is.EqualTo(id));
            Assert.That(unknown.Output.Operation.RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Unspecified));
            Assert.That(unknown.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            Assert.That(unknown.Output.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.None));
            Assert.That(unknown.Output.Operation.ServerToken.IsNull, Is.True);
            Assert.That(unknown.Output.Operation.ServiceId.IsNull, Is.True);
            Assert.That(unknown.Output.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(unknown.Output.Operation.ExecutionAllowance, Is.Zero);
            Assert.That(unknown.Output.Operation.ResultRetentionTime, Is.Zero);
            Assert.That(unknown.Output.Result.IsNull, Is.True);
        }

        /// <summary>
        /// Ordinary Session authentication is required independently of operation lookup.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task ManagerCallWithoutSessionCannotExecuteOrDisclose(bool collect, bool createUnactivated)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            CallRequest request = collect
                ? test.Call(RseBrowseNames.GetResult, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ClientOperationId = Uuid.NewUuid(),
                    ServerToken = default
                })
                : test.Call(RseBrowseNames.Submit, ExecutionTestContext.ReadRequest());
            request.RequestHeader.AuthenticationToken = default;
            if (createUnactivated)
            {
                CreateSessionResponse created = await test.SendAsync<CreateSessionResponse>(new CreateSessionRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 500, Timestamp = DateTimeUtc.Now },
                    ClientDescription = new ApplicationDescription
                    {
                        ApplicationUri = "urn:opcfoundation:tests:unactivated-recovery",
                        ApplicationName = new LocalizedText("Unactivated recovery"),
                        ApplicationType = ApplicationType.Client
                    },
                    SessionName = "Unactivated recovery",
                    RequestedSessionTimeout = 60_000,
                    MaxResponseMessageSize = 65_536
                }).ConfigureAwait(false);
                request.RequestHeader.AuthenticationToken = created.AuthenticationToken;
                created.Reuse();
            }
            IServiceResponse response = await test.SendAsync<IServiceResponse>(request).ConfigureAwait(false);
            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(createUnactivated
                ? StatusCodes.BadSessionNotActivated : StatusCodes.BadServiceUnsupported));
            (response as IPooledEncodeable)?.Reuse();
            Assert.That(test.Server.InvocationCount, Is.Zero);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AdmittedCount, Is.Zero);
        }

        /// <summary>
        /// An unknown nonempty token is an invalid Session, not a request to invoke a Session-less Call.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ManagerCallWithUnknownSessionTokenCannotExecuteOrDisclose(bool collect)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            CallRequest request = collect
                ? test.Call(RseBrowseNames.GetResult, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ClientOperationId = Uuid.NewUuid()
                })
                : test.Call(RseBrowseNames.Submit, ExecutionTestContext.ReadRequest());
            request.RequestHeader.AuthenticationToken = new NodeId(Guid.NewGuid(), test.Manager.NodeId.NamespaceIndex);

            IServiceResponse response = await test.SendAsync<IServiceResponse>(request).ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            ((ServiceFault)response).Reuse();
            Assert.That(test.Server.InvocationCount, Is.Zero);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AdmittedCount, Is.Zero);
        }

        /// <summary>
        /// Exchange handles may be reused after replies while distinct retained operations keep their own identities.
        /// </summary>
        [Test]
        public async Task ReusedExchangeHandleDoesNotAliasRetainedOperations()
        {
            await using ExecutionTestContext test = await ExecutionTestContext.CreateAsync(delayRead: true)
                .ConfigureAwait(false);
            SubmitRequestDataType first = ExecutionTestContext.ReadRequest();
            SubmitRequestDataType second = ExecutionTestContext.ReadRequest();
            RequestHeader firstHeader = test.NewHeader();
            RequestHeader secondHeader = test.NewHeader();
            firstHeader.RequestHandle = 77;
            secondHeader.RequestHandle = 77;
            using ManagerReply<SubmitResponseDataType> admittedFirst =
                await test.SubmitAsync(first, firstHeader).ConfigureAwait(false);
            ByteString firstToken = admittedFirst.Output.Operation.ServerToken;
            Assert.That(admittedFirst.Response.ResponseHeader.RequestHandle, Is.EqualTo(77u));
            admittedFirst.Dispose();
            using ManagerReply<SubmitResponseDataType> admittedSecond =
                await test.SubmitAsync(second, secondHeader).ConfigureAwait(false);
            ByteString secondToken = admittedSecond.Output.Operation.ServerToken;
            Assert.That(admittedSecond.Response.ResponseHeader.RequestHandle, Is.EqualTo(77u));
            Assert.That(second.ClientOperationId, Is.Not.EqualTo(first.ClientOperationId));
            Assert.That(secondToken, Is.Not.EqualTo(firstToken));
            admittedSecond.Dispose();
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            using ManagerReply<GetResultResponseDataType> firstResult =
                await test.GetAsync(firstToken).ConfigureAwait(false);
            using ManagerReply<GetResultResponseDataType> secondResult =
                await test.GetAsync(secondToken).ConfigureAwait(false);
            Assert.That(firstResult.Output.Operation.ClientOperationId, Is.EqualTo(first.ClientOperationId));
            Assert.That(secondResult.Output.Operation.ClientOperationId, Is.EqualTo(second.ClientOperationId));
            Assert.That(firstResult.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(secondResult.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Anonymous plus Sign cannot promise the same-Session replacement-channel recovery contract.
        /// </summary>
        [Test]
        public async Task AnonymousSignAdmissionIsRejectedBeforeDispatch()
        {
            await using ExecutionTestContext test = await ExecutionTestContext.CreateAsync(
                delayRead: false, signOnlyAnonymous: true).ConfigureAwait(false);
            await test.RejectAsync(
                RseBrowseNames.Submit, ExecutionTestContext.ReadRequest(), StatusCodes.BadSecurityModeRejected)
                .ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.Zero);
        }

        /// <summary>
        /// Unknown before registration is not a cached non-execution result; pre-admission Cancel can still prevent
        /// dispatch.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LookupCanOvertakeAdmissionWithoutDecidingExecution(bool cancelBeforeAdmission)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var policy = new RevocablePolicy { SubmissionEntered = entered, SubmissionRelease = release.Task };
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false, policy: policy)
                .ConfigureAwait(false);
            SubmitRequestDataType request = ExecutionTestContext.ReadRequest(wait: 5000);
            CallRequest call = test.Call(RseBrowseNames.Submit, request);
            Task<IServiceResponse> submitting = test.SendAsync<IServiceResponse>(call).AsTask();
            try
            {
                await entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
                using ManagerReply<GetResultResponseDataType> before = await test.GetAsync(
                    new GetResultRequestDataType
                    {
                        RecoveryMode = ServiceOperationRecoveryMode.Session,
                        ClientOperationId = request.ClientOperationId,
                        ServerToken = default
                    }).ConfigureAwait(false);
                Assert.That(before.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
                before.Dispose();
                if (cancelBeforeAdmission)
                {
                    CancelResponse cancellation = await test.SendAsync<CancelResponse>(new CancelRequest
                    {
                        RequestHeader = test.NewHeader(),
                        RequestHandle = call.RequestHeader.RequestHandle
                    }).ConfigureAwait(false);
                    Assert.That(cancellation.CancelCount, Is.EqualTo(1));
                    cancellation.Reuse();
                }
                release.TrySetResult(true);
                IServiceResponse response = await submitting.ConfigureAwait(false);
                if (cancelBeforeAdmission)
                {
                    Assert.That(response, Is.InstanceOf<ServiceFault>());
                    Assert.That(response.ResponseHeader.ServiceResult,
                        Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                    Assert.That(test.Server.InvocationCount, Is.Zero);
                }
                else
                {
                    Assert.That(response, Is.InstanceOf<CallResponse>());
                    Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    var outer = (CallResponse)response;
                    Assert.That(outer.Results.Count, Is.EqualTo(1));
                    SubmitResponseDataType admitted =
                        ReadOutput<SubmitResponseDataType>(outer.Results[0], test.Server.MessageContext);
                    Assert.That(admitted.Operation.ClientOperationId, Is.EqualTo(request.ClientOperationId));
                    Assert.That(admitted.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                    Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
                }
            }
            finally
            {
                release.TrySetResult(true);
                (await submitting.ConfigureAwait(false) as IPooledEncodeable)?.Reuse();
            }
        }

        /// <summary>
        /// Work ending just before E still receives its entire accepted retention interval from readiness.
        /// </summary>
        [Test]
        public async Task LateReadyOutcomeReceivesFullRetentionInterval()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                ExecutionTestContext.ReadRequest(execution: 1000, retention: 500)).ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(999));
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            using ManagerReply<GetResultResponseDataType> ready = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(ready.Output.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(ready.Output.Operation.ResultRemaining, Is.EqualTo(500));
            ready.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(499));
            using ManagerReply<GetResultResponseDataType> retained = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(retained.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(retained.Output.Operation.ResultRemaining, Is.EqualTo(1));
            retained.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(1));
            using ManagerReply<GetResultResponseDataType> expired = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(expired.Output.Operation.State, Is.EqualTo(ServiceOperationState.Expired));
            Assert.That(expired.Output.Operation.HistoryRemaining, Is.EqualTo(500));
        }

        /// <summary>
        /// Wall-clock corrections do not alter elapsed execution, retention or retry-advice bounds.
        /// </summary>
        [Test]
        public async Task WallClockChangesDoNotRenewOrExpireElapsedLifetimes()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                ExecutionTestContext.ReadRequest(execution: 1000, retention: 500)).ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            // Cross E/R without expiring the owning Session's separate ordinary inactivity timeout.
            test.Clock.UtcOffset = TimeSpan.FromSeconds(2);
            using ManagerReply<GetResultResponseDataType> forward = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(forward.Output.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(forward.Output.Operation.ExecutionRemaining, Is.EqualTo(1000));
            Assert.That(forward.Output.Operation.RetryAfter, Is.LessThan(forward.Output.Operation.ResultRetentionTime));
            Assert.That(forward.Output.Operation.RetryAfter,
                Is.LessThanOrEqualTo(forward.Output.Operation.ExecutionRemaining));
            forward.Dispose();
            test.Clock.UtcOffset = TimeSpan.FromSeconds(-2);
            test.Clock.Advance(TimeSpan.FromMilliseconds(1000));
            using ManagerReply<GetResultResponseDataType> deadline = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(deadline.Output.Operation.OutcomeKind,
                Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(deadline.Output.Operation.ResultRemaining, Is.EqualTo(500));
            deadline.Dispose();
            test.Clock.UtcOffset = TimeSpan.FromSeconds(4);
            using ManagerReply<GetResultResponseDataType> retained = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(retained.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(retained.Output.Operation.ResultRemaining, Is.EqualTo(500));
            retained.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(500));
            using ManagerReply<GetResultResponseDataType> expired = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(expired.Output.Operation.State, Is.EqualTo(ServiceOperationState.Expired));
        }

        /// <summary>
        /// Foreign-Session identifiers are indistinguishable from unknown identifiers in the requesting Session.
        /// </summary>
        [Test]
        public async Task ForeignSessionTokenHasTheSameUnknownLookupAndControlDisposition()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> submitted =
                await test.SubmitAsync(ExecutionTestContext.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = submitted.Output.Operation.ServerToken;
            submitted.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            byte[] missing = token.Span.ToArray();
            missing[0] ^= 0xff;
            ByteString unknown = new(missing);
            (RequestHeader otherHeader, SecureChannelContext otherChannel) =
                await test.Server.CreateAndActivateSessionAsync("OtherAsyncSession").ConfigureAwait(false);
            var endpoint = new SessionEndpoint(test.Server);
            byte[]? unknownMetadata = null;
            uint handle = 90;
            foreach (ByteString selector in new[] { unknown, token })
            {
                IServiceResponse received = await endpoint.ProcessRequestAsync(otherChannel,
                    test.Call(RseBrowseNames.GetResult, new GetResultRequestDataType
                    {
                        RecoveryMode = ServiceOperationRecoveryMode.Session,
                        ServerToken = selector
                    }, Header(otherHeader, handle++)), test.Timeout).ConfigureAwait(false);
                Assert.That(received, Is.InstanceOf<CallResponse>(),
                    $"Foreign/unknown lookup returned {received.ResponseHeader.ServiceResult}.");
                using var lookup = new ManagerReply<GetResultResponseDataType>(
                    (CallResponse)received, test.Server.MessageContext);
                Assert.That(lookup.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
                Assert.That(lookup.Output.Operation.ServerToken.IsNull, Is.True);
                Assert.That(lookup.Output.Result.IsNull, Is.True);
                byte[] metadata = BinaryEncoder.EncodeMessage(lookup.Output.Operation, test.Server.MessageContext);
                unknownMetadata ??= metadata;
                Assert.That(metadata, Is.EqualTo(unknownMetadata));
                lookup.Dispose();

                IServiceResponse controlled = await endpoint.ProcessRequestAsync(otherChannel,
                    test.Call(RseBrowseNames.Abandon, new OperationControlRequestDataType
                    {
                        ServerToken = selector
                    }, Header(otherHeader, handle++)), test.Timeout).ConfigureAwait(false);
                Assert.That(controlled, Is.InstanceOf<CallResponse>());
                using var disposition = new ManagerReply<ControlResponseDataType>(
                    (CallResponse)controlled, test.Server.MessageContext);
                Assert.That(disposition.Output.Result, Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
                Assert.That(disposition.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
                Assert.That(BinaryEncoder.EncodeMessage(disposition.Output.Operation, test.Server.MessageContext),
                    Is.EqualTo(unknownMetadata));
            }
            using ManagerReply<GetResultResponseDataType> owner = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(owner.Output.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(owner.Output.Operation.CancellationRequested, Is.False);
            owner.Dispose();
            test.Server.Release();
        }

        /// <summary>
        /// A foreign creator sees canonical Unknown while the original creator's retained outcome stays intact.
        /// </summary>
        [Test]
        public async Task SameSessionForeignCreatorSeesUnknownAndOriginalCreatorCollectsUnchangedOutcome()
        {
            await using ExecutionTestContext test = await ExecutionTestContext.CreateAsync(
                delayRead: false, userNamePolicy: true).ConfigureAwait(false);
            string userName = "synthetic-" + Guid.NewGuid().ToString("N");
            ByteString password = new(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")));
            test.Server.CurrentInstance.IdentityRegistry.Register(new UserNamePasswordAuthenticator((identity, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                if (identity.Token is not UserNameIdentityToken userToken ||
                    identity.UserName != userName ||
                    userToken.Password != password)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                return new ValueTask<IUserIdentity>(new UserIdentity(identity));
            }));
            SubmitRequestDataType request = ExecutionTestContext.ReadRequest(wait: 5000);
            RequestHeader exchange = test.NewHeader();
            using ManagerReply<SubmitResponseDataType> submission = await test.SubmitAsync(request, exchange)
                .ConfigureAwait(false);
            ByteString token = submission.Output.Operation.ServerToken;
            Assert.That(submission.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(submission.Output.Result.TryGetValue(out ReadResponse? accepted), Is.True);
            Assert.That(accepted!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(accepted.ResponseHeader.RequestHandle, Is.EqualTo(17u));
            Assert.That(accepted.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            byte[] frozen = BinaryEncoder.EncodeMessage(accepted, test.Server.MessageContext);
            submission.Dispose();
            ISession serverSession = test.Server.CurrentInstance.SessionManager
                .GetSession(exchange.AuthenticationToken)!;
            NodeId sessionId = serverSession.Id;
            Assert.That(serverSession.IdentityToken.TokenType, Is.EqualTo(UserTokenType.Anonymous));
            UserTokenPolicy userPolicy = serverSession.EndpointDescription.UserIdentityTokens
                .Find(static policy => policy.TokenType == UserTokenType.UserName)!;
            Assert.That(userPolicy, Is.Not.Null);
            Assert.That(userPolicy.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
            ActivateSessionResponse changed = await test.SendAsync<ActivateSessionResponse>(new ActivateSessionRequest
            {
                RequestHeader = test.NewHeader(),
                UserIdentityToken = new ExtensionObject(new UserNameIdentityToken
                {
                    PolicyId = userPolicy.PolicyId,
                    UserName = userName,
                    Password = password
                })
            }).ConfigureAwait(false);
            changed.Reuse();
            Assert.That(test.Server.CurrentInstance.SessionManager
                .GetSession(exchange.AuthenticationToken)!.Id, Is.EqualTo(sessionId));
            Assert.That(serverSession.IdentityToken.TokenType, Is.EqualTo(UserTokenType.UserName));
            GetResultRequestDataType[] selectors =
            [
                new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ServerToken = token
                },
                new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ClientOperationId = request.ClientOperationId,
                    ServerToken = default
                }
            ];
            foreach (GetResultRequestDataType selector in selectors)
            {
                Uuid expectedClientId = selector.ServerToken.IsNull ? request.ClientOperationId : Uuid.Empty;
                using ManagerReply<GetResultResponseDataType> foreign =
                    await test.GetAsync(selector).ConfigureAwait(false);
                Assert.That(foreign.Response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(foreign.Response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(foreign.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
                Assert.That(foreign.Output.Operation.RecoveryMode,
                    Is.EqualTo(ServiceOperationRecoveryMode.Unspecified));
                Assert.That(foreign.Output.Operation.ClientOperationId, Is.EqualTo(expectedClientId));
                Assert.That(foreign.Output.Operation.ServerToken.IsNull, Is.True);
                Assert.That(foreign.Output.Operation.ServiceId.IsNull, Is.True);
                Assert.That(foreign.Output.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.None));
                Assert.That(foreign.Output.Result.IsNull, Is.True);
                var canonical = new ServiceOperationInfoDataType
                {
                    ClientOperationId = expectedClientId,
                    ServerToken = default
                };
                Assert.That(BinaryEncoder.EncodeMessage(foreign.Output.Operation, test.Server.MessageContext),
                    Is.EqualTo(BinaryEncoder.EncodeMessage(canonical, test.Server.MessageContext)));
            }
            using ManagerReply<ControlResponseDataType> acknowledgement = await test.ControlAsync(
                token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(acknowledgement.Output.Result, Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
            Assert.That(acknowledgement.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            Assert.That(acknowledgement.Output.Operation.RecoveryMode,
                Is.EqualTo(ServiceOperationRecoveryMode.Unspecified));
            Assert.That(acknowledgement.Output.Operation.ClientOperationId, Is.EqualTo(Uuid.Empty));
            Assert.That(acknowledgement.Output.Operation.ServerToken.IsNull, Is.True);
            byte[] unknownMetadata = BinaryEncoder.EncodeMessage(
                new ServiceOperationInfoDataType { ServerToken = default }, test.Server.MessageContext);
            Assert.That(BinaryEncoder.EncodeMessage(acknowledgement.Output.Operation, test.Server.MessageContext),
                Is.EqualTo(unknownMetadata));
            acknowledgement.Dispose();
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AcknowledgedCount, Is.Zero);
            ActivateSessionResponse restored = await test.SendAsync<ActivateSessionResponse>(new ActivateSessionRequest
            {
                RequestHeader = test.NewHeader(),
                UserIdentityToken = default
            }).ConfigureAwait(false);
            restored.Reuse();
            Assert.That(serverSession.IdentityToken.TokenType, Is.EqualTo(UserTokenType.Anonymous));
            using ManagerReply<GetResultResponseDataType> result = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(result.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(result.Output.Operation.RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Session));
            Assert.That(result.Output.Operation.ClientOperationId, Is.EqualTo(request.ClientOperationId));
            Assert.That(result.Output.Operation.ServerToken, Is.EqualTo(token));
            Assert.That(result.Output.Result.TryGetValue(out ReadResponse? original), Is.True);
            Assert.That(original!.ResponseHeader.RequestHandle, Is.EqualTo(17u));
            Assert.That(original.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(original.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(BinaryEncoder.EncodeMessage(original, test.Server.MessageContext), Is.EqualTo(frozen));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Invalid duration representations fail before dispatch rather than being rounded or silently revised.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(-1, false)]
        [TestCase(double.NaN, false)]
        [TestCase(double.PositiveInfinity, false)]
        [TestCase(0, true)]
        [TestCase(-1, true)]
        [TestCase(double.NaN, true)]
        [TestCase(double.PositiveInfinity, true)]
        public async Task InvalidDurationsRejectBeforeDispatch(double invalid, bool retention)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            SubmitRequestDataType request = retention
                ? ExecutionTestContext.ReadRequest(retention: invalid)
                : ExecutionTestContext.ReadRequest(execution: invalid);
            await test.RejectAsync(RseBrowseNames.Submit, request, StatusCodes.BadInvalidArgument)
                .ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.Zero);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AdmittedCount, Is.Zero);
            Assert.That(test.Server.ExecutionFactory.Provider.GetStatistics().RejectedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Unsupported exact positive allowances are rejected, not revised down to the advertised maximum.
        /// </summary>
        [TestCase(600_001, false)]
        [TestCase(1.000_000_01, false)]
        [TestCase(1.6384999999999998, false)]
        [TestCase(0.5, false)]
        [TestCase(600_001, true)]
        [TestCase(1.000_000_01, true)]
        [TestCase(1.6384999999999998, true)]
        [TestCase(0.5, true)]
        public async Task UnsupportedPositiveDurationRejectsBeforeDispatch(double unsupported, bool retention)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            await test.RejectAsync(RseBrowseNames.Submit,
                retention
                    ? ExecutionTestContext.ReadRequest(retention: unsupported)
                    : ExecutionTestContext.ReadRequest(execution: unsupported),
                StatusCodes.BadOutOfRange).ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.Zero);
        }

        /// <summary>
        /// Invalid admission envelopes do not dispatch work or consume persistent reservations.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [TestCase("empty-id")]
        [TestCase("null-request")]
        [TestCase("not-request")]
        [TestCase("foreign-authentication")]
        [TestCase("negative-wait")]
        [TestCase("infinite-wait")]
        [TestCase("nan-wait")]
        [TestCase("invalid-retention")]
        [TestCase("unspecified-mode")]
        [TestCase("invalid-mode")]
        public async Task InvalidAdmissionEnvelopeRejectsBeforeDispatch(string fault)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            SubmitRequestDataType request = ExecutionTestContext.ReadRequest();
            switch (fault)
            {
                case "empty-id":
                    request.ClientOperationId = Uuid.Empty;
                    break;
                case "null-request":
                    request.Request = default;
                    break;
                case "not-request":
                    request.Request = new ExtensionObject(new ServiceOperationInfoDataType());
                    break;
                case "foreign-authentication":
                    Assert.That(request.Request.TryGetValue(out ReadRequest? original), Is.True);
                    original!.RequestHeader.AuthenticationToken = new NodeId(1234, 1);
                    break;
                case "negative-wait":
                    request.WaitTime = -1;
                    break;
                case "infinite-wait":
                    request.WaitTime = double.PositiveInfinity;
                    break;
                case "nan-wait":
                    request.WaitTime = double.NaN;
                    break;
                case "invalid-retention":
                    request.ResultRetentionTime = double.NaN;
                    break;
                case "unspecified-mode":
                    request.RecoveryMode = ServiceOperationRecoveryMode.Unspecified;
                    break;
                case "invalid-mode":
                    request.RecoveryMode = (ServiceOperationRecoveryMode)4;
                    break;
                default:
                    throw new ArgumentException("Unknown invalid envelope.", nameof(fault));
            }
            await test.RejectAsync(RseBrowseNames.Submit, request, fault == "foreign-authentication"
                ? StatusCodes.BadUserAccessDenied : StatusCodes.BadInvalidArgument).ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.Zero);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AdmittedCount, Is.Zero);
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
            await test.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 0).ConfigureAwait(false);
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
        }

        /// <summary>
        /// Null, empty, oversized and conflicting selectors are rejected independently of lookup.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [TestCase("neither")]
        [TestCase("both")]
        [TestCase("empty-token")]
        [TestCase("long-token")]
        [TestCase("negative-wait")]
        [TestCase("infinite-wait")]
        [TestCase("nan-wait")]
        [TestCase("unspecified-mode")]
        [TestCase("invalid-mode")]
        public async Task InvalidCollectionSelectorIsRejected(string fault)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            var request = new GetResultRequestDataType
            {
                RecoveryMode = ServiceOperationRecoveryMode.Session,
                ServerToken = default
            };
            switch (fault)
            {
                case "neither":
                    break;
                case "both":
                    request.ClientOperationId = Uuid.NewUuid();
                    request.ServerToken = new ByteString(new byte[32]);
                    break;
                case "empty-token":
                    request.ServerToken = ByteString.Empty;
                    break;
                case "long-token":
                    request.ServerToken = new ByteString(new byte[65]);
                    break;
                case "negative-wait":
                    request.ClientOperationId = Uuid.NewUuid();
                    request.WaitTime = -1;
                    break;
                case "infinite-wait":
                    request.ClientOperationId = Uuid.NewUuid();
                    request.WaitTime = double.PositiveInfinity;
                    break;
                case "nan-wait":
                    request.ClientOperationId = Uuid.NewUuid();
                    request.WaitTime = double.NaN;
                    break;
                case "unspecified-mode":
                    request.ClientOperationId = Uuid.NewUuid();
                    request.RecoveryMode = ServiceOperationRecoveryMode.Unspecified;
                    break;
                case "invalid-mode":
                    request.ClientOperationId = Uuid.NewUuid();
                    request.RecoveryMode = (ServiceOperationRecoveryMode)4;
                    break;
                default:
                    throw new ArgumentException("Unknown invalid selector.", nameof(fault));
            }
            await test.RejectAsync(RseBrowseNames.GetResult, request, StatusCodes.BadInvalidArgument)
                .ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.Zero);
        }

        /// <summary>
        /// Each distinct control Method requires a nonempty token bounded to 64 bytes.
        /// </summary>
        [TestCase(-1, RseBrowseNames.Acknowledge)]
        [TestCase(0, RseBrowseNames.Acknowledge)]
        [TestCase(65, RseBrowseNames.Acknowledge)]
        [TestCase(-1, RseBrowseNames.Cancel)]
        [TestCase(0, RseBrowseNames.Cancel)]
        [TestCase(65, RseBrowseNames.Cancel)]
        [TestCase(-1, RseBrowseNames.Abandon)]
        [TestCase(0, RseBrowseNames.Abandon)]
        [TestCase(65, RseBrowseNames.Abandon)]
        public async Task InvalidControlIsRejected(int tokenLength, string method)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            await test.RejectAsync(method, new OperationControlRequestDataType
            {
                ServerToken = tokenLength < 0 ? default : new ByteString(new byte[tokenLength])
            }, StatusCodes.BadInvalidArgument).ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.Zero);
        }

        /// <summary>
        /// Both endpoints of the valid token length interval remain unknown lookups, not malformed requests.
        /// </summary>
        [Test]
        public async Task ValidTokenLengthBoundariesAreUnknownForEveryControlAndCollection()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            foreach (int length in new[] { 1, 64 })
            {
                ByteString token = new(new byte[length]);
                using ManagerReply<GetResultResponseDataType> result = await test.GetAsync(token).ConfigureAwait(false);
                Assert.That(result.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
                Assert.That(result.Output.Operation.ServerToken.IsNull, Is.True);
                Assert.That(result.Output.Result.IsNull, Is.True);
                foreach (string method in new[]
                {
                    RseBrowseNames.Acknowledge, RseBrowseNames.Cancel, RseBrowseNames.Abandon
                })
                {
                    using ManagerReply<ControlResponseDataType> control =
                        await test.ControlAsync(token, method).ConfigureAwait(false);
                    Assert.That(control.Output.Result, Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
                    Assert.That(control.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
                    Assert.That(control.Output.Operation.ServerToken.IsNull, Is.True);
                    Assert.That(control.Output.Operation.CancellationRequested, Is.False);
                }
            }
            Assert.That(test.Server.InvocationCount, Is.Zero);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AdmittedCount, Is.Zero);
        }

        /// <summary>
        /// Exact tick-representable values are accepted without revising either lifetime or overstating remaining time.
        /// </summary>
        [TestCase(1)]
        [TestCase(1.0009)]
        [TestCase(100.0007)]
        [TestCase(600_000)]
        public async Task ExactDurationBoundariesAreAcceptedWithoutRevision(double duration)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                ExecutionTestContext.ReadRequest(execution: duration, retention: duration)).ConfigureAwait(false);
            Assert.That(admitted.Output.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(admitted.Output.Operation.ExecutionAllowance, Is.EqualTo(duration));
            Assert.That(admitted.Output.Operation.ExecutionRemaining, Is.EqualTo(Math.Floor(duration)));
            Assert.That(admitted.Output.Operation.ResultRetentionTime, Is.EqualTo(duration));
            Assert.That(admitted.Output.Operation.ServerToken.Length, Is.EqualTo(32));
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AdmittedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Remembered Client IDs are never a fingerprint-based resubmission mechanism.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DuplicateClientIdRejectsEqualAndChangedInputs(bool changeInput)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            SubmitRequestDataType first = ExecutionTestContext.ReadRequest(wait: 5000);
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(first).ConfigureAwait(false);
            Assert.That(admitted.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            admitted.Dispose();
            SubmitRequestDataType duplicate = ExecutionTestContext.ReadRequest(wait: 5000);
            duplicate.ClientOperationId = first.ClientOperationId;
            if (changeInput)
            {
                Assert.That(duplicate.Request.TryGetValue(out ReadRequest? original), Is.True);
                original!.NodesToRead = [];
            }
            await test.RejectAsync(RseBrowseNames.Submit, duplicate, StatusCodes.BadInvalidState).ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Concurrent senders retain identical original bytes and their charges through ACK, abandon and expiry.
        /// </summary>
        [TestCase(ServiceOperationState.Acknowledged)]
        [TestCase(ServiceOperationState.Abandoned)]
        [TestCase(ServiceOperationState.Expired)]
        public async Task InFlightCollectorsSurviveLogicalRelease(ServiceOperationState releasedState)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                ExecutionTestContext.ReadRequest(wait: 5000)).ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            Assert.That(admitted.Output.Result.TryGetValue(out ReadResponse? original), Is.True);
            byte[] frozen = BinaryEncoder.EncodeMessage(original!, test.Server.MessageContext);
            admitted.Dispose();
            ManagerReply<GetResultResponseDataType>[] collectors = await Task.WhenAll(
                test.GetAsync(token).AsTask(), test.GetAsync(token).AsTask(), test.GetAsync(token).AsTask())
                .ConfigureAwait(false);
            try
            {
                if (releasedState == ServiceOperationState.Expired)
                {
                    test.Clock.Advance(TimeSpan.FromMilliseconds(5000));
                }
                else
                {
                    using ManagerReply<ControlResponseDataType> receipt = await test.ControlAsync(token,
                        releasedState == ServiceOperationState.Acknowledged
                            ? RseBrowseNames.Acknowledge : RseBrowseNames.Abandon)
                        .ConfigureAwait(false);
                    Assert.That(receipt.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                }
                using ManagerReply<GetResultResponseDataType> afterRelease =
                    await test.GetAsync(token).ConfigureAwait(false);
                Assert.That(afterRelease.Output.Operation.State, Is.EqualTo(releasedState));
                Assert.That(afterRelease.Output.Result.IsNull, Is.True);
                afterRelease.Dispose();
                Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().RetainedOutcomeBytes,
                    Is.GreaterThan(0));
                Assert.That(collectors.Select(value => value.Response.ResponseHeader.RequestHandle), Is.Unique);
                foreach (ManagerReply<GetResultResponseDataType> collector in collectors)
                {
                    Assert.That(collector.Output.Result.TryGetValue(out ReadResponse? retained), Is.True);
                    Assert.That(BinaryEncoder.EncodeMessage(retained!, test.Server.MessageContext), Is.EqualTo(frozen));
                    Assert.That(collector.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                }
                test.Clock.Advance(TimeSpan.FromMilliseconds(5000));
                Assert.That(test.Server.ExecutionFactory.Provider.GetStatistics().TerminalRecordCount, Is.Zero);
                Assert.That(test.Server.ExecutionFactory.Provider.GetStatistics().RetainedOutcomeBytes,
                    Is.GreaterThan(0), "Public history expiry cannot free a transport's output buffer.");
            }
            finally
            {
                foreach (ManagerReply<GetResultResponseDataType> collector in collectors)
                {
                    collector.Dispose();
                }
            }
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().RetainedOutcomeBytes, Is.Zero);
        }

        /// <summary>
        /// Competing ACK, abandon and expiry transitions choose one release reason without invalidating a sender.
        /// </summary>
        [Test]
        public async Task CompetingReleaseActionsKeepOneWinningReason()
        {
            await using ExecutionTestContext test = await ExecutionTestContext.CreateAsync(delayRead: false)
                .ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> sender =
                await test.SubmitAsync(ExecutionTestContext.ReadRequest(wait: 5000))
                .ConfigureAwait(false);
            ByteString token = sender.Output.Operation.ServerToken;
            try
            {
                test.Clock.Advance(TimeSpan.FromMilliseconds(4999));
                Task<ManagerReply<ControlResponseDataType>> acknowledge =
                    test.ControlAsync(token, RseBrowseNames.Acknowledge).AsTask();
                Task<ManagerReply<ControlResponseDataType>> abandon =
                    test.ControlAsync(token, RseBrowseNames.Abandon).AsTask();
                test.Clock.Advance(TimeSpan.FromMilliseconds(1));
                ManagerReply<ControlResponseDataType>[] controls = await Task.WhenAll(acknowledge, abandon)
                    .ConfigureAwait(false);
                using ManagerReply<GetResultResponseDataType> current =
                    await test.GetAsync(token).ConfigureAwait(false);
                Assert.That(current.Output.Operation.State, Is.AnyOf(ServiceOperationState.Acknowledged,
                    ServiceOperationState.Abandoned, ServiceOperationState.Expired));
                Assert.That(current.Output.Result.IsNull, Is.True);
                foreach (ManagerReply<ControlResponseDataType> control in controls)
                {
                    Assert.That(control.Output.Operation.State, Is.EqualTo(current.Output.Operation.State));
                    control.Dispose();
                }
                current.Dispose();
                AsyncServiceExecutionStatistics statistics = test.Server.ExecutionFactory.Provider!.GetStatistics();
                Assert.That(statistics.AcknowledgedCount + statistics.AbandonedCount + statistics.ExpiredCount,
                    Is.EqualTo(1));
                Assert.That(sender.Output.Result.TryGetValue(out ReadResponse? original), Is.True);
                Assert.That(original!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(statistics.RetainedOutcomeBytes, Is.GreaterThan(0));
            }
            finally
            {
                sender.Dispose();
            }
        }

        /// <summary>
        /// Original Service faults remain complete outcomes inside a successful outer Call and Method item.
        /// </summary>
        [Test]
        public async Task OriginalServiceFaultRetainsItsHeaderAndDiagnostics()
        {
            await using ExecutionTestContext test = await ExecutionTestContext.CreateAsync(delayRead: false)
                .ConfigureAwait(false);
            SubmitRequestDataType request = ExecutionTestContext.ReadRequest(wait: 5000);
            Assert.That(request.Request.TryGetValue(out ReadRequest? read), Is.True);
            read!.NodesToRead = [];
            read.RequestHeader.ReturnDiagnostics = (uint)DiagnosticsMasks.All;
            using ManagerReply<SubmitResponseDataType> submission = await test.SubmitAsync(request)
                .ConfigureAwait(false);
            ByteString token = submission.Output.Operation.ServerToken;
            Assert.That(submission.Response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(submission.Response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(submission.Output.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(submission.Output.Result.TryGetValue(out ServiceFault? fault), Is.True);
            Assert.That(fault!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadNothingToDo));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(17u));
            Assert.That(submission.Output.Operation.OutcomeStatus, Is.EqualTo(fault.ResponseHeader.ServiceResult));
            byte[] frozen = BinaryEncoder.EncodeMessage(fault, test.Server.MessageContext);
            submission.Dispose();
            using ManagerReply<GetResultResponseDataType> collected = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(collected.Response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(collected.Response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(collected.Output.Result.TryGetValue(out ServiceFault? retained), Is.True);
            Assert.That(BinaryEncoder.EncodeMessage(retained!, test.Server.MessageContext), Is.EqualTo(frozen));
        }

        /// <summary>
        /// Measures the complete encoded envelope at the exact payload boundary, not an inner-result approximation.
        /// </summary>
        [TestCase(RequestEncoding.Binary)]
        [TestCase(RequestEncoding.Json)]
        public async Task EncodedEnvelopeBoundaryIncludesWholeCallOverhead(RequestEncoding mapping)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false, mapping: mapping)
                .ConfigureAwait(false);
            test.Server.Scenario.Blob = ByteString.Empty;
            using ManagerReply<SubmitResponseDataType> probe = await test.SubmitAsync(test.BlobRequest())
                .ConfigureAwait(false);
            Assert.That(probe.Output.Result.TryGetValue(out ReadResponse? original), Is.True);
            ReadResponse template = CoreUtils.Clone(original)!;
            ServiceOperationInfoDataType metadata = CoreUtils.Clone(probe.Output.Operation)!;
            ByteString probeToken = probe.Output.Operation.ServerToken;
            probe.Dispose();
            using ManagerReply<ControlResponseDataType> probeAck = await test.ControlAsync(
                probeToken, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            probeAck.Dispose();
            var measurement = new ServiceMessageContext(
                test.Server.MessageContext, test.Server.MessageContext.Telemetry)
            {
                MaxMessageSize = int.MaxValue
            };
            int Measure(int payloadSize)
            {
                ReadResponse candidate = CoreUtils.Clone(template)!;
                candidate.Results =
                [
                    template.Results[0].WithWrappedValue(new Variant(new ByteString(new byte[payloadSize])))
                ];
                var envelope = new CallResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        RequestHandle = uint.MaxValue,
                        Timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567)
                    },
                    Results =
                    [
                        new CallMethodResult
                        {
                            OutputArguments =
                            [
                                Variant.FromStructure(new GetResultResponseDataType
                                {
                                    Operation = metadata,
                                    Result = new ExtensionObject(candidate)
                                })
                            ]
                        }
                    ]
                };
                if (mapping == RequestEncoding.Binary)
                {
                    return BinaryEncoder.EncodeMessage(envelope, measurement).Length;
                }
                using var stream = new MemoryStream();
                using var encoder = new JsonEncoder(stream, measurement);
                encoder.EncodeMessage(envelope, envelope.TypeId);
                return encoder.Close();
            }
            foreach (int excess in new[] { 0, 1 })
            {
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                test.Server.BeforeRead = async ct => await release.Task.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    SubmitRequestDataType request = test.BlobRequest();
                    request.WaitTime = 0;
                    using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(request)
                        .ConfigureAwait(false);
                    Assert.That(admitted.Output.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
                    metadata = CoreUtils.Clone(admitted.Output.Operation)!;
                    metadata.State = ServiceOperationState.OutcomeAvailable;
                    metadata.OutcomeKind = ServiceOperationOutcome.ServiceResponse;
                    metadata.ExecutionRemaining = 0;
                    metadata.ResultRemaining = metadata.ResultRetentionTime;
                    ByteString token = admitted.Output.Operation.ServerToken;
                    admitted.Dispose();
                    // JSON escaping makes opaque-token width variable: measure this admission's actual token.
                    int fitting = 0;
                    int rejected = 65_536;
                    while (rejected - fitting > 1)
                    {
                        int midpoint = fitting + ((rejected - fitting) / 2);
                        if (Measure(midpoint) <= 65_536)
                        {
                            fitting = midpoint;
                        }
                        else
                        {
                            rejected = midpoint;
                        }
                    }
                    Assert.That(Measure(fitting), Is.LessThanOrEqualTo(65_536));
                    Assert.That(Measure(rejected), Is.GreaterThan(65_536));
                    Assert.That(rejected, Is.EqualTo(fitting + 1));
                    test.Server.Scenario.Blob = new ByteString(new byte[fitting + excess]);
                    release.TrySetResult(true);
                    using ManagerReply<GetResultResponseDataType> outcome = await test.GetAsync(
                        new GetResultRequestDataType
                        {
                            RecoveryMode = ServiceOperationRecoveryMode.Session,
                            ServerToken = token,
                            WaitTime = 5000
                        }).ConfigureAwait(false);
                    Assert.That(outcome.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                    if (excess == 0)
                    {
                        Assert.That(outcome.Output.Operation.OutcomeKind,
                            Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
                        Assert.That(outcome.Output.Result.TryGetValue(out ReadResponse? read), Is.True);
                        Assert.That(read!.Results[0].WrappedValue.TryGetValue(out ByteString received), Is.True);
                        Assert.That(received.Length, Is.EqualTo(fitting));
                    }
                    else
                    {
                        Assert.That(outcome.Output.Operation.OutcomeKind,
                            Is.EqualTo(ServiceOperationOutcome.ResultUnavailable));
                        Assert.That(outcome.Output.Operation.OutcomeStatus,
                            Is.EqualTo(StatusCodes.BadResponseTooLarge));
                        Assert.That(outcome.Output.Result.IsNull, Is.True);
                        using ManagerReply<GetResultResponseDataType> retry =
                            await test.GetAsync(token).ConfigureAwait(false);
                        Assert.That(retry.Output.Operation.OutcomeKind,
                            Is.EqualTo(ServiceOperationOutcome.ResultUnavailable));
                        Assert.That(retry.Output.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadResponseTooLarge));
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                }
            }
            Assert.That(test.Server.InvocationCount, Is.EqualTo(3), "Oversized work must never be re-executed.");
        }

        /// <summary>
        /// A smaller current delivery limit fails only that exchange and preserves the previously frozen outcome.
        /// </summary>
        [TestCase(RequestEncoding.Binary)]
        [TestCase(RequestEncoding.Json)]
        public async Task SmallerDeliveryLimitDoesNotRewriteRetainedOutcome(RequestEncoding mapping)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false, mapping: mapping)
                .ConfigureAwait(false);
            test.Server.Scenario.Blob = new ByteString(new byte[4096]);
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.BlobRequest())
                .ConfigureAwait(false);
            Assert.That(admitted.Output.Result.TryGetValue(out ReadResponse? original), Is.True);
            byte[] frozen = BinaryEncoder.EncodeMessage(original!, test.Server.MessageContext);
            ByteString token = admitted.Output.Operation.ServerToken;
            admitted.Dispose();
            ISession session = test.Server.CurrentInstance.SessionManager.GetSessions().Single();
            uint previousLimit = session.ReadDiagnostics(static value => value.MaxResponseMessageSize);
            session.UpdateDiagnostics(static value => value.MaxResponseMessageSize = 1024);
            try
            {
                await test.RejectAsync(RseBrowseNames.GetResult, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ServerToken = token
                }, StatusCodes.BadResponseTooLarge).ConfigureAwait(false);
            }
            finally
            {
                session.UpdateDiagnostics(value => value.MaxResponseMessageSize = previousLimit);
            }
            using ManagerReply<GetResultResponseDataType> recovered = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(recovered.Output.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(recovered.Output.Result.TryGetValue(out ReadResponse? retained), Is.True);
            Assert.That(BinaryEncoder.EncodeMessage(retained!, test.Server.MessageContext), Is.EqualTo(frozen));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Polling and repeated ACKs renew neither retention nor terminal history.
        /// </summary>
        [Test]
        public async Task PollingAndRepeatedAcknowledgementDoNotRenewWindows()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                ExecutionTestContext.ReadRequest(wait: 5000)).ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            admitted.Dispose();
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(4000));
            using ManagerReply<GetResultResponseDataType> polled = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(polled.Output.Operation.ResultRemaining, Is.EqualTo(1000));
            polled.Dispose();
            using ManagerReply<ControlResponseDataType> first = await test.ControlAsync(
                token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(first.Output.Operation.HistoryRemaining, Is.EqualTo(5000));
            first.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(4000));
            using ManagerReply<ControlResponseDataType> repeated = await test.ControlAsync(
                token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(repeated.Output.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            Assert.That(repeated.Output.Operation.HistoryRemaining, Is.EqualTo(1000));
            repeated.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(1000));
            using ManagerReply<GetResultResponseDataType> unknown = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(unknown.Output.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            unknown.Dispose();
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().ActiveOperationCount, Is.Zero);
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
        }

        /// <summary>
        /// Shutdown requests cancellation but keeps owned work charged and waits for its actual completion.
        /// </summary>
        [Test]
        public async Task ProviderShutdownWaitsForOwnedInvocationCleanup()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(ExecutionTestContext.ReadRequest())
                .ConfigureAwait(false);
            admitted.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            AsyncServiceExecutionProvider provider = test.Server.ExecutionFactory.Provider!;
            Task stopping = provider.DisposeAsync().AsTask();
            try
            {
                await test.Server.CancellationRequested.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
                Assert.That(stopping.IsCompleted, Is.False);
                Assert.That(provider.GetStatistics().ActiveOperationCount, Is.EqualTo(1));
                Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.GreaterThan(0));
                test.Server.Release();
                await stopping.WaitAsync(test.Timeout).ConfigureAwait(false);
                Assert.That(provider.GetStatistics().ActiveOperationCount, Is.Zero);
                Assert.That(provider.GetStatistics().RetainedOutcomeBytes, Is.Zero);
                await test.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
                await test.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 0).ConfigureAwait(false);
                await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
            }
            finally
            {
                test.Server.Release();
                await stopping.WaitAsync(test.Timeout).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Lifecycle, subscription, ordinary Cancel, recursive manager Calls and Query stay excluded.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [TestCase(nameof(CreateSessionRequest))]
        [TestCase(nameof(CancelRequest))]
        [TestCase(nameof(PublishRequest))]
        [TestCase(RseBrowseNames.Submit)]
        [TestCase(RseBrowseNames.GetResult)]
        [TestCase(RseBrowseNames.Acknowledge)]
        [TestCase(RseBrowseNames.Cancel)]
        [TestCase(RseBrowseNames.Abandon)]
        [TestCase(nameof(QueryFirstRequest))]
        [TestCase(nameof(QueryNextRequest))]
        public async Task ExcludedOriginalServiceRejectsBeforeDispatch(string name)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            IServiceRequest original = name switch
            {
                nameof(CreateSessionRequest) => new CreateSessionRequest(),
                nameof(CancelRequest) => new CancelRequest(),
                nameof(PublishRequest) => new PublishRequest(),
                RseBrowseNames.Submit => test.Call(name, ExecutionTestContext.ReadRequest()),
                RseBrowseNames.GetResult => test.Call(name, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ClientOperationId = Uuid.NewUuid()
                }),
                RseBrowseNames.Acknowledge or RseBrowseNames.Cancel or RseBrowseNames.Abandon =>
                    test.Call(name, new OperationControlRequestDataType
                    {
                        ServerToken = new ByteString(new byte[32])
                    }),
                nameof(QueryFirstRequest) => new QueryFirstRequest(),
                nameof(QueryNextRequest) => new QueryNextRequest(),
                _ => throw new ArgumentException("Unknown test case.", nameof(name))
            };
            original.RequestHeader = new RequestHeader();
            SubmitRequestDataType request = ExecutionTestContext.ReadRequest();
            request.Request = new ExtensionObject(original);
            await test.RejectAsync(RseBrowseNames.Submit, request, StatusCodes.BadServiceUnsupported)
                .ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.Zero);
        }

        /// <summary>
        /// Retained values obey current effective read access without running their value callback again.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task RetainedReadRechecksUserAccessLevelWithoutRepeatingTheRead(bool callback, bool duringWait)
        {
            var policy = new RevocablePolicy
            {
                CollectionEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false, policy: policy).ConfigureAwait(false);
            test.Server.Scenario.Blob = ByteString.From([1, 2, 3]);
            test.Server.Scenario.DelayBlob = duringWait;
            SubmitRequestDataType request = test.BlobRequest();
            request.WaitTime = duringWait ? 0 : 5000;
            Assert.That(request.Request.TryGetValue(out ReadRequest? original), Is.True);
            NodeId nodeId = original!.NodesToRead[0].NodeId;
            (object? handle, _) =
                await test.Server.CurrentInstance.NodeManager.GetManagerHandleAsync(nodeId, test.Timeout)
                    .ConfigureAwait(false);
            Assert.That(handle, Is.InstanceOf<NodeHandle>());
            Assert.That(((NodeHandle)handle!).Node, Is.InstanceOf<BaseVariableState>());
            var variable = (BaseVariableState)((NodeHandle)handle).Node!;
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(request).ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            Assert.That(admitted.Output.Operation.State,
                Is.EqualTo(duringWait ? ServiceOperationState.Pending : ServiceOperationState.OutcomeAvailable));
            if (!duringWait)
            {
                Assert.That(admitted.Output.Result.TryGetValue(out ReadResponse? initial), Is.True);
                Assert.That(initial!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            admitted.Dispose();
            Task<CallResponse>? waiting = null;
            if (duringWait)
            {
                await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
                waiting = test.SendAsync<CallResponse>(test.Call(RseBrowseNames.GetResult, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ServerToken = token,
                    WaitTime = 5000
                })).AsTask();
                await policy.CollectionEntered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
                Assert.That(waiting.IsCompleted, Is.False);
            }
            if (callback)
            {
                variable.OnReadUserAccessLevel = (context, node, ref access) =>
                {
                    access = AccessLevels.CurrentWrite;
                    return ServiceResult.Good;
                };
            }
            else
            {
                variable.UserAccessLevel = AccessLevels.CurrentWrite;
            }
            ReadResponse ordinary = await test.SendAsync<ReadResponse>(new ReadRequest
            {
                RequestHeader = test.NewHeader(),
                NodesToRead = [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }]
            }).ConfigureAwait(false);
            Assert.That(ordinary.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            ordinary.Reuse();
            test.Server.Scenario.Release();
            CallResponse denied = waiting != null
                ? await waiting.ConfigureAwait(false)
                : await test.SendAsync<CallResponse>(test.Call(RseBrowseNames.GetResult, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ServerToken = token
                })).ConfigureAwait(false);
            AssertMethodFailure(denied, StatusCodes.BadUserAccessDenied);
            denied.Reuse();
            Assert.That(test.Server.Scenario.BlobReads, Is.EqualTo(1));
            variable.OnReadUserAccessLevel = null;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            using ManagerReply<GetResultResponseDataType> recovered = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(recovered.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(recovered.Output.Result.TryGetValue(out ReadResponse? result), Is.True);
            Assert.That(result!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Results[0].WrappedValue.TryGetValue(out ByteString value), Is.True);
            Assert.That(value, Is.EqualTo(test.Server.Scenario.Blob));
            Assert.That(test.Server.Scenario.BlobReads, Is.EqualTo(1));
        }

        /// <summary>
        /// A changed collection policy is rechecked after waiting and does not rewrite or release the outcome.
        /// </summary>
        [Test]
        public async Task CurrentCollectionAuthorizationIsRecheckedAfterWait()
        {
            var policy = new RevocablePolicy();
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true, policy: policy)
                .ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> submitted =
                await test.SubmitAsync(ExecutionTestContext.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = submitted.Output.Operation.ServerToken;
            submitted.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            policy.CollectionEntered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<CallResponse> waiting = test.SendAsync<CallResponse>(
                test.Call(RseBrowseNames.GetResult, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ServerToken = token,
                    WaitTime = 5000
                })).AsTask();
            await policy.CollectionEntered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            policy.Deny(AsyncServiceAccess.Collect);
            test.Server.Release();
            CallResponse denied = await waiting.ConfigureAwait(false);
            AssertMethodFailure(denied, StatusCodes.BadUserAccessDenied);
            denied.Reuse();
            policy.Allow();
            using ManagerReply<GetResultResponseDataType> recovered = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(recovered.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(recovered.Output.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
        }

        /// <summary>
        /// The policy can deny stopping work while still allowing the separate abandonment action.
        /// </summary>
        [Test]
        public async Task ControlAuthorizationIsActionSpecific()
        {
            var policy = new RevocablePolicy();
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true, policy: policy)
                .ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> submitted =
                await test.SubmitAsync(ExecutionTestContext.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = submitted.Output.Operation.ServerToken;
            submitted.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            policy.Deny(AsyncServiceAccess.Cancel);
            await test.RejectAsync(RseBrowseNames.Cancel, new OperationControlRequestDataType
            {
                ServerToken = token
            }, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            Assert.That(test.Server.CancellationRequested.Task.IsCompleted, Is.False);
            using ManagerReply<ControlResponseDataType> abandoned = await test.ControlAsync(
                token, RseBrowseNames.Abandon).ConfigureAwait(false);
            Assert.That(abandoned.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(abandoned.Output.Operation.CancellationRequested, Is.False);
            abandoned.Dispose();
            test.Server.Release();
        }

        /// <summary>
        /// Original headers stay byte-for-byte intact while owned work uses a separate effective Session context.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OriginalHeaderIsImmutableAndOwnedContextCopiesSessionCoordinates(bool explicitAuthentication)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            ActivateSessionResponse activated = await test.SendAsync<ActivateSessionResponse>(new ActivateSessionRequest
            {
                RequestHeader = test.NewHeader(),
                LocaleIds = ["en-US", "de-DE"]
            }).ConfigureAwait(false);
            activated.Reuse();
            RequestHeader exchange = test.NewHeader();
            SubmitRequestDataType request = ExecutionTestContext.ReadRequest();
            Assert.That(request.Request.TryGetValue(out ReadRequest? original), Is.True);
            original!.RequestHeader.AuthenticationToken =
                explicitAuthentication ? exchange.AuthenticationToken : default;
            original.RequestHeader.Timestamp = test.Clock.GetUtcNow().UtcDateTime;
            original.RequestHeader.AuditEntryId = "immutable-original-header";
            original.RequestHeader.ReturnDiagnostics =
                (uint)(DiagnosticsMasks.All & ~DiagnosticsMasks.UserPermissionAdditionalInfo);
            byte[] frozen = BinaryEncoder.EncodeMessage(original.RequestHeader, test.Server.MessageContext);
            using ManagerReply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(request, exchange).ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            Assert.That(admitted.Output.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            admitted.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            OperationContext owned = test.Server.OwnedReadContext!;
            Assert.That(owned.AuthenticationToken, Is.EqualTo(exchange.AuthenticationToken));
            Assert.That(owned.ClientHandle, Is.EqualTo(17u));
            Assert.That(owned.AuditEntryId, Is.EqualTo("immutable-original-header"));
            Assert.That(owned.DiagnosticsMask,
                Is.EqualTo(DiagnosticsMasks.All & ~DiagnosticsMasks.UserPermissionAdditionalInfo));
            Assert.That(owned.OperationDeadline, Is.EqualTo(DateTime.MaxValue));
            Assert.That(owned.PreferredLocales, Is.EqualTo(new ArrayOf<string>(["en-US", "de-DE"])));
            ActivateSessionResponse changed = await test.SendAsync<ActivateSessionResponse>(new ActivateSessionRequest
            {
                RequestHeader = test.NewHeader(),
                LocaleIds = ["fr-FR"]
            }).ConfigureAwait(false);
            changed.Reuse();
            Assert.That(owned.Session.PreferredLocales, Is.EqualTo(new ArrayOf<string>(["fr-FR"])));
            Assert.That(owned.PreferredLocales, Is.EqualTo(new ArrayOf<string>(["en-US", "de-DE"])));
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            Assert.That(BinaryEncoder.EncodeMessage(
                test.Server.CompletedOriginalHeader!, test.Server.MessageContext), Is.EqualTo(frozen));
            Assert.That(BinaryEncoder.EncodeMessage(original.RequestHeader, test.Server.MessageContext),
                Is.EqualTo(frozen));
            using ManagerReply<GetResultResponseDataType> result = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(result.Output.Result.TryGetValue(out ReadResponse? read), Is.True);
            Assert.That(read!.ResponseHeader.RequestHandle, Is.EqualTo(17u));
            Assert.That(read.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A failed sibling Method neither faults the outer Call nor revokes the other selected outcomes.
        /// </summary>
        [Test]
        public async Task WholeCallBatchRetainsBothOutcomesAcrossReleaseAndHistoryExpiry()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            var tokens = new ByteString[2];
            byte[][] originals = new byte[2][];
            ByteString[] values = [ByteString.From([1, 2, 3]), ByteString.From([4, 5, 6, 7])];
            for (int index = 0; index < values.Length; index++)
            {
                test.Server.Scenario.Blob = values[index];
                using ManagerReply<SubmitResponseDataType> admitted =
                    await test.SubmitAsync(test.BlobRequest()).ConfigureAwait(false);
                tokens[index] = admitted.Output.Operation.ServerToken;
                Assert.That(admitted.Output.Result.TryGetValue(out ReadResponse? original), Is.True);
                originals[index] = BinaryEncoder.EncodeMessage(original!, test.Server.MessageContext);
            }
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            CallMethodRequest Collect(ByteString token)
            {
                return test.Method(RseBrowseNames.GetResult, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ServerToken = token
                });
            }
            var request = new CallRequest
            {
                RequestHeader = test.NewHeader(),
                MethodsToCall = [Collect(tokens[0]), Collect(ByteString.Empty), Collect(tokens[1])]
            };
            CallResponse response = await test.SendAsync<CallResponse>(request).ConfigureAwait(false);
            try
            {
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(request.RequestHeader.RequestHandle));
                Assert.That(response.Results.Count, Is.EqualTo(3));
                Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(response.Results[1].OutputArguments, Is.Empty);
                GetResultResponseDataType[] selected =
                [
                    ReadOutput<GetResultResponseDataType>(response.Results[0], test.Server.MessageContext),
                    ReadOutput<GetResultResponseDataType>(response.Results[2], test.Server.MessageContext)
                ];
                using ManagerReply<ControlResponseDataType> acknowledged = await test.ControlAsync(
                    tokens[0], RseBrowseNames.Acknowledge).ConfigureAwait(false);
                using ManagerReply<ControlResponseDataType> abandoned = await test.ControlAsync(
                    tokens[1], RseBrowseNames.Abandon).ConfigureAwait(false);
                Assert.That(acknowledged.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                Assert.That(abandoned.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                acknowledged.Dispose();
                abandoned.Dispose();
                test.Clock.Advance(TimeSpan.FromMilliseconds(5000));
                AsyncServiceExecutionStatistics statistics = test.Server.ExecutionFactory.Provider!.GetStatistics();
                Assert.That(statistics.TerminalRecordCount, Is.Zero);
                Assert.That(statistics.RetainedOutcomeCount, Is.Zero);
                Assert.That(statistics.RetainedOutcomeBytes, Is.GreaterThan(0));
                for (int index = 0; index < selected.Length; index++)
                {
                    Assert.That(selected[index].Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                    Assert.That(selected[index].Operation.ServerToken, Is.EqualTo(tokens[index]));
                    Assert.That(selected[index].Result.TryGetValue(out ReadResponse? retained), Is.True);
                    Assert.That(retained!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(retained.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(retained.Results[0].WrappedValue.TryGetValue(out ByteString value), Is.True);
                    Assert.That(value, Is.EqualTo(values[index]));
                    Assert.That(BinaryEncoder.EncodeMessage(retained, test.Server.MessageContext),
                        Is.EqualTo(originals[index]));
                }
            }
            finally
            {
                response.Reuse();
            }
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().RetainedOutcomeBytes, Is.Zero);
            Assert.That(test.Server.Scenario.BlobReads, Is.EqualTo(2));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(2));
        }

        /// <summary>
        /// A failed manager sibling cannot revoke a successful admission in the same ordinary Call.
        /// </summary>
        [Test]
        public async Task FailedManagerSiblingDoesNotRollBackAdmission()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            SubmitRequestDataType submission = ExecutionTestContext.ReadRequest();
            var request = new CallRequest
            {
                RequestHeader = test.NewHeader(),
                MethodsToCall =
                [
                    test.Method(RseBrowseNames.GetResult, new GetResultRequestDataType
                    {
                        RecoveryMode = ServiceOperationRecoveryMode.Session,
                        ServerToken = ByteString.Empty
                    }),
                    test.Method(RseBrowseNames.Submit, submission)
                ]
            };
            CallResponse response = await test.SendAsync<CallResponse>(request).ConfigureAwait(false);
            ByteString token;
            try
            {
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results, Has.Count.EqualTo(2));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(response.Results[0].OutputArguments, Is.Empty);
                SubmitResponseDataType admitted =
                    ReadOutput<SubmitResponseDataType>(response.Results[1], test.Server.MessageContext);
                Assert.That(admitted.Operation.ClientOperationId, Is.EqualTo(submission.ClientOperationId));
                Assert.That(admitted.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
                Assert.That(admitted.Operation.ExecutionAllowance, Is.EqualTo(submission.ExecutionAllowance));
                Assert.That(admitted.Operation.ResultRetentionTime, Is.EqualTo(submission.ResultRetentionTime));
                token = admitted.Operation.ServerToken;
                Assert.That(token.Length, Is.EqualTo(32));
            }
            finally
            {
                response.Reuse();
            }
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            using ManagerReply<GetResultResponseDataType> collected = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(collected.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(collected.Output.Operation.ClientOperationId, Is.EqualTo(submission.ClientOperationId));
            Assert.That(collected.Output.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(collected.Output.Result.TryGetValue(out ReadResponse? original), Is.True);
            Assert.That(original!.ResponseHeader.RequestHandle, Is.EqualTo(17u));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AdmittedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Distinct token controls in either batch order retain their effects without a transactional interpretation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BatchedCancelAndAbandonHaveIndependentEffects(bool cancelFirst)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            using ManagerReply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(ExecutionTestContext.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            CallMethodRequest cancel = test.Method(RseBrowseNames.Cancel,
                new OperationControlRequestDataType { ServerToken = token });
            CallMethodRequest abandon = test.Method(RseBrowseNames.Abandon,
                new OperationControlRequestDataType { ServerToken = token });
            CallResponse response = await test.SendAsync<CallResponse>(new CallRequest
            {
                RequestHeader = test.NewHeader(),
                MethodsToCall = cancelFirst ? [cancel, abandon] : [abandon, cancel]
            }).ConfigureAwait(false);
            try
            {
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results, Has.Count.EqualTo(2));
                foreach (CallMethodResult item in response.Results)
                {
                    ControlResponseDataType applied =
                        ReadOutput<ControlResponseDataType>(item, test.Server.MessageContext);
                    Assert.That(applied.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                    Assert.That(applied.Operation.ServerToken, Is.EqualTo(token));
                }
            }
            finally
            {
                response.Reuse();
            }
            await test.Server.CancellationRequested.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            using ManagerReply<GetResultResponseDataType> state = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(state.Output.Operation.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(state.Output.Operation.CancellationRequested, Is.True);
            Assert.That(state.Output.Result.IsNull, Is.True);
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            state.Dispose();
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            using ManagerReply<ControlResponseDataType> stopped =
                await test.ControlAsync(token, RseBrowseNames.Cancel).ConfigureAwait(false);
            Assert.That(stopped.Output.Result, Is.EqualTo(ServiceOperationControlResult.NotApplicable));
        }

        /// <summary>
        /// A shared outer cancellation ends a mixed zero/positive-wait batch without changing admitted E/R or work.
        /// </summary>
        [Test]
        public async Task MixedWaitBatchSharesOuterCancellationWithoutCancellingOwnedWork()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            SubmitRequestDataType submission = ExecutionTestContext.ReadRequest();
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(submission)
                .ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            var parked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnCapacity(ResourceIsolationStage stage)
            {
                if (stage == ResourceIsolationStage.RequestExecution &&
                    test.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest) == 2)
                {
                    parked.TrySetResult(true);
                }
            }
            test.Isolation.CapacityAvailable += OnCapacity;
            Task<IServiceResponse>? waiting = null;
            IServiceResponse? response = null;
            try
            {
                var batch = new CallRequest
                {
                    RequestHeader = test.NewHeader(),
                    MethodsToCall =
                    [
                        test.Method(RseBrowseNames.GetResult, new GetResultRequestDataType
                        {
                            RecoveryMode = ServiceOperationRecoveryMode.Session,
                            ServerToken = token,
                            WaitTime = 0
                        }),
                        test.Method(RseBrowseNames.GetResult, new GetResultRequestDataType
                        {
                            RecoveryMode = ServiceOperationRecoveryMode.Session,
                            ServerToken = token,
                            WaitTime = 5000
                        })
                    ]
                };
                waiting = test.SendAsync<IServiceResponse>(batch).AsTask();
                await parked.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
                Assert.That(waiting.IsCompleted, Is.False,
                    "The zero-wait sibling does not obtain a separate outer response.");
                test.Clock.Advance(TimeSpan.FromMilliseconds(1));
                CancelResponse cancelled = await test.SendAsync<CancelResponse>(new CancelRequest
                {
                    RequestHeader = test.NewHeader(),
                    RequestHandle = batch.RequestHeader.RequestHandle
                }).ConfigureAwait(false);
                try
                {
                    Assert.That(cancelled.CancelCount, Is.EqualTo(1));
                }
                finally
                {
                    cancelled.Reuse();
                }
                response = await waiting.ConfigureAwait(false);
                Assert.That(response, Is.InstanceOf<ServiceFault>());
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(batch.RequestHeader.RequestHandle));
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                Assert.That(test.Server.CancellationRequested.Task.IsCompleted, Is.False);
                await test.ReleaseWorkAsync().ConfigureAwait(false);
                using ManagerReply<GetResultResponseDataType> collected =
                    await test.GetAsync(token).ConfigureAwait(false);
                Assert.That(collected.Output.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                Assert.That(collected.Output.Operation.ExecutionAllowance, Is.EqualTo(submission.ExecutionAllowance));
                Assert.That(collected.Output.Operation.ResultRetentionTime, Is.EqualTo(submission.ResultRetentionTime));
                Assert.That(collected.Output.Operation.ResultRemaining, Is.EqualTo(submission.ResultRetentionTime));
                Assert.That(collected.Output.Operation.CancellationRequested, Is.False);
                Assert.That(collected.Output.Result.TryGetValue(out ReadResponse? original), Is.True);
                Assert.That(original!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            }
            finally
            {
                test.Isolation.CapacityAvailable -= OnCapacity;
                test.Server.Release();
                if (waiting != null)
                {
                    response ??= await waiting.ConfigureAwait(false);
                }
                (response as IPooledEncodeable)?.Reuse();
            }
        }

        /// <summary>
        /// Two individually deliverable outcomes can exceed the whole-Call limit without leaking their selections.
        /// </summary>
        [TestCase(RequestEncoding.Binary)]
        [TestCase(RequestEncoding.Json)]
        public async Task OversizedWholeCallReleasesSelectionsWithoutRewritingOutcomes(RequestEncoding mapping)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false, mapping: mapping).ConfigureAwait(false);
            test.Server.Scenario.Blob = new ByteString(new byte[35_000]);
            using ManagerReply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(test.BlobRequest()).ConfigureAwait(false);
            ByteString token = admitted.Output.Operation.ServerToken;
            Assert.That(admitted.Output.Result.TryGetValue(out ReadResponse? original), Is.True);
            byte[] frozen = BinaryEncoder.EncodeMessage(original!, test.Server.MessageContext);
            admitted.Dispose();
            var selector = new GetResultRequestDataType
            {
                RecoveryMode = ServiceOperationRecoveryMode.Session,
                ServerToken = token
            };
            IServiceResponse rejected = await test.SendAsync<IServiceResponse>(new CallRequest
            {
                RequestHeader = test.NewHeader(),
                MethodsToCall =
                [
                    test.Method(RseBrowseNames.GetResult, selector),
                    test.Method(RseBrowseNames.GetResult, selector)
                ]
            }).ConfigureAwait(false);
            Assert.That(rejected, Is.InstanceOf<ServiceFault>());
            Assert.That(rejected.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadResponseTooLarge));
            ((ServiceFault)rejected).Reuse();
            using ManagerReply<GetResultResponseDataType> recovered = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(recovered.Output.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(recovered.Output.Result.TryGetValue(out ReadResponse? retained), Is.True);
            Assert.That(BinaryEncoder.EncodeMessage(retained!, test.Server.MessageContext), Is.EqualTo(frozen));
            recovered.Dispose();
            using ManagerReply<ControlResponseDataType> acknowledged = await test.ControlAsync(
                token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(acknowledged.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            acknowledged.Dispose();
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(5000));
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().RetainedOutcomeBytes, Is.Zero);
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Ordinary and retained BrowseNext release responses are empty after actually releasing their cursor.
        /// </summary>
        [TestCase(false, RequestEncoding.Binary)]
        [TestCase(true, RequestEncoding.Binary)]
        [TestCase(false, RequestEncoding.Json)]
        [TestCase(true, RequestEncoding.Json)]
        public async Task BrowseNextReleasePreservesEmptyOriginalResponse(bool recoverable, RequestEncoding mapping)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false, mapping: mapping).ConfigureAwait(false);
            BrowseResponse browse = await test.SendAsync<BrowseResponse>(new BrowseRequest
            {
                RequestHeader = test.NewHeader(),
                RequestedMaxReferencesPerNode = 1,
                NodesToBrowse =
                [
                    new BrowseDescription
                    {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ]
            }).ConfigureAwait(false);
            ByteString cursor;
            try
            {
                Assert.That(browse.Results, Has.Count.EqualTo(1));
                Assert.That(browse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                cursor = browse.Results[0].ContinuationPoint.Copy();
                Assert.That(cursor.IsEmpty, Is.False);
            }
            finally
            {
                browse.Reuse();
            }
            RequestHeader header = test.NewHeader();
            header.ReturnDiagnostics = (uint)DiagnosticsMasks.OperationAll;
            var release = new BrowseNextRequest
            {
                RequestHeader = header,
                ReleaseContinuationPoints = true,
                ContinuationPoints = [cursor, ByteString.From("unknown-cursor"u8)]
            };
            BrowseNextResponse released;
            if (recoverable)
            {
                using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                    new SubmitRequestDataType
                    {
                        ClientOperationId = Uuid.NewUuid(),
                        RecoveryMode = ServiceOperationRecoveryMode.Session,
                        ExecutionAllowance = 10_000,
                        ResultRetentionTime = 5000,
                        WaitTime = 5000,
                        Request = new ExtensionObject(release)
                    }).ConfigureAwait(false);
                Assert.That(admitted.Output.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
                Assert.That(admitted.Output.Result.TryGetValue(out BrowseNextResponse? original), Is.True);
                released = CoreUtils.Clone(original)!;
                using ManagerReply<GetResultResponseDataType> collected =
                    await test.GetAsync(admitted.Output.Operation.ServerToken).ConfigureAwait(false);
                Assert.That(collected.Output.Result.TryGetValue(out BrowseNextResponse? retained), Is.True);
                Assert.That(BinaryEncoder.EncodeMessage(retained!, test.Server.MessageContext),
                    Is.EqualTo(BinaryEncoder.EncodeMessage(released, test.Server.MessageContext)));
                using ManagerReply<ControlResponseDataType> acknowledged = await test.ControlAsync(
                    admitted.Output.Operation.ServerToken, RseBrowseNames.Acknowledge).ConfigureAwait(false);
                Assert.That(acknowledged.Output.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            }
            else
            {
                released = await test.SendAsync<BrowseNextResponse>(release).ConfigureAwait(false);
            }
            try
            {
                Assert.That(released.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(released.ResponseHeader.RequestHandle, Is.EqualTo(header.RequestHandle));
                Assert.That(released.Results, Is.Empty);
                Assert.That(released.DiagnosticInfos, Is.Empty);
                BrowseNextResponse resumed = await test.SendAsync<BrowseNextResponse>(new BrowseNextRequest
                {
                    RequestHeader = test.NewHeader(),
                    ContinuationPoints = [cursor]
                }).ConfigureAwait(false);
                try
                {
                    Assert.That(resumed.Results, Has.Count.EqualTo(1));
                    Assert.That(resumed.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AdmittedCount,
                        Is.EqualTo(recoverable ? 1 : 0));
                }
                finally
                {
                    resumed.Reuse();
                }
            }
            finally
            {
                released.Reuse();
            }
        }

        /// <summary>
        /// Successful browse-source and intermediate-path permissions remain part of retained-result authorization.
        /// </summary>
        [TestCase("browse")]
        [TestCase("translate-start")]
        [TestCase("translate-intermediate")]
        public async Task RevokedBrowseSourcePermissionDeniesRetainedCollection(string service)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            MethodState target = test.Server.Scenario.DeclaredMethod!;
            NodeState source = target.Parent!;
            var header = new RequestHeader { RequestHandle = 73 };
            IServiceRequest request;
            if (service == "browse")
            {
                request = new BrowseRequest
                {
                    RequestHeader = header,
                    NodesToBrowse =
                    [
                        new BrowseDescription
                        {
                            NodeId = source.NodeId,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                            IncludeSubtypes = true,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ]
                };
            }
            else
            {
                var last = new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IncludeSubtypes = true,
                    TargetName = target.BrowseName
                };
                request = new TranslateBrowsePathsToNodeIdsRequest
                {
                    RequestHeader = header,
                    BrowsePaths =
                    [
                        new BrowsePath
                        {
                            StartingNode = service == "translate-start" ? source.NodeId : ObjectIds.ObjectsFolder,
                            RelativePath = new RelativePath
                            {
                                Elements = service == "translate-start" ? [last] :
                                [
                                    new RelativePathElement
                                    {
                                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                                        IncludeSubtypes = true,
                                        TargetName = source.BrowseName
                                    },
                                    last
                                ]
                            }
                        }
                    ]
                };
            }
            using ManagerReply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                new SubmitRequestDataType
                {
                    ClientOperationId = Uuid.NewUuid(),
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    Request = new ExtensionObject(request),
                    ExecutionAllowance = 10_000,
                    ResultRetentionTime = 5000,
                    WaitTime = 5000
                }).ConfigureAwait(false);
            Assert.That(admitted.Output.Result.TryGetValue(out IServiceResponse? original), Is.True);
            if (original is BrowseResponse browse)
            {
                Assert.That(browse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(browse.Results[0].References, Is.Not.Empty);
            }
            else
            {
                var translated = (TranslateBrowsePathsToNodeIdsResponse)original!;
                Assert.That(translated.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(translated.Results[0].Targets, Has.Count.EqualTo(1));
                Assert.That(translated.Results[0].Targets[0].TargetId, Is.EqualTo(new ExpandedNodeId(target.NodeId)));
            }
            byte[] frozen = BinaryEncoder.EncodeMessage(original!, test.Server.MessageContext);
            ByteString token = admitted.Output.Operation.ServerToken.Copy();
            ArrayOf<RolePermissionType> previous = source.RolePermissions;
            try
            {
                source.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                        Permissions = (uint)PermissionType.Browse
                    }
                ];
                await test.RejectAsync(RseBrowseNames.GetResult, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ServerToken = token
                }, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            }
            finally
            {
                source.RolePermissions = previous;
            }
            using ManagerReply<GetResultResponseDataType> collected =
                await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(collected.Output.Result.TryGetValue(out IServiceResponse? retained), Is.True);
            Assert.That(BinaryEncoder.EncodeMessage(retained!, test.Server.MessageContext), Is.EqualTo(frozen));
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AdmittedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Queue policy recognizes only installed manager identities and classifies the complete nonempty batch.
        /// </summary>
        [Test]
        public async Task ManagerBatchPolicyProtectsOnlyInstalledControlCalls()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            AsyncServiceExecutionProvider provider = test.Server.ExecutionFactory.Provider!;
            CallMethodRequest submit = test.Method(RseBrowseNames.Submit, ExecutionTestContext.ReadRequest());
            CallMethodRequest get = test.Method(RseBrowseNames.GetResult, new GetResultRequestDataType
            {
                RecoveryMode = ServiceOperationRecoveryMode.Session,
                ClientOperationId = Uuid.NewUuid()
            });
            var token = new OperationControlRequestDataType { ServerToken = ByteString.From([1]) };
            CallMethodRequest acknowledge = test.Method(RseBrowseNames.Acknowledge, token);
            CallMethodRequest cancel = test.Method(RseBrowseNames.Cancel, token);
            CallMethodRequest abandon = test.Method(RseBrowseNames.Abandon, token);
            var ordinary = new CallMethodRequest
            {
                ObjectId = ObjectIds.Server,
                MethodId = MethodIds.Server_GetMonitoredItems
            };
            CallMethodRequest wrongObject = CoreUtils.Clone(get)!;
            wrongObject.ObjectId = ObjectIds.Server;
            CallMethodRequest wrongMethod = CoreUtils.Clone(get)!;
            wrongMethod.MethodId = new NodeId(Guid.NewGuid(), get.MethodId.NamespaceIndex);
            (ArrayOf<CallMethodRequest> Items, bool Control, bool Park)[] cases =
            [
                ([], false, false),
                ([submit], false, true),
                ([get], true, true),
                ([acknowledge], true, false),
                ([cancel], true, false),
                ([abandon], true, false),
                ([acknowledge, cancel, abandon], true, false),
                ([get, acknowledge], true, true),
                ([submit, get], false, true),
                ([ordinary], false, false),
                ([get, ordinary], false, false),
                ([ordinary, acknowledge], false, false),
                ([wrongObject], false, false),
                ([wrongMethod], false, false)
            ];
            foreach ((ArrayOf<CallMethodRequest> items, bool control, bool park) in cases)
            {
                var call = new CallRequest { RequestHeader = test.NewHeader(), MethodsToCall = items };
                Assert.That(provider.IsControlRequest(call), Is.EqualTo(control));
                Assert.That(provider.CanPark(call), Is.EqualTo(park));
            }
            Assert.That(provider.IsControlRequest(new ReadRequest()), Is.False);
            Assert.That(provider.CanPark(new ReadRequest()), Is.False);
            Assert.That(provider.GetStatistics().AdmittedCount, Is.Zero);
            Assert.That(test.Server.InvocationCount, Is.Zero);
        }

        /// <summary>
        /// Supplies a real Server/Session/endpoint, fake elapsed time and a bounded owned-handler gate.
        /// </summary>
        private sealed class ExecutionTestContext : IAsyncDisposable
        {
            private ExecutionTestContext(
                ServerFixture<GatedServer> fixture, GatedServer server,
                RequestHeader header, SecureChannelContext channel, ExecutionClock clock)
            {
                m_fixture = fixture;
                Server = server;
                m_header = header;
                m_channel = channel;
                Clock = clock;
                m_endpoint = new SessionEndpoint(server);
                Isolation = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
            }

            public GatedServer Server { get; }
            public ExecutionClock Clock { get; }
            public DefaultServerResourceIsolationProvider Isolation { get; }
            public CancellationToken Timeout => m_timeout.Token;

            public static async Task<ExecutionTestContext> CreateAsync(
                bool delayRead, bool enabled = true, IAsyncServiceAuthorizationPolicy? policy = null,
                bool auditing = true, bool userNamePolicy = false,
                ResourceIsolationStage? ownerBoundStage = null, RequestEncoding mapping = RequestEncoding.Binary,
                bool aggregateBound = false, bool signOnlyAnonymous = false)
            {
                var clock = new ExecutionClock(new DateTimeOffset(
                    (DateTime.UtcNow.Ticks / 10 * 10) + 7, TimeSpan.Zero));
                var fixture = new ServerFixture<GatedServer>(telemetry =>
                {
                    var server = new GatedServer(telemetry, clock, delayRead, enabled, policy);
                    if (ownerBoundStage is { } stage)
                    {
                        long unit = stage == ResourceIsolationStage.RequestQueueBytes ? 65_536 : 1;
                        // Three retained message-sized leases plus the separate outer queued Call and history.
                        long shared = stage == ResourceIsolationStage.RequestQueueBytes ? (4 * unit) + 1024 : 2;
                        server.ResourceIsolationOptions.Stages =
                        [
                            new ResourceIsolationStageOptions
                            {
                                Stage = stage,
                                OwnerHardLimit = aggregateBound ? null : shared,
                                Capacity = aggregateBound ? shared + (3 * unit) : null,
                                BootstrapReserved = aggregateBound ? unit : null,
                                ReconnectReserved = aggregateBound ? unit : null,
                                ControlReserved = aggregateBound ? unit : null
                            }
                        ];
                    }
                    return server;
                })
                {
                    SecurityNone = true,
                    AutoAccept = true,
                    AllNodeManagers = false
                };
                try
                {
                    await fixture.LoadConfigurationAsync().ConfigureAwait(false);
                    fixture.Config.TransportQuotas!.MaxMessageSize = 65_536;
                    fixture.Config.ServerConfiguration!.AuditingEnabled = auditing;
                    if (userNamePolicy)
                    {
                        fixture.Config.ServerConfiguration.UserTokenPolicies =
                        [
                            new UserTokenPolicy
                            {
                                PolicyId = "anonymous",
                                TokenType = UserTokenType.Anonymous,
                                SecurityPolicyUri = SecurityPolicies.None
                            },
                            new UserTokenPolicy
                            {
                                PolicyId = "synthetic-user",
                                TokenType = UserTokenType.UserName,
                                SecurityPolicyUri = SecurityPolicies.None
                            }
                        ];
                    }
                    GatedServer server = await fixture.StartAsync().ConfigureAwait(false);
                    (RequestHeader header, SecureChannelContext channel) =
                        await server.CreateAndActivateSessionAsync(
                            "AsyncLifetimeTests", useSecurity: signOnlyAnonymous,
                            clientApplicationUri: signOnlyAnonymous ? "urn:opcfoundation:tests:async-lifetimes" : null)
                            .ConfigureAwait(false);
                    return new ExecutionTestContext(fixture, server, header,
                        mapping == channel.MessageEncoding ? channel :
                            new SecureChannelContext(
                                channel.SecureChannelId, channel.EndpointDescription, mapping), clock);
                }
                catch
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public RequestHeader NewHeader()
            {
                return new RequestHeader
                {
                    AuthenticationToken = m_header.AuthenticationToken,
                    RequestHandle = (uint)Interlocked.Increment(ref m_nextHandle),
                    Timestamp = Clock.GetUtcNow().UtcDateTime,
                    TimeoutHint = 10_000
                };
            }

            public static SubmitRequestDataType ReadRequest(
                double execution = 10_000, double retention = 5000, double wait = 0)
            {
                return new SubmitRequestDataType
                {
                    ClientOperationId = Uuid.NewUuid(),
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ExecutionAllowance = execution,
                    ResultRetentionTime = retention,
                    WaitTime = wait,
                    Request = new ExtensionObject(new ReadRequest
                    {
                        RequestHeader = new RequestHeader { RequestHandle = 17, TimeoutHint = 1 },
                        TimestampsToReturn = TimestampsToReturn.Neither,
                        NodesToRead =
                        [
                            new ReadValueId
                            {
                                NodeId = VariableIds.Server_ServerStatus_State,
                                AttributeId = Attributes.Value
                            }
                        ]
                    })
                };
            }

            public async ValueTask<T> SendAsync<T>(IServiceRequest request) where T : class, IServiceResponse
            {
                IServiceResponse response = await m_endpoint.ProcessRequestAsync(m_channel, request, Timeout)
                    .ConfigureAwait(false);
                Assert.That(response, Is.InstanceOf<T>(), $"Service returned {response.ResponseHeader.ServiceResult}.");
                return (T)response;
            }

            public SubmitRequestDataType BlobRequest()
            {
                SubmitRequestDataType request = ReadRequest(wait: 5000);
                Assert.That(request.Request.TryGetValue(out ReadRequest? read), Is.True);
                read!.NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = ExpandedNodeId.ToNodeId(
                            Model.VariableIds.Probe_Blob, Server.MessageContext.NamespaceUris),
                        AttributeId = Attributes.Value
                    }
                ];
                return request;
            }

            public RecoverableExecutionManagerState Manager =>
                Server.CurrentInstance.DiagnosticsNodeManager.FindPredefinedNode<RecoverableExecutionManagerState>(
                    ExpandedNodeId.ToNodeId(
                        RseObjectIds.RecoverableExecutionManager, Server.MessageContext.NamespaceUris));

            public CallMethodRequest Method(string name, IEncodeable argument)
            {
                MethodState method = name switch
                {
                    RseBrowseNames.Submit => Manager.Submit!,
                    RseBrowseNames.GetResult => Manager.GetResult!,
                    RseBrowseNames.Acknowledge => Manager.Acknowledge!,
                    RseBrowseNames.Cancel => Manager.Cancel!,
                    RseBrowseNames.Abandon => Manager.Abandon!,
                    _ => throw new ArgumentException("Unknown manager Method.", nameof(name))
                };
                return new CallMethodRequest
                {
                    ObjectId = Manager.NodeId,
                    MethodId = method.NodeId,
                    InputArguments = [Variant.FromStructure(argument)]
                };
            }

            public CallRequest Call(string name, IEncodeable argument, RequestHeader? header = null)
            {
                return new CallRequest
                {
                    RequestHeader = header ?? NewHeader(),
                    MethodsToCall = [Method(name, argument)]
                };
            }

            public ValueTask<ManagerReply<SubmitResponseDataType>> SubmitAsync(
                SubmitRequestDataType request, RequestHeader? header = null)
            {
                return InvokeAsync<SubmitResponseDataType>(RseBrowseNames.Submit, request, header);
            }

            public ValueTask<ManagerReply<GetResultResponseDataType>> GetAsync(ByteString token)
            {
                return GetAsync(new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Session,
                    ServerToken = token
                });
            }

            public ValueTask<ManagerReply<GetResultResponseDataType>> GetAsync(GetResultRequestDataType request)
            {
                return InvokeAsync<GetResultResponseDataType>(RseBrowseNames.GetResult, request);
            }

            public ValueTask<ManagerReply<ControlResponseDataType>> ControlAsync(ByteString token, string method)
            {
                return InvokeAsync<ControlResponseDataType>(
                    method, new OperationControlRequestDataType { ServerToken = token });
            }

            public async ValueTask<ManagerReply<T>> InvokeAsync<T>(
                string method, IEncodeable request, RequestHeader? header = null) where T : class, IEncodeable
            {
                CallResponse response = await SendAsync<CallResponse>(Call(method, request, header))
                    .ConfigureAwait(false);
                return new ManagerReply<T>(response, Server.MessageContext);
            }

            public async Task RejectAsync(string method, IEncodeable request, StatusCode expectedStatus)
            {
                CallResponse response = await SendAsync<CallResponse>(Call(method, request)).ConfigureAwait(false);
                try
                {
                    AssertMethodFailure(response, expectedStatus);
                }
                finally
                {
                    response.Reuse();
                }
            }

            public async Task ReleaseWorkAsync()
            {
                var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnReleased(ResourceIsolationStage stage)
                {
                    if (stage == ResourceIsolationStage.RequestExecution &&
                        Isolation.GetUsage(stage) == 0)
                    {
                        stopped.TrySetResult(true);
                    }
                }
                Isolation.CapacityAvailable += OnReleased;
                try
                {
                    Server.Release();
                    OnReleased(ResourceIsolationStage.RequestExecution);
                    await stopped.Task.WaitAsync(Timeout).ConfigureAwait(false);
                }
                finally
                {
                    Isolation.CapacityAvailable -= OnReleased;
                }
            }

            public async Task WaitForUsageAsync(ResourceIsolationStage stage, long expected)
            {
                var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnReleased(ResourceIsolationStage released)
                {
                    if (released == stage && Isolation.GetUsage(stage) == expected)
                    {
                        ready.TrySetResult(true);
                    }
                }
                Isolation.CapacityAvailable += OnReleased;
                try
                {
                    OnReleased(stage);
                    await ready.Task.WaitAsync(Timeout).ConfigureAwait(false);
                }
                finally
                {
                    Isolation.CapacityAvailable -= OnReleased;
                }
            }

            public async ValueTask DisposeAsync()
            {
                Server.Release();
                Server.Scenario.Release();
                await m_fixture.StopAsync().ConfigureAwait(false);
                m_timeout.Dispose();
            }

            private readonly ServerFixture<GatedServer> m_fixture;
            private readonly RequestHeader m_header;
            private readonly SecureChannelContext m_channel;
            private readonly SessionEndpoint m_endpoint;
            private readonly CancellationTokenSource m_timeout = new(TimeSpan.FromSeconds(15));
            private int m_nextHandle;
        }

        /// <summary>
        /// Owns the real outer Call response, not the generated scalar selected from one Method result.
        /// </summary>
        /// <typeparam name="T">The generated manager response type.</typeparam>
        private sealed class ManagerReply<T> : IDisposable where T : class, IEncodeable
        {
            public ManagerReply(CallResponse response, IServiceMessageContext context)
            {
                Response = response;
                try
                {
                    Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(response.Results.Count, Is.EqualTo(1));
                    Output = ReadOutput<T>(response.Results[0], context);
                }
                catch
                {
                    response.Reuse();
                    throw;
                }
            }

            public CallResponse Response { get; }
            public T Output { get; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    Response.Reuse();
                }
            }

            private int m_disposed;
        }

        /// <summary>
        /// Checks the Method layer independently of its outer Call and decodes the real generated scalar.
        /// </summary>
        /// <typeparam name="T">The generated manager response type.</typeparam>
        private static T ReadOutput<T>(CallMethodResult result, IServiceMessageContext context)
            where T : class, IEncodeable
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            foreach (StatusCode status in result.InputArgumentResults)
            {
                Assert.That(status, Is.EqualTo(StatusCodes.Good));
            }
            Assert.That(result.OutputArguments.Count, Is.EqualTo(1));
            Assert.That(result.OutputArguments[0].TryGetValue(out ExtensionObject body), Is.True);
            Assert.That(body.TryGetValue(out T? output, context), Is.True);
            return output!;
        }

        /// <summary>
        /// A provider rejection is a failed Method item in a successful Call, never an invented ServiceFault.
        /// </summary>
        private static void AssertMethodFailure(CallResponse response, StatusCode expectedStatus)
        {
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(response.Results[0].OutputArguments, Is.Empty);
        }

        /// <summary>
        /// Keeps deterministic elapsed time independent of explicit wall-clock corrections.
        /// </summary>
        private sealed class ExecutionClock(DateTimeOffset start) : FakeTimeProvider(start)
        {
            /// <summary>
            /// Gets or sets a wall-clock-only correction without advancing elapsed time or firing timers.
            /// </summary>
            public TimeSpan UtcOffset { get; set; }

            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return base.GetUtcNow() + UtcOffset;
            }

            /// <inheritdoc/>
            public override long GetTimestamp()
            {
                return base.GetUtcNow().Ticks;
            }
        }

        /// <summary>
        /// Delays the ordinary validated Read handler to expose ownership and cancellation boundaries.
        /// </summary>
        private sealed class GatedServer : StandardServer
        {
            public GatedServer(
                ITelemetryContext telemetry, TimeProvider clock, bool delayRead,
                bool enabled, IAsyncServiceAuthorizationPolicy? policy)
                : base(telemetry, clock)
            {
                AsyncServices.Enabled = enabled;
                ExecutionFactory = new CapturingExecutionFactory(policy);
                AsyncServiceExecutionProviderFactory = ExecutionFactory;
                m_delayRead = delayRead;
                AddNodeManager(new AsyncExecutionScenarioFactory(Scenario));
            }

            public CapturingExecutionFactory ExecutionFactory { get; }

            public AsyncExecutionScenario Scenario { get; } = new();

            public Func<CancellationToken, ValueTask>? BeforeRead { get; set; }

            public OperationContext? OwnedReadContext { get; private set; }

            public RequestHeader? CompletedOriginalHeader { get; private set; }

            public TaskCompletionSource<DateTime> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> CancellationRequested { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int InvocationCount => Volatile.Read(ref m_invocations);

            public void Release()
            {
                m_released.TrySetResult(true);
            }

            /// <inheritdoc/>
            protected override ResponseHeader CreateResponse(RequestHeader requestHeader, StringTable stringTable)
            {
                if (OwnedReadContext is { } owned && requestHeader.RequestHandle == owned.ClientHandle)
                {
                    CompletedOriginalHeader = CoreUtils.Clone(requestHeader);
                }
                ResponseHeader response = base.CreateResponse(requestHeader, stringTable);
                response.Timestamp = TimeProvider.GetUtcNow().UtcDateTime;
                return response;
            }

            protected override async ValueTask OnRequestValidatedAsync(OperationContext context)
            {
                if (context.RequestType == RequestType.Read)
                {
                    if (context.IsOwnedInvocation)
                    {
                        OwnedReadContext = context;
                    }
                    Interlocked.Increment(ref m_invocations);
                    using CancellationTokenRegistration registration = context.CancellationToken.Register(
                        () => CancellationRequested.TrySetResult(true));
                    Entered.TrySetResult(context.OperationDeadline);
                    if (m_delayRead)
                    {
                        await m_released.Task.ConfigureAwait(false);
                    }
                    if (BeforeRead is { } beforeRead)
                    {
                        await beforeRead(context.CancellationToken).ConfigureAwait(false);
                    }
                }
                await base.OnRequestValidatedAsync(context).ConfigureAwait(false);
            }

            private readonly TaskCompletionSource<bool> m_released =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly bool m_delayRead;
            private int m_invocations;
        }

        /// <summary>
        /// Observes the public provider factory seam without looking up private state.
        /// </summary>
        private sealed class CapturingExecutionFactory(IAsyncServiceAuthorizationPolicy? policy) :
            IAsyncServiceExecutionProviderFactory
        {
            public AsyncServiceExecutionProvider? Provider { get; private set; }

            public IAsyncServiceExecutionProvider Create(
                StandardServer server,
                ApplicationConfiguration configuration,
                AsyncServiceExecutionOptions options,
                TimeProvider timeProvider)
            {
                Provider = new AsyncServiceExecutionProvider(server, configuration, options, timeProvider, policy);
                return Provider;
            }
        }

        /// <summary>
        /// Supplies deterministic external authorization changes at the documented policy boundary.
        /// </summary>
        private sealed class RevocablePolicy : IAsyncServiceAuthorizationPolicy
        {
            public TaskCompletionSource<bool>? CollectionEntered { get; set; }
            public TaskCompletionSource<bool>? SubmissionEntered { get; set; }
            public Task? SubmissionRelease { get; set; }

            public void Deny(AsyncServiceAccess access)
            {
                Volatile.Write(ref m_denied, (int)access);
            }

            public void Allow()
            {
                Volatile.Write(ref m_denied, -1);
            }

            public ValueTask AuthorizeAsync(
                OperationContext context, Uuid clientOperationId, NodeId serviceId,
                AsyncServiceAccess access, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (Volatile.Read(ref m_denied) == (int)access)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                if (access == AsyncServiceAccess.Collect)
                {
                    CollectionEntered?.TrySetResult(true);
                }
                if (access == AsyncServiceAccess.Submit)
                {
                    SubmissionEntered?.TrySetResult(true);
                    if (SubmissionRelease != null)
                    {
                        return new ValueTask(SubmissionRelease.WaitAsync(ct));
                    }
                }
                return default;
            }

            private int m_denied = -1;
        }

        /// <summary>
        /// Builds a fresh exchange header without reusing the original operation's request handle.
        /// </summary>
        private static RequestHeader Header(RequestHeader session, uint handle)
        {
            return new RequestHeader
            {
                AuthenticationToken = session.AuthenticationToken,
                RequestHandle = handle,
                Timestamp = DateTimeUtc.Now,
                TimeoutHint = 10_000
            };
        }

        private static readonly string[] s_submitFields =
        [
            "ClientOperationId", "RecoveryMode", "Request", "ExecutionAllowance", "ResultRetentionTime", "WaitTime"
        ];
    }
}

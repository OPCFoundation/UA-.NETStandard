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
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry)
            {
                AsyncServices = new AsyncServiceExecutionOptions { Enabled = true }
            })
            {
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = false
            };
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                (RequestHeader sessionHeader, SecureChannelContext channel) =
                    await server.CreateAndActivateSessionAsync("RecoverableRead").ConfigureAwait(false);
                var endpoint = new SessionEndpoint(server);
                var request = new SubmitServiceRequest
                {
                    RequestHeader = Header(sessionHeader, 29),
                    ClientOperationId = Uuid.NewUuid(),
                    Request = new ExtensionObject(new ReadRequest
                    {
                        RequestHeader = new RequestHeader { RequestHandle = 13, TimeoutHint = 1 },
                        TimestampsToReturn = TimestampsToReturn.Neither,
                        NodesToRead =
                        [
                            new ReadValueId
                            {
                                NodeId = VariableIds.Server_ServerStatus_State,
                                AttributeId = Attributes.Value
                            }
                        ]
                    }),
                    ExecutionAllowance = 10_000,
                    ResultRetentionTime = 5000,
                    WaitTime = 5000
                };
                IServiceResponse received = await endpoint.ProcessRequestAsync(channel, request, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(received, Is.InstanceOf<SubmitServiceResponse>(),
                    $"Submission returned {received.ResponseHeader.ServiceResult}.");
                var submission = (SubmitServiceResponse)received;
                try
                {
                    Assert.That(submission.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                    Assert.That(submission.Operation.ServerToken.Length, Is.EqualTo(32));
                    Assert.That(submission.ResponseHeader.RequestHandle, Is.EqualTo(29u));
                    Assert.That(
                        submission.Result.TryGetValue(out ReadResponse? original, server.MessageContext), Is.True);
                    Assert.That(original!.ResponseHeader.RequestHandle, Is.EqualTo(13u));
                    Assert.That(original.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    ByteString token = submission.Operation.ServerToken;
                    IServiceResponse acknowledged = await endpoint.ProcessRequestAsync(channel,
                        new ControlServiceExecutionRequest
                        {
                            RequestHeader = Header(sessionHeader, 30),
                            ServerToken = token,
                            Action = ServiceOperationControlAction.Acknowledge
                        }, timeout.Token).ConfigureAwait(false);
                    Assert.That(acknowledged, Is.InstanceOf<ControlServiceExecutionResponse>());
                    var acknowledgement = (ControlServiceExecutionResponse)acknowledged;
                    Assert.That(acknowledgement.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                    Assert.That(acknowledgement.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
                    acknowledgement.Reuse();

                    IServiceResponse collected = await endpoint.ProcessRequestAsync(channel,
                        new GetServiceResultRequest
                        {
                            RequestHeader = Header(sessionHeader, 31),
                            ServerToken = token
                        }, timeout.Token).ConfigureAwait(false);
                    Assert.That(collected, Is.InstanceOf<GetServiceResultResponse>());
                    var released = (GetServiceResultResponse)collected;
                    Assert.That(released.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
                    Assert.That(released.Result.IsNull, Is.True);
                    Assert.That(original.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good),
                        "Another collector's ACK must not invalidate an in-flight response.");
                    released.Reuse();
                }
                finally
                {
                    submission.Reuse();
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A deadline is immutable; acknowledged/forgotten work remains charged until its actual invocation ends.
        /// </summary>
        [Test]
        public async Task DeadlineAndHistoryDoNotReleaseStillRunningWork()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            SubmitServiceRequest request = test.ReadRequest(execution: 100, retention: 200);
            SubmitServiceResponse admitted = await test.SendAsync<SubmitServiceResponse>(request).ConfigureAwait(false);
            ByteString token = admitted.Operation.ServerToken;
            Assert.That(admitted.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            admitted.Reuse();
            DateTime deadline = await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            Assert.That(deadline, Is.EqualTo(DateTime.MaxValue),
                "The original timeoutHint must not impose a second owned-work deadline.");

            test.Clock.Advance(TimeSpan.FromMilliseconds(100));
            await test.Server.CancellationRequested.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            GetServiceResultResponse expiredExecution = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(expiredExecution.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(expiredExecution.Operation.OutcomeKind,
                Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(expiredExecution.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(expiredExecution.Result.IsNull, Is.True);
            expiredExecution.Reuse();

            ControlServiceExecutionResponse acknowledged = await test.ControlAsync(
                token, ServiceOperationControlAction.Acknowledge).ConfigureAwait(false);
            Assert.That(acknowledged.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            acknowledged.Reuse();
            test.Clock.Advance(TimeSpan.FromMilliseconds(200));
            GetServiceResultResponse forgotten = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(forgotten.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            forgotten.Reuse();
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.GreaterThanOrEqualTo(1));

            SubmitServiceRequest duplicate = test.ReadRequest();
            duplicate.ClientOperationId = request.ClientOperationId;
            IServiceResponse rejected = await test.SendAsync<IServiceResponse>(duplicate).ConfigureAwait(false);
            Assert.That(rejected.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadInvalidState));
            (rejected as IPooledEncodeable)?.Reuse();
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));

            await test.ReleaseWorkAsync().ConfigureAwait(false);
            GetServiceResultResponse afterLateCompletion = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(afterLateCompletion.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            Assert.That(afterLateCompletion.Result.IsNull, Is.True);
            afterLateCompletion.Reuse();
        }

        /// <summary>
        /// A late handler completion cannot replace a deadline outcome that is still available for collection.
        /// </summary>
        [Test]
        public async Task LateCompletionCannotReplaceAvailableDeadlineOutcome()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            SubmitServiceResponse admission = await test.SendAsync<SubmitServiceResponse>(
                test.ReadRequest(execution: 100)).ConfigureAwait(false);
            ByteString token = admission.Operation.ServerToken;
            admission.Reuse();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(100));
            GetServiceResultResponse deadline = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(deadline.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            deadline.Reuse();
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(1000));
            GetServiceResultResponse late = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(late.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(late.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(late.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(late.Operation.ResultRemaining, Is.EqualTo(4000));
            Assert.That(late.Result.IsNull, Is.True);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().ExecutionDeadlineCount, Is.EqualTo(1));
            late.Reuse();
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
            SubmitServiceResponse admission = await test.SendAsync<SubmitServiceResponse>(test.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = admission.Operation.ServerToken;
            admission.Reuse();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            long retained = stage == ResourceIsolationStage.RequestQueueBytes ? (2 * 65_536) + 1024 : 1;
            await test.WaitForUsageAsync(stage, retained).ConfigureAwait(false);

            IServiceResponse rejected =
                await test.SendAsync<IServiceResponse>(test.ReadRequest()).ConfigureAwait(false);
            Assert.That(rejected.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            (rejected as IPooledEncodeable)?.Reuse();
            await test.WaitForUsageAsync(stage, retained).ConfigureAwait(false);
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            GetServiceResultResponse pending = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(pending.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            pending.Reuse();
            await test.WaitForUsageAsync(stage, retained).ConfigureAwait(false);
            ControlServiceExecutionResponse cancelled = await test.ControlAsync(
                token, ServiceOperationControlAction.Cancel).ConfigureAwait(false);
            Assert.That(cancelled.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            cancelled.Reuse();
            await test.WaitForUsageAsync(stage, retained).ConfigureAwait(false);
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.GreaterThanOrEqualTo(1));
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            GetServiceResultResponse result = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(result.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            result.Reuse();
            await test.WaitForUsageAsync(stage, stage == ResourceIsolationStage.RequestExecution ? 0 : retained)
                .ConfigureAwait(false);
            ControlServiceExecutionResponse acknowledged = await test.ControlAsync(
                token, ServiceOperationControlAction.Acknowledge).ConfigureAwait(false);
            Assert.That(acknowledged.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            acknowledged.Reuse();
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
            SubmitServiceResponse admitted = await test.SendAsync<SubmitServiceResponse>(test.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Operation.ServerToken;
            admitted.Reuse();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            await test.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 1).ConfigureAwait(false);
            policy.CollectionEntered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<GetServiceResultResponse> waiting = test.SendAsync<GetServiceResultResponse>(
                new GetServiceResultRequest
                {
                    RequestHeader = test.NewHeader(),
                    ServerToken = token,
                    WaitTime = 5000
                }).AsTask();
            GetServiceResultResponse? held = null;
            try
            {
                await policy.CollectionEntered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
                Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.EqualTo(2));
                IServiceResponse refused = await test.SendAsync<IServiceResponse>(new GetServiceResultRequest
                {
                    RequestHeader = test.NewHeader(),
                    ServerToken = token,
                    WaitTime = 5000
                }).ConfigureAwait(false);
                Assert.That(refused.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServerTooBusy));
                (refused as IPooledEncodeable)?.Reuse();
                Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.EqualTo(2));
                test.Server.Release();
                held = await waiting.ConfigureAwait(false);
                Assert.That(held.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                ControlServiceExecutionResponse receipt = await test.ControlAsync(
                    token, ServiceOperationControlAction.Acknowledge).ConfigureAwait(false);
                Assert.That(receipt.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                receipt.Reuse();
                Assert.That(held.Result.TryGetValue(out ReadResponse? original), Is.True);
                Assert.That(original!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            }
            finally
            {
                test.Server.Release();
                held ??= await waiting.ConfigureAwait(false);
                held.Reuse();
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
            SubmitServiceRequest request = test.ReadRequest(wait: 5000);
            Assert.That(request.Request.TryGetValue(out ReadRequest? original), Is.True);
            original!.RequestHeader.RequestHandle = request.RequestHeader.RequestHandle;
            Task<IServiceResponse> submission = test.SendAsync<IServiceResponse>(request).AsTask();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(1));
            CancelResponse cancelled = await test.SendAsync<CancelResponse>(new CancelRequest
            {
                RequestHeader = test.NewHeader(),
                RequestHandle = request.RequestHeader.RequestHandle
            }).ConfigureAwait(false);
            Assert.That(cancelled.CancelCount, Is.EqualTo(1));
            cancelled.Reuse();
            IServiceResponse wait = await submission.ConfigureAwait(false);
            Assert.That(wait.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            (wait as IPooledEncodeable)?.Reuse();
            Assert.That(test.Server.CancellationRequested.Task.IsCompleted, Is.False);

            test.Server.Release();
            GetServiceResultResponse result = await test.SendAsync<GetServiceResultResponse>(new GetServiceResultRequest
            {
                RequestHeader = test.NewHeader(),
                ClientOperationId = request.ClientOperationId,
                ServerToken = default,
                WaitTime = 5000
            }).ConfigureAwait(false);
            Assert.That(result.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(result.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(result.Result.TryGetValue(out ReadResponse? response), Is.True);
            Assert.That(response!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            result.Reuse();
        }

        /// <summary>
        /// Abandon releases recovery only; a token Cancel independently requests stopping the owned invocation.
        /// </summary>
        [Test]
        public async Task AbandonAndCancelHaveIndependentEffects()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            SubmitServiceResponse admission = await test.SendAsync<SubmitServiceResponse>(test.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = admission.Operation.ServerToken;
            admission.Reuse();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            ControlServiceExecutionResponse abandoned = await test.ControlAsync(
                token, ServiceOperationControlAction.Abandon).ConfigureAwait(false);
            Assert.That(abandoned.Operation.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(abandoned.Operation.CancellationRequested, Is.False);
            abandoned.Reuse();
            Assert.That(test.Server.CancellationRequested.Task.IsCompleted, Is.False);

            ControlServiceExecutionResponse cancelled = await test.ControlAsync(
                token, ServiceOperationControlAction.Cancel).ConfigureAwait(false);
            Assert.That(cancelled.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(cancelled.Operation.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(cancelled.Operation.CancellationRequested, Is.True);
            cancelled.Reuse();
            await test.Server.CancellationRequested.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            ControlServiceExecutionResponse repeated = await test.ControlAsync(
                token, ServiceOperationControlAction.Cancel).ConfigureAwait(false);
            Assert.That(repeated.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            repeated.Reuse();
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            ControlServiceExecutionResponse stopped = await test.ControlAsync(
                token, ServiceOperationControlAction.Cancel).ConfigureAwait(false);
            Assert.That(stopped.Result, Is.EqualTo(ServiceOperationControlResult.NotApplicable));
            stopped.Reuse();
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
                SubmitServiceRequest request = test.ReadRequest(wait: 5000);
                SubmitServiceResponse response = await test.SendAsync<SubmitServiceResponse>(request)
                    .ConfigureAwait(false);
                ByteString token = response.Operation.ServerToken;
                response.Reuse();
                ControlServiceExecutionResponse acknowledged = await test.ControlAsync(
                    token, ServiceOperationControlAction.Acknowledge).ConfigureAwait(false);
                acknowledged.Reuse();
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
                        .GetSession(request.RequestHeader.AuthenticationToken)!.Id));
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
            IServiceResponse response = await test.SendAsync<IServiceResponse>(test.ReadRequest())
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            (response as IPooledEncodeable)?.Reuse();
            Assert.That(test.Server.InvocationCount, Is.Zero);
            Assert.That(test.Server.ExecutionFactory.Provider, Is.Null);
            ReadResponse metadata = await test.SendAsync<ReadResponse>(new ReadRequest
            {
                RequestHeader = test.NewHeader(),
                NodesToRead = [new ReadValueId { NodeId = new NodeId(1200100u), AttributeId = Attributes.NodeClass }]
            }).ConfigureAwait(false);
            Assert.That(metadata.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            metadata.Reuse();
        }

        /// <summary>
        /// Enabled roots use the frozen IDs; live children have application IDs and namespace-zero browse names.
        /// </summary>
        [Test]
        public async Task EnabledModelUsesApplicationIdsAndPrivilegedDiagnostics()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            AsyncServiceCapabilitiesState capabilities = test.Server.CurrentInstance.DiagnosticsNodeManager
                .FindPredefinedNode<AsyncServiceCapabilitiesState>(new NodeId(1200100u));
            Assert.That(capabilities, Is.Not.Null);
            Assert.That(capabilities.ContractVersion!.Value, Is.EqualTo("0.2.0"));
            Assert.That(capabilities.SupportedServices!.Value,
                Is.EqualTo(new ArrayOf<NodeId>(
                    [DataTypeIds.ReadRequest, DataTypeIds.WriteRequest, DataTypeIds.CallRequest])));
            Assert.That(capabilities.SupportedServices.NodeId.NamespaceIndex, Is.GreaterThan(0));
            Assert.That(capabilities.SupportedServices.BrowseName.NamespaceIndex, Is.Zero);
            AsyncServiceDiagnosticsState diagnostics = test.Server.CurrentInstance.DiagnosticsNodeManager
                .FindPredefinedNode<AsyncServiceDiagnosticsState>(new NodeId(1200101u));
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
            Assert.That(revision, Is.EqualTo("0.2.0"));
            Assert.That(read.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            read.Reuse();
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
            GetServiceResultResponse unknown = await test.SendAsync<GetServiceResultResponse>(
                new GetServiceResultRequest
                {
                    RequestHeader = test.NewHeader(),
                    ClientOperationId = id,
                    ServerToken = default
                }).ConfigureAwait(false);
            Assert.That(unknown.Operation.ClientOperationId, Is.EqualTo(id));
            Assert.That(unknown.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            Assert.That(unknown.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.None));
            Assert.That(unknown.Operation.ServerToken.IsNull, Is.True);
            Assert.That(unknown.Operation.ServiceId.IsNull, Is.True);
            Assert.That(unknown.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(unknown.Operation.ExecutionAllowance, Is.Zero);
            Assert.That(unknown.Operation.ResultRetentionTime, Is.Zero);
            Assert.That(unknown.Result.IsNull, Is.True);
            unknown.Reuse();
        }

        /// <summary>
        /// Ordinary Session authentication is required independently of operation lookup.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task WrapperWithoutSessionCannotExecuteOrDisclose(bool collect, bool createUnactivated)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            IServiceRequest request = collect
                ? new GetServiceResultRequest
                {
                    RequestHeader = test.NewHeader(),
                    ClientOperationId = Uuid.NewUuid(),
                    ServerToken = default
                }
                : test.ReadRequest();
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
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(createUnactivated
                ? StatusCodes.BadSessionNotActivated : StatusCodes.BadSessionIdInvalid));
            (response as IPooledEncodeable)?.Reuse();
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
            SubmitServiceRequest first = test.ReadRequest();
            SubmitServiceRequest second = test.ReadRequest();
            first.RequestHeader.RequestHandle = 77;
            second.RequestHeader.RequestHandle = 77;
            SubmitServiceResponse admittedFirst =
                await test.SendAsync<SubmitServiceResponse>(first).ConfigureAwait(false);
            ByteString firstToken = admittedFirst.Operation.ServerToken;
            Assert.That(admittedFirst.ResponseHeader.RequestHandle, Is.EqualTo(77u));
            admittedFirst.Reuse();
            SubmitServiceResponse admittedSecond =
                await test.SendAsync<SubmitServiceResponse>(second).ConfigureAwait(false);
            ByteString secondToken = admittedSecond.Operation.ServerToken;
            Assert.That(admittedSecond.ResponseHeader.RequestHandle, Is.EqualTo(77u));
            Assert.That(second.ClientOperationId, Is.Not.EqualTo(first.ClientOperationId));
            Assert.That(secondToken, Is.Not.EqualTo(firstToken));
            admittedSecond.Reuse();
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            GetServiceResultResponse firstResult = await test.GetAsync(firstToken).ConfigureAwait(false);
            GetServiceResultResponse secondResult = await test.GetAsync(secondToken).ConfigureAwait(false);
            Assert.That(firstResult.Operation.ClientOperationId, Is.EqualTo(first.ClientOperationId));
            Assert.That(secondResult.Operation.ClientOperationId, Is.EqualTo(second.ClientOperationId));
            Assert.That(firstResult.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(secondResult.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(2));
            firstResult.Reuse();
            secondResult.Reuse();
        }

        /// <summary>
        /// Anonymous plus Sign cannot promise the same-Session replacement-channel recovery contract.
        /// </summary>
        [Test]
        public async Task AnonymousSignAdmissionIsRejectedBeforeDispatch()
        {
            await using ExecutionTestContext test = await ExecutionTestContext.CreateAsync(
                delayRead: false, signOnlyAnonymous: true).ConfigureAwait(false);
            IServiceResponse response =
                await test.SendAsync<IServiceResponse>(test.ReadRequest()).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
            (response as IPooledEncodeable)?.Reuse();
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
            SubmitServiceRequest request = test.ReadRequest(wait: 5000);
            Task<IServiceResponse> submitting = test.SendAsync<IServiceResponse>(request).AsTask();
            try
            {
                await entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
                GetServiceResultResponse before = await test.SendAsync<GetServiceResultResponse>(
                    new GetServiceResultRequest
                    {
                        RequestHeader = test.NewHeader(),
                        ClientOperationId = request.ClientOperationId,
                        ServerToken = default
                    }).ConfigureAwait(false);
                Assert.That(before.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
                before.Reuse();
                if (cancelBeforeAdmission)
                {
                    CancelResponse cancellation = await test.SendAsync<CancelResponse>(new CancelRequest
                    {
                        RequestHeader = test.NewHeader(),
                        RequestHandle = request.RequestHeader.RequestHandle
                    }).ConfigureAwait(false);
                    Assert.That(cancellation.CancelCount, Is.EqualTo(1));
                    cancellation.Reuse();
                }
                release.TrySetResult(true);
                IServiceResponse response = await submitting.ConfigureAwait(false);
                if (cancelBeforeAdmission)
                {
                    Assert.That(response.ResponseHeader.ServiceResult,
                        Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                    Assert.That(test.Server.InvocationCount, Is.Zero);
                }
                else
                {
                    Assert.That(response, Is.InstanceOf<SubmitServiceResponse>());
                    var admitted = (SubmitServiceResponse)response;
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
            SubmitServiceResponse admitted = await test.SendAsync<SubmitServiceResponse>(
                test.ReadRequest(execution: 1000, retention: 500)).ConfigureAwait(false);
            ByteString token = admitted.Operation.ServerToken;
            admitted.Reuse();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(999));
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            GetServiceResultResponse ready = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(ready.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(ready.Operation.ResultRemaining, Is.EqualTo(500));
            ready.Reuse();
            test.Clock.Advance(TimeSpan.FromMilliseconds(499));
            GetServiceResultResponse retained = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(retained.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(retained.Operation.ResultRemaining, Is.EqualTo(1));
            retained.Reuse();
            test.Clock.Advance(TimeSpan.FromMilliseconds(1));
            GetServiceResultResponse expired = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(expired.Operation.State, Is.EqualTo(ServiceOperationState.Expired));
            Assert.That(expired.Operation.HistoryRemaining, Is.EqualTo(500));
            expired.Reuse();
        }

        /// <summary>
        /// Wall-clock corrections do not alter elapsed execution, retention or retry-advice bounds.
        /// </summary>
        [Test]
        public async Task WallClockChangesDoNotRenewOrExpireElapsedLifetimes()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            SubmitServiceResponse admitted = await test.SendAsync<SubmitServiceResponse>(
                test.ReadRequest(execution: 1000, retention: 500)).ConfigureAwait(false);
            ByteString token = admitted.Operation.ServerToken;
            admitted.Reuse();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            // Cross E/R without expiring the owning Session's separate ordinary inactivity timeout.
            test.Clock.UtcOffset = TimeSpan.FromSeconds(2);
            GetServiceResultResponse forward = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(forward.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(forward.Operation.ExecutionRemaining, Is.EqualTo(1000));
            Assert.That(forward.Operation.RetryAfter, Is.LessThan(forward.Operation.ResultRetentionTime));
            Assert.That(forward.Operation.RetryAfter, Is.LessThanOrEqualTo(forward.Operation.ExecutionRemaining));
            forward.Reuse();
            test.Clock.UtcOffset = TimeSpan.FromSeconds(-2);
            test.Clock.Advance(TimeSpan.FromMilliseconds(1000));
            GetServiceResultResponse deadline = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(deadline.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(deadline.Operation.ResultRemaining, Is.EqualTo(500));
            deadline.Reuse();
            test.Clock.UtcOffset = TimeSpan.FromSeconds(4);
            GetServiceResultResponse retained = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(retained.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(retained.Operation.ResultRemaining, Is.EqualTo(500));
            retained.Reuse();
            test.Clock.Advance(TimeSpan.FromMilliseconds(500));
            GetServiceResultResponse expired = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(expired.Operation.State, Is.EqualTo(ServiceOperationState.Expired));
            expired.Reuse();
        }

        /// <summary>
        /// Foreign-Session identifiers are indistinguishable from unknown identifiers in the requesting Session.
        /// </summary>
        [Test]
        public async Task ForeignSessionTokenHasTheSameUnknownLookupAndControlDisposition()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: true).ConfigureAwait(false);
            SubmitServiceResponse submitted = await test.SendAsync<SubmitServiceResponse>(test.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = submitted.Operation.ServerToken;
            submitted.Reuse();
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
                    new GetServiceResultRequest
                    {
                        RequestHeader = Header(otherHeader, handle++),
                        ServerToken = selector
                    }, test.Timeout).ConfigureAwait(false);
                Assert.That(received, Is.InstanceOf<GetServiceResultResponse>(),
                    $"Foreign/unknown lookup returned {received.ResponseHeader.ServiceResult}.");
                var lookup = (GetServiceResultResponse)received;
                Assert.That(lookup.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
                Assert.That(lookup.Operation.ServerToken.IsNull, Is.True);
                Assert.That(lookup.Result.IsNull, Is.True);
                byte[] metadata = BinaryEncoder.EncodeMessage(lookup.Operation, test.Server.MessageContext);
                unknownMetadata ??= metadata;
                Assert.That(metadata, Is.EqualTo(unknownMetadata));
                lookup.Reuse();

                IServiceResponse controlled = await endpoint.ProcessRequestAsync(otherChannel,
                    new ControlServiceExecutionRequest
                    {
                        RequestHeader = Header(otherHeader, handle++),
                        ServerToken = selector,
                        Action = ServiceOperationControlAction.Abandon
                    }, test.Timeout).ConfigureAwait(false);
                Assert.That(controlled, Is.InstanceOf<ControlServiceExecutionResponse>());
                var disposition = (ControlServiceExecutionResponse)controlled;
                Assert.That(disposition.Result, Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
                Assert.That(disposition.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
                Assert.That(BinaryEncoder.EncodeMessage(disposition.Operation, test.Server.MessageContext),
                    Is.EqualTo(unknownMetadata));
                disposition.Reuse();
            }
            GetServiceResultResponse owner = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(owner.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(owner.Operation.CancellationRequested, Is.False);
            owner.Reuse();
            test.Server.Release();
        }

        /// <summary>
        /// A validated identity change within the original Session denies collection/control without releasing data.
        /// </summary>
        [Test]
        public async Task SameSessionWrongCreatorIsDeniedAndOriginalCreatorCanStillCollect()
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
            SubmitServiceRequest request = test.ReadRequest(wait: 5000);
            SubmitServiceResponse submission = await test.SendAsync<SubmitServiceResponse>(request)
                .ConfigureAwait(false);
            ByteString token = submission.Operation.ServerToken;
            submission.Reuse();
            ISession serverSession = test.Server.CurrentInstance.SessionManager
                .GetSession(request.RequestHeader.AuthenticationToken)!;
            NodeId sessionId = serverSession.Id;
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
                .GetSession(request.RequestHeader.AuthenticationToken)!.Id, Is.EqualTo(sessionId));
            IServiceResponse denied = await test.SendAsync<IServiceResponse>(new GetServiceResultRequest
            {
                RequestHeader = test.NewHeader(),
                ServerToken = token
            }).ConfigureAwait(false);
            Assert.That(denied.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            (denied as IPooledEncodeable)?.Reuse();
            IServiceResponse control = await test.SendAsync<IServiceResponse>(new ControlServiceExecutionRequest
            {
                RequestHeader = test.NewHeader(),
                ServerToken = token,
                Action = ServiceOperationControlAction.Acknowledge
            }).ConfigureAwait(false);
            Assert.That(control.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            (control as IPooledEncodeable)?.Reuse();
            ActivateSessionResponse restored = await test.SendAsync<ActivateSessionResponse>(new ActivateSessionRequest
            {
                RequestHeader = test.NewHeader(),
                UserIdentityToken = default
            }).ConfigureAwait(false);
            restored.Reuse();
            GetServiceResultResponse result = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(result.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(result.Result.TryGetValue(out ReadResponse? original), Is.True);
            Assert.That(original!.ResponseHeader.RequestHandle, Is.EqualTo(17u));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            result.Reuse();
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
            SubmitServiceRequest request = retention
                ? test.ReadRequest(retention: invalid) : test.ReadRequest(execution: invalid);
            IServiceResponse response = await test.SendAsync<IServiceResponse>(request).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadInvalidArgument));
            (response as IPooledEncodeable)?.Reuse();
            Assert.That(test.Server.InvocationCount, Is.Zero);
            Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().AdmittedCount, Is.Zero);
            Assert.That(test.Server.ExecutionFactory.Provider.GetStatistics().RejectedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Unsupported exact positive allowances are rejected, not revised down to the advertised maximum.
        /// </summary>
        [TestCase(600_001, false)]
        [TestCase(1.000_000_01, false)]
        [TestCase(0.5, false)]
        [TestCase(600_001, true)]
        [TestCase(1.000_000_01, true)]
        [TestCase(0.5, true)]
        public async Task UnsupportedPositiveDurationRejectsBeforeDispatch(double unsupported, bool retention)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            IServiceResponse response = await test.SendAsync<IServiceResponse>(
                retention ? test.ReadRequest(retention: unsupported) : test.ReadRequest(execution: unsupported))
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadOutOfRange));
            (response as IPooledEncodeable)?.Reuse();
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
        [TestCase("invalid-retention")]
        public async Task InvalidAdmissionEnvelopeRejectsBeforeDispatch(string fault)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            SubmitServiceRequest request = test.ReadRequest();
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
                case "invalid-retention":
                    request.ResultRetentionTime = double.NaN;
                    break;
                default:
                    throw new ArgumentException("Unknown invalid envelope.", nameof(fault));
            }
            IServiceResponse response = await test.SendAsync<IServiceResponse>(request).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(fault == "foreign-authentication"
                ? StatusCodes.BadUserAccessDenied : StatusCodes.BadInvalidArgument));
            (response as IPooledEncodeable)?.Reuse();
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
        public async Task InvalidCollectionSelectorIsRejected(string fault)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            var request = new GetServiceResultRequest
            {
                RequestHeader = test.NewHeader(),
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
                default:
                    throw new ArgumentException("Unknown invalid selector.", nameof(fault));
            }
            IServiceResponse response = await test.SendAsync<IServiceResponse>(request).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadInvalidArgument));
            (response as IPooledEncodeable)?.Reuse();
            Assert.That(test.Server.InvocationCount, Is.Zero);
        }

        /// <summary>
        /// A control requires a valid bounded token and an explicitly defined action.
        /// </summary>
        [TestCase(-1, ServiceOperationControlAction.Cancel)]
        [TestCase(0, ServiceOperationControlAction.Cancel)]
        [TestCase(65, ServiceOperationControlAction.Cancel)]
        [TestCase(32, ServiceOperationControlAction.Unspecified)]
        [TestCase(32, (ServiceOperationControlAction)4)]
        public async Task InvalidControlIsRejected(int tokenLength, ServiceOperationControlAction action)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            IServiceResponse response = await test.SendAsync<IServiceResponse>(new ControlServiceExecutionRequest
            {
                RequestHeader = test.NewHeader(),
                ServerToken = tokenLength < 0 ? default : new ByteString(new byte[tokenLength]),
                Action = action
            }).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadInvalidArgument));
            (response as IPooledEncodeable)?.Reuse();
            Assert.That(test.Server.InvocationCount, Is.Zero);
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
            SubmitServiceRequest first = test.ReadRequest(wait: 5000);
            SubmitServiceResponse admitted = await test.SendAsync<SubmitServiceResponse>(first).ConfigureAwait(false);
            Assert.That(admitted.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            admitted.Reuse();
            SubmitServiceRequest duplicate = test.ReadRequest(wait: 5000);
            duplicate.ClientOperationId = first.ClientOperationId;
            if (changeInput)
            {
                Assert.That(duplicate.Request.TryGetValue(out ReadRequest? original), Is.True);
                original!.NodesToRead = [];
            }
            IServiceResponse response = await test.SendAsync<IServiceResponse>(duplicate).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadInvalidState));
            (response as IPooledEncodeable)?.Reuse();
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
            SubmitServiceResponse admitted = await test.SendAsync<SubmitServiceResponse>(
                test.ReadRequest(wait: 5000)).ConfigureAwait(false);
            ByteString token = admitted.Operation.ServerToken;
            Assert.That(admitted.Result.TryGetValue(out ReadResponse? original), Is.True);
            byte[] frozen = BinaryEncoder.EncodeMessage(original!, test.Server.MessageContext);
            admitted.Reuse();
            GetServiceResultResponse[] collectors = await Task.WhenAll(
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
                    ControlServiceExecutionResponse receipt = await test.ControlAsync(token,
                        releasedState == ServiceOperationState.Acknowledged
                            ? ServiceOperationControlAction.Acknowledge : ServiceOperationControlAction.Abandon)
                        .ConfigureAwait(false);
                    Assert.That(receipt.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                    receipt.Reuse();
                }
                GetServiceResultResponse afterRelease = await test.GetAsync(token).ConfigureAwait(false);
                Assert.That(afterRelease.Operation.State, Is.EqualTo(releasedState));
                Assert.That(afterRelease.Result.IsNull, Is.True);
                afterRelease.Reuse();
                Assert.That(test.Server.ExecutionFactory.Provider!.GetStatistics().RetainedOutcomeBytes,
                    Is.GreaterThan(0));
                Assert.That(collectors.Select(value => value.ResponseHeader.RequestHandle), Is.Unique);
                foreach (GetServiceResultResponse collector in collectors)
                {
                    Assert.That(collector.Result.TryGetValue(out ReadResponse? retained), Is.True);
                    Assert.That(BinaryEncoder.EncodeMessage(retained!, test.Server.MessageContext), Is.EqualTo(frozen));
                    Assert.That(collector.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                }
                test.Clock.Advance(TimeSpan.FromMilliseconds(5000));
                Assert.That(test.Server.ExecutionFactory.Provider.GetStatistics().TerminalRecordCount, Is.Zero);
                Assert.That(test.Server.ExecutionFactory.Provider.GetStatistics().RetainedOutcomeBytes,
                    Is.GreaterThan(0), "Public history expiry cannot free a transport's output buffer.");
            }
            finally
            {
                foreach (GetServiceResultResponse collector in collectors)
                {
                    collector.Reuse();
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
            SubmitServiceResponse sender = await test.SendAsync<SubmitServiceResponse>(test.ReadRequest(wait: 5000))
                .ConfigureAwait(false);
            ByteString token = sender.Operation.ServerToken;
            try
            {
                test.Clock.Advance(TimeSpan.FromMilliseconds(4999));
                Task<ControlServiceExecutionResponse> acknowledge =
                    test.ControlAsync(token, ServiceOperationControlAction.Acknowledge).AsTask();
                Task<ControlServiceExecutionResponse> abandon =
                    test.ControlAsync(token, ServiceOperationControlAction.Abandon).AsTask();
                test.Clock.Advance(TimeSpan.FromMilliseconds(1));
                ControlServiceExecutionResponse[] controls = await Task.WhenAll(acknowledge, abandon)
                    .ConfigureAwait(false);
                GetServiceResultResponse current = await test.GetAsync(token).ConfigureAwait(false);
                Assert.That(current.Operation.State, Is.AnyOf(ServiceOperationState.Acknowledged,
                    ServiceOperationState.Abandoned, ServiceOperationState.Expired));
                Assert.That(current.Result.IsNull, Is.True);
                foreach (ControlServiceExecutionResponse control in controls)
                {
                    Assert.That(control.Operation.State, Is.EqualTo(current.Operation.State));
                    control.Reuse();
                }
                current.Reuse();
                AsyncServiceExecutionStatistics statistics = test.Server.ExecutionFactory.Provider!.GetStatistics();
                Assert.That(statistics.AcknowledgedCount + statistics.AbandonedCount + statistics.ExpiredCount,
                    Is.EqualTo(1));
                Assert.That(sender.Result.TryGetValue(out ReadResponse? original), Is.True);
                Assert.That(original!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(statistics.RetainedOutcomeBytes, Is.GreaterThan(0));
            }
            finally
            {
                sender.Reuse();
            }
        }

        /// <summary>
        /// Original Service validation failures remain complete ServiceFault outcomes inside successful wrappers.
        /// </summary>
        [Test]
        public async Task OriginalServiceFaultRetainsItsHeaderAndDiagnostics()
        {
            await using ExecutionTestContext test = await ExecutionTestContext.CreateAsync(delayRead: false)
                .ConfigureAwait(false);
            SubmitServiceRequest request = test.ReadRequest(wait: 5000);
            Assert.That(request.Request.TryGetValue(out ReadRequest? read), Is.True);
            read!.NodesToRead = [];
            read.RequestHeader.ReturnDiagnostics = (uint)DiagnosticsMasks.All;
            SubmitServiceResponse submission = await test.SendAsync<SubmitServiceResponse>(request)
                .ConfigureAwait(false);
            ByteString token = submission.Operation.ServerToken;
            Assert.That(submission.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(submission.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(submission.Result.TryGetValue(out ServiceFault? fault), Is.True);
            Assert.That(fault!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadNothingToDo));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(17u));
            Assert.That(submission.Operation.OutcomeStatus, Is.EqualTo(fault.ResponseHeader.ServiceResult));
            byte[] frozen = BinaryEncoder.EncodeMessage(fault, test.Server.MessageContext);
            submission.Reuse();
            GetServiceResultResponse collected = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(collected.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(collected.Result.TryGetValue(out ServiceFault? retained), Is.True);
            Assert.That(BinaryEncoder.EncodeMessage(retained!, test.Server.MessageContext), Is.EqualTo(frozen));
            collected.Reuse();
        }

        /// <summary>
        /// Measures the complete encoded envelope at the exact payload boundary, not an inner-result approximation.
        /// </summary>
        [TestCase(RequestEncoding.Binary)]
        [TestCase(RequestEncoding.Json)]
        public async Task EncodedEnvelopeBoundaryIncludesWrapperOverhead(RequestEncoding mapping)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false, mapping: mapping)
                .ConfigureAwait(false);
            test.Server.Scenario.Blob = ByteString.Empty;
            SubmitServiceResponse probe = await test.SendAsync<SubmitServiceResponse>(test.BlobRequest())
                .ConfigureAwait(false);
            Assert.That(probe.Result.TryGetValue(out ReadResponse? original), Is.True);
            ReadResponse template = CoreUtils.Clone(original)!;
            ServiceOperationInfoDataType metadata = CoreUtils.Clone(probe.Operation)!;
            ByteString probeToken = probe.Operation.ServerToken;
            probe.Reuse();
            ControlServiceExecutionResponse probeAck = await test.ControlAsync(
                probeToken, ServiceOperationControlAction.Acknowledge).ConfigureAwait(false);
            probeAck.Reuse();
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
                var envelope = new GetServiceResultResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        RequestHandle = uint.MaxValue,
                        Timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567)
                    },
                    Operation = metadata,
                    Result = new ExtensionObject(candidate)
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
                    SubmitServiceRequest request = test.BlobRequest();
                    request.WaitTime = 0;
                    SubmitServiceResponse admitted = await test.SendAsync<SubmitServiceResponse>(request)
                        .ConfigureAwait(false);
                    Assert.That(admitted.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
                    metadata = CoreUtils.Clone(admitted.Operation)!;
                    metadata.State = ServiceOperationState.OutcomeAvailable;
                    metadata.OutcomeKind = ServiceOperationOutcome.ServiceResponse;
                    metadata.ExecutionRemaining = 0;
                    metadata.ResultRemaining = metadata.ResultRetentionTime;
                    ByteString token = admitted.Operation.ServerToken;
                    admitted.Reuse();
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
                    GetServiceResultResponse outcome = await test.SendAsync<GetServiceResultResponse>(
                        new GetServiceResultRequest
                        {
                            RequestHeader = test.NewHeader(),
                            ServerToken = token,
                            WaitTime = 5000
                        }).ConfigureAwait(false);
                    Assert.That(outcome.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                    if (excess == 0)
                    {
                        Assert.That(outcome.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
                        Assert.That(outcome.Result.TryGetValue(out ReadResponse? read), Is.True);
                        Assert.That(read!.Results[0].WrappedValue.TryGetValue(out ByteString received), Is.True);
                        Assert.That(received.Length, Is.EqualTo(fitting));
                    }
                    else
                    {
                        Assert.That(
                            outcome.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ResultUnavailable));
                        Assert.That(outcome.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadResponseTooLarge));
                        Assert.That(outcome.Result.IsNull, Is.True);
                        GetServiceResultResponse retry = await test.GetAsync(token).ConfigureAwait(false);
                        Assert.That(retry.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ResultUnavailable));
                        Assert.That(retry.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadResponseTooLarge));
                        retry.Reuse();
                    }
                    outcome.Reuse();
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
            SubmitServiceResponse admitted = await test.SendAsync<SubmitServiceResponse>(test.BlobRequest())
                .ConfigureAwait(false);
            Assert.That(admitted.Result.TryGetValue(out ReadResponse? original), Is.True);
            byte[] frozen = BinaryEncoder.EncodeMessage(original!, test.Server.MessageContext);
            ByteString token = admitted.Operation.ServerToken;
            admitted.Reuse();
            ISession session = test.Server.CurrentInstance.SessionManager.GetSessions().Single();
            uint previousLimit = session.ReadDiagnostics(static value => value.MaxResponseMessageSize);
            session.UpdateDiagnostics(static value => value.MaxResponseMessageSize = 1024);
            try
            {
                IServiceResponse rejected = await test.SendAsync<IServiceResponse>(new GetServiceResultRequest
                {
                    RequestHeader = test.NewHeader(),
                    ServerToken = token
                }).ConfigureAwait(false);
                Assert.That(rejected.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadResponseTooLarge));
                (rejected as IPooledEncodeable)?.Reuse();
            }
            finally
            {
                session.UpdateDiagnostics(value => value.MaxResponseMessageSize = previousLimit);
            }
            GetServiceResultResponse recovered = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(recovered.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(recovered.Result.TryGetValue(out ReadResponse? retained), Is.True);
            Assert.That(BinaryEncoder.EncodeMessage(retained!, test.Server.MessageContext), Is.EqualTo(frozen));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            recovered.Reuse();
        }

        /// <summary>
        /// Polling and repeated ACKs renew neither retention nor terminal history.
        /// </summary>
        [Test]
        public async Task PollingAndRepeatedAcknowledgementDoNotRenewWindows()
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            SubmitServiceResponse admitted = await test.SendAsync<SubmitServiceResponse>(
                test.ReadRequest(wait: 5000)).ConfigureAwait(false);
            ByteString token = admitted.Operation.ServerToken;
            admitted.Reuse();
            await test.ReleaseWorkAsync().ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(4000));
            GetServiceResultResponse polled = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(polled.Operation.ResultRemaining, Is.EqualTo(1000));
            polled.Reuse();
            ControlServiceExecutionResponse first = await test.ControlAsync(
                token, ServiceOperationControlAction.Acknowledge).ConfigureAwait(false);
            Assert.That(first.Operation.HistoryRemaining, Is.EqualTo(5000));
            first.Reuse();
            test.Clock.Advance(TimeSpan.FromMilliseconds(4000));
            ControlServiceExecutionResponse repeated = await test.ControlAsync(
                token, ServiceOperationControlAction.Acknowledge).ConfigureAwait(false);
            Assert.That(repeated.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            Assert.That(repeated.Operation.HistoryRemaining, Is.EqualTo(1000));
            repeated.Reuse();
            test.Clock.Advance(TimeSpan.FromMilliseconds(1000));
            GetServiceResultResponse unknown = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(unknown.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            unknown.Reuse();
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
            SubmitServiceResponse admitted = await test.SendAsync<SubmitServiceResponse>(test.ReadRequest())
                .ConfigureAwait(false);
            admitted.Reuse();
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
        /// Lifecycle, subscription, ordinary Cancel, wrapper and unadvertised Services cannot be wrapped.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [TestCase(nameof(CreateSessionRequest))]
        [TestCase(nameof(CancelRequest))]
        [TestCase(nameof(PublishRequest))]
        [TestCase(nameof(SubmitServiceRequest))]
        [TestCase(nameof(GetServiceResultRequest))]
        [TestCase(nameof(ControlServiceExecutionRequest))]
        [TestCase(nameof(HistoryReadRequest))]
        public async Task ExcludedOriginalServiceRejectsBeforeDispatch(string name)
        {
            await using ExecutionTestContext test =
                await ExecutionTestContext.CreateAsync(delayRead: false).ConfigureAwait(false);
            IServiceRequest original = name switch
            {
                nameof(CreateSessionRequest) => new CreateSessionRequest(),
                nameof(CancelRequest) => new CancelRequest(),
                nameof(PublishRequest) => new PublishRequest(),
                nameof(SubmitServiceRequest) => new SubmitServiceRequest(),
                nameof(GetServiceResultRequest) => new GetServiceResultRequest(),
                nameof(ControlServiceExecutionRequest) => new ControlServiceExecutionRequest(),
                nameof(HistoryReadRequest) => new HistoryReadRequest(),
                _ => throw new ArgumentException("Unknown test case.", nameof(name))
            };
            original.RequestHeader = new RequestHeader();
            SubmitServiceRequest request = test.ReadRequest();
            request.Request = new ExtensionObject(original);
            IServiceResponse response = await test.SendAsync<IServiceResponse>(request).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            (response as IPooledEncodeable)?.Reuse();
            Assert.That(test.Server.InvocationCount, Is.Zero);
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
            SubmitServiceResponse submitted = await test.SendAsync<SubmitServiceResponse>(test.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = submitted.Operation.ServerToken;
            submitted.Reuse();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            policy.CollectionEntered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<IServiceResponse> waiting = test.SendAsync<IServiceResponse>(new GetServiceResultRequest
            {
                RequestHeader = test.NewHeader(),
                ServerToken = token,
                WaitTime = 5000
            }).AsTask();
            await policy.CollectionEntered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            policy.Deny(AsyncServiceAccess.Collect);
            test.Server.Release();
            IServiceResponse denied = await waiting.ConfigureAwait(false);
            Assert.That(denied.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            (denied as IPooledEncodeable)?.Reuse();
            policy.Allow();
            GetServiceResultResponse recovered = await test.GetAsync(token).ConfigureAwait(false);
            Assert.That(recovered.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(recovered.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(test.Server.InvocationCount, Is.EqualTo(1));
            recovered.Reuse();
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
            SubmitServiceResponse submitted = await test.SendAsync<SubmitServiceResponse>(test.ReadRequest())
                .ConfigureAwait(false);
            ByteString token = submitted.Operation.ServerToken;
            submitted.Reuse();
            await test.Server.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            policy.Deny(AsyncServiceAccess.Cancel);
            IServiceResponse denied = await test.SendAsync<IServiceResponse>(new ControlServiceExecutionRequest
            {
                RequestHeader = test.NewHeader(),
                ServerToken = token,
                Action = ServiceOperationControlAction.Cancel
            }).ConfigureAwait(false);
            Assert.That(denied.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            (denied as IPooledEncodeable)?.Reuse();
            Assert.That(test.Server.CancellationRequested.Task.IsCompleted, Is.False);
            ControlServiceExecutionResponse abandoned = await test.ControlAsync(
                token, ServiceOperationControlAction.Abandon).ConfigureAwait(false);
            Assert.That(abandoned.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(abandoned.Operation.CancellationRequested, Is.False);
            abandoned.Reuse();
            test.Server.Release();
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
                        long shared = stage == ResourceIsolationStage.RequestQueueBytes ? (3 * unit) + 1024 : 2;
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

            public SubmitServiceRequest ReadRequest(double execution = 10_000, double retention = 5000, double wait = 0)
            {
                return new SubmitServiceRequest
                {
                    RequestHeader = NewHeader(),
                    ClientOperationId = Uuid.NewUuid(),
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

            public SubmitServiceRequest BlobRequest()
            {
                SubmitServiceRequest request = ReadRequest(wait: 5000);
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

            public ValueTask<GetServiceResultResponse> GetAsync(ByteString token)
            {
                return SendAsync<GetServiceResultResponse>(new GetServiceResultRequest
                {
                    RequestHeader = NewHeader(),
                    ServerToken = token
                });
            }

            public ValueTask<ControlServiceExecutionResponse> ControlAsync(
                ByteString token, ServiceOperationControlAction action)
            {
                return SendAsync<ControlServiceExecutionResponse>(new ControlServiceExecutionRequest
                {
                    RequestHeader = NewHeader(),
                    ServerToken = token,
                    Action = action
                });
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
                ResponseHeader response = base.CreateResponse(requestHeader, stringTable);
                response.Timestamp = TimeProvider.GetUtcNow().UtcDateTime;
                return response;
            }

            protected override async ValueTask OnRequestValidatedAsync(OperationContext context)
            {
                if (context.RequestType == RequestType.Read)
                {
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
    }
}

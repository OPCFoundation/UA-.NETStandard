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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Bounded integrated regressions through real startup, manager Calls and original Service dispatch.
    /// Recreated protected store images model interruption boundaries, not an OS kill or wire-native/AOT gate.
    /// </summary>
    [TestFixture]
    [Category("AsyncServices")]
    [NonParallelizable]
    public sealed class RecoverableDurableExecutionTests
    {
        [Test]
        public async Task DependenciesDoNotEnableDurable()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(
                state, enabled: false).ConfigureAwait(false);

            Assert.That(host.Server.AsyncServices.Enabled, Is.False);
            Assert.That(host.Server.AsyncServices.RecoveryModes,
                Is.EqualTo([ServiceOperationRecoveryMode.Session]));
            Assert.That(host.Server.Provider, Is.Null);
            Assert.That(state.Store.Attempts, Is.Zero);
            Assert.That(state.Codecs.Calls, Is.Zero);
            Assert.That(state.Eligibility.AcquiredIds, Is.Empty);
            Assert.That(host.Server.Dispatches, Is.Zero);
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(7));
        }

        [Test]
        public async Task RegisteredDurableDependenciesDoNotEnableUnconfiguredMode()
        {
            await using var state = new DurableExecutionTestState();
            ServiceOperationRecoveryMode[] modes =
                [ServiceOperationRecoveryMode.Session, ServiceOperationRecoveryMode.Owner];
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state, modes: modes)
                .ConfigureAwait(false);
            await host.RejectAsync(RseBrowseNames.Submit, host.Request(), StatusCodes.BadNotSupported)
                .ConfigureAwait(false);
            Assert.That(host.Provider.SupportedRecoveryModes, Is.EqualTo(modes));
            Assert.That(host.Manager.Capabilities!.SupportedRecoveryModes!.Value, Is.EqualTo(modes));
            Assert.That(state.Store.Attempts, Is.Zero);
            Assert.That(state.Eligibility.AcquiredIds, Is.Empty);
            Assert.That(state.Eligibility.OwnerAcquisitions, Is.Zero);
            AssertNoDispatch(host);
        }

        [TestCase("owner")]
        [TestCase("eligibility")]
        [TestCase("ordinary-eligibility")]
        [TestCase("store")]
        [TestCase("unqualified-store")]
        [TestCase("protector")]
        [TestCase("plaintext-protector")]
        [TestCase("clock")]
        [TestCase("codec")]
        public async Task DurableStartupRequiresQualification(string missing)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            var ordinary = new Mock<IRecoverableExecutionEligibilityProvider>(MockBehavior.Strict);
            var unqualified = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            var options = new AsyncServiceExecutionOptions
            {
                Enabled = true,
                RecoveryModes = DurableExecutionTestHost.AllModes
            };
            AsyncServiceExecutionProvider? provider = null;
            Exception? rejected = null;
            try
            {
                provider = new AsyncServiceExecutionProvider(
                    host.Server, host.Configuration, options, state.Clock.Local,
                    ownerProvider: missing == "owner" ? null : state.Owners,
                    eligibilityProvider: missing switch
                    {
                        "eligibility" => null,
                        "ordinary-eligibility" => ordinary.Object,
                        _ => state.Eligibility
                    },
                    durableStore: missing switch
                    {
                        "store" => null,
                        "unqualified-store" => unqualified.Object,
                        _ => state.Store
                    },
                    recordProtector: missing switch
                    {
                        "protector" => null,
                        "plaintext-protector" => NullRecordProtector.Instance,
                        _ => state.Protector
                    },
                    durableClock: missing == "clock" ? null : state.Clock,
                    codecQualifier: missing == "codec" ? null : state.Codecs);
                await provider.InitializeAsync(default).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is ArgumentException or ServiceResultException)
            {
                rejected = exception;
            }
            finally
            {
                if (provider != null)
                {
                    await provider.DisposeAsync().ConfigureAwait(false);
                }
            }

            if (missing is "unqualified-store" or "plaintext-protector" or "codec")
            {
                Assert.That(rejected, Is.InstanceOf<ServiceResultException>());
                Assert.That(((ServiceResultException)rejected!).StatusCode,
                    Is.EqualTo(StatusCodes.BadResourceUnavailable));
            }
            else
            {
                Assert.That(rejected, Is.InstanceOf<ArgumentException>());
                Assert.That(((ArgumentException)rejected!).ParamName, Is.EqualTo("options"));
            }
            Assert.That(state.Store.Attempts, Is.Zero);
            Assert.That(state.Eligibility.AcquiredIds, Is.Empty);
            Assert.That(host.Server.Dispatches, Is.Zero);
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.Zero);
            ordinary.VerifyNoOtherCalls();
            unqualified.VerifyNoOtherCalls();
        }

        [Test]
        public async Task DurableRequiresExistingStableOwnerAccounting()
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            var accounting = new Mock<IServerResourceIsolationProvider>(MockBehavior.Strict);
            server.ResourceIsolationProvider = accounting.Object;
            await using var state = new DurableExecutionTestState();
            Assert.That(() => new AsyncServiceExecutionProvider(server,
                new ApplicationConfiguration
                {
                    ApplicationUri = "urn:durable-test:server",
                    TransportQuotas = new TransportQuotas { MaxMessageSize = DurableExecutionTestHost.MessageLimit }
                },
                new AsyncServiceExecutionOptions
                {
                    Enabled = true,
                    RecoveryModes = DurableExecutionTestHost.AllModes
                },
                state.Clock.Local, ownerProvider: state.Owners, eligibilityProvider: state.Eligibility,
                durableStore: state.Store, recordProtector: state.Protector,
                durableClock: state.Clock, codecQualifier: state.Codecs),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("options"));
            Assert.That(state.Store.Attempts, Is.Zero);
            Assert.That(state.Eligibility.AcquiredIds, Is.Empty);
            accounting.VerifyNoOtherCalls();
        }

        [TestCase(null, DurableExecutionTestHost.MessageLimit)]
        [TestCase("", DurableExecutionTestHost.MessageLimit)]
        [TestCase("relative/server", DurableExecutionTestHost.MessageLimit)]
        [TestCase("urn:durable-test:server", 0)]
        [TestCase("urn:durable-test:server", -1)]
        public async Task DurableConstructorRejectsUnqualifiedServerIdentityOrMessageBound(string? uri, int limit)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            Assert.That(() => new AsyncServiceExecutionProvider(host.Server,
                new ApplicationConfiguration
                {
                    ApplicationUri = uri!,
                    TransportQuotas = new TransportQuotas { MaxMessageSize = limit }
                },
                new AsyncServiceExecutionOptions { Enabled = true, RecoveryModes = DurableExecutionTestHost.AllModes },
                state.Clock.Local, ownerProvider: state.Owners, eligibilityProvider: state.Eligibility,
                durableStore: state.Store, recordProtector: state.Protector, durableClock: state.Clock,
                codecQualifier: state.Codecs),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("options"));
            Assert.That(state.Store.Attempts, Is.Zero);
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.Zero);
            AssertNoDispatch(host);
        }

        [TestCase(ServiceOperationRecoveryMode.Session)]
        [TestCase(ServiceOperationRecoveryMode.Owner)]
        [TestCase(ServiceOperationRecoveryMode.Durable)]
        public async Task AllThreeModesUseTheRealDispatcher(ServiceOperationRecoveryMode mode)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            SubmitRequestDataType request = host.Request(mode: mode);
            using DurableExecutionReply<SubmitResponseDataType> admitted = await host.SubmitAsync(request)
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            Assert.That(admitted.Value.Operation.RecoveryMode, Is.EqualTo(mode));
            Assert.That(admitted.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(admitted.Value.Operation.ClientOperationId, Is.EqualTo(request.ClientOperationId));
            admitted.Dispose();
            await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            await host.FinishAsync().ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> result = await host.GetAsync(token, mode: mode)
                .ConfigureAwait(false);

            AssertOriginal(result.Value.Result, host, "Call", 12);
            Assert.That(result.Value.Operation.RecoveryMode, Is.EqualTo(mode));
            Assert.That(host.Provider.SupportedRecoveryModes, Is.EqualTo(DurableExecutionTestHost.AllModes));
            Assert.That(host.Manager.Capabilities!.SupportedRecoveryModes!.Value,
                Is.EqualTo(DurableExecutionTestHost.AllModes));
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
            Assert.That(state.Eligibility.OwnerAcquisitions,
                Is.EqualTo(mode == ServiceOperationRecoveryMode.Owner ? 1 : 0));
            Assert.That(state.Eligibility.AcquiredIds,
                Has.Count.EqualTo(mode == ServiceOperationRecoveryMode.Durable ? 1 : 0));
            if (mode == ServiceOperationRecoveryMode.Durable)
            {
                RecoverableExecutionDurableRecord record = await state.Store.RecordAsync(request.ClientOperationId)
                    .ConfigureAwait(false);
                Assert.That(record.Incarnation, Is.EqualTo(state.Eligibility.AcquiredIds.Single()));
                Assert.That(record.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomeAvailable));
                Assert.That(record.WorkFinished, Is.True);
            }
            else
            {
                Assert.That(state.Store.Attempts, Is.Zero);
            }
        }

        [Test]
        public async Task AdvertisedFamiliesAndQueryRejectionRemainExact()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            NodeId[] expected =
            [
                DataTypeIds.ReadRequest, DataTypeIds.WriteRequest, DataTypeIds.CallRequest,
                DataTypeIds.HistoryReadRequest, DataTypeIds.HistoryUpdateRequest,
                DataTypeIds.BrowseRequest, DataTypeIds.BrowseNextRequest,
                DataTypeIds.TranslateBrowsePathsToNodeIdsRequest, DataTypeIds.RegisterNodesRequest,
                DataTypeIds.UnregisterNodesRequest, DataTypeIds.AddNodesRequest, DataTypeIds.AddReferencesRequest,
                DataTypeIds.DeleteNodesRequest, DataTypeIds.DeleteReferencesRequest
            ];
            Assert.That(host.Provider.SupportedServices, Is.EqualTo(expected));
            Assert.That(host.Manager.Capabilities!.SupportedServices!.Value, Is.EqualTo(expected));
            Assert.That(RseNamespaces.Rse, Is.EqualTo("http://opcfoundation.org/UA/RSE/"));
            foreach (MethodState method in new[]
            {
                host.Manager.Submit!, host.Manager.GetResult!, host.Manager.Acknowledge!,
                host.Manager.Cancel!, host.Manager.Abandon!
            })
            {
                Assert.That(host.Provider.IsManagerCall(new CallMethodRequest
                {
                    ObjectId = host.Manager.NodeId,
                    MethodId = method.NodeId
                }), Is.True);
            }
            foreach (string family in new[] { "QueryFirst", "QueryNext" })
            {
                await host.RejectAsync(RseBrowseNames.Submit, host.Request(family),
                    StatusCodes.BadServiceUnsupported).ConfigureAwait(false);
            }
            Assert.That(state.Store.Attempts, Is.Zero);
            Assert.That(state.Eligibility.AcquiredIds, Is.Empty);
            Assert.That(host.Server.Dispatches, Is.Zero);
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(7));
        }

        [TestCaseSource(nameof(LostReadinessCases))]
        public async Task LostReadinessFailsClosedForEveryManagerMethod(string lost, string method, string selector)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            SubmitRequestDataType request = host.Request();
            using DurableExecutionReply<SubmitResponseDataType> admitted = await host.SubmitAsync(request)
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await host.FinishAsync().ConfigureAwait(false);
            IReadOnlyList<KeyValuePair<string, ByteString>> before = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            state.Store.Available = lost != "store";
            state.Clock.Available = lost != "clock";

            ByteString selectedToken = selector == "known" ? token : ByteString.From(new byte[32]);
            IEncodeable argument = method switch
            {
                RseBrowseNames.Submit => host.Request(),
                RseBrowseNames.GetResult => new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Durable,
                    ServerToken = selector == "unknown-client" ? default : selectedToken,
                    ClientOperationId = selector == "unknown-client" ? Uuid.NewUuid() : default
                },
                _ => new OperationControlRequestDataType { ServerToken = selectedToken }
            };
            // This is the first manager operation after loss. A prior failed Submit must not mask
            // an unknown-lookup path that checks only a cached readiness flag.
            await host.RejectAsync(method, argument, StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
            Assert.That(host.Provider.GetStatistics().AdmittedCount, Is.EqualTo(1));
            Assert.That(host.Provider.SupportedRecoveryModes, Is.EqualTo(
            [
                ServiceOperationRecoveryMode.Session, ServiceOperationRecoveryMode.Owner
            ]));
        }

        [Test]
        public async Task RecreatedServerReturnsCompleteOriginalServiceFaultWithoutRedispatch()
        {
            await using var state = new DurableExecutionTestState();
            RecoverableExecutionDurableRecord original;
            ByteString bytes;
            await using (DurableExecutionTestHost first = await DurableExecutionTestHost.StartAsync(
                state, configure: server => server.OwnedValidationFailure = StatusCodes.BadUnexpectedError)
                .ConfigureAwait(false))
            {
                SubmitRequestDataType request = first.Request();
                using DurableExecutionReply<SubmitResponseDataType> submitted = await first.SubmitAsync(request)
                    .ConfigureAwait(false);
                ByteString token = submitted.Value.Operation.ServerToken;
                submitted.Dispose();
                await first.FinishAsync().ConfigureAwait(false);
                using DurableExecutionReply<GetResultResponseDataType> result = await first.GetAsync(token)
                    .ConfigureAwait(false);
                Assert.That(result.Value.Result.TryGetValue(out ServiceFault? fault, first.Server.MessageContext),
                    Is.True);
                Assert.That(fault!.ResponseHeader.RequestHandle, Is.EqualTo(71u));
                Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUnexpectedError));
                Assert.That(result.Value.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
                Assert.That(result.Value.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadUnexpectedError));
                bytes = ByteString.From(BinaryEncoder.EncodeMessage(fault, first.Server.MessageContext));
                original = await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false);
                Assert.That(original.Response, Is.EqualTo(bytes));
                Assert.That(first.Server.Dispatches, Is.EqualTo(1));
                Assert.That(first.Server.Scenario.Invocations, Is.Zero);
                Assert.That(first.Server.Scenario.Counter, Is.EqualTo(7));
            }
            state.Clock.Advance(TimeSpan.FromMilliseconds(50));
            await using DurableExecutionTestHost next = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> recovered = await next.GetAsync(original.Token)
                .ConfigureAwait(false);
            Assert.That(recovered.Value.Result.TryGetValue(out ServiceFault? restored, next.Server.MessageContext),
                Is.True);
            Assert.That(restored!.ResponseHeader.RequestHandle, Is.EqualTo(71u));
            Assert.That(restored.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(ByteString.From(BinaryEncoder.EncodeMessage(restored, next.Server.MessageContext)),
                Is.EqualTo(bytes));
            Assert.That(recovered.Value.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(recovered.Value.Operation.ResultRemaining, Is.EqualTo(150));
            Assert.That((await state.Store.RecordAsync(original.ClientId).ConfigureAwait(false)).OutcomeAt,
                Is.EqualTo(original.OutcomeAt));
            AssertNoDispatch(next);
        }

        [TestCase("Read", 7)]
        [TestCase("Write", 19)]
        [TestCase("Call", 12)]
        public async Task RecreatedServerReturnsOriginalReadWriteCallWithoutDispatch(string family, int expected)
        {
            await using var state = new DurableExecutionTestState();
            ByteString token;
            ByteString originalBytes;
            RecoverableExecutionDurableRecord stored;
            NodeId issuingSession;
            await using (DurableExecutionTestHost first = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false))
            {
                SubmitRequestDataType request = first.Request(family, value: family == "Write" ? expected : 5);
                using DurableExecutionReply<SubmitResponseDataType> submitted = await first.SubmitAsync(request)
                    .ConfigureAwait(false);
                token = submitted.Value.Operation.ServerToken;
                submitted.Dispose();
                await first.FinishAsync().ConfigureAwait(false);
                using DurableExecutionReply<GetResultResponseDataType> result = await first.GetAsync(token)
                    .ConfigureAwait(false);
                originalBytes = AssertOriginal(result.Value.Result, first, family, expected);
                stored = await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false);
                issuingSession = first.First.Session.Id;
                Assert.That(stored.Response, Is.EqualTo(originalBytes));
                Assert.That(stored.IssuingSessionId, Is.EqualTo(issuingSession));
                Assert.That(first.Server.Dispatches, Is.EqualTo(1));
                Assert.That(first.Server.Scenario.Invocations, Is.EqualTo(family == "Call" ? 1 : 0));
                Assert.That(first.Server.Scenario.BlobReads, Is.EqualTo(family == "Read" ? 1 : 0));
                Assert.That(first.Server.Scenario.Counter, Is.EqualTo(expected));
            }
            state.Clock.Advance(TimeSpan.FromMilliseconds(50));
            await using DurableExecutionTestHost next = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> recovered = await next.GetAsync(token)
                .ConfigureAwait(false);
            Assert.That(next.First.Session.Id, Is.Not.EqualTo(issuingSession));
            Assert.That(AssertOriginal(recovered.Value.Result, next, family, expected), Is.EqualTo(originalBytes));
            Assert.That(recovered.Value.Operation.ResultRemaining, Is.EqualTo(150));
            Assert.That(recovered.Value.Operation.ServiceId, Is.EqualTo(stored.ServiceId));
            Assert.That(recovered.Value.Operation.ClientOperationId, Is.EqualTo(stored.ClientId));
            Assert.That(recovered.Value.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(recovered.Value.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That((await state.Store.RecordAsync(stored.ClientId).ConfigureAwait(false)).OutcomeAt,
                Is.EqualTo(stored.OutcomeAt));
            Assert.That(state.Policy.Checks.Any(check => check.Session == next.First.Session.Id &&
                check.Access == AsyncServiceAccess.Collect), Is.True);
            AssertNoDispatch(next);
        }

        [Test]
        public async Task AdmissionCommitsPrecedeDispatchAndExcludeOnlyFirstWriteLatency()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionGate admission = state.Store.Gate(
                record => record.Phase == RecoverableExecutionCommitPhase.Admission);
            using DurableExecutionGate deadline = state.Store.Gate(
                record => record.Phase == RecoverableExecutionCommitPhase.Pending && !record.DispatchStarted);
            SubmitRequestDataType request = host.Request(execution: 100);
            Task<DurableExecutionReply<SubmitResponseDataType>> submitting = host.SubmitAsync(request);
            await admission.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            state.Clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.That(submitting.IsCompleted, Is.False);
            Assert.That(state.Store.Writes, Is.Empty);
            AssertNoDispatch(host);
            RecoverableExecutionTime origin = state.Clock.Now;
            admission.Release();
            await deadline.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            Assert.That((await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false)).Phase,
                Is.EqualTo(RecoverableExecutionCommitPhase.Admission));
            state.Clock.Advance(TimeSpan.FromMilliseconds(30));
            Assert.That(submitting.IsCompleted, Is.False);
            AssertNoDispatch(host);
            deadline.Release();
            using DurableExecutionReply<SubmitResponseDataType> submitted = await submitting.ConfigureAwait(false);
            ByteString token = submitted.Value.Operation.ServerToken;
            Assert.That(submitted.Value.Operation.ExecutionAllowance, Is.EqualTo(100));
            Assert.That(submitted.Value.Operation.ExecutionRemaining, Is.EqualTo(70));
            Assert.That(submitted.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            submitted.Dispose();
            await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            RecoverableExecutionDurableRecord saved = await state.Store.RecordAsync(request.ClientOperationId)
                .ConfigureAwait(false);
            Assert.That(saved.AdmittedAt, Is.EqualTo(origin));
            Assert.That(saved.DispatchStarted, Is.True);
            Assert.That(saved.Incarnation, Is.EqualTo(state.Eligibility.AcquiredIds.Single()));
            Assert.That(state.Store.Writes.Take(2).Select(write => write.Record!.Phase),
                Is.EqualTo(
                [
                    RecoverableExecutionCommitPhase.Admission, RecoverableExecutionCommitPhase.Pending
                ]));
            DurableExecutionWrite[] atDispatch = host.Server.DispatchCommits.Single();
            Assert.That(atDispatch.Select(write => write.Record!.Phase), Is.EqualTo(
            [
                RecoverableExecutionCommitPhase.Admission, RecoverableExecutionCommitPhase.Pending,
                RecoverableExecutionCommitPhase.Pending
            ]));
            Assert.That(atDispatch[2].Record!.DispatchStarted, Is.True,
                "The dispatch witness must be confirmed before the real owned handler is entered.");
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(7));
            state.Clock.Advance(TimeSpan.FromMilliseconds(69));
            using DurableExecutionReply<GetResultResponseDataType> before = await host.GetAsync(token)
                .ConfigureAwait(false);
            Assert.That(before.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(before.Value.Operation.ExecutionRemaining, Is.EqualTo(1));
        }

        [TestCase(100)]
        [TestCase(101)]
        public async Task SecondAdmissionWriteCanExhaustExecutionBeforeDispatch(int latency)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionGate deadline = state.Store.Gate(
                record => record.Phase == RecoverableExecutionCommitPhase.Pending && !record.DispatchStarted);
            Task<DurableExecutionReply<SubmitResponseDataType>> submitting =
                host.SubmitAsync(host.Request(execution: 100));
            await deadline.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            state.Clock.Advance(TimeSpan.FromMilliseconds(latency));
            AssertNoDispatch(host);
            Assert.That(submitting.IsCompleted, Is.False);
            deadline.Release();
            using DurableExecutionReply<SubmitResponseDataType> submitted = await submitting.ConfigureAwait(false);
            ByteString token = submitted.Value.Operation.ServerToken;
            submitted.Dispose();
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
            await state.Eligibility.WaitForReleasesAsync(1).ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> result = await host.GetAsync(token)
                .ConfigureAwait(false);

            Assert.That(result.Value.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(result.Value.Operation.OutcomeKind,
                Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(result.Value.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(result.Value.Operation.ResultRemaining, Is.EqualTo(200));
            Assert.That(result.Value.Result.IsNull, Is.True);
            Assert.That(state.Eligibility.Releases, Is.EqualTo(1));
            AssertNoDispatch(host);
        }

        [Test]
        public async Task InitialSubmitWaitUsesPostAdmissionOrigin()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionGate admission = state.Store.Gate(
                record => record.Phase == RecoverableExecutionCommitPhase.Admission);
            using DurableExecutionGate deadline = state.Store.Gate(
                record => record.Phase == RecoverableExecutionCommitPhase.Pending && !record.DispatchStarted);
            TaskCompletionSource<bool> waiting = DurableExecutionGate.Signal();
            state.Clock.Local.TimerCreated = due =>
            {
                if (due == TimeSpan.FromMilliseconds(70))
                {
                    waiting.TrySetResult(true);
                }
            };
            Task<DurableExecutionReply<SubmitResponseDataType>> submitting = host.SubmitAsync(
                host.Request(execution: 1000, wait: 100));
            await admission.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            state.Clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.That(submitting.IsCompleted, Is.False);
            admission.Release();
            await deadline.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            state.Clock.Advance(TimeSpan.FromMilliseconds(30));
            deadline.Release();
            await waiting.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            state.Clock.Advance(TimeSpan.FromMilliseconds(69));
            Assert.That(submitting.IsCompleted, Is.False);
            state.Clock.Advance(TimeSpan.FromMilliseconds(1));
            using DurableExecutionReply<SubmitResponseDataType> submitted = await submitting
                .WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);

            Assert.That(submitted.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(submitted.Value.Operation.ExecutionRemaining, Is.EqualTo(900));
            Assert.That(submitted.Value.Operation.ResultRemaining, Is.Zero);
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(7));
            Assert.That(host.Server.OwnedContext!.CancellationToken.IsCancellationRequested, Is.False);
        }

        [Test]
        public async Task OutcomeIsNotAvailableUntilBothCommitsReturn()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            SubmitRequestDataType request = host.Request();
            using DurableExecutionReply<SubmitResponseDataType> admitted = await host.SubmitAsync(request)
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            using DurableExecutionGate prepare = state.Store.Gate(
                record => record.Phase == RecoverableExecutionCommitPhase.OutcomePreparing);
            using DurableExecutionGate available = state.Store.Gate(
                record => record.Phase == RecoverableExecutionCommitPhase.OutcomeAvailable);
            host.Server.Scenario.Release();
            await prepare.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            state.Policy.CollectionEntered = DurableExecutionGate.Signal();
            Task<DurableExecutionReply<GetResultResponseDataType>> collecting = host.GetAsync(token);
            await state.Policy.CollectionEntered.Task.WaitAsync(DurableExecutionTestHost.Watchdog)
                .ConfigureAwait(false);
            Assert.That(collecting.IsCompleted, Is.False);
            Assert.That((await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false)).Phase,
                Is.EqualTo(RecoverableExecutionCommitPhase.Pending));
            state.Clock.Advance(TimeSpan.FromMilliseconds(40));
            RecoverableExecutionTime outcomeOrigin = state.Clock.Now;
            prepare.Release();
            await available.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            Assert.That(collecting.IsCompleted, Is.False);
            Assert.That((await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false)).Phase,
                Is.EqualTo(RecoverableExecutionCommitPhase.OutcomePreparing));
            state.Clock.Advance(TimeSpan.FromMilliseconds(25));
            available.Release();
            using DurableExecutionReply<GetResultResponseDataType> result = await collecting.ConfigureAwait(false);

            AssertOriginal(result.Value.Result, host, "Call", 12);
            Assert.That(result.Value.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(result.Value.Operation.ResultRemaining, Is.EqualTo(175));
            Assert.That((await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false)).OutcomeAt,
                Is.EqualTo(outcomeOrigin));
            Assert.That(state.Store.Writes.Count(IsOutcomeSelection), Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
        }

        [TestCase(RseBrowseNames.Acknowledge, false)]
        [TestCase(RseBrowseNames.Acknowledge, true)]
        [TestCase(RseBrowseNames.Abandon, false)]
        [TestCase(RseBrowseNames.Abandon, true)]
        public async Task ReleaseIsNotAppliedUntilCommitReturns(string method, bool committedButUnconfirmed)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            SubmitRequestDataType request = host.Request();
            using DurableExecutionReply<SubmitResponseDataType> admitted = await host.SubmitAsync(request)
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            if (method == RseBrowseNames.Acknowledge)
            {
                await host.FinishAsync().ConfigureAwait(false);
            }
            using DurableExecutionGate release = state.Store.Gate(
                record => record.Phase == RecoverableExecutionCommitPhase.Released, committedButUnconfirmed);
            RecoverableExecutionTime releasedAt = state.Clock.Now;
            Task<DurableExecutionReply<ControlResponseDataType>> controlling = host.ControlAsync(method, token);
            await release.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            state.Clock.Advance(TimeSpan.FromMilliseconds(25));
            Assert.That(controlling.IsCompleted, Is.False,
                "Even an actually published write must not be called Applied before confirmation.");
            RecoverableExecutionDurableRecord stored = await state.Store.RecordAsync(request.ClientOperationId)
                .ConfigureAwait(false);
            Assert.That(stored.IsReleased, Is.EqualTo(committedButUnconfirmed));
            release.Release();
            using DurableExecutionReply<ControlResponseDataType> applied = await controlling.ConfigureAwait(false);
            Assert.That(applied.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(applied.Value.Operation.State, Is.EqualTo(ReleasedState(method)));
            Assert.That(applied.Value.Operation.HistoryRemaining, Is.EqualTo(175));
            Assert.That((await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false)).ReleasedAt,
                Is.EqualTo(releasedAt));
            applied.Dispose();
            using DurableExecutionReply<ControlResponseDataType> again = await host.ControlAsync(method, token)
                .ConfigureAwait(false);
            Assert.That(again.Value.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            Assert.That(again.Value.Operation.HistoryRemaining, Is.EqualTo(175));
            Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
        }

        [TestCaseSource(nameof(UncertainWriteCases))]
        public async Task UncertainAdmissionOutcomeOrReleaseFailsClosed(string transition, bool afterCommit)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            SubmitRequestDataType request = host.Request();
            bool admission = transition is "Admission" or "Deadline";
            state.Store.FailAfterCommit = afterCommit;
            if (admission)
            {
                state.Store.FailWrite = record => MatchesTransition(record, transition);
                await host.RejectAsync(RseBrowseNames.Submit, request, StatusCodes.BadResourceUnavailable)
                    .ConfigureAwait(false);
                AssertNoDispatch(host);
            }
            else
            {
                using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(request)
                    .ConfigureAwait(false);
                ByteString token = submitted.Value.Operation.ServerToken;
                submitted.Dispose();
                await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
                if (transition == RseBrowseNames.Acknowledge)
                {
                    await host.FinishAsync().ConfigureAwait(false);
                }
                state.Store.FailWrite = record => MatchesTransition(record, transition);
                if (transition is "OutcomePreparing" or "OutcomeAvailable")
                {
                    host.Server.Scenario.Release();
                    await state.Logs.Unavailable.Task.WaitAsync(DurableExecutionTestHost.Watchdog)
                        .ConfigureAwait(false);
                    await state.Eligibility.WaitForReleasesAsync(1).ConfigureAwait(false);
                }
                else
                {
                    await host.RejectAsync(transition, new OperationControlRequestDataType { ServerToken = token },
                        StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
                }
                await host.RejectAsync(RseBrowseNames.GetResult, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Durable,
                    ServerToken = token
                }, StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
                Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
            }
            Assert.That(state.Store.Failures, Is.EqualTo(1));
            Assert.That(state.Store.Writes.Count(write => write.Record != null &&
                MatchesTransition(write.Record, transition)), Is.EqualTo(afterCommit ? 1 : 0));
            await host.RejectAsync(RseBrowseNames.GetResult, new GetResultRequestDataType
            {
                RecoveryMode = ServiceOperationRecoveryMode.Durable,
                ClientOperationId = request.ClientOperationId
            }, StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
            Assert.That(host.Server.Dispatches, Is.EqualTo(admission ? 0 : 1));
        }

        [TestCase("store", false)]
        [TestCase("clock", false)]
        [TestCase("write", false)]
        [TestCase("write", true)]
        public async Task PreCasQualificationLossAndAmbiguousAdmissionHaveDistinctAccounting(
            string failure, bool afterCommit)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using var qualified = new DurableExecutionGate();
            state.Owners.ValidationGate = qualified;
            bool ambiguous = failure == "write";
            if (ambiguous)
            {
                state.Store.FailWrite = record => record.Phase == RecoverableExecutionCommitPhase.Admission;
                state.Store.FailAfterCommit = afterCommit;
            }
            SubmitRequestDataType request = host.Request();
            Task rejected = host.RejectAsync(RseBrowseNames.Submit, request, StatusCodes.BadResourceUnavailable);
            await qualified.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            Assert.That(state.Eligibility.AcquiredIds, Has.Count.EqualTo(1),
                "The held reservation must have returned before qualification is lost.");
            Assert.That(state.Eligibility.ReleaseCalls, Is.Zero);
            Assert.That(state.Store.Attempts, Is.Zero);
            AssertNoDispatch(host);
            if (failure == "store")
            {
                state.Store.Available = false;
            }
            else if (failure == "clock")
            {
                state.Clock.Available = false;
            }
            qualified.Release();
            await rejected.ConfigureAwait(false);
            await state.Eligibility.WaitForReleasesAsync(1).ConfigureAwait(false);

            long work = ambiguous ? 1 : 0;
            long bytes = ambiguous ? kCompletedBytes : 0;
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, work).ConfigureAwait(false);
            await host.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, work).ConfigureAwait(false);
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, bytes).ConfigureAwait(false);
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(work));
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.EqualTo(work));
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(bytes));
            Assert.That(host.Provider.GetStatistics().ActiveOperationCount, Is.EqualTo(work));
            Assert.That(host.Provider.GetStatistics().AdmittedCount, Is.Zero,
                "A possible write is not confirmed admission.");
            Assert.That(host.Provider.GetStatistics().RetainedOutcomeCount, Is.Zero);
            Assert.That(host.Provider.GetStatistics().TerminalRecordCount, Is.Zero);
            Assert.That(state.Eligibility.ReleaseCalls, Is.EqualTo(1));
            Assert.That(state.Eligibility.Releases, Is.EqualTo(1));
            Assert.That(state.Store.Attempts, Is.EqualTo(ambiguous ? 1 : 0));
            Assert.That(state.Store.Failures, Is.EqualTo(ambiguous ? 1 : 0));
            Assert.That(state.Store.ConfirmedWrites, Is.Empty);
            IReadOnlyList<KeyValuePair<string, ByteString>> image = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            Assert.That(image, Has.Count.EqualTo(ambiguous && afterCommit ? 1 : 0));
            if (ambiguous && afterCommit)
            {
                RecoverableExecutionDurableRecord stored = state.Store.Decode(image[0].Key, image[0].Value)!;
                Assert.That(stored.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Admission));
                Assert.That(stored.ClientId, Is.EqualTo(request.ClientOperationId));
                Assert.That(stored.DispatchStarted, Is.False);
                Assert.That(stored.WorkFinished, Is.False);
            }

            // Owner shares this ClientID namespace, but not the failed Durable health qualification.
            // Reject at Owner eligibility so reaching that seam proves index removal without dispatch.
            state.Eligibility.OwnerRejection = StatusCodes.BadServiceUnsupported;
            SubmitRequestDataType probe = host.Request(mode: ServiceOperationRecoveryMode.Owner);
            probe.ClientOperationId = request.ClientOperationId;
            await host.RejectAsync(RseBrowseNames.Submit, probe,
                ambiguous ? StatusCodes.BadInvalidState : StatusCodes.BadServiceUnsupported).ConfigureAwait(false);
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, work).ConfigureAwait(false);
            await host.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, work).ConfigureAwait(false);
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, bytes).ConfigureAwait(false);
            Assert.That(state.Eligibility.OwnerAcquisitions, Is.Zero);
            Assert.That(state.Eligibility.AcquiredIds, Has.Count.EqualTo(1));
            Assert.That(state.Store.Attempts, Is.EqualTo(ambiguous ? 1 : 0));
            AssertNoDispatch(host);
        }

        [TestCase(RseBrowseNames.Acknowledge)]
        [TestCase(RseBrowseNames.Abandon)]
        [TestCase(RseBrowseNames.Cancel)]
        public async Task ControlsAreDistinctAndIdempotent(string method)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(host.Request())
                .ConfigureAwait(false);
            ByteString token = submitted.Value.Operation.ServerToken;
            Uuid clientId = submitted.Value.Operation.ClientOperationId;
            submitted.Dispose();
            await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            using (DurableExecutionReply<ControlResponseDataType> premature = await host.ControlAsync(
                RseBrowseNames.Acknowledge, token).ConfigureAwait(false))
            {
                Assert.That(premature.Value.Result, Is.EqualTo(ServiceOperationControlResult.NotApplicable));
                Assert.That(premature.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            }
            if (method == RseBrowseNames.Acknowledge)
            {
                await host.FinishAsync().ConfigureAwait(false);
            }
            using DurableExecutionReply<ControlResponseDataType> first = await host.ControlAsync(method, token)
                .ConfigureAwait(false);
            Assert.That(first.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(first.Value.Operation.State, Is.EqualTo(method == RseBrowseNames.Cancel
                ? ServiceOperationState.Pending : ReleasedState(method)));
            Assert.That(first.Value.Operation.CancellationRequested, Is.EqualTo(method == RseBrowseNames.Cancel));
            Assert.That(first.Value.Operation.HistoryRemaining, Is.EqualTo(method == RseBrowseNames.Cancel ? 0 : 200));
            first.Dispose();
            using DurableExecutionReply<ControlResponseDataType> again = await host.ControlAsync(method, token)
                .ConfigureAwait(false);
            Assert.That(again.Value.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            Assert.That(again.Value.Operation.CancellationRequested, Is.EqualTo(method == RseBrowseNames.Cancel));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(method == RseBrowseNames.Acknowledge ? 12 : 7));
            if (method == RseBrowseNames.Abandon)
            {
                Assert.That(host.Server.OwnedContext!.CancellationToken.IsCancellationRequested, Is.False);
                Assert.That(host.Provider.GetStatistics().ActiveOperationCount, Is.EqualTo(1));
                Assert.That(state.Eligibility.Releases, Is.Zero);
            }
            if (method == RseBrowseNames.Cancel)
            {
                await CancellationObservedAsync(host.Server.OwnedContext!.CancellationToken).ConfigureAwait(false);
                Assert.That(host.Provider.GetStatistics().ActiveOperationCount, Is.EqualTo(1));
                Assert.That(state.Eligibility.Releases, Is.Zero);
            }
            again.Dispose();
            await host.FinishAsync().ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> result = await host.GetAsync(token)
                .ConfigureAwait(false);
            if (method == RseBrowseNames.Cancel)
            {
                RecoverableExecutionDurableRecord stored = await state.Store.RecordAsync(clientId)
                    .ConfigureAwait(false);
                Assert.That(AssertCancelledOriginal(result.Value.Result, host), Is.EqualTo(stored.Response));
                Assert.That(result.Value.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                Assert.That(result.Value.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
                Assert.That(result.Value.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                Assert.That(stored.Status, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            }
            else
            {
                Assert.That(result.Value.Operation.State, Is.EqualTo(ReleasedState(method)));
                Assert.That(result.Value.Result.IsNull, Is.True);
            }
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
            Assert.That(state.Eligibility.Releases, Is.EqualTo(1));
        }

        [TestCase(RseBrowseNames.Acknowledge)]
        [TestCase(RseBrowseNames.Abandon)]
        [TestCase(RseBrowseNames.Cancel)]
        public async Task LostControlReplyRetriesOnlyControl(string method)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(host.Request())
                .ConfigureAwait(false);
            ByteString token = submitted.Value.Operation.ServerToken;
            submitted.Dispose();
            await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            if (method == RseBrowseNames.Acknowledge)
            {
                await host.FinishAsync().ConfigureAwait(false);
            }
            using (DurableExecutionReply<ControlResponseDataType> lost = await host.ControlAsync(method, token)
                .ConfigureAwait(false))
            {
                Assert.That(lost.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                // Dispose the enclosing response without giving its disposition to the hypothetical caller.
            }
            DurableExecutionPeer replacement = await host.AddPeerAsync().ConfigureAwait(false);
            state.Clock.Advance(TimeSpan.FromMilliseconds(25));
            int commits = state.Store.Writes.Count;
            using DurableExecutionReply<ControlResponseDataType> retried = await host.ControlAsync(
                method, token, replacement).ConfigureAwait(false);

            Assert.That(retried.Value.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            Assert.That(retried.Value.Operation.HistoryRemaining,
                Is.EqualTo(method == RseBrowseNames.Cancel ? 0 : 175));
            Assert.That(retried.Value.Operation.CancellationRequested, Is.EqualTo(method == RseBrowseNames.Cancel));
            Assert.That(state.Store.Writes, Has.Count.EqualTo(commits));
            Assert.That(state.Eligibility.AcquiredIds, Has.Count.EqualTo(1));
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
        }

        [Test]
        public async Task CancellingWrapperWaitDoesNotCancelDurableWork()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(host.Request())
                .ConfigureAwait(false);
            ByteString token = submitted.Value.Operation.ServerToken;
            submitted.Dispose();
            await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            state.Policy.CollectionEntered = DurableExecutionGate.Signal();
            // Colliding original and wrapper handles must not make ordinary Cancel target owned work.
            Task<IServiceResponse> waiting = host.SendAsync(RseBrowseNames.GetResult, new GetResultRequestDataType
            {
                RecoveryMode = ServiceOperationRecoveryMode.Durable,
                ServerToken = token,
                WaitTime = 5000
            }, handle: 71);
            await state.Policy.CollectionEntered.Task.WaitAsync(DurableExecutionTestHost.Watchdog)
                .ConfigureAwait(false);
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 1).ConfigureAwait(false);
            Assert.That(await host.CancelExchangeAsync(71).ConfigureAwait(false), Is.EqualTo(1u));
            IServiceResponse cancelled = await waiting.ConfigureAwait(false);
            try
            {
                DurableExecutionTestHost.AssertFailure(cancelled, StatusCodes.BadRequestCancelledByClient);
            }
            finally
            {
                (cancelled as IPooledEncodeable)?.Reuse();
            }
            using DurableExecutionReply<GetResultResponseDataType> pending = await host.GetAsync(token)
                .ConfigureAwait(false);
            Assert.That(pending.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(pending.Value.Operation.CancellationRequested, Is.False);
            Assert.That(host.Server.OwnedContext!.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(state.Eligibility.Releases, Is.Zero);
            pending.Dispose();
            await host.FinishAsync().ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> result = await host.GetAsync(token)
                .ConfigureAwait(false);
            AssertOriginal(result.Value.Result, host, "Call", 12);
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
        }

        [TestCase(ServiceOperationRecoveryMode.Owner, ServiceOperationRecoveryMode.Durable, false)]
        [TestCase(ServiceOperationRecoveryMode.Owner, ServiceOperationRecoveryMode.Durable, true)]
        [TestCase(ServiceOperationRecoveryMode.Durable, ServiceOperationRecoveryMode.Owner, false)]
        [TestCase(ServiceOperationRecoveryMode.Durable, ServiceOperationRecoveryMode.Owner, true)]
        public async Task OwnerAndDurableShareDuplicateNamespace(
            ServiceOperationRecoveryMode firstMode, ServiceOperationRecoveryMode secondMode, bool differentInput)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            SubmitRequestDataType original = host.Request(mode: firstMode);
            using DurableExecutionReply<SubmitResponseDataType> first = await host.SubmitAsync(original)
                .ConfigureAwait(false);
            ByteString token = first.Value.Operation.ServerToken;
            first.Dispose();
            DurableExecutionPeer replacement = await host.AddPeerAsync().ConfigureAwait(false);
            SubmitRequestDataType duplicate = host.Request(mode: secondMode, value: differentInput ? 900 : 5);
            duplicate.ClientOperationId = original.ClientOperationId;
            await host.RejectAsync(RseBrowseNames.Submit, duplicate, StatusCodes.BadInvalidState, replacement)
                .ConfigureAwait(false);
            SubmitRequestDataType session = host.Request(mode: ServiceOperationRecoveryMode.Session, value: 2);
            session.ClientOperationId = original.ClientOperationId;
            using DurableExecutionReply<SubmitResponseDataType> distinct = await host.SubmitAsync(session, replacement)
                .ConfigureAwait(false);
            Assert.That(distinct.Value.Operation.ServerToken, Is.Not.EqualTo(token));
            Assert.That(distinct.Value.Operation.RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Session));
            distinct.Dispose();
            await host.FinishAsync().ConfigureAwait(false);

            Assert.That(host.Server.Dispatches, Is.EqualTo(2));
            Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(2));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(14),
                "The rejected duplicate, with either matching or different inputs, must never reach the handler.");
            Assert.That(host.Provider.GetStatistics().AdmittedCount, Is.EqualTo(2));
        }

        [TestCase(ServiceOperationRecoveryMode.Owner, ServiceOperationRecoveryMode.Durable)]
        [TestCase(ServiceOperationRecoveryMode.Durable, ServiceOperationRecoveryMode.Owner)]
        [TestCase(ServiceOperationRecoveryMode.Durable, ServiceOperationRecoveryMode.Durable)]
        public async Task ReplacementSessionsCannotMultiplySharedOwnerCapacity(
            ServiceOperationRecoveryMode firstMode, ServiceOperationRecoveryMode secondMode)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(
                state, ownerExecutionLimit: 1).ConfigureAwait(false);
            using DurableExecutionReply<SubmitResponseDataType> first =
                await host.SubmitAsync(host.Request(mode: firstMode))
                .ConfigureAwait(false);
            first.Dispose();
            await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            DurableExecutionPeer replacement = await host.AddPeerAsync().ConfigureAwait(false);
            await host.RejectAsync(RseBrowseNames.Submit, host.Request(mode: secondMode),
                StatusCodes.BadResourceUnavailable, replacement).ConfigureAwait(false);

            await host.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 1).ConfigureAwait(false);
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(host.Provider.GetStatistics().ActiveOperationCount, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(7));
            await host.FinishAsync().ConfigureAwait(false);
            using DurableExecutionReply<SubmitResponseDataType> nowFits = await host.SubmitAsync(
                host.Request(mode: secondMode), replacement).ConfigureAwait(false);
            nowFits.Dispose();
            await host.FinishAsync().ConfigureAwait(false);
            Assert.That(host.Server.Dispatches, Is.EqualTo(2));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(17));
        }

        [TestCase("Admission")]
        [TestCase("Pending")]
        [TestCase("OutcomePreparing")]
        public async Task IncompleteAdmissionOrOutcomeRecoversIndeterminateOnceWithoutRedispatch(string phase)
        {
            (DurableExecutionTestState recoveredState, RecoverableExecutionDurableRecord interrupted) =
                await InterruptedImageAsync(phase).ConfigureAwait(false);
            await using DurableExecutionTestState state = recoveredState;
            state.Clock.Advance(TimeSpan.FromSeconds(2));
            using DurableExecutionGate outcome = state.Store.Gate(record =>
                record.Phase == RecoverableExecutionCommitPhase.OutcomePreparing &&
                record.Kind == ServiceOperationOutcome.ExecutionIndeterminate);
            Task<DurableExecutionTestHost> starting = DurableExecutionTestHost.StartAsync(state);
            await outcome.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            state.Clock.Advance(TimeSpan.FromMilliseconds(37));
            RecoverableExecutionTime origin = state.Clock.Now;
            bool earlyStartup = starting.IsCompleted;
            bool advertisedEarly = state.LastServer!.Provider!.SupportedRecoveryModes
                .Contains(ServiceOperationRecoveryMode.Durable);
            outcome.Release();
            await using (DurableExecutionTestHost first = await starting.ConfigureAwait(false))
            {
                Assert.That(earlyStartup, Is.False);
                Assert.That(advertisedEarly, Is.False);
                using DurableExecutionReply<GetResultResponseDataType> result = await first.GetAsync(
                    clientId: interrupted.ClientId).ConfigureAwait(false);
                Assert.That(result.Value.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                Assert.That(result.Value.Operation.OutcomeKind,
                    Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
                Assert.That(result.Value.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadUnexpectedError));
                Assert.That(result.Value.Operation.ServerToken, Is.EqualTo(interrupted.Token));
                Assert.That(result.Value.Operation.ResultRemaining, Is.EqualTo(200));
                Assert.That(result.Value.Operation.ExecutionRemaining, Is.Zero);
                Assert.That(result.Value.Result.IsNull, Is.True);
                RecoverableExecutionDurableRecord saved = await state.Store.RecordAsync(interrupted.ClientId)
                    .ConfigureAwait(false);
                Assert.That(saved.OutcomeAt, Is.EqualTo(origin));
                Assert.That(saved.Incarnation, Is.EqualTo(interrupted.Incarnation));
                Assert.That(saved.AdmittedAt, Is.EqualTo(interrupted.AdmittedAt));
                AssertNoDispatch(first);
            }
            state.Clock.Advance(TimeSpan.FromMilliseconds(50));
            await using DurableExecutionTestHost second = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> again = await second.GetAsync(interrupted.Token)
                .ConfigureAwait(false);
            Assert.That(again.Value.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(again.Value.Operation.ResultRemaining, Is.EqualTo(150));
            Assert.That((await state.Store.RecordAsync(interrupted.ClientId).ConfigureAwait(false)).OutcomeAt,
                Is.EqualTo(origin));
            Assert.That(state.Store.Writes.Count(write =>
                IsOutcomeSelection(write) && write.Record!.Kind == ServiceOperationOutcome.ExecutionIndeterminate),
                Is.EqualTo(1),
                "A second startup must not select another indeterminate outcome or restart R.");
            Assert.That(state.Eligibility.AcquiredIds, Is.Empty, "Recovery is not another admission.");
            Assert.That(state.Eligibility.RecoveredIds,
                Is.EqualTo(!interrupted.WorkFinished && interrupted.DispatchStarted
                    ? (Uuid[])[interrupted.Incarnation] : []));
            AssertNoDispatch(second);
        }

        [TestCase(1_999_999L, ServiceOperationState.OutcomeAvailable, 0, 0)]
        [TestCase(2_000_000L, ServiceOperationState.Expired, 0, 200)]
        [TestCase(2_000_001L, ServiceOperationState.Expired, 0, 199)]
        [TestCase(2_500_000L, ServiceOperationState.Expired, 0, 150)]
        [TestCase(3_999_999L, ServiceOperationState.Expired, 0, 0)]
        [TestCase(4_000_000L, ServiceOperationState.Unknown, 0, 0)]
        [TestCase(4_000_001L, ServiceOperationState.Unknown, 0, 0)]
        public async Task DowntimeUsesOriginalResultAndHistoryBoundaries(
            long downtimeTicks, ServiceOperationState expected, double resultRemaining, double historyRemaining)
        {
            await using var state = new DurableExecutionTestState();
            RecoverableExecutionDurableRecord original;
            await using (DurableExecutionTestHost first = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false))
            {
                SubmitRequestDataType request = first.Request();
                using DurableExecutionReply<SubmitResponseDataType> submitted = await first.SubmitAsync(request)
                    .ConfigureAwait(false);
                submitted.Dispose();
                await first.FinishAsync().ConfigureAwait(false);
                original = await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false);
                Assert.That(original.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomeAvailable));
            }
            // No old provider remains to process a timer during this elapsed downtime.
            state.Clock.Advance(TimeSpan.FromTicks(downtimeTicks));
            await using DurableExecutionTestHost next = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> result = await next.GetAsync(original.Token)
                .ConfigureAwait(false);

            Assert.That(result.Value.Operation.State, Is.EqualTo(expected));
            Assert.That(result.Value.Operation.ResultRemaining, Is.EqualTo(resultRemaining));
            Assert.That(result.Value.Operation.HistoryRemaining, Is.EqualTo(historyRemaining));
            if (expected == ServiceOperationState.Unknown)
            {
                AssertUnknown(result.Value);
                Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.Empty);
            }
            else
            {
                RecoverableExecutionDurableRecord saved = await state.Store.RecordAsync(original.ClientId)
                    .ConfigureAwait(false);
                Assert.That(saved.OutcomeAt, Is.EqualTo(original.OutcomeAt));
                Assert.That(saved.AdmittedAt, Is.EqualTo(original.AdmittedAt));
                if (expected == ServiceOperationState.Expired)
                {
                    Assert.That(saved.ReleasedAt, Is.EqualTo(new RecoverableExecutionTime(
                        original.OutcomeAt.DomainId, original.OutcomeAt.Ticks + original.Retention.Ticks)));
                    Assert.That(saved.Response.IsNull, Is.True);
                    Assert.That(result.Value.Result.IsNull, Is.True);
                }
                else
                {
                    Assert.That(AssertOriginal(result.Value.Result, next, "Call", 12), Is.EqualTo(original.Response));
                }
            }
            AssertNoDispatch(next);
        }

        [TestCase(RseBrowseNames.Acknowledge, 1_999_999L)]
        [TestCase(RseBrowseNames.Acknowledge, 2_000_000L)]
        [TestCase(RseBrowseNames.Acknowledge, 2_000_001L)]
        [TestCase(RseBrowseNames.Abandon, 1_999_999L)]
        [TestCase(RseBrowseNames.Abandon, 2_000_000L)]
        [TestCase(RseBrowseNames.Abandon, 2_000_001L)]
        public async Task ReleasedRecordsNeverResurrectOrRenewHistory(string method, long downtimeTicks)
        {
            await using var state = new DurableExecutionTestState();
            RecoverableExecutionDurableRecord released;
            await using (DurableExecutionTestHost first = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false))
            {
                SubmitRequestDataType request = first.Request();
                using DurableExecutionReply<SubmitResponseDataType> submitted = await first.SubmitAsync(request)
                    .ConfigureAwait(false);
                ByteString token = submitted.Value.Operation.ServerToken;
                submitted.Dispose();
                await first.FinishAsync().ConfigureAwait(false);
                state.Clock.Advance(TimeSpan.FromMilliseconds(25));
                using DurableExecutionReply<ControlResponseDataType> control = await first.ControlAsync(method, token)
                    .ConfigureAwait(false);
                Assert.That(control.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                released = await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false);
            }
            state.Clock.Advance(TimeSpan.FromTicks(downtimeTicks));
            await using DurableExecutionTestHost next = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> result = await next.GetAsync(released.Token)
                .ConfigureAwait(false);
            if (downtimeTicks >= released.Retention.Ticks)
            {
                AssertUnknown(result.Value);
                Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.Empty);
            }
            else
            {
                Assert.That(result.Value.Operation.State, Is.EqualTo(ReleasedState(method)));
                Assert.That(result.Value.Operation.HistoryRemaining, Is.Zero,
                    "One remaining tick rounds down but does not make the released record Unknown early.");
                Assert.That(result.Value.Operation.ResultRemaining, Is.Zero);
                Assert.That(result.Value.Result.IsNull, Is.True);
                RecoverableExecutionDurableRecord saved = await state.Store.RecordAsync(released.ClientId)
                    .ConfigureAwait(false);
                Assert.That(saved.ReleasedAt, Is.EqualTo(released.ReleasedAt));
                Assert.That(saved.OutcomeAt, Is.EqualTo(released.OutcomeAt));
                Assert.That(saved.Response.IsNull, Is.True);
                result.Dispose();
                using DurableExecutionReply<ControlResponseDataType> retry =
                    await next.ControlAsync(method, released.Token)
                    .ConfigureAwait(false);
                Assert.That(retry.Value.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
                Assert.That(retry.Value.Operation.HistoryRemaining, Is.Zero);
            }
            Assert.That(state.Store.Writes.Count(IsOutcomeSelection), Is.EqualTo(1));
            AssertNoDispatch(next);
        }

        [Test]
        public async Task RecoveryReservesExistingPoolsBeforeAdvertisingDurable()
        {
            await using DurableExecutionTestState state = await PendingImageAsync(2).ConfigureAwait(false);
            state.Eligibility.TerminationEstablished = false;
            state.Store.GateScanAt = 2;
            using var scan = new DurableExecutionGate();
            state.Store.ScanGate = scan;
            Task<DurableExecutionTestHost> starting = DurableExecutionTestHost.StartAsync(state);
            await scan.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            DurableExecutionTestServer server = state.LastServer!;
            var isolation = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
            bool earlyStartup = starting.IsCompleted;
            ArrayOf<ServiceOperationRecoveryMode> earlyModes = server.Provider!.SupportedRecoveryModes;
            long execution = isolation.GetUsage(ResourceIsolationStage.RequestExecution);
            long history = isolation.GetUsage(ResourceIsolationStage.ParkedRequest);
            long bytes = isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes);
            int fences = state.Eligibility.ActiveFences;
            scan.Release();
            await using DurableExecutionTestHost host = await starting.ConfigureAwait(false);

            Assert.That(earlyStartup, Is.False);
            Assert.That(earlyModes, Is.EqualTo(
            [
                ServiceOperationRecoveryMode.Session, ServiceOperationRecoveryMode.Owner
            ]));
            Assert.That(execution, Is.EqualTo(1));
            Assert.That(history, Is.EqualTo(1));
            Assert.That(bytes, Is.EqualTo(kRestoredActiveBytes));
            Assert.That(fences, Is.EqualTo(1));
            Assert.That(host.Provider.SupportedRecoveryModes, Is.EqualTo(DurableExecutionTestHost.AllModes));
            Assert.That(host.Manager.Capabilities!.SupportedRecoveryModes!.Value,
                Is.EqualTo(DurableExecutionTestHost.AllModes));
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(2));
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.EqualTo(2));
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes),
                Is.EqualTo(2 * kRestoredActiveBytes));
            Assert.That(state.Eligibility.RecoveredIds.Distinct().Count(), Is.EqualTo(2),
                "Two admissions need two execution identities, not one process incarnation.");
            Assert.That(state.Eligibility.ActiveFences, Is.EqualTo(2));
            AssertNoDispatch(host);
        }

        [Test]
        public async Task FailedFullLoadReturnsEveryRestoredReservation()
        {
            await using DurableExecutionTestState state = await PendingImageAsync(2).ConfigureAwait(false);
            state.Eligibility.TerminationEstablished = false;
            state.Store.GateScanAt = 2;
            state.Store.FailScanAt = 2;
            using var scan = new DurableExecutionGate();
            state.Store.ScanGate = scan;
            Task<DurableExecutionTestHost> starting = DurableExecutionTestHost.StartAsync(state);
            await scan.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            DurableExecutionTestServer server = state.LastServer!;
            var isolation = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
            long charged = isolation.GetUsage(ResourceIsolationStage.RequestExecution);
            int fenced = state.Eligibility.ActiveFences;
            scan.Release();
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () =>
                {
                    await using DurableExecutionTestHost unexpected = await starting.ConfigureAwait(false);
                }).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(charged, Is.EqualTo(1), "The later failure must follow a real restored reservation.");
            Assert.That(fenced, Is.EqualTo(1));
            Assert.That(isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.Zero);
            Assert.That(isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.Zero);
            Assert.That(isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes), Is.Zero);
            Assert.That(state.Eligibility.RecoveryReleases, Is.EqualTo(1));
            Assert.That(state.Eligibility.ActiveFences, Is.Zero);
            Assert.That(server.Provider!.SupportedRecoveryModes.Span.ToArray(),
                Does.Not.Contain(ServiceOperationRecoveryMode.Durable));
            Assert.That(server.Dispatches, Is.Zero);
            Assert.That(server.Scenario.Counter, Is.EqualTo(7));
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Has.Count.EqualTo(2),
                "Startup cleanup releases local reservations, not durable results or identities.");
        }

        [Test]
        public async Task ActiveOrphanRemainsChargedPastHistoryUntilExplicitTerminationProof()
        {
            await using DurableExecutionTestState state = await PendingImageAsync(1).ConfigureAwait(false);
            IReadOnlyList<KeyValuePair<string, ByteString>> image = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            RecoverableExecutionDurableRecord pending = state.Store.Decode(image[0].Key, image[0].Value)!;
            state.Eligibility.TerminationEstablished = false;
            await using (DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(
                state, ownerExecutionLimit: 1).ConfigureAwait(false))
            {
                using (DurableExecutionReply<GetResultResponseDataType> outcome = await host.GetAsync(pending.Token)
                    .ConfigureAwait(false))
                {
                    Assert.That(outcome.Value.Operation.OutcomeKind,
                        Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
                    Assert.That(outcome.Value.Operation.ResultRemaining, Is.EqualTo(200));
                }
                Assert.That(state.Eligibility.RecoveredIds.Single(), Is.EqualTo(pending.Incarnation));
                state.Clock.Advance(TimeSpan.FromMilliseconds(200));
                using (DurableExecutionReply<GetResultResponseDataType> expired = await host.GetAsync(pending.Token)
                    .ConfigureAwait(false))
                {
                    Assert.That(expired.Value.Operation.State, Is.EqualTo(ServiceOperationState.Expired));
                    Assert.That(expired.Value.Operation.HistoryRemaining, Is.EqualTo(200));
                }
                state.Clock.Advance(TimeSpan.FromMilliseconds(200));
                using DurableExecutionReply<GetResultResponseDataType> gone = await host.GetAsync(pending.Token)
                    .ConfigureAwait(false);
                AssertUnknown(gone.Value);
                gone.Dispose();
                await host.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, kOrphanAfterReleaseBytes)
                    .ConfigureAwait(false);
                await host.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 1).ConfigureAwait(false);
                await host.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 1).ConfigureAwait(false);
                Assert.That(host.Provider.GetStatistics().ActiveOperationCount, Is.EqualTo(1));
                Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
                Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
                Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes),
                    Is.EqualTo(kOrphanAfterReleaseBytes),
                    "Public release can free journal publication buffers, not the still-owned work/input/history.");
                Assert.That(state.Eligibility.ActiveFences, Is.EqualTo(1));
                Assert.That(state.Eligibility.RecoveryReleases, Is.Zero);
                RecoverableExecutionDurableRecord active = await state.Store.RecordAsync(pending.ClientId)
                    .ConfigureAwait(false);
                Assert.That(active.WorkFinished, Is.False);
                Assert.That(active.State, Is.EqualTo(ServiceOperationState.Expired));
                SubmitRequestDataType duplicate = host.Request(mode: ServiceOperationRecoveryMode.Owner);
                duplicate.ClientOperationId = pending.ClientId;
                await host.RejectAsync(RseBrowseNames.Submit, duplicate, StatusCodes.BadInvalidState)
                    .ConfigureAwait(false);
                DurableExecutionPeer replacement = await host.AddPeerAsync().ConfigureAwait(false);
                await host.RejectAsync(
                    RseBrowseNames.Submit, host.Request(), StatusCodes.BadResourceUnavailable, replacement)
                    .ConfigureAwait(false);
                AssertNoDispatch(host);
            }
            Assert.That((await state.Store.RecordAsync(pending.ClientId).ConfigureAwait(false)).WorkFinished, Is.False,
                "Disposing a local recovery fence is not persisted proof that the original invocation ended.");
            state.Eligibility.TerminationEstablished = true;
            await using DurableExecutionTestHost proved = await DurableExecutionTestHost.StartAsync(
                state, ownerExecutionLimit: 1).ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> stillGone = await proved.GetAsync(pending.Token)
                .ConfigureAwait(false);
            AssertUnknown(stillGone.Value);
            stillGone.Dispose();
            await proved.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
            await proved.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 0).ConfigureAwait(false);
            await proved.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
            Assert.That(state.Eligibility.RecoveredIds, Is.EqualTo([pending.Incarnation, pending.Incarnation]));
            Assert.That(state.Eligibility.ActiveFences, Is.Zero);
            Assert.That(proved.Provider.GetStatistics().ActiveOperationCount, Is.Zero);
            Assert.That(proved.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.Zero);
            Assert.That(proved.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.Zero);
            Assert.That(proved.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes), Is.Zero);
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.Empty);
            Assert.That(state.Store.Writes.Any(write => write.Record is
            { WorkFinished: true, State: ServiceOperationState.Expired }), Is.True);
            AssertNoDispatch(proved);
        }

        [Test]
        public async Task OutcomeKeepsWorkChargedUntilEligibilityDisposalCompletes()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using var disposal = new DurableExecutionGate();
            state.Eligibility.ReleaseGate = disposal;
            SubmitRequestDataType request = host.Request();
            using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(request)
                .ConfigureAwait(false);
            ByteString token = submitted.Value.Operation.ServerToken;
            submitted.Dispose();
            host.Server.Scenario.Release();
            await disposal.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            RecoverableExecutionDurableRecord before = await state.Store.RecordAsync(request.ClientOperationId)
                .ConfigureAwait(false);
            Assert.That(before.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomeAvailable));
            Assert.That(before.WorkFinished, Is.False);
            Assert.That(state.Eligibility.ReleaseCalls, Is.EqualTo(1));
            Assert.That(state.Eligibility.Releases, Is.Zero);
            Assert.That(host.Provider.GetStatistics().ActiveOperationCount, Is.EqualTo(1));
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            using (DurableExecutionReply<GetResultResponseDataType> available = await host.GetAsync(token)
                .ConfigureAwait(false))
            {
                Assert.That(AssertOriginal(available.Value.Result, host, "Call", 12), Is.EqualTo(before.Response));
                Assert.That(available.Value.Operation.ResultRemaining, Is.EqualTo(200));
            }
            Task stopping = host.Provider.DisposeAsync().AsTask();
            bool stoppedBeforeDisposal = stopping.IsCompleted;
            disposal.Release();
            await stopping.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            RecoverableExecutionDurableRecord after = await state.Store.RecordAsync(request.ClientOperationId)
                .ConfigureAwait(false);

            Assert.That(stoppedBeforeDisposal, Is.False);
            Assert.That(after.WorkFinished, Is.True);
            Assert.That(after.Response, Is.EqualTo(before.Response));
            Assert.That(after.OutcomeAt, Is.EqualTo(before.OutcomeAt));
            Assert.That(after.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(state.Eligibility.ReleaseCalls, Is.EqualTo(1));
            Assert.That(state.Eligibility.Releases, Is.EqualTo(1));
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.Zero);
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes), Is.Zero);
            Assert.That(state.Store.Writes.Count(IsOutcomeSelection), Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
        }

        [Test]
        public async Task FailedEligibilityDisposalPreservesOutcomeAndWithdrawsReadiness()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using var disposal = new DurableExecutionGate();
            state.Eligibility.ReleaseGate = disposal;
            state.Eligibility.FailRelease = true;
            SubmitRequestDataType request = host.Request();
            using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(request)
                .ConfigureAwait(false);
            ByteString token = submitted.Value.Operation.ServerToken;
            submitted.Dispose();
            host.Server.Scenario.Release();
            await disposal.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            RecoverableExecutionDurableRecord before = await state.Store.RecordAsync(request.ClientOperationId)
                .ConfigureAwait(false);
            using (DurableExecutionReply<GetResultResponseDataType> available = await host.GetAsync(token)
                .ConfigureAwait(false))
            {
                Assert.That(AssertOriginal(available.Value.Result, host, "Call", 12), Is.EqualTo(before.Response));
            }
            disposal.Release();
            await state.Logs.Unavailable.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            await host.RejectAsync(RseBrowseNames.GetResult, new GetResultRequestDataType
            {
                RecoveryMode = ServiceOperationRecoveryMode.Durable,
                ServerToken = token
            }, StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 1).ConfigureAwait(false);
            RecoverableExecutionDurableRecord after = await state.Store.RecordAsync(request.ClientOperationId)
                .ConfigureAwait(false);

            Assert.That(after.WorkFinished, Is.False);
            Assert.That(after.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomeAvailable));
            Assert.That(after.Response, Is.EqualTo(before.Response));
            Assert.That(after.OutcomeAt, Is.EqualTo(before.OutcomeAt));
            Assert.That(state.Eligibility.ReleaseCalls, Is.EqualTo(1));
            Assert.That(state.Eligibility.Releases, Is.Zero);
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(host.Provider.GetStatistics().ActiveOperationCount, Is.EqualTo(1));
            Assert.That(host.Provider.SupportedRecoveryModes, Is.EqualTo(
            [
                ServiceOperationRecoveryMode.Session, ServiceOperationRecoveryMode.Owner
            ]));
            Assert.That(state.Store.Writes.Count(IsOutcomeSelection), Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
        }

        [Test]
        public async Task CommittedOutcomeWithUnprovedTerminationRestoresOldRetentionAndFence()
        {
            (DurableExecutionTestState recoveredState, RecoverableExecutionDurableRecord committed) =
                await InterruptedImageAsync("OutcomeAvailable").ConfigureAwait(false);
            await using DurableExecutionTestState state = recoveredState;
            Assert.That(committed.WorkFinished, Is.False);
            Assert.That(committed.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            state.Eligibility.TerminationEstablished = false;
            state.Clock.Advance(TimeSpan.FromMilliseconds(50));
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> recovered = await host.GetAsync(committed.Token)
                .ConfigureAwait(false);

            Assert.That(AssertOriginal(recovered.Value.Result, host, "Call", 12), Is.EqualTo(committed.Response));
            Assert.That(recovered.Value.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(recovered.Value.Operation.ResultRemaining, Is.EqualTo(150));
            Assert.That((await state.Store.RecordAsync(committed.ClientId).ConfigureAwait(false)).OutcomeAt,
                Is.EqualTo(committed.OutcomeAt));
            Assert.That(state.Store.Writes.Count(IsOutcomeSelection), Is.Zero);
            Assert.That(state.Eligibility.RecoveredIds, Is.EqualTo([committed.Incarnation]));
            Assert.That(state.Eligibility.ActiveFences, Is.EqualTo(1));
            recovered.Dispose();
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 1).ConfigureAwait(false);
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(host.Provider.GetStatistics().ActiveOperationCount, Is.EqualTo(1));
            AssertNoDispatch(host);
        }

        [Test]
        public async Task CollectorRacingAcknowledgePinsJournalQuotaUntilActualUseEnds()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            SubmitRequestDataType request = host.Request();
            using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(request)
                .ConfigureAwait(false);
            ByteString token = submitted.Value.Operation.ServerToken;
            submitted.Dispose();
            await host.FinishAsync().ConfigureAwait(false);
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(kCompletedBytes));
            using var collection = new DurableExecutionGate();
            state.Policy.Gated = AsyncServiceAccess.Collect;
            state.Policy.Gate = collection;
            Task<DurableExecutionReply<GetResultResponseDataType>> collecting = host.GetAsync(token);
            await collection.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            long withCollector = host.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes);
            using (DurableExecutionReply<ControlResponseDataType> ack = await host.ControlAsync(
                RseBrowseNames.Acknowledge, token).ConfigureAwait(false))
            {
                Assert.That(ack.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                Assert.That(ack.Value.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
            }
            Assert.That(collecting.IsCompleted, Is.False);
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes),
                Is.EqualTo(withCollector - (4L * DurableExecutionTestHost.MessageLimit)),
        "ACK can release input and outcome ownership, but the active collector still pins journal publication quota.");
            RecoverableExecutionDurableRecord released = await state.Store.RecordAsync(request.ClientOperationId)
                .ConfigureAwait(false);
            Assert.That(released.State, Is.EqualTo(ServiceOperationState.Acknowledged));
            Assert.That(released.Response.IsNull, Is.True);
            collection.Release();
            using DurableExecutionReply<GetResultResponseDataType> reply = await collecting.ConfigureAwait(false);
            Assert.That(reply.Value.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
            Assert.That(reply.Value.Result.IsNull, Is.True);
            reply.Dispose();
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, kHistoryBytes).ConfigureAwait(false);
            await host.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 1).ConfigureAwait(false);
            Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
            Assert.That(host.Provider.GetStatistics().RetainedOutcomeBytes, Is.Zero);
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
        }

        [Test]
        public async Task HeldCollectedResponsePinsOutcomeBytesUntilDisposed()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(host.Request())
                .ConfigureAwait(false);
            ByteString token = submitted.Value.Operation.ServerToken;
            submitted.Dispose();
            await host.FinishAsync().ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> held = await host.GetAsync(token)
                .ConfigureAwait(false);
            ByteString original = AssertOriginal(held.Value.Result, host, "Call", 12);
            using (DurableExecutionReply<ControlResponseDataType> ack = await host.ControlAsync(
                RseBrowseNames.Acknowledge, token).ConfigureAwait(false))
            {
                Assert.That(ack.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                Assert.That(ack.Value.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
            }

            Assert.That(AssertOriginal(held.Value.Result, host, "Call", 12), Is.EqualTo(original));
            Assert.That(host.Provider.GetStatistics().RetainedOutcomeCount, Is.Zero);
            Assert.That(host.Provider.GetStatistics().RetainedOutcomeBytes,
                Is.EqualTo((ulong)DurableExecutionTestHost.MessageLimit));
            Assert.That(host.Provider.GetStatistics().TerminalRecordCount, Is.EqualTo(1));
            held.Dispose();
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, kHistoryBytes).ConfigureAwait(false);
            Assert.That(host.Provider.GetStatistics().RetainedOutcomeBytes, Is.Zero);
            using DurableExecutionReply<GetResultResponseDataType> released = await host.GetAsync(token)
                .ConfigureAwait(false);
            Assert.That(released.Value.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
            Assert.That(released.Value.Result.IsNull, Is.True);
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
        }

        [TestCaseSource(nameof(InterpretationCases))]
        public async Task ChangedInterpretationCannotCollectOrControlStoredOutcome(string change, string firstMethod)
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            SubmitRequestDataType request = host.Request();
            using DurableExecutionReply<SubmitResponseDataType> admitted = await host.SubmitAsync(request)
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await host.FinishAsync().ConfigureAwait(false);
            IReadOnlyList<KeyValuePair<string, ByteString>> before = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            ChangeInterpretation(host.Server, state, change);
            IEncodeable argument = firstMethod == RseBrowseNames.GetResult
                ? new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Durable,
                    ServerToken = token
                }
                : new OperationControlRequestDataType { ServerToken = token };
            await host.RejectAsync(firstMethod, argument, StatusCodes.BadResourceUnavailable).ConfigureAwait(false);

            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(host.Provider.SupportedRecoveryModes, Is.EqualTo(
            [
                ServiceOperationRecoveryMode.Session, ServiceOperationRecoveryMode.Owner
            ]));
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
            Assert.That(state.Store.Writes.Count(write => write.Record?.IsReleased == true), Is.Zero);
        }

        [TestCase("available", false)]
        [TestCase("indeterminate", false)]
        [TestCase("indeterminate", true)]
        [TestCase("deadline", false)]
        [TestCase("acknowledged", false)]
        [TestCase("abandoned", false)]
        [TestCase("expired", false)]
        public async Task RestoringCommittedTransitionsDoesNotReplayLifetimeCounters(string transition, bool active)
        {
            (DurableExecutionTestState savedState, RecoverableExecutionDurableRecord original) =
                await CommittedCounterStateAsync(transition, active).ConfigureAwait(false);
            await using DurableExecutionTestState state = savedState;
            IReadOnlyList<KeyValuePair<string, ByteString>> before = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            state.Clock.Advance(TimeSpan.FromMilliseconds(50));
            using var audits = new DurableExecutionAuditCapture();
            state.BeforeProviderCreate = audits.Attach;
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state, auditing: true)
                .ConfigureAwait(false);
            AsyncServiceExecutionStatistics restored = host.Provider.GetStatistics();
            using (DurableExecutionReply<GetResultResponseDataType> result = await host.GetAsync(original.Token)
                .ConfigureAwait(false))
            {
                Assert.That(result.Value.Operation.State, Is.EqualTo(original.State));
                Assert.That(result.Value.Operation.OutcomeKind, Is.EqualTo(original.Kind));
                Assert.That(result.Value.Operation.OutcomeStatus, Is.EqualTo(original.Status));
                Assert.That(result.Value.Operation.ResultRemaining, Is.EqualTo(original.IsReleased ? 0 : 150));
                Assert.That(result.Value.Operation.HistoryRemaining, Is.EqualTo(original.IsReleased ? 150 : 0));
                if (transition == "available")
                {
                    Assert.That(AssertOriginal(result.Value.Result, host, "Call", 12), Is.EqualTo(original.Response));
                }
                else
                {
                    Assert.That(result.Value.Result.IsNull, Is.True);
                }
            }
            // A real denied manager Call proves this is a live capture, not a vacuously empty event queue.
            state.Policy.Denied = AsyncServiceAccess.Collect;
            try
            {
                await host.RejectAsync(RseBrowseNames.GetResult, new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Durable,
                    ServerToken = original.Token
                }, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            }
            finally
            {
                state.Policy.Denied = null;
            }
            RecoverableExecutionDurableRecord unchanged = await state.Store.RecordAsync(original.ClientId)
                .ConfigureAwait(false);
            IReadOnlyList<KeyValuePair<string, ByteString>> after = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            ServiceOperationAuditAction[] actions = [.. audits.Events.Select(value => value.Action!.Value)];
            ServiceOperationAuditAction[] replayed =
            [
                .. actions.Where(action =>
                    action is not (ServiceOperationAuditAction.Collected or ServiceOperationAuditAction.Denied))
            ];
            using (Assert.EnterMultipleScope())
            {
                AssertLifetimeCounters(restored);
                AssertLifetimeCounters(host.Provider.GetStatistics());
                AssertRecoveryGauges(restored, active ? 1u : 0u, original.IsReleased ? 0u : 1u,
                    original.IsReleased ? 1u : 0u,
                    original.IsReleased ? 0UL : transition == "available"
                        ? DurableExecutionTestHost.MessageLimit + kRetainedMetadataBytes
                        : kRetainedMetadataBytes);
                Assert.That(actions, Does.Contain(ServiceOperationAuditAction.Denied));
                Assert.That(replayed, Is.Empty, "Startup must not report historical transitions as new events.");
                Assert.That(after, Is.EqualTo(before), "Observation must not rewrite the committed record.");
                Assert.That(unchanged.AdmittedAt, Is.EqualTo(original.AdmittedAt));
                Assert.That(unchanged.OutcomeAt, Is.EqualTo(original.OutcomeAt));
                Assert.That(unchanged.ReleasedAt, Is.EqualTo(original.ReleasedAt));
                Assert.That(unchanged.Response, Is.EqualTo(original.Response));
                AssertNoDispatch(host);
            }
        }

        [Test]
        public async Task RecoveringPendingCountsOneNewIndeterminateCommitForThisProvider()
        {
            await using DurableExecutionTestState state = await PendingImageAsync(1).ConfigureAwait(false);
            IReadOnlyList<KeyValuePair<string, ByteString>> before = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            RecoverableExecutionDurableRecord pending = state.Store.Decode(before[0].Key, before[0].Value)!;
            using var audits = new DurableExecutionAuditCapture();
            state.BeforeProviderCreate = audits.Attach;
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state, auditing: true)
                .ConfigureAwait(false);
            RecoverableExecutionDurableRecord committed = await state.Store.RecordAsync(pending.ClientId)
                .ConfigureAwait(false);
            Assert.That(committed.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(committed.OutcomeAt, Is.EqualTo(state.Clock.Now));
            Assert.That(committed.AdmittedAt, Is.EqualTo(pending.AdmittedAt));
            AssertLifetimeCounters(host.Provider.GetStatistics(), indeterminate: 1);
            AssertRecoveryGauges(host.Provider.GetStatistics(), 0, 1, 0, kRetainedMetadataBytes);
            for (int observation = 0; observation < 2; observation++)
            {
                using DurableExecutionReply<GetResultResponseDataType> result = await host.GetAsync(pending.Token)
                    .ConfigureAwait(false);
                Assert.That(result.Value.Operation.OutcomeKind,
                    Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
                Assert.That(result.Value.Operation.ResultRemaining, Is.EqualTo(200));
                Assert.That(result.Value.Result.IsNull, Is.True);
                AssertLifetimeCounters(host.Provider.GetStatistics(), indeterminate: 1);
            }
            using (DurableExecutionReply<ControlResponseDataType> ack = await host.ControlAsync(
                RseBrowseNames.Acknowledge, pending.Token).ConfigureAwait(false))
            {
                Assert.That(ack.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                Assert.That(ack.Value.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
            }
            AssertLifetimeCounters(host.Provider.GetStatistics(), indeterminate: 1, acknowledged: 1);
            AssertRecoveryGauges(host.Provider.GetStatistics(), 0, 0, 1, 0);
            state.Clock.Advance(TimeSpan.FromMilliseconds(200));
            using (DurableExecutionReply<GetResultResponseDataType> forgotten = await host.GetAsync(pending.Token)
                .ConfigureAwait(false))
            {
                AssertUnknown(forgotten.Value);
            }
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.Empty);
            AssertLifetimeCounters(host.Provider.GetStatistics(), indeterminate: 1, acknowledged: 1);
            AssertRecoveryGauges(host.Provider.GetStatistics(), 0, 0, 0, 0);
            Assert.That(audits.Events.Where(value =>
                value.Action!.Value == ServiceOperationAuditAction.OutcomeCommitted).ToArray(), Has.Length.EqualTo(1));
            Assert.That(audits.Events.Where(value =>
                value.Action!.Value == ServiceOperationAuditAction.Acknowledged).ToArray(), Has.Length.EqualTo(1));
            AssertNoDispatch(host);
        }

        [Test]
        public async Task DowntimeExpiryCountsOneNewTransitionWithoutRenewingRetention()
        {
            (DurableExecutionTestState savedState, RecoverableExecutionDurableRecord original) =
                await CommittedCounterStateAsync("available", active: false).ConfigureAwait(false);
            await using DurableExecutionTestState state = savedState;
            state.Clock.Advance(TimeSpan.FromMilliseconds(250));
            using var audits = new DurableExecutionAuditCapture();
            state.BeforeProviderCreate = audits.Attach;
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state, auditing: true)
                .ConfigureAwait(false);
            AsyncServiceExecutionStatistics restored = host.Provider.GetStatistics();
            ServiceOperationAuditAction[] startupActions = [.. audits.Events.Select(value => value.Action!.Value)];
            RecoverableExecutionDurableRecord expired = await state.Store.RecordAsync(original.ClientId)
                .ConfigureAwait(false);
            using (DurableExecutionReply<GetResultResponseDataType> result = await host.GetAsync(original.Token)
                .ConfigureAwait(false))
            {
                Assert.That(result.Value.Operation.State, Is.EqualTo(ServiceOperationState.Expired));
                Assert.That(result.Value.Operation.HistoryRemaining, Is.EqualTo(150));
                Assert.That(result.Value.Result.IsNull, Is.True);
            }
            // Let all reachable observations run before reporting the startup event/counter mismatch.
            state.Clock.Advance(TimeSpan.FromMilliseconds(150));
            using (DurableExecutionReply<GetResultResponseDataType> forgotten = await host.GetAsync(original.Token)
                .ConfigureAwait(false))
            {
                AssertUnknown(forgotten.Value);
            }
            await host.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                AssertLifetimeCounters(restored, expired: 1);
                AssertRecoveryGauges(restored, 0, 0, 1, 0);
                Assert.That(startupActions,
                    Is.EqualTo([ServiceOperationAuditAction.Expired]),
                    "Actual downtime expiry is new; the old outcome commit is not.");
                Assert.That(expired.AdmittedAt, Is.EqualTo(original.AdmittedAt));
                Assert.That(expired.OutcomeAt, Is.EqualTo(original.OutcomeAt));
                Assert.That(expired.ReleasedAt, Is.EqualTo(new RecoverableExecutionTime(
                    original.OutcomeAt.DomainId, original.OutcomeAt.Ticks + original.Retention.Ticks)));
                Assert.That(expired.Retention, Is.EqualTo(original.Retention));
                Assert.That(expired.Response.IsNull, Is.True);
                AssertLifetimeCounters(host.Provider.GetStatistics(), expired: 1);
                AssertRecoveryGauges(host.Provider.GetStatistics(), 0, 0, 0, 0);
                Assert.That(audits.Events.Where(value =>
                    value.Action!.Value == ServiceOperationAuditAction.Expired).ToArray(), Has.Length.EqualTo(1));
                AssertNoDispatch(host);
            }
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.Empty);
        }

        [TestCase("namespace")]
        [TestCase("registration")]
        [TestCase("codec-revision")]
        [TestCase("codec-revocation")]
        public async Task ChangedStartupWitnessRejectsBeforeRestorationOrDispatch(string change)
        {
            await using var state = new DurableExecutionTestState();
            await using (DurableExecutionTestHost first = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false))
            {
                using DurableExecutionReply<SubmitResponseDataType> submitted = await first.SubmitAsync(first.Request())
                    .ConfigureAwait(false);
                submitted.Dispose();
                await first.FinishAsync().ConfigureAwait(false);
            }
            IReadOnlyList<KeyValuePair<string, ByteString>> before = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            state.BeforeProviderCreate = server => ChangeInterpretation(server, state, change);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await using DurableExecutionTestHost unexpected = await DurableExecutionTestHost.StartAsync(state)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(state.Eligibility.RecoveredIds, Is.Empty);
            Assert.That(state.LastServer!.Dispatches, Is.Zero);
            Assert.That(state.LastServer.Provider!.SupportedRecoveryModes.Span.ToArray(),
                Does.Not.Contain(ServiceOperationRecoveryMode.Durable));
        }

        [Test]
        public async Task RevokedPersistedOwnerPreventsDurableStartupWithoutChangingStoredOutcome()
        {
            await using var state = new DurableExecutionTestState();
            await using (DurableExecutionTestHost first = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false))
            {
                using DurableExecutionReply<SubmitResponseDataType> submitted = await first.SubmitAsync(first.Request())
                    .ConfigureAwait(false);
                submitted.Dispose();
                await first.FinishAsync().ConfigureAwait(false);
            }
            IReadOnlyList<KeyValuePair<string, ByteString>> before = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            state.Owners.Failure = StatusCodes.BadUserAccessDenied;
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await using DurableExecutionTestHost unexpected = await DurableExecutionTestHost.StartAsync(state)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(state.Eligibility.RecoveredIds, Is.Empty);
            Assert.That(state.LastServer!.Dispatches, Is.Zero);
            Assert.That(state.LastServer.Provider!.SupportedRecoveryModes.Span.ToArray(),
                Does.Not.Contain(ServiceOperationRecoveryMode.Durable));
        }

        [TestCase("permissions")]
        [TestCase("roles")]
        [TestCase("policy")]
        public async Task CurrentPermissionsProtectRecreatedOutcomeAndAcknowledge(string revocation)
        {
            await using var state = new DurableExecutionTestState();
            RecoverableExecutionDurableRecord original;
            await using (DurableExecutionTestHost first = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false))
            {
                SubmitRequestDataType request = first.Request();
                using DurableExecutionReply<SubmitResponseDataType> submitted = await first.SubmitAsync(request)
                    .ConfigureAwait(false);
                submitted.Dispose();
                await first.FinishAsync().ConfigureAwait(false);
                original = await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false);
            }
            await using DurableExecutionTestHost next = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            ArrayOf<RolePermissionType> previousPermissions = next.Server.Scenario.DeclaredMethod!.RolePermissions;
            ArrayOf<RolePermissionType> previousAckPermissions = next.Manager.Acknowledge!.RolePermissions;
            IUserIdentity previousIdentity = next.First.Session.EffectiveIdentity;
            if (revocation == "permissions")
            {
                next.Server.Scenario.DeclaredMethod.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                        Permissions = (uint)(PermissionType.Call | PermissionType.Browse)
                    }
                ];
            }
            else if (revocation == "roles")
            {
                next.First.Session.RefreshEffectiveIdentity(new SyntheticOwnerIdentity
                {
                    TokenHandler = next.First.Session.Identity.TokenHandler,
                    EffectiveRoles = [ObjectIds.WellKnownRole_AuthenticatedUser]
                });
            }
            else
            {
                state.Policy.Denied = AsyncServiceAccess.Collect;
            }
            IReadOnlyList<KeyValuePair<string, ByteString>> before = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            await next.RejectAsync(RseBrowseNames.GetResult, new GetResultRequestDataType
            {
                RecoveryMode = ServiceOperationRecoveryMode.Durable,
                ServerToken = original.Token
            }, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            if (revocation == "policy")
            {
                state.Policy.Denied = AsyncServiceAccess.Acknowledge;
            }
            else if (revocation == "permissions")
            {
                // Collection requires current original-node permissions; the separate control action
                // requires the current manager Method's permissions, not a cached admission permission.
                next.Manager.Acknowledge!.RolePermissions = next.Server.Scenario.DeclaredMethod!.RolePermissions;
            }
            await next.RejectAsync(RseBrowseNames.Acknowledge,
                new OperationControlRequestDataType { ServerToken = original.Token }, StatusCodes.BadUserAccessDenied)
                .ConfigureAwait(false);
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.EqualTo(before));
            next.Server.Scenario.DeclaredMethod.RolePermissions = previousPermissions;
            next.Manager.Acknowledge!.RolePermissions = previousAckPermissions;
            next.First.Session.RefreshEffectiveIdentity(previousIdentity);
            state.Policy.Denied = null;
            using DurableExecutionReply<GetResultResponseDataType> allowed = await next.GetAsync(original.Token)
                .ConfigureAwait(false);
            Assert.That(AssertOriginal(allowed.Value.Result, next, "Call", 12), Is.EqualTo(original.Response));
            Assert.That(allowed.Value.Operation.ResultRemaining, Is.EqualTo(200));
            AssertNoDispatch(next);
        }

        [Test]
        public async Task CurrentOwnerRevocationCannotReleaseStoredOutcome()
        {
            await using var state = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<SubmitResponseDataType> admitted = await host.SubmitAsync(host.Request())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await host.FinishAsync().ConfigureAwait(false);
            IReadOnlyList<KeyValuePair<string, ByteString>> before = await state.Store.ImageAsync()
                .ConfigureAwait(false);
            DurableExecutionPeer other = await host.AddPeerAsync(new RecoverableExecutionOwnerId(
                DurableExecutionOwners.Owner.AuthorityId, DurableExecutionOwners.Owner.ApplicationId,
                new Uuid("73000000-0000-0000-0000-000000000002"))).ConfigureAwait(false);
            using (DurableExecutionReply<GetResultResponseDataType> hidden = await host.GetAsync(token, peer: other)
                .ConfigureAwait(false))
            {
                AssertUnknown(hidden.Value);
            }
            foreach (string method in s_controls)
            {
                using DurableExecutionReply<ControlResponseDataType> denied =
                    await host.ControlAsync(method, token, other)
                    .ConfigureAwait(false);
                Assert.That(denied.Value.Result, Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
                Assert.That(denied.Value.Operation.State, Is.EqualTo(ServiceOperationState.Unknown));
                Assert.That(denied.Value.Operation.ServerToken.IsNull, Is.True);
            }
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.EqualTo(before));
            state.Owners.Failure = StatusCodes.BadUserAccessDenied;
            await host.RejectAsync(RseBrowseNames.GetResult, new GetResultRequestDataType
            {
                RecoveryMode = ServiceOperationRecoveryMode.Durable,
                ServerToken = token
            }, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
        }

        [Test]
        public async Task ShutdownWaitsForOwnedCallbackAndPreservesItsCommittedOutcome()
        {
            await using var state = new DurableExecutionTestState();
            RecoverableExecutionDurableRecord saved;
            await using (DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false))
            {
                SubmitRequestDataType request = host.Request();
                using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(request)
                    .ConfigureAwait(false);
                submitted.Dispose();
                await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
                Task stopping = host.Provider.DisposeAsync().AsTask();
                await CancellationObservedAsync(host.Server.OwnedContext!.CancellationToken).ConfigureAwait(false);
                Assert.That(stopping.IsCompleted, Is.False, "The non-cooperative ordinary callback still owns work.");
                Assert.That(host.Server.Scenario.Counter, Is.EqualTo(7));
                Assert.That(state.Eligibility.Releases, Is.Zero);
                host.Server.Scenario.Release();
                await stopping.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
                saved = await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false);
                Assert.That(saved.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomeAvailable));
                Assert.That(saved.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
                Assert.That(saved.Status, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                Assert.That(saved.WorkFinished, Is.True);
                Assert.That(saved.CancellationRequested, Is.True);
                Assert.That(state.Eligibility.Releases, Is.EqualTo(1));
                Assert.That(host.Server.Dispatches, Is.EqualTo(1));
                Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
                Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
                Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.Zero);
                Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.Zero);
                Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes), Is.Zero);
            }
            await using DurableExecutionTestHost next = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> result = await next.GetAsync(saved.Token)
                .ConfigureAwait(false);
            Assert.That(AssertCancelledOriginal(result.Value.Result, next), Is.EqualTo(saved.Response));
            Assert.That(result.Value.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(result.Value.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(result.Value.Operation.CancellationRequested, Is.True);
            Assert.That(result.Value.Operation.ResultRemaining, Is.EqualTo(200));
            AssertNoDispatch(next);
        }

        [Test]
        public async Task ShutdownDrainsActiveCollectionWithoutDestroyingDurableResult()
        {
            await using var state = new DurableExecutionTestState();
            RecoverableExecutionDurableRecord saved;
            await using (DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false))
            {
                SubmitRequestDataType request = host.Request();
                using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(request)
                    .ConfigureAwait(false);
                ByteString token = submitted.Value.Operation.ServerToken;
                submitted.Dispose();
                await host.FinishAsync().ConfigureAwait(false);
                saved = await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false);
                using var collection = new DurableExecutionGate();
                state.Policy.Gate = collection;
                state.Policy.Gated = AsyncServiceAccess.Collect;
                Task<IServiceResponse> collecting = host.SendAsync(RseBrowseNames.GetResult,
                    new GetResultRequestDataType
                    {
                        RecoveryMode = ServiceOperationRecoveryMode.Durable,
                        ServerToken = token
                    });
                await collection.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
                Task stopping = host.Provider.DisposeAsync().AsTask();
                bool stoppedEarly = stopping.IsCompleted;
                collection.Release();
                IServiceResponse reply = await collecting.ConfigureAwait(false);
                try
                {
                    DurableExecutionTestHost.AssertFailure(reply, StatusCodes.BadServerHalted);
                }
                finally
                {
                    (reply as IPooledEncodeable)?.Reuse();
                }
                await stopping.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
                await host.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
                await host.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
                Assert.That(stoppedEarly, Is.False);
                RecoverableExecutionDurableRecord after = await state.Store.RecordAsync(request.ClientOperationId)
                    .ConfigureAwait(false);
                Assert.That(after.Response, Is.EqualTo(saved.Response));
                Assert.That(after.OutcomeAt, Is.EqualTo(saved.OutcomeAt));
                Assert.That(after.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomeAvailable));
                Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.Zero);
                Assert.That(host.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes), Is.Zero);
                state.Policy.Gated = null;
                state.Policy.Gate = null;
            }
            await using DurableExecutionTestHost next = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> result = await next.GetAsync(saved.Token)
                .ConfigureAwait(false);
            Assert.That(AssertOriginal(result.Value.Result, next, "Call", 12), Is.EqualTo(saved.Response));
            AssertNoDispatch(next);
        }

        [TestCase(null)]
        [TestCase(RseBrowseNames.Acknowledge)]
        [TestCase(RseBrowseNames.Abandon)]
        public async Task ShutdownPreservesRetainedAndReleasedDurableRecords(string? release)
        {
            await using var state = new DurableExecutionTestState();
            RecoverableExecutionDurableRecord saved;
            IReadOnlyList<KeyValuePair<string, ByteString>> before;
            await using (DurableExecutionTestHost first = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false))
            {
                SubmitRequestDataType request = first.Request();
                using DurableExecutionReply<SubmitResponseDataType> submitted = await first.SubmitAsync(request)
                    .ConfigureAwait(false);
                ByteString token = submitted.Value.Operation.ServerToken;
                submitted.Dispose();
                await first.FinishAsync().ConfigureAwait(false);
                if (release != null)
                {
                    using DurableExecutionReply<ControlResponseDataType> control =
                        await first.ControlAsync(release, token)
                        .ConfigureAwait(false);
                    Assert.That(control.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                }
                saved = await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false);
                before = await state.Store.ImageAsync().ConfigureAwait(false);
            }
            Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.EqualTo(before),
                "Shutdown is not a durable Abandon, release, or new retention origin.");
            state.Clock.Advance(TimeSpan.FromMilliseconds(40));
            await using DurableExecutionTestHost next = await DurableExecutionTestHost.StartAsync(state)
                .ConfigureAwait(false);
            using DurableExecutionReply<GetResultResponseDataType> result = await next.GetAsync(saved.Token)
                .ConfigureAwait(false);
            Assert.That(result.Value.Operation.State, Is.EqualTo(saved.State));
            Assert.That(result.Value.Operation.ResultRemaining, Is.EqualTo(release == null ? 160 : 0));
            Assert.That(result.Value.Operation.HistoryRemaining, Is.EqualTo(release == null ? 0 : 160));
            if (release == null)
            {
                Assert.That(AssertOriginal(result.Value.Result, next, "Call", 12), Is.EqualTo(saved.Response));
            }
            else
            {
                Assert.That(result.Value.Result.IsNull, Is.True);
            }
            AssertNoDispatch(next);
        }

        [Test]
        public async Task FileStoreAndProviderRecreationPreserveOriginalResponse()
        {
            string directory = Path.Combine(Path.GetTempPath(), "rse-durable-provider-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using var state = new DurableExecutionTestState(directory);
                RecoverableExecutionDurableRecord saved;
                await using (DurableExecutionTestHost first = await DurableExecutionTestHost.StartAsync(state)
                    .ConfigureAwait(false))
                {
                    SubmitRequestDataType request = first.Request();
                    using DurableExecutionReply<SubmitResponseDataType> submitted = await first.SubmitAsync(request)
                        .ConfigureAwait(false);
                    submitted.Dispose();
                    await first.FinishAsync().ConfigureAwait(false);
                    saved = await state.Store.RecordAsync(request.ClientOperationId).ConfigureAwait(false);
                    Assert.That(first.Server.Scenario.Counter, Is.EqualTo(12));
                }
                // Close the actual store and its directory lease, then instantiate a new backend.
                await state.ReopenFileAsync().ConfigureAwait(false);
                state.Clock.Advance(TimeSpan.FromMilliseconds(50));
                await using DurableExecutionTestHost next = await DurableExecutionTestHost.StartAsync(state)
                    .ConfigureAwait(false);
                using DurableExecutionReply<GetResultResponseDataType> result = await next.GetAsync(saved.Token)
                    .ConfigureAwait(false);
                Assert.That(AssertOriginal(result.Value.Result, next, "Call", 12), Is.EqualTo(saved.Response));
                Assert.That(result.Value.Operation.ResultRemaining, Is.EqualTo(150));
                Assert.That((await state.Store.RecordAsync(saved.ClientId).ConfigureAwait(false)).OutcomeAt,
                    Is.EqualTo(saved.OutcomeAt));
                AssertNoDispatch(next);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FullFileStoreRejectsAdmissionWithoutPoisoningExistingCompletionAndRelease(bool expire)
        {
            string directory = Path.Combine(Path.GetTempPath(), "rse-durable-capacity-" + Guid.NewGuid().ToString("N"));
            // One fully reserved future outcome fits, as does its small released history plus a replacement.
            // Two unreleased slots cannot fit. The backend must report that rejection before publication.
            const int capacity = DurableExecutionTestState.ProtectedSlotBytes +
                DurableExecutionTestState.HistorySlotBudget +
                DurableExecutionTestState.SnapshotOverheadBudget;
            Assert.That(capacity, Is.LessThan(2 * DurableExecutionTestState.ProtectedSlotBytes));
            try
            {
                await using var state = new DurableExecutionTestState(directory, maximumSnapshotBytes: capacity);
                await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                    .ConfigureAwait(false);
                SubmitRequestDataType original = host.Request();
                using DurableExecutionReply<SubmitResponseDataType> admitted = await host.SubmitAsync(original)
                    .ConfigureAwait(false);
                ByteString token = admitted.Value.Operation.ServerToken;
                admitted.Dispose();
                await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
                IReadOnlyList<KeyValuePair<string, ByteString>> full = await state.Store.ImageAsync()
                    .ConfigureAwait(false);
                Assert.That(full, Has.Count.EqualTo(1));
                Assert.That(full[0].Value.Length,
                    Is.EqualTo((4 * DurableExecutionTestHost.MessageLimit) + 8192 + 4096 + 8));
                SubmitRequestDataType rejected = host.Request();
                await host.RejectAsync(RseBrowseNames.Submit, rejected, StatusCodes.BadResourceUnavailable)
                    .ConfigureAwait(false);

                Assert.That(state.Store.CapacityRejections, Is.EqualTo(1),
                "The actual file capacity result, not an isolation failure or thrown IO error, must reject admission.");
                Assert.That(state.Store.Failures, Is.Zero);
                Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Is.EqualTo(full));
                Assert.That(host.Provider.SupportedRecoveryModes, Is.EqualTo(DurableExecutionTestHost.AllModes));
                Assert.That(host.Provider.GetStatistics().AdmittedCount, Is.EqualTo(1));
                Assert.That(host.Server.Dispatches, Is.EqualTo(1));
                Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(1));
                Assert.That(host.Server.Scenario.Counter, Is.EqualTo(7));
                using (DurableExecutionReply<GetResultResponseDataType> pending = await host.GetAsync(token)
                    .ConfigureAwait(false))
                {
                    Assert.That(pending.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
                    Assert.That(pending.Value.Operation.ExecutionRemaining, Is.EqualTo(10_000));
                }
                await host.FinishAsync().ConfigureAwait(false);
                using (DurableExecutionReply<GetResultResponseDataType> result = await host.GetAsync(token)
                    .ConfigureAwait(false))
                {
                    AssertOriginal(result.Value.Result, host, "Call", 12);
                    Assert.That(result.Value.Operation.ResultRemaining, Is.EqualTo(200));
                }
                IReadOnlyList<KeyValuePair<string, ByteString>> completed = await state.Store.ImageAsync()
                    .ConfigureAwait(false);
                Assert.That(completed.Single().Value.Length, Is.EqualTo(full[0].Value.Length),
                    "Completing the full original response must not grow the admission's reserved storage slot.");
                if (expire)
                {
                    state.Clock.Advance(TimeSpan.FromMilliseconds(200));
                    using DurableExecutionReply<GetResultResponseDataType> expired = await host.GetAsync(token)
                        .ConfigureAwait(false);
                    Assert.That(expired.Value.Operation.State, Is.EqualTo(ServiceOperationState.Expired));
                    Assert.That(expired.Value.Operation.HistoryRemaining, Is.EqualTo(200));
                    Assert.That(expired.Value.Result.IsNull, Is.True);
                }
                else
                {
                    using DurableExecutionReply<ControlResponseDataType> ack = await host.ControlAsync(
                        RseBrowseNames.Acknowledge, token).ConfigureAwait(false);
                    Assert.That(ack.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                    Assert.That(ack.Value.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
                    Assert.That(ack.Value.Operation.HistoryRemaining, Is.EqualTo(200));
                }
                IReadOnlyList<KeyValuePair<string, ByteString>> history = await state.Store.ImageAsync()
                    .ConfigureAwait(false);
                Assert.That(history, Has.Count.EqualTo(1));
                Assert.That(history[0].Value.Length, Is.LessThanOrEqualTo(DurableExecutionTestState.HistorySlotBudget));
                Assert.That(history[0].Value.Length, Is.LessThan(full[0].Value.Length));
                RecoverableExecutionDurableRecord released = await state.Store.RecordAsync(original.ClientOperationId)
                    .ConfigureAwait(false);
                Assert.That(released.Response.IsNull, Is.True);
                Assert.That(released.State, Is.EqualTo(expire
                    ? ServiceOperationState.Expired : ServiceOperationState.Acknowledged));

                // Positive non-admission evidence permits this ClientID to be tried after capacity returns.
                using DurableExecutionReply<SubmitResponseDataType> replacement = await host.SubmitAsync(rejected)
                    .ConfigureAwait(false);
                ByteString replacementToken = replacement.Value.Operation.ServerToken;
                Assert.That(replacement.Value.Operation.ClientOperationId, Is.EqualTo(rejected.ClientOperationId));
                Assert.That(replacementToken, Is.Not.EqualTo(token));
                replacement.Dispose();
                await host.FinishAsync().ConfigureAwait(false);
                using DurableExecutionReply<GetResultResponseDataType> replacementResult =
                    await host.GetAsync(replacementToken)
                    .ConfigureAwait(false);
                AssertOriginal(replacementResult.Value.Result, host, "Call", 17);
                Assert.That(host.Server.Dispatches, Is.EqualTo(2));
                Assert.That(host.Server.Scenario.Invocations, Is.EqualTo(2));
                Assert.That(host.Server.Scenario.Counter, Is.EqualTo(17));
                Assert.That(host.Provider.GetStatistics().AdmittedCount, Is.EqualTo(2));
                Assert.That(state.Store.CapacityRejections, Is.EqualTo(1));
                Assert.That(state.Store.Failures, Is.Zero);
                Assert.That(host.Provider.SupportedRecoveryModes, Is.EqualTo(DurableExecutionTestHost.AllModes));
                Assert.That(await state.Store.ImageAsync().ConfigureAwait(false), Has.Count.EqualTo(2));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static async Task<(DurableExecutionTestState State, RecoverableExecutionDurableRecord Record)>
            CommittedCounterStateAsync(string transition, bool active)
        {
            DurableExecutionTestState state = transition == "indeterminate"
                ? await PendingImageAsync(1).ConfigureAwait(false) : new DurableExecutionTestState();
            try
            {
                state.Eligibility.TerminationEstablished = !active;
                Uuid clientId;
                await using (DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(state)
                    .ConfigureAwait(false))
                {
                    if (transition == "indeterminate")
                    {
                        IReadOnlyList<KeyValuePair<string, ByteString>> image = await state.Store.ImageAsync()
                            .ConfigureAwait(false);
                        RecoverableExecutionDurableRecord record = state.Store.Decode(image[0].Key, image[0].Value)!;
                        clientId = record.ClientId;
                        Assert.That(record.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
                        Assert.That(record.WorkFinished, Is.EqualTo(!active));
                        AssertLifetimeCounters(host.Provider.GetStatistics(), indeterminate: 1);
                    }
                    else
                    {
                        SubmitRequestDataType request = host.Request(
                            execution: transition == "deadline" ? 100 : 10_000);
                        clientId = request.ClientOperationId;
                        using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(request)
                            .ConfigureAwait(false);
                        ByteString token = submitted.Value.Operation.ServerToken;
                        submitted.Dispose();
                        await host.Server.Scenario.Entered.WaitAsync(DurableExecutionTestHost.Watchdog)
                            .ConfigureAwait(false);
                        if (transition == "deadline")
                        {
                            state.Clock.Advance(TimeSpan.FromMilliseconds(100));
                            using DurableExecutionReply<GetResultResponseDataType> deadline = await host.GetAsync(token)
                                .ConfigureAwait(false);
                            Assert.That(deadline.Value.Operation.OutcomeKind,
                                Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
                            Assert.That(host.Provider.GetStatistics().ExecutionDeadlineCount, Is.EqualTo(1));
                        }
                        await host.FinishAsync().ConfigureAwait(false);
                        if (transition is "acknowledged" or "abandoned")
                        {
                            using DurableExecutionReply<ControlResponseDataType> released = await host.ControlAsync(
                                transition == "acknowledged"
                                    ? RseBrowseNames.Acknowledge : RseBrowseNames.Abandon, token)
                                .ConfigureAwait(false);
                            Assert.That(released.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                            Assert.That(released.Value.Operation.State, Is.EqualTo(transition == "acknowledged"
                                ? ServiceOperationState.Acknowledged : ServiceOperationState.Abandoned));
                        }
                        else if (transition == "expired")
                        {
                            state.Clock.Advance(TimeSpan.FromMilliseconds(200));
                            using DurableExecutionReply<GetResultResponseDataType> expired = await host.GetAsync(token)
                                .ConfigureAwait(false);
                            Assert.That(expired.Value.Operation.State, Is.EqualTo(ServiceOperationState.Expired));
                            Assert.That(host.Provider.GetStatistics().ExpiredCount, Is.EqualTo(1));
                        }
                    }
                }
                // Capture after orderly stop, including any legitimate cancellation signal for an active orphan.
                return (state, await state.Store.RecordAsync(clientId).ConfigureAwait(false));
            }
            catch
            {
                await state.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static void AssertLifetimeCounters(
            AsyncServiceExecutionStatistics value, ulong indeterminate = 0, ulong deadline = 0,
            ulong acknowledged = 0, ulong abandoned = 0, ulong expired = 0)
        {
            Assert.That(value.AdmittedCount, Is.Zero);
            Assert.That(value.RejectedCount, Is.Zero);
            Assert.That(value.ExecutionIndeterminateCount, Is.EqualTo(indeterminate));
            Assert.That(value.ExecutionDeadlineCount, Is.EqualTo(deadline));
            Assert.That(value.AcknowledgedCount, Is.EqualTo(acknowledged));
            Assert.That(value.AbandonedCount, Is.EqualTo(abandoned));
            Assert.That(value.ExpiredCount, Is.EqualTo(expired));
        }

        private static void AssertRecoveryGauges(
            AsyncServiceExecutionStatistics value, uint active, uint retained, uint history, ulong bytes)
        {
            Assert.That(value.ActiveOperationCount, Is.EqualTo(active));
            Assert.That(value.RetainedOutcomeCount, Is.EqualTo(retained));
            Assert.That(value.TerminalRecordCount, Is.EqualTo(history));
            Assert.That(value.RetainedOutcomeBytes, Is.EqualTo(bytes));
        }

        private static async Task<(DurableExecutionTestState State, RecoverableExecutionDurableRecord Record)>
            InterruptedImageAsync(string phase)
        {
            await using var source = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(source)
                .ConfigureAwait(false);
            using DurableExecutionGate boundary = source.Store.Gate(record => phase switch
            {
                "Admission" => record.Phase == RecoverableExecutionCommitPhase.Admission,
                "Pending" => record.Phase == RecoverableExecutionCommitPhase.Pending && record.DispatchStarted,
                "OutcomePreparing" => record.Phase == RecoverableExecutionCommitPhase.OutcomePreparing,
                "OutcomeAvailable" => record.Phase == RecoverableExecutionCommitPhase.OutcomeAvailable,
                _ => throw new ArgumentException("Unknown interrupted phase.", nameof(phase))
            }, afterCommit: true);
            SubmitRequestDataType request = host.Request();
            Task<DurableExecutionReply<SubmitResponseDataType>> submitting = host.SubmitAsync(request);
            if (phase is "OutcomePreparing" or "OutcomeAvailable")
            {
                _ = await submitting.ConfigureAwait(false);
                host.Server.Scenario.Release();
            }
            await boundary.Entered.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
            IReadOnlyList<KeyValuePair<string, ByteString>> image = await source.Store.ImageAsync()
                .ConfigureAwait(false);
            RecoverableExecutionDurableRecord record = source.Store.Decode(image.Single().Key, image.Single().Value)!;
            boundary.Release();
            using (DurableExecutionReply<SubmitResponseDataType> admitted = await submitting.ConfigureAwait(false))
            {
                Assert.That(admitted.Value.Operation.ServerToken, Is.EqualTo(record.Token));
            }
            await host.FinishAsync().ConfigureAwait(false);
            Assert.That(host.Server.Dispatches, Is.EqualTo(1));
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(12));
            // The source fixture is now known to have ended. The fork retains only its earlier committed image.
            return (await source.ForkAsync(image).ConfigureAwait(false), record);
        }

        private static async Task<DurableExecutionTestState> PendingImageAsync(int count)
        {
            await using var source = new DurableExecutionTestState();
            await using DurableExecutionTestHost host = await DurableExecutionTestHost.StartAsync(source)
                .ConfigureAwait(false);
            for (int index = 0; index < count; index++)
            {
                using DurableExecutionReply<SubmitResponseDataType> submitted = await host.SubmitAsync(host.Request())
                    .ConfigureAwait(false);
                Assert.That(submitted.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            }
            await host.Server.WaitForDispatchesAsync(count).ConfigureAwait(false);
            IReadOnlyList<KeyValuePair<string, ByteString>> image = await source.Store.ImageAsync()
                .ConfigureAwait(false);
            Assert.That(image, Has.Count.EqualTo(count));
            foreach (KeyValuePair<string, ByteString> item in image)
            {
                RecoverableExecutionDurableRecord record = source.Store.Decode(item.Key, item.Value)!;
                Assert.That(record.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Pending));
                Assert.That(record.DispatchStarted, Is.True);
                Assert.That(record.WorkFinished, Is.False);
            }
            await host.FinishAsync().ConfigureAwait(false);
            Assert.That(host.Server.Dispatches, Is.EqualTo(count));
            Assert.That(source.Eligibility.Releases, Is.EqualTo(count));
            return await source.ForkAsync(image).ConfigureAwait(false);
        }

        private static void ChangeInterpretation(
            StandardServer server, DurableExecutionTestState state, string change)
        {
            switch (change)
            {
                case "namespace":
                    server.MessageContext.NamespaceUris.GetIndexOrAppend("urn:durable-test:changed-namespace");
                    break;
                case "registration":
                    Assert.That(server.MessageContext.Factory.TryGetEncodeableType(
                        DataTypeIds.WriteResponse, out IEncodeableType? codec), Is.True);
                    server.MessageContext.Factory.Builder.AddEncodeableType(
                        DurableExecutionTestState.CodecAlias, codec!).Commit();
                    break;
                case "codec-revision":
                    state.Codecs.Revision = ByteString.From([0x44, 0x55, 2]);
                    break;
                case "codec-revocation":
                    state.Codecs.Revision = default;
                    break;
                default:
                    throw new ArgumentException("Unknown interpretation change.", nameof(change));
            }
        }

        private static void AssertUnknown(GetResultResponseDataType response)
        {
            ServiceOperationInfoDataType operation = response.Operation;
            Assert.That(operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            Assert.That(operation.ClientOperationId, Is.EqualTo(Uuid.Empty));
            Assert.That(operation.ServerToken.IsNull, Is.True);
            Assert.That(operation.ServiceId.IsNull, Is.True);
            Assert.That(operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.None));
            Assert.That(operation.OutcomeStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(operation.ExecutionAllowance, Is.Zero);
            Assert.That(operation.ExecutionRemaining, Is.Zero);
            Assert.That(operation.ResultRetentionTime, Is.Zero);
            Assert.That(operation.ResultRemaining, Is.Zero);
            Assert.That(operation.HistoryRemaining, Is.Zero);
            Assert.That(operation.CancellationRequested, Is.False);
            Assert.That(response.Result.IsNull, Is.True);
        }

        private static IEnumerable<TestCaseData> UncertainWriteCases()
        {
            foreach (string transition in new[]
            {
                "Admission", "Deadline", "OutcomePreparing", "OutcomeAvailable",
                RseBrowseNames.Acknowledge, RseBrowseNames.Abandon, RseBrowseNames.Cancel
            })
            {
                yield return new TestCaseData(transition, false);
                yield return new TestCaseData(transition, true);
            }
        }

        private static IEnumerable<TestCaseData> LostReadinessCases()
        {
            foreach (string loss in new[] { "store", "clock" })
            {
                yield return new TestCaseData(loss, RseBrowseNames.Submit, "known");
                foreach (string selector in new[] { "known", "unknown-token", "unknown-client" })
                {
                    yield return new TestCaseData(loss, RseBrowseNames.GetResult, selector);
                }
                foreach (string method in s_controls)
                {
                    yield return new TestCaseData(loss, method, "known");
                    yield return new TestCaseData(loss, method, "unknown-token");
                }
            }
        }

        private static IEnumerable<TestCaseData> InterpretationCases()
        {
            foreach (string change in new[] { "namespace", "registration", "codec-revision", "codec-revocation" })
            {
                foreach (string method in new[]
                {
                    RseBrowseNames.GetResult, RseBrowseNames.Acknowledge, RseBrowseNames.Abandon, RseBrowseNames.Cancel
                })
                {
                    yield return new TestCaseData(change, method);
                }
            }
        }

        private static bool MatchesTransition(RecoverableExecutionDurableRecord record, string transition)
        {
            return transition switch
            {
                "Admission" => record.Phase == RecoverableExecutionCommitPhase.Admission,
                "Deadline" => record.Phase == RecoverableExecutionCommitPhase.Pending && !record.DispatchStarted,
                "OutcomePreparing" => record.Phase == RecoverableExecutionCommitPhase.OutcomePreparing,
                "OutcomeAvailable" => record.Phase == RecoverableExecutionCommitPhase.OutcomeAvailable,
                RseBrowseNames.Acknowledge => record.State == ServiceOperationState.Acknowledged,
                RseBrowseNames.Abandon => record.State == ServiceOperationState.Abandoned,
                RseBrowseNames.Cancel => record.CancellationRequested,
                _ => throw new ArgumentException("Unknown commit transition.", nameof(transition))
            };
        }

        private static bool IsOutcomeSelection(DurableExecutionWrite write)
        {
            return write.Record?.Phase == RecoverableExecutionCommitPhase.OutcomeAvailable &&
                write.Previous?.Phase != RecoverableExecutionCommitPhase.OutcomeAvailable;
        }

        private static ServiceOperationState ReleasedState(string method)
        {
            return method == RseBrowseNames.Acknowledge
                ? ServiceOperationState.Acknowledged : ServiceOperationState.Abandoned;
        }

        private static ByteString AssertOriginal(
            ExtensionObject result, DurableExecutionTestHost host, string family, int expected)
        {
            Assert.That(result.TryGetValue(out IServiceResponse? response, host.Server.MessageContext), Is.True);
            Assert.That(response!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(71u));
            switch (family)
            {
                case "Read":
                    Assert.That(response, Is.InstanceOf<ReadResponse>());
                    var read = (ReadResponse)response;
                    Assert.That(read.Results, Has.Count.EqualTo(1));
                    Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(read.Results[0].WrappedValue.TryGetValue(out ByteString bytes), Is.True);
                    Assert.That(bytes, Is.EqualTo(ByteString.From([3, 1, 4, 1, 5])));
                    break;
                case "Write":
                    Assert.That(response, Is.InstanceOf<WriteResponse>());
                    Assert.That(((WriteResponse)response).Results, Is.EqualTo([StatusCodes.Good]));
                    break;
                case "Call":
                    Assert.That(response, Is.InstanceOf<CallResponse>());
                    var call = (CallResponse)response;
                    Assert.That(call.Results, Has.Count.EqualTo(1));
                    Assert.That(call.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(call.Results[0].OutputArguments, Has.Count.EqualTo(1));
                    Assert.That(call.Results[0].OutputArguments[0].TryGetValue(out int amount), Is.True);
                    Assert.That(amount, Is.EqualTo(expected));
                    break;
                default:
                    Assert.Fail("Unknown original response family.");
                    break;
            }
            return ByteString.From(BinaryEncoder.EncodeMessage(response, host.Server.MessageContext));
        }

        private static ByteString AssertCancelledOriginal(ExtensionObject result, DurableExecutionTestHost host)
        {
            Assert.That(result.TryGetValue(out ServiceFault? fault, host.Server.MessageContext), Is.True);
            Assert.That(fault!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(71u));
            return ByteString.From(BinaryEncoder.EncodeMessage(fault, host.Server.MessageContext));
        }

        private static void AssertNoDispatch(DurableExecutionTestHost host)
        {
            Assert.That(host.Server.Dispatches, Is.Zero);
            Assert.That(host.Server.Scenario.Invocations, Is.Zero);
            Assert.That(host.Server.Scenario.BlobReads, Is.Zero);
            Assert.That(host.Server.Scenario.Counter, Is.EqualTo(7));
        }

        private static async Task CancellationObservedAsync(CancellationToken work)
        {
            TaskCompletionSource<bool> observed = DurableExecutionGate.Signal();
            using CancellationTokenRegistration registration = work.Register(() => observed.TrySetResult(true));
            await observed.Task.WaitAsync(DurableExecutionTestHost.Watchdog, CancellationToken.None)
                .ConfigureAwait(false);
        }

        private static readonly string[] s_controls =
            [RseBrowseNames.Acknowledge, RseBrowseNames.Abandon, RseBrowseNames.Cancel];

        /// <summary>
        /// The existing isolation pools reserve 3 envelopes for owned input, 16 + 49152 bytes for
        /// journal publication, and 8 * 1024 + 16384 bytes for protected owner/history metadata.
        /// </summary>
        private const long kRestoredActiveBytes = (19L * DurableExecutionTestHost.MessageLimit) + 73_728;
        private const long kOrphanAfterReleaseBytes = (3L * DurableExecutionTestHost.MessageLimit) + 24_576;
        private const long kCompletedBytes = (20L * DurableExecutionTestHost.MessageLimit) + 73_728;
        private const long kHistoryBytes = 24_576;
        private const ulong kRetainedMetadataBytes = 1024;
    }
}

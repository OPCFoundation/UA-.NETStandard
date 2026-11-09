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
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Identity;
using Opc.Ua.Programs;
using Opc.Ua.Server.Programs;
using Opc.Ua.Tests;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests.Programs
{
    /// <summary>
    /// Exercises managed Programs through the same method and provider seams used by applications.
    /// </summary>
    [TestFixture]
    [Category("Programs")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ProgramRuntimeTests
    {
        /// <summary>
        /// A remote request without a Session remains a caller, not a trusted in-process diagnostic read.
        /// </summary>
        [TestCase(false, MessageSecurityMode.SignAndEncrypt, false)]
        [TestCase(true, MessageSecurityMode.SignAndEncrypt, true)]
        [TestCase(true, MessageSecurityMode.Sign, false)]
        [Category("SecurityRegression")]
        public async Task SecurityR34RemoteSessionlessReadIsNotTrustedInternalDiagnosticsAsync(
            bool securityAdmin, MessageSecurityMode mode, bool allowed)
        {
            await using Fixture fixture = await Fixture.CreateAsync(diagnostics: true).ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var sessions = new SessionManager(server.Object, new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration()
            }))
            {
                var identity = new RoleBasedIdentity(new UserIdentity("diagnostic-reader", [1]),
                    securityAdmin ? [Role.SecurityAdmin] : [Role.AuthenticatedUser], server.Object.NamespaceUris);
                sessions.ValidateSessionLessRequest += (_, args) => args.Identity = identity;
                var channel = new SecureChannelContext("diagnostic-sessionless", new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost/diagnostic-sessionless",
                    TransportProfileUri = Profiles.UaTcpTransport,
                    SecurityMode = mode
                }, RequestEncoding.Binary);
                using OperationContext operation = await sessions.ValidateRequestAsync(
                    new RequestHeader { AuthenticationToken = new NodeId("accepted-token", 1) },
                    channel, RequestType.Read, RequestLifetime.None).ConfigureAwait(false);
                Assert.That(operation.Session, Is.Null);
                Assert.That(operation.UserIdentity.DisplayName, Is.EqualTo("diagnostic-reader"));
                ServerSystemContext caller = server.Object.DefaultSystemContext.Copy(operation);
                var ordinary = new BaseDataVariableState(null)
                {
                    Value = 42,
                    DataType = DataTypeIds.Int32,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead
                };
                (ServiceResult ordinaryResult, DataValue ordinaryValue) = await ordinary.ReadAttributeAsync(
                    caller, Attributes.Value, default, QualifiedName.Null, new DataValue())
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(ordinaryResult), Is.True);
                Assert.That(ordinaryValue.WrappedValue, Is.EqualTo(new Variant(42)));

                var variables = new List<BaseVariableState>();
                CollectDiagnosticVariables(fixture.Context, fixture.Runtime.Node.ProgramDiagnostic!, variables);
                Assert.That(variables, Has.Count.GreaterThan(10));
                foreach (BaseVariableState variable in variables)
                {
                    (ServiceResult result, DataValue value) = await variable.ReadAttributeAsync(
                        caller, Attributes.Value, default, QualifiedName.Null, new DataValue()).ConfigureAwait(false);
                    Assert.That(result.StatusCode, Is.EqualTo(
                        allowed ? StatusCodes.Good : StatusCodes.BadUserAccessDenied), variable.BrowseName.ToString());
                    if (!allowed)
                    {
                        Assert.That(value.WrappedValue.IsNull, Is.True, variable.BrowseName.ToString());
                    }
                }
                (ServiceResult internalResult, DataValue internalValue) =
                    await fixture.Runtime.Node.ProgramDiagnostic!.LastMethodCall!.ReadAttributeAsync(
                        fixture.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                        .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(internalResult), Is.True);
                Assert.That(internalValue.WrappedValue, Is.EqualTo(new Variant("Start")));
                if (allowed)
                {
                    (ServiceResult result, DataValue value) =
                        await fixture.Runtime.Node.ProgramDiagnostic.LastMethodCall.ReadAttributeAsync(
                            caller, Attributes.Value, default, QualifiedName.Null, new DataValue()).ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(result), Is.True);
                    Assert.That(value.WrappedValue, Is.EqualTo(new Variant("Start")));
                }
            }
        }

        /// <summary>
        /// Provider-driven diagnostics use the subscriber's policy even after a denied initial read.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR33NonCreatorCannotReceiveDiagnosticUpdatesAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync(diagnostics: true).ConfigureAwait(false);
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var owner = new TestableAsyncCustomNodeManager(
                    server.Object, new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                BaseVariableState variable = fixture.Runtime.Node.ProgramDiagnostic!.LastMethodCall!;
                variable.NodeId = new NodeId("ProtectedProgramLastMethod", owner.NamespaceIndex);
                await owner.AddNodeAsync(owner.SystemContext, NodeId.Null, variable).ConfigureAwait(false);
                fixture.Runtime.RegisterDiagnosticReadAuthorization(owner);
                Mock.Get(server.Object.NodeManager)
                    .Setup(value => value.GetManagerHandleAsync(variable.NodeId, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<(object? Handle, IAsyncNodeManager? Manager)>(
                        (new NodeHandle(variable.NodeId, variable), owner)));
                var deniedValues = new ConcurrentQueue<DataValue>();
                var adminValues = new ConcurrentQueue<DataValue>();
                await using var monitored = new MonitoredNode2(owner, server.Object, variable);
                Mock<IDataChangeMonitoredItem2> denied = CreateDiagnosticReceiver(
                    server.Object, 1, false, deniedValues);
                monitored.Add(denied.Object);
                monitored.Add(CreateDiagnosticReceiver(server.Object, 2, true, adminValues).Object);

                using var initialRead = new OperationContext(denied.Object.Session, DiagnosticsMasks.None);
                (ServiceResult initialResult, DataValue initialValue) = await variable.ReadAttributeAsync(
                    server.Object.DefaultSystemContext.Copy(initialRead), Attributes.Value, default,
                    QualifiedName.Null, new DataValue()).ConfigureAwait(false);
                Assert.That(initialResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(initialValue.WrappedValue.IsNull, Is.True);

                await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await monitored.WaitForPendingNotificationsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(fixture.Execution.CallCount, Is.EqualTo(1));
                Assert.That(deniedValues, Has.Count.EqualTo(1));
                Assert.That(deniedValues.ToArray()[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(deniedValues.ToArray()[0].WrappedValue.IsNull, Is.True);
                Assert.That(adminValues, Has.Count.EqualTo(1));
                Assert.That(adminValues.ToArray()[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(adminValues.ToArray()[0].WrappedValue, Is.EqualTo(new Variant("Start")));
            }
        }

        /// <summary>
        /// Enumerates the same diagnostic descendant graph protected by the runtime.
        /// </summary>
        private static void CollectDiagnosticVariables(
            ISystemContext context, NodeState node, List<BaseVariableState> variables)
        {
            if (node is BaseVariableState variable)
            {
                variables.Add(variable);
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                CollectDiagnosticVariables(context, child, variables);
            }
        }

        /// <summary>
        /// Models separate real-session identities with encrypted subscriber endpoints.
        /// </summary>
        private static Mock<IDataChangeMonitoredItem2> CreateDiagnosticReceiver(
            IServerInternal server, uint id, bool securityAdmin, ConcurrentQueue<DataValue> values)
        {
            var identity = new RoleBasedIdentity(new UserIdentity("reader", [1]),
                securityAdmin ? [Role.SecurityAdmin] : [Role.AuthenticatedUser], server.NamespaceUris);
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(id, 1));
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity);
            session.SetupGet(value => value.EndpointDescription).Returns(new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                TransportProfileUri = Profiles.UaTcpTransport
            });
            var item = new Mock<IDataChangeMonitoredItem2>();
            item.SetupGet(value => value.Id).Returns(id);
            item.SetupGet(value => value.Session).Returns(session.Object);
            item.SetupGet(value => value.EffectiveIdentity).Returns(identity);
            item.SetupGet(value => value.AttributeId).Returns(Attributes.Value);
            item.SetupGet(value => value.IndexRange).Returns(NumericRange.Null);
            item.SetupGet(value => value.DataEncoding).Returns(QualifiedName.Null);
            item.Setup(value => value.QueueValue(It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult>()))
                .Callback(new DiagnosticQueueValueCallback((in value, _) => values.Enqueue(value)));
            item.Setup(value => value.QueueValue(
                    It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult>(), It.IsAny<bool>()))
                .Callback(new DiagnosticFilteredQueueValueCallback((in value, _, _) => values.Enqueue(value)));
            return item;
        }

        /// <summary>
        /// Captures the ref-readonly queued diagnostic value.
        /// </summary>
        private delegate void DiagnosticQueueValueCallback(in DataValue value, ServiceResult? error);

        /// <summary>
        /// Captures the production enqueue overload that explicitly selects monitored-item filter handling.
        /// </summary>
        private delegate void DiagnosticFilteredQueueValueCallback(
            in DataValue value, ServiceResult? error, bool ignoreFilters);

        [Test]
        public async Task ControlsFollowStrictProgramSemanticsAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Suspend, ProgramState.Suspended).ConfigureAwait(false);
            MethodInvocationResult reset = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Reset).ConfigureAwait(false);
            Assert.That(reset.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadNotExecutable));
            Assert.That(fixture.Runtime.CurrentState, Is.EqualTo(ProgramState.Suspended));
            await fixture.CallAsync(ProgramControlMethod.Resume, ProgramState.Running).ConfigureAwait(false);
            Assert.That(fixture.Runtime.Node.RecycleCount!.Value, Is.Zero);
            await fixture.CallAsync(ProgramControlMethod.Halt, ProgramState.Halted).ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Reset, ProgramState.Ready).ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
            Assert.That(fixture.Runtime.Node.RecycleCount!.Value, Is.EqualTo(1));
        }

        [Test]
        public async Task RejectedProviderDoesNotCommitOrConsumeAnExecutionAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            fixture.Execution.Result = new ServiceResult(StatusCodes.BadResourceUnavailable);
            MethodInvocationResult result = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                Assert.That(fixture.Runtime.CurrentState, Is.EqualTo(ProgramState.Ready));
                Assert.That(fixture.Runtime.RunId, Is.Zero);
                Assert.That(fixture.Runtime.Node.RecycleCount!.Value, Is.Zero);
            });
        }

        [Test]
        public async Task GuardsRunBeforeTheProviderAndOnlyOnceAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            int guards = 0;
            fixture.Runtime.Node.OnBeforeTransition = (_, _, _, _, _, _) =>
            {
                guards++;
                return StatusCodes.BadInvalidState;
            };
            MethodInvocationResult result = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(guards, Is.EqualTo(1));
                Assert.That(fixture.Execution.CallCount, Is.Zero);
            });
        }

        [Test]
        public async Task InternalCompletionAndStaleRunAreDistinguishedAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
            long firstRun = fixture.Runtime.RunId;
            ServiceResult completion = await fixture.Runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(firstRun, ProgramState.Ready)).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(completion), Is.True);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
            ServiceResult stale = await fixture.Runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(firstRun, ProgramState.Halted)).ConfigureAwait(false);
            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(fixture.Runtime.CurrentState, Is.EqualTo(ProgramState.Running));
        }

        [Test]
        public async Task RunOncePolicyKeepsCompletedInvocationHaltedAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync(maxRecycles: 0).ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Halt, ProgramState.Halted).ConfigureAwait(false);
            MethodInvocationResult reset = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Reset).ConfigureAwait(false);
            Assert.That(reset.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadNotExecutable));
            MethodInvocationResult secondStart = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
            Assert.That(secondStart.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadNotExecutable));
            Assert.That(fixture.Execution.CallCount, Is.EqualTo(2));
            Assert.That(fixture.Runtime.CurrentState, Is.EqualTo(ProgramState.Halted));
        }

        [Test]
        public async Task InvalidArgumentsNeverReachTheProviderAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            MethodInvocationResult result = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Start, [new Variant(42)]).ConfigureAwait(false);
            Assert.That(result.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadTooManyArguments));
            Assert.That(fixture.Execution.CallCount, Is.Zero);
        }

        [Test]
        public async Task ClientDeletionRequiresHaltedAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            ServiceResult running = await fixture.Runtime.PrepareDeleteAsync(byClient: true).ConfigureAwait(false);
            Assert.That(running.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            await fixture.CallAsync(ProgramControlMethod.Halt, ProgramState.Halted).ConfigureAwait(false);
            ServiceResult halted = await fixture.Runtime.PrepareDeleteAsync(byClient: true).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(halted), Is.True);
        }

        [Test]
        public async Task DiagnosticsRecordValidationFailuresAndInternalTransitionsAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync(diagnostics: true).ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
            MethodInvocationResult invalid = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
            Assert.That(invalid.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadNotExecutable));
            ProgramDiagnostic2DataType diagnostic = fixture.Runtime.Node.ProgramDiagnostic!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(diagnostic.LastMethodCall, Is.EqualTo("Start"));
                Assert.That(diagnostic.LastMethodReturnStatus, Is.EqualTo(StatusCodes.BadNotExecutable));
                Assert.That(fixture.Runtime.Node.ProgramDiagnostic.LastMethodCall!.Value,
                    Is.EqualTo(diagnostic.LastMethodCall));
            });
            ServiceResult result = await fixture.Runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(fixture.Runtime.RunId, ProgramState.Ready)).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(fixture.Runtime.Node.ProgramDiagnostic.Value.LastTransitionTime,
                Is.EqualTo(fixture.Runtime.Node.LastTransition!.TransitionTime!.Value));
        }

        [Test]
        public async Task DisposalReleasesTheProviderAsync()
        {
            Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            await fixture.DisposeAsync().ConfigureAwait(false);
            Assert.That(fixture.Execution.Disposed, Is.True);
            await fixture.Runtime.DisposeAsync().ConfigureAwait(false);
        }

        [TestCase((long)int.MaxValue - 1)]
        [TestCase((long)int.MaxValue)]
        public void RepresentableRecycleCountsRemainAccurate(long count)
        {
            PropertyState<int> variable = PropertyState<int>.With<VariantBuilder>(new BaseObjectState(null));
            ProgramRuntime.PublishRecycleCount(variable, count);
            Assert.Multiple(() =>
            {
                Assert.That(variable.Value, Is.EqualTo(count));
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
            });
        }

        [Test]
        public void UnlimitedRecyclingContinuesAfterTheWireCounterOverflows()
        {
            PropertyState<int> variable =
                PropertyState<int>.With<VariantBuilder>(new BaseObjectState(null), int.MaxValue);
            long count = (long)int.MaxValue + 1;
            ProgramRuntime.PublishRecycleCount(variable, count);
            Assert.Multiple(() =>
            {
                Assert.That(variable.Value, Is.EqualTo(int.MaxValue));
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(ProgramRuntime.CanRestart(true, count, uint.MaxValue), Is.True);
                Assert.That(ProgramRuntime.CanRestart(true, count, int.MaxValue), Is.False);
                Assert.That(ProgramRuntime.CanRestart(true, (long)uint.MaxValue - 1, uint.MaxValue - 1), Is.False);
            });
        }

        [Test]
        public async Task TransitionEventsRetainTheirCapturedStateAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            var events = new List<ProgramTransitionEventState>();
            fixture.Runtime.Node.OnReportEvent = (_, _, value) =>
            {
                if (value is ProgramTransitionEventState transition)
                {
                    events.Add(transition);
                }
            };
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Suspend, ProgramState.Suspended).ConfigureAwait(false);
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0].ToState!.Id!.Value.TryGetValue(out NodeId firstTarget), Is.True);
            Assert.That(firstTarget, Is.EqualTo(ObjectIds.ProgramStateMachineType_Running));
            Assert.That(events[1].ToState!.Id!.Value.TryGetValue(out NodeId secondTarget), Is.True);
            Assert.That(secondTarget, Is.EqualTo(ObjectIds.ProgramStateMachineType_Suspended));
        }

        [Test]
        public async Task DuplicateAttachmentCannotResetAnExistingExecutionAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ProgramRuntime.AttachAsync(fixture.Runtime.Node, fixture.Runtime.Definition,
                    fixture.Context, TimeProvider.System).ConfigureAwait(false)))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Runtime.Node.CurrentState!.Number!.Value, Is.EqualTo((uint)ProgramState.Running));
            Assert.That(fixture.Execution.Disposed, Is.False);
        }

        [Test]
        public async Task ProviderOutputTypesAreValidatedBeforeCommitAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            var method = (MethodState)fixture.Runtime.Node.FindChild(
                fixture.Context, new QualifiedName("Start", 1))!;
            method.OutputArguments = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method,
                [new Argument { Name = "Result", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }]);
            fixture.Execution.Outputs = [Variant.From("not an integer")];
            MethodInvocationResult result = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(fixture.Runtime.CurrentState, Is.EqualTo(ProgramState.Ready));
                Assert.That(fixture.Runtime.RunId, Is.Zero);
            });
        }

        [Test]
        public async Task UserPermissionFailureDoesNotInvokeTheProviderAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync(diagnostics: true).ConfigureAwait(false);
            fixture.Runtime.Node.OnCheckUserPermission = (_, _, _, _, _, _) => StatusCodes.BadUserAccessDenied;
            MethodInvocationResult result = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
            Assert.That(result.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.Execution.CallCount, Is.Zero);
            Assert.That(fixture.Runtime.Node.ProgramDiagnostic!.Value.LastMethodReturnStatus,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task InitiallyHaltedProgramCanBecomeReadyWhenResourcesArriveAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync(initialState: ProgramState.Halted)
                .ConfigureAwait(false);
            ServiceResult result = await fixture.Runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(0, ProgramState.Ready)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(fixture.Runtime.CurrentState, Is.EqualTo(ProgramState.Ready));
                Assert.That(fixture.Runtime.RunId, Is.Zero);
                Assert.That(fixture.Execution.CallCount, Is.Zero);
            });
        }

        [Test]
        public async Task AutonomousRunsUseNewGenerationsAndTheSameRecyclingPolicyAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            ServiceResult first = await fixture.Runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(1, ProgramState.Running)).ConfigureAwait(false);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResult pause = await fixture.Runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(1, ProgramState.Suspended)).ConfigureAwait(false);
            Assert.That(pause.StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResult complete = await fixture.Runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(1, ProgramState.Ready)).ConfigureAwait(false);
            Assert.That(complete.StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResult restart = await fixture.Runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(2, ProgramState.Running)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(restart.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(fixture.Runtime.RunId, Is.EqualTo(2));
                Assert.That(fixture.Runtime.Node.RecycleCount!.Value, Is.EqualTo(1));
                Assert.That(fixture.Execution.CallCount, Is.Zero);
            });
        }

        [Test]
        public async Task ControlsRemainDisabledUntilRegistrationIsActivatedAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync(startObserving: false).ConfigureAwait(false);
            MethodInvocationResult before = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
            Assert.That(before.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadNotExecutable));
            Assert.That(fixture.Execution.CallCount, Is.Zero);
            await fixture.Runtime.ActivateAsync().ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
        }

        [Test]
        public async Task ActivationWaitsForObservationBeforeEnablingControlsAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync(startObserving: false).ConfigureAwait(false);
            fixture.Execution.PublishHotCompletion = true;
            var halted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Runtime.Node.OnReportEvent = (_, _, value) =>
            {
                if (value is ProgramTransitionEventState transition &&
                    transition.ToState!.Id!.Value.TryGetValue(out NodeId state) &&
                    state == ObjectIds.ProgramStateMachineType_Halted)
                {
                    halted.TrySetResult(true);
                }
            };
            var scheduler = new DeferredSynchronizationContext();
            SynchronizationContext? previous = SynchronizationContext.Current;
            ValueTask activation;
            try
            {
                SynchronizationContext.SetSynchronizationContext(scheduler);
                activation = fixture.Runtime.ActivateAsync();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
            try
            {
                Assert.That(activation.IsCompleted, Is.False,
                    "Activation must wait until the provider's first MoveNext has installed its observer.");
                Assert.That(fixture.Execution.ObservationStarted.Task.IsCompleted, Is.False);
                MethodInvocationResult before = await fixture.Runtime.ControlAsync(
                    fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
                Assert.That(before.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadNotExecutable));
                Assert.That(fixture.Execution.CallCount, Is.Zero);
                scheduler.RunPostedCallbacks();
                await activation.ConfigureAwait(false);
                Assert.That(fixture.Execution.ObservationStarted.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                MethodInvocationResult start = await fixture.Runtime.ControlAsync(
                    fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
                Assert.That(start.OperationResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                await halted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(fixture.Execution.DroppedUpdates, Is.Zero,
                    "A provider that drops updates without an observer must retain the first Start completion.");
            }
            finally
            {
                scheduler.RunPostedCallbacks();
            }
        }

        [Test]
        public async Task ActivationFailureKeepsControlsDisabledAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync(startObserving: false).ConfigureAwait(false);
            fixture.Execution.OnObservationStarting =
                () => throw new InvalidOperationException("Observer setup failed.");

            InvalidOperationException exception = (await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.Runtime.ActivateAsync().AsTask()))!;

            Assert.That(exception.Message, Is.EqualTo("Observer setup failed."));
            MethodInvocationResult start = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
            Assert.That(start.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadNotExecutable));
            Assert.That(fixture.Execution.CallCount, Is.Zero);
        }

        [Test]
        public async Task ActivationCancellationDrainsPendingEnumerationAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync(startObserving: false).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            fixture.Execution.OnObservationStarting = cancellation.Cancel;

            await Assert.ThrowsAsync<TaskCanceledException>(
                () => fixture.Runtime.ActivateAsync(cancellation.Token).AsTask());

            await fixture.Runtime.DisposeAsync().ConfigureAwait(false);
            Assert.That(fixture.Execution.Disposed, Is.True);
            Assert.That(fixture.Execution.CallCount, Is.Zero);
        }

        [Test]
        public async Task ResetWaitsForRestartedObservationBeforeEnablingControlsAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            var halted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Runtime.Node.OnReportEvent = (_, _, value) =>
            {
                if (value is ProgramTransitionEventState transition &&
                    transition.ToState!.Id!.Value.TryGetValue(out NodeId state) &&
                    state == ObjectIds.ProgramStateMachineType_Halted)
                {
                    halted.TrySetResult(true);
                }
            };
            fixture.Execution.FailObservation();
            await halted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            var scheduler = new DeferredSynchronizationContext();
            SynchronizationContext? previous = SynchronizationContext.Current;
            ValueTask<MethodInvocationResult> reset;
            try
            {
                SynchronizationContext.SetSynchronizationContext(scheduler);
                reset = fixture.Runtime.ControlAsync(fixture.Context, ProgramControlMethod.Reset);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
                scheduler.RunPostedCallbacks();
            }
            MethodInvocationResult result = await reset.ConfigureAwait(false);
            Assert.That(result.OperationResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(fixture.Execution.ObservationStarted.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion),
                "A completed Reset must have reinstalled the observer before another Start can run.");
            fixture.Execution.PublishHotCompletion = true;
            MethodInvocationResult start = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Start).ConfigureAwait(false);
            Assert.That(start.OperationResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(fixture.Execution.DroppedUpdates, Is.Zero);
        }

        [Test]
        public async Task FailedObservationRestartCanBeResetAgainAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            var halted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Runtime.Node.OnReportEvent = (_, _, value) =>
            {
                if (value is ProgramTransitionEventState transition &&
                    transition.ToState!.Id!.Value.TryGetValue(out NodeId state) &&
                    state == ObjectIds.ProgramStateMachineType_Halted)
                {
                    halted.TrySetResult(true);
                }
            };
            fixture.Execution.FailObservation();
            await halted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            halted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Execution.OnObservationStarting =
                () => throw new InvalidOperationException("Observer could not restart.");
            MethodInvocationResult failedReset = await fixture.Runtime.ControlAsync(
                fixture.Context, ProgramControlMethod.Reset).ConfigureAwait(false);
            Assert.That(failedReset.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            await halted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(fixture.Runtime.CurrentState, Is.EqualTo(ProgramState.Halted));

            fixture.Execution.OnObservationStarting = null;
            await fixture.CallAsync(ProgramControlMethod.Reset, ProgramState.Ready).ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
        }

        [Test]
        public async Task ObservationFailureHaltsAnUnstartedProgramAndResetRestartsObservationAsync()
        {
            await using Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            var halted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Runtime.Node.OnReportEvent = (_, _, value) =>
            {
                if (value is ProgramTransitionEventState transition &&
                    transition.ToState!.Id!.Value.TryGetValue(out NodeId state) &&
                    state == ObjectIds.ProgramStateMachineType_Halted)
                {
                    halted.TrySetResult(true);
                }
            };
            fixture.Execution.FailObservation();
            await halted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(fixture.Runtime.RunId, Is.Zero);
            await fixture.CallAsync(ProgramControlMethod.Reset, ProgramState.Ready).ConfigureAwait(false);
            await fixture.CallAsync(ProgramControlMethod.Start, ProgramState.Running).ConfigureAwait(false);
            Assert.That(fixture.Runtime.RunId, Is.EqualTo(1));
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private Fixture(SystemContext context, ControlledExecution execution, ProgramRuntime runtime)
            {
                Context = context;
                Execution = execution;
                Runtime = runtime;
            }

            public SystemContext Context { get; }

            public ControlledExecution Execution { get; }

            public ProgramRuntime Runtime { get; }

            public static async Task<Fixture> CreateAsync(
                uint maxRecycles = uint.MaxValue,
                bool diagnostics = false,
                ProgramState initialState = ProgramState.Ready,
                bool startObserving = true)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var namespaces = new NamespaceTable();
                namespaces.GetIndexOrAppend(global::Opc.Ua.Namespaces.OpcUa);
                namespaces.GetIndexOrAppend("urn:program-tests");
                namespaces.GetIndexOrAppend(global::Opc.Ua.Programs.Namespaces.OpcUaPrograms);
                var context = new SystemContext(telemetry)
                {
                    NamespaceUris = namespaces,
                    TypeTable = new TypeTable(namespaces)
                };
                var execution = new ControlledExecution();
                var definition = new ProgramDefinition(
                    new ExpandedNodeId(ObjectTypes.ProgramStateMachineType),
                    static parent => new ProgramStateMachineState(parent),
                    new DelegateProgramProvider((_, _) => new ValueTask<IProgramExecution>(execution)),
                    [ProgramControlMethod.Start, ProgramControlMethod.Suspend, ProgramControlMethod.Resume,
                        ProgramControlMethod.Halt, ProgramControlMethod.Reset],
                    initialState, 1, maxRecycles)
                {
                    Deletable = true,
                    DiagnosticsEnabled = diagnostics
                };
                var node = new ProgramStateMachineState(null);
                node.Create(context, new NodeId("Program", 1),
                    new QualifiedName("Program", 1), new LocalizedText("Program"), false);
                ProgramRuntime runtime = await ProgramRuntime.AttachAsync(
                    node, definition, context, TimeProvider.System, startObserving: startObserving)
                    .ConfigureAwait(false);
                return new Fixture(context, execution, runtime);
            }

            public async Task CallAsync(ProgramControlMethod control, ProgramState expected)
            {
                MethodInvocationResult result = await Runtime.ControlAsync(Context, control).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result.OperationResult), Is.True, result.OperationResult.ToString());
                Assert.That(Runtime.CurrentState, Is.EqualTo(expected));
            }

            public ValueTask DisposeAsync()
            {
                return Runtime.DisposeAsync();
            }
        }

        private sealed class ControlledExecution : IProgramExecution
        {
            public ServiceResult Result { get; set; } = ServiceResult.Good;

            public int CallCount { get; private set; }

            public bool Disposed { get; private set; }

            public ArrayOf<Variant> Outputs { get; set; }

            public bool PublishHotCompletion { get; set; }

            public int DroppedUpdates { get; private set; }

            public TaskCompletionSource<bool> ObservationStarted { get; private set; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Action? OnObservationStarting { get; set; }

            public ValueTask<ProgramControlResult> ControlAsync(
                ProgramControlContext context, CancellationToken cancellationToken = default)
            {
                CallCount++;
                if (context.Control == ProgramControlMethod.Reset && m_failed)
                {
                    m_updates = Channel.CreateUnbounded<ProgramExecutionUpdate>();
                    ObservationStarted = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    m_failed = false;
                }
                if (context.Control == ProgramControlMethod.Start && PublishHotCompletion)
                {
                    if (ObservationStarted.Task.IsCompleted)
                    {
                        m_updates.Writer.TryWrite(new ProgramExecutionUpdate(context.RunId, ProgramState.Halted));
                    }
                    else
                    {
                        DroppedUpdates++;
                    }
                }
                return new ValueTask<ProgramControlResult>(new ProgramControlResult(Result, Outputs));
            }

            public void FailObservation()
            {
                m_failed = true;
                m_updates.Writer.TryComplete(new InvalidOperationException("Simulated execution stream failure."));
            }

            public async IAsyncEnumerable<ProgramExecutionUpdate> ObserveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                OnObservationStarting?.Invoke();
                ObservationStarted.TrySetResult(true);
                await foreach (ProgramExecutionUpdate update in
                    m_updates.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return update;
                }
            }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                m_updates.Writer.TryComplete();
                return default;
            }

            private Channel<ProgramExecutionUpdate> m_updates =
                Channel.CreateUnbounded<ProgramExecutionUpdate>();
            private bool m_failed;
        }

        private sealed class DeferredSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback callback, object? state)
            {
                m_callbacks.Enqueue(() => callback(state));
            }

            public void RunPostedCallbacks()
            {
                while (m_callbacks.Count != 0)
                {
                    m_callbacks.Dequeue()();
                }
            }

            private readonly Queue<Action> m_callbacks = new();
        }
    }
}

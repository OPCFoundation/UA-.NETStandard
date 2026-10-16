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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PlasticsRubber.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.PlasticsRubber.Runtime.Tests
{
    /// <summary>
    /// Exercises generated instances and their real asynchronous method/write dispatch.
    /// </summary>
    [TestFixture]
    public sealed partial class CommonBindingTests
    {
        /// <summary>
        /// Creates an isolated generated-model context without a network or hardware dependency.
        /// </summary>
        public CommonBindingTests()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext message = ServiceMessageContext.Create(telemetry);
            message.NamespaceUris.GetIndexOrAppend("urn:plastics-runtime-tests:instances");
            message.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            message.NamespaceUris.GetIndexOrAppend(Namespaces.PlasticsRubber);
            message.Factory.Builder.AddOpcUaPlasticsRubber().Commit();
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory,
                TypeTable = new TypeTable(message.NamespaceUris)
            };
        }

        /// <summary>
        /// Acknowledged start requests preserve the original caller and do not invent physical state.
        /// </summary>
        [Test]
        public async Task StartPreservesCallerAndObservedStateAsync()
        {
            StartDeviceState node = CreateStartDevice();
            node.Status!.Value = StartEnumeration.READY_TO_START;
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            provider.Setup(value => value.StartAsync(
                It.Is<PlasticsRubberRequestContext>(request =>
                    ReferenceEquals(request.SystemContext, m_context) && request.TargetId == node.NodeId),
                It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            await using var binding = new StartDeviceBinding(node, m_context, provider.Object, AllowAccess());

            ServiceResult result = await node.StartRequest!.CallAsync(
                m_context, node.NodeId, [], [], [], CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(node.Status.Value, Is.EqualTo(StartEnumeration.READY_TO_START));
            });
            provider.VerifyAll();
        }

        /// <summary>
        /// An explicit client start interlock prevents provider invocation.
        /// </summary>
        [Test]
        public async Task StartInterlockPreventsRequestAsync()
        {
            StartDeviceState node = CreateStartDevice();
            node.StartBlockedByClient!.Value = true;
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            await using var binding = new StartDeviceBinding(node, m_context, provider.Object, AllowAccess());

            ServiceResult result = await node.StartRequest!.CallAsync(
                m_context, node.NodeId, [], [], [], CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Authorization fails before the provider sees a command.
        /// </summary>
        [Test]
        public async Task DeniedStartDoesNotReachProviderAsync()
        {
            StartDeviceState node = CreateStartDevice();
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            var access = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            access.Setup(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Throws(ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "Denied by test policy."));
            await using var binding = new StartDeviceBinding(node, m_context, provider.Object, access.Object);

            ServiceResult result = await node.StartRequest!.CallAsync(
                m_context, node.NodeId, [], [], [], CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A device failure remains the exact OPC UA method failure.
        /// </summary>
        [Test]
        public async Task ProviderFailureIsNotAcknowledgedAsSuccessAsync()
        {
            StartDeviceState node = CreateStartDevice();
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            provider.Setup(value => value.StopAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                .Throws(ServiceResultException.Create(StatusCodes.BadResourceUnavailable, "Device unavailable."));
            await using var binding = new StartDeviceBinding(node, m_context, provider.Object, AllowAccess());

            ServiceResult result = await node.StopRequest!.CallAsync(
                m_context, node.NodeId, [], [], [], CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
        }

        /// <summary>
        /// Interlock writes invoke the authority before becoming visible.
        /// </summary>
        [Test]
        public async Task InterlockWriteWaitsForCommitAsync()
        {
            StartDeviceState node = CreateStartDevice();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            provider.Setup(value => value.SetStartBlockedAsync(
                It.IsAny<PlasticsRubberRequestContext>(), true, It.IsAny<CancellationToken>()))
                .Returns(async (PlasticsRubberRequestContext _, bool _, CancellationToken _) =>
                {
                    entered.TrySetResult(true);
                    await completion.Task.ConfigureAwait(false);
                });
            await using var binding = new StartDeviceBinding(node, m_context, provider.Object, AllowAccess());

            Task<ServiceResult> writing = node.StartBlockedByClient!.WriteAttributeAsync(
                m_context, Attributes.Value, default, new DataValue(Variant.From(true))).AsTask();
            try
            {
                Task first = await Task.WhenAny(entered.Task, writing)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (first == writing)
                {
                    ServiceResult early = await writing.ConfigureAwait(false);
                    Assert.Fail($"The write completed before reaching the commit provider: {early}.");
                }
                Assert.That(node.StartBlockedByClient.Value, Is.False);
            }
            finally
            {
                completion.TrySetResult(true);
            }
            ServiceResult result = await writing.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(node.StartBlockedByClient.Value, Is.True);
            });
        }

        /// <summary>
        /// A rejected interlock write preserves the previous cached value.
        /// </summary>
        [Test]
        public async Task RejectedInterlockWritePreservesValueAsync()
        {
            StartDeviceState node = CreateStartDevice();
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            provider.Setup(value => value.SetStartBlockedAsync(
                It.IsAny<PlasticsRubberRequestContext>(), true, It.IsAny<CancellationToken>()))
                .Throws(ServiceResultException.Create(StatusCodes.BadInvalidState, "A safety transition is active."));
            await using var binding = new StartDeviceBinding(node, m_context, provider.Object, AllowAccess());

            ServiceResult result = await node.StartBlockedByClient!.WriteAttributeAsync(
                m_context, Attributes.Value, default, new DataValue(Variant.From(true))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(node.StartBlockedByClient.Value, Is.False);
            });
        }

        /// <summary>
        /// Disposing a binding prevents later calls and clears its advertised write capability.
        /// </summary>
        [Test]
        public async Task DisposedBindingCannotExecuteOrAcceptWritesAsync()
        {
            StartDeviceState node = CreateStartDevice();
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            var binding = new StartDeviceBinding(node, m_context, provider.Object, AllowAccess());
            MethodState start = node.StartRequest!;
            await binding.DisposeAsync().ConfigureAwait(false);
            ServiceResult result = await start.CallAsync(
                m_context, node.NodeId, [], [], [], CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsBad(result), Is.True);
                Assert.That(start.Executable, Is.False);
                Assert.That(node.StartBlockedByClient!.AccessLevel & AccessLevels.CurrentWrite, Is.Zero);
            });
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Concurrent disposal cancels active work and drains queued requests before releasing their gate.
        /// </summary>
        [Test]
        public async Task ConcurrentDisposalDrainsActiveAndQueuedRequestsAsync()
        {
            StartDeviceState node = CreateStartDevice();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            provider.Setup(value => value.StartAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(async (PlasticsRubberRequestContext _, CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                });
            await using var binding = new StartDeviceBinding(node, m_context, provider.Object, AllowAccess());
            MethodState start = node.StartRequest!;
            GenericMethodCalledEventHandler2Async handler = start.OnCallMethod2Async!;
            Task<ServiceResult> active = handler(
                m_context, start, node.NodeId, [], [], CancellationToken.None).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task<ServiceResult> queued = handler(
                m_context, start, node.NodeId, [], [], CancellationToken.None).AsTask();

            Task firstDisposal = binding.DisposeAsync().AsTask();
            Task secondDisposal = binding.DisposeAsync().AsTask();
            await Task.WhenAll(firstDisposal, secondDisposal).WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            Assert.CatchAsync<OperationCanceledException>(async () => await active.ConfigureAwait(false));
            Assert.CatchAsync<OperationCanceledException>(async () => await queued.ConfigureAwait(false));
            provider.Verify(value => value.StartAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(start.Executable, Is.False);
        }

        /// <summary>
        /// A handler may not be called against a different instance that happens to expose the same method.
        /// </summary>
        [Test]
        public async Task ForeignTargetNeverReachesAuthorityAsync()
        {
            StartDeviceState node = CreateStartDevice();
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            await using var binding = new StartDeviceBinding(node, m_context, provider.Object, AllowAccess());
            MethodState start = node.StartRequest!;
            ServiceResult result = await start.OnCallMethod2Async!(
                m_context, start, new NodeId("different-device", 1), [], [], CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Unsupported scalar index ranges fail without modifying a value or invoking its provider.
        /// </summary>
        [Test]
        public async Task IndexedInterlockWriteIsRejectedAsync()
        {
            StartDeviceState node = CreateStartDevice();
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            await using var binding = new StartDeviceBinding(node, m_context, provider.Object, AllowAccess());
            PropertyState<bool> interlock = node.StartBlockedByClient!;
            AttributeWriteResult result = await interlock.OnWriteValueAsync!(
                m_context, interlock, new NumericRange(0), Variant.From(true), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Result.StatusCode, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
            Assert.That(interlock.Value, Is.False);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Maintenance reset changes only remaining interval/status, not total operating history.
        /// </summary>
        [Test]
        public async Task MaintenanceResetPreservesLifetimeCounterAsync()
        {
            MaintenanceState node = m_context.CreateInstanceOfMaintenanceType(
                Parent(), new QualifiedName("Maintenance", 1));
            node.AddInterval(m_context);
            node.AddRemainingInterval(m_context);
            node.AddTotalOperation(m_context);
            node.Interval!.Value = 1000;
            node.RemainingInterval!.Value = -20;
            node.TotalOperation!.Value = 8020;
            node.Status!.Value = MaintenanceStatusEnumeration.DUE;
            var provider = new Mock<IMaintenanceProvider>(MockBehavior.Strict);
            provider.Setup(value => value.ResetAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            await using var binding = new MaintenanceBinding(node, m_context, provider.Object, AllowAccess());

            ServiceResult result = await node.Reset!.CallAsync(
                m_context, node.NodeId, [], [], [], CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(node.RemainingInterval.Value, Is.EqualTo(1000));
                Assert.That(node.Status.Value, Is.EqualTo(MaintenanceStatusEnumeration.NOT_DUE));
                Assert.That(node.TotalOperation.Value, Is.EqualTo(8020));
            });
        }

        /// <summary>
        /// Releases the test logger after the fixture completes.
        /// </summary>
        [OneTimeTearDown]
        public void TearDownTelemetry()
        {
            if (m_context.Telemetry is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <summary>
        /// Creates a generated StartDevice with all optional control members.
        /// </summary>
        private StartDeviceState CreateStartDevice()
        {
            StartDeviceState node = m_context.CreateInstanceOfStartDeviceType(
                Parent(), new QualifiedName("StartDevice", 1));
            node.AddStartRequest(m_context);
            node.AddStopRequest(m_context);
            node.AddStartBlockedByClient(m_context);
            return node;
        }

        /// <summary>
        /// Creates the application-owned parent from which generated child identities are derived.
        /// </summary>
        private static BaseObjectState Parent()
        {
            return new BaseObjectState(null)
            {
                NodeId = new NodeId("machine", 1),
                BrowseName = new QualifiedName("Machine", 1)
            };
        }

        /// <summary>
        /// Provides an explicit test-only policy; production callers must supply their own policy.
        /// </summary>
        private static IPlasticsRubberAccessPolicy AllowAccess()
        {
            var access = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            access.Setup(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            return access.Object;
        }

        /// <summary>
        /// The generated-model context shared by this sequential fixture.
        /// </summary>
        private readonly SystemContext m_context;
    }
}

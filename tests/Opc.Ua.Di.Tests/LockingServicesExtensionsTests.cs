/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Argument-validation tests for
    /// <see cref="LockingServicesExtensions.BindToLockService"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Locking")]
    public sealed class LockingServicesExtensionsTests
    {
        private static readonly NodeId s_elementId = new("element-1", 2);

        [Test]
        public void BindToLockServiceThrowsOnNullLockState()
        {
            ILockService service = new Mock<ILockService>().Object;

            Assert.Throws<ArgumentNullException>(
                () => LockingServicesExtensions.BindToLockService(
                    lockState: null!,
                    s_elementId,
                    service));
        }

        [Test]
        public void BindToLockServiceThrowsOnNullElementId()
        {
            var lockState = new LockingServicesState(parent: null);
            ILockService service = new Mock<ILockService>().Object;

            Assert.Throws<ArgumentNullException>(
                () => lockState.BindToLockService(NodeId.Null, service));
        }

        [Test]
        public void BindToLockServiceThrowsOnNullService()
        {
            var lockState = new LockingServicesState(parent: null);

            Assert.Throws<ArgumentNullException>(
                () => lockState.BindToLockService(s_elementId, service: null!));
        }

        [Test]
        public void BindToLockServiceIsNoOpWhenAllMethodChildrenAreNull()
        {
            // A freshly-constructed LockingServicesState has all four
            // method children (InitLock/RenewLock/ExitLock/BreakLock) as
            // null until the predefined-node pipeline materialises them.
            // Binding should silently skip each missing method.
            var lockState = new LockingServicesState(parent: null);
            ILockService service = new Mock<ILockService>().Object;

            Assert.That(lockState.InitLock, Is.Null);
            Assert.That(lockState.RenewLock, Is.Null);
            Assert.That(lockState.ExitLock, Is.Null);
            Assert.That(lockState.BreakLock, Is.Null);

            Assert.DoesNotThrow(() => lockState.BindToLockService(s_elementId, service));
        }

        [Test]
        public async Task BindToLockServiceRoutesInitLockToServiceAsync()
        {
            ISystemContext ctx = Mock.Of<ISystemContext>();
            var service = new Mock<ILockService>(MockBehavior.Strict);
            service.Setup(s => s.InitLock(ctx, s_elementId, "tag")).Returns(42);

            var lockState = new LockingServicesState(parent: null)
            {
                InitLock = new InitLockMethodState(parent: null)
            };

            lockState.BindToLockService(s_elementId, service.Object);

            Assert.That(lockState.InitLock!.OnCall, Is.Null);
            InitLockMethodStateResult result = await lockState.InitLock.OnCallAsync!(
                ctx, lockState.InitLock, NodeId.Null, "tag", CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult, Is.EqualTo(ServiceResult.Good));
            Assert.That(result.InitLockStatus, Is.EqualTo(42));
            service.Verify(s => s.InitLock(ctx, s_elementId, "tag"), Times.Once);
        }

        [Test]
        public async Task BindToLockServiceRoutesRenewLockToServiceAsync()
        {
            ISystemContext ctx = Mock.Of<ISystemContext>();
            var service = new Mock<ILockService>(MockBehavior.Strict);
            service.Setup(s => s.RenewLock(ctx, s_elementId)).Returns(7);

            var lockState = new LockingServicesState(parent: null)
            {
                RenewLock = new RenewLockMethodState(parent: null)
            };

            lockState.BindToLockService(s_elementId, service.Object);

            Assert.That(lockState.RenewLock!.OnCall, Is.Null);
            RenewLockMethodStateResult result = await lockState.RenewLock.OnCallAsync!(
                ctx, lockState.RenewLock, NodeId.Null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult, Is.EqualTo(ServiceResult.Good));
            Assert.That(result.RenewLockStatus, Is.EqualTo(7));
            service.Verify(s => s.RenewLock(ctx, s_elementId), Times.Once);
        }

        [Test]
        public async Task BindToLockServiceRoutesExitLockToServiceAsync()
        {
            ISystemContext ctx = Mock.Of<ISystemContext>();
            var service = new Mock<ILockService>(MockBehavior.Strict);
            service.Setup(s => s.ExitLock(ctx, s_elementId)).Returns(13);

            var lockState = new LockingServicesState(parent: null)
            {
                ExitLock = new ExitLockMethodState(parent: null)
            };

            lockState.BindToLockService(s_elementId, service.Object);

            Assert.That(lockState.ExitLock!.OnCall, Is.Null);
            ExitLockMethodStateResult result = await lockState.ExitLock.OnCallAsync!(
                ctx, lockState.ExitLock, NodeId.Null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult, Is.EqualTo(ServiceResult.Good));
            Assert.That(result.ExitLockStatus, Is.EqualTo(13));
            service.Verify(s => s.ExitLock(ctx, s_elementId), Times.Once);
        }

        [Test]
        public async Task BindToLockServiceRoutesBreakLockToServiceAsync()
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(value => value.GrantedRoleIds)
                .Returns([global::Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin]);
            var channel = new SecureChannelContext(
                "test-channel",
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                RequestEncoding.Binary);
            using var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity.Object);
            var ctx = new SessionSystemContext(operation, telemetry: null!);
            var service = new Mock<ILockService>(MockBehavior.Strict);
            service.Setup(s => s.BreakLock(ctx, s_elementId)).Returns(99);

            var lockState = new LockingServicesState(parent: null)
            {
                BreakLock = new BreakLockMethodState(parent: null)
            };

            lockState.BindToLockService(s_elementId, service.Object);

            Assert.That(lockState.BreakLock!.OnCall, Is.Null);
            BreakLockMethodStateResult result = await lockState.BreakLock.OnCallAsync!(
                ctx, lockState.BreakLock, NodeId.Null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult, Is.EqualTo(ServiceResult.Good));
            Assert.That(result.BreakLockStatus, Is.EqualTo(99));
            service.Verify(s => s.BreakLock(ctx, s_elementId), Times.Once);
        }

        [Test]
        public async Task BindToLockServiceRejectsUnauthorizedBreakWithoutCallingLegacyProviderAsync()
        {
            var service = new Mock<ILockService>(MockBehavior.Strict);
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(value => value.GrantedRoleIds).Returns([global::Opc.Ua.ObjectIds.WellKnownRole_Operator]);
            var channel = new SecureChannelContext(
                "test-channel",
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                RequestEncoding.Binary);
            using var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity.Object);
            var context = new SessionSystemContext(operation, telemetry: null!);
            var lockState = new LockingServicesState(parent: null)
            {
                BreakLock = new BreakLockMethodState(parent: null)
            };
            lockState.BindToLockService(s_elementId, service.Object);
            BreakLockMethodStateResult result = await lockState.BreakLock!.OnCallAsync!(
                context, lockState.BreakLock, NodeId.Null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            service.VerifyNoOtherCalls();
        }

        [Test]
        public void LegacyProviderDoesNotRequireOwnershipCapability()
        {
            ILockService service = new Mock<ILockService>().Object;
            Assert.That(service, Is.Not.InstanceOf<ILockOwnershipService>());
            var state = new LockingServicesState(parent: null);
            Assert.DoesNotThrow(() => state.BindToLockService(s_elementId, service));
        }

        [Test]
        public void BindToLockServiceBindsOnlyMethodsThatExistOnLockState()
        {
            // Mixed scenario: only InitLock + ExitLock are attached.
            // The other two must remain null after binding (i.e. no
            // method-state is fabricated by BindToLockService).
            ISystemContext ctx = Mock.Of<ISystemContext>();
            var service = new Mock<ILockService>();
            service.Setup(s => s.InitLock(It.IsAny<ISystemContext>(),
                It.IsAny<NodeId>(), It.IsAny<string>())).Returns(1);
            service.Setup(s => s.ExitLock(It.IsAny<ISystemContext>(),
                It.IsAny<NodeId>())).Returns(2);

            var lockState = new LockingServicesState(parent: null)
            {
                InitLock = new InitLockMethodState(parent: null),
                ExitLock = new ExitLockMethodState(parent: null)
            };

            lockState.BindToLockService(s_elementId, service.Object);

            Assert.That(lockState.InitLock!.OnCallAsync, Is.Not.Null);
            Assert.That(lockState.ExitLock!.OnCallAsync, Is.Not.Null);
            Assert.That(lockState.RenewLock, Is.Null);
            Assert.That(lockState.BreakLock, Is.Null);
        }

        [Test]
        public async Task RefreshLockStateAsyncDoesNotBlockOnAsyncNotifications()
        {
            var service = new Mock<ILockService>(MockBehavior.Strict);
            service.Setup(value => value.GetState(s_elementId))
                .Returns(new LockState(true, "client", "user", 1));
            var state = CreateLockingStateWithProperties();
            ISystemContext context = Mock.Of<ISystemContext>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Locked!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };

            Task<ValueTask> invocation = Task.Run(
                () => state.RefreshLockStateAsync(service.Object, s_elementId, context));
            await WaitForNotificationAsync(entered.Task, release, invocation).ConfigureAwait(false);
            bool returnedBeforeNotificationCompleted = invocation.IsCompleted;
            ValueTask refresh = default;
            bool refreshCaptured = false;
            if (returnedBeforeNotificationCompleted)
            {
                refresh = await invocation.ConfigureAwait(false);
                refreshCaptured = true;
            }
            release.TrySetResult(true);
            if (!refreshCaptured)
            {
                refresh = await invocation.ConfigureAwait(false);
            }
            bool refreshPending = refreshCaptured && !refresh.IsCompleted;
            await refresh.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(returnedBeforeNotificationCompleted, Is.True);
                Assert.That(refreshPending, Is.True);
                Assert.That(state.Locked!.Value, Is.True);
            });
        }

        [Test]
        public async Task InitLockAsyncDoesNotBlockOnAsyncNotifications()
        {
            ISystemContext context = Mock.Of<ISystemContext>();
            var service = new Mock<ILockService>(MockBehavior.Strict);
            service.Setup(value => value.InitLock(context, s_elementId, "tag")).Returns(42);
            service.Setup(value => value.GetState(s_elementId))
                .Returns(new LockState(true, "client", "user", 1));
            var state = CreateLockingStateWithProperties();
            var initLock = new InitLockMethodState(state);
            state.InitLock = initLock;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Locked!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };
            state.BindToLockService(s_elementId, service.Object);

            Task<ValueTask<InitLockMethodStateResult>> invocation = Task.Run(
                () => initLock.OnCallAsync!(
                    context, initLock, NodeId.Null, "tag", CancellationToken.None));
            await WaitForNotificationAsync(entered.Task, release, invocation).ConfigureAwait(false);
            bool returnedBeforeNotificationCompleted = invocation.IsCompleted;
            ValueTask<InitLockMethodStateResult> call = default;
            bool callCaptured = false;
            if (returnedBeforeNotificationCompleted)
            {
                call = await invocation.ConfigureAwait(false);
                callCaptured = true;
            }
            release.TrySetResult(true);
            if (!callCaptured)
            {
                call = await invocation.ConfigureAwait(false);
            }
            bool callPending = callCaptured && !call.IsCompleted;
            InitLockMethodStateResult result = await call.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(returnedBeforeNotificationCompleted, Is.True);
                Assert.That(callPending, Is.True);
                Assert.That(result.ServiceResult, Is.EqualTo(ServiceResult.Good));
                Assert.That(result.InitLockStatus, Is.EqualTo(42));
            });
        }

        /// <summary>
        /// Creates a locking state with all monitored lock properties attached.
        /// </summary>
        private static LockingServicesState CreateLockingStateWithProperties()
        {
            var state = new LockingServicesState(parent: null);
            state.Locked = PropertyState<bool>.With<VariantBuilder>(state);
            state.LockingClient = PropertyState<string>.With<VariantBuilder>(state);
            state.LockingUser = PropertyState<string>.With<VariantBuilder>(state);
            state.RemainingLockTime = PropertyState<double>.With<VariantBuilder>(state);
            return state;
        }

        /// <summary>
        /// Waits for a notification to start and releases the invocation if it does not.
        /// </summary>
        private static async Task WaitForNotificationAsync<T>(
            Task notificationEntered,
            TaskCompletionSource<bool> release,
            Task<T> invocation)
        {
            Task completed = await Task.WhenAny(notificationEntered, Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);
            if (completed != notificationEntered)
            {
                release.TrySetResult(true);
                await invocation.ConfigureAwait(false);
                Assert.Fail("The asynchronous state notification did not start.");
            }

            completed = await Task.WhenAny(invocation, Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);
            if (completed != invocation)
            {
                release.TrySetResult(true);
                await invocation.ConfigureAwait(false);
                Assert.Fail("The operation blocked until its asynchronous state notification completed.");
            }
        }

        /// <summary>
        /// Proves metadata reads and monitored refreshes reflect expiry without renewing the lease.
        /// </summary>
        [Test]
        public async Task LockMetadataUsesAuthoritativeLeaseAndMillisecondDuration()
        {
            var service = new Mock<ILockService>(MockBehavior.Strict);
            LockState current = new(true, "client", "user", 2.5);
            service.Setup(value => value.GetState(s_elementId)).Returns(() => current);
            var state = new LockingServicesState(null);
            state.Locked = PropertyState<bool>.With<VariantBuilder>(state);
            state.LockingClient = PropertyState<string>.With<VariantBuilder>(state);
            state.LockingUser = PropertyState<string>.With<VariantBuilder>(state);
            state.RemainingLockTime = PropertyState<double>.With<VariantBuilder>(state);
            ISystemContext context = Mock.Of<ISystemContext>();
            state.BindToLockService(s_elementId, service.Object);

            AttributeSimpleReadResult read = await state.RemainingLockTime.OnSimpleReadValueAsync!(
                context, state.RemainingLockTime, CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Value.TryGetValue(out double remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(2500));

            await state.RefreshLockStateAsync(service.Object, s_elementId, context).ConfigureAwait(false);
            Assert.That(state.Locked.Value, Is.True);
            Assert.That(state.LockingClient.Value, Is.EqualTo("client"));
            Assert.That(state.LockingUser.Value, Is.EqualTo("user"));
            Assert.That(state.RemainingLockTime.Value, Is.EqualTo(2500));

            current = new LockState(false, string.Empty, string.Empty, 0);
            await state.RefreshLockStateAsync(service.Object, s_elementId, context).ConfigureAwait(false);
            Assert.That(state.Locked.Value, Is.False);
            Assert.That(state.LockingClient.Value, Is.Empty);
            Assert.That(state.LockingUser.Value, Is.Empty);
            Assert.That(state.RemainingLockTime.Value, Is.Zero);
            service.Verify(value => value.GetState(s_elementId), Times.Exactly(3));
            service.VerifyNoOtherCalls();
        }
    }
}

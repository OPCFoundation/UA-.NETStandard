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
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.ECM.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using static Opc.Ua.ECM.Server.EcmModelNodes;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Exercises WOL-only callbacks without a standby graph or any physical power operation.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmWolBindingTests
    {
        /// <summary>
        /// An incomplete generated graph cannot publish partial metadata or retain a control callback.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void MissingWolChildDoesNotPublishMetadataOrInstallControlHandler(int missingChild)
        {
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            var packet = node.WOLMagicPacket ?? throw new InvalidOperationException("Missing packet node.");
            var method = node.SwitchOffWOL ?? throw new InvalidOperationException("Missing method node.");
            packet.Value = ByteString.From(new byte[] { 9, 8, 7, 6, 5, 4 }.AsSpan());
            switch (missingChild)
            {
                case 0:
                    node.WOLMagicPacket = null;
                    break;
                case 1:
                    node.RegularTimeToOperate = null;
                    break;
                case 2:
                    node.TimeMinPause = null;
                    break;
                case 3:
                    node.ModePowerConsumption = null;
                    break;
                case 4:
                    node.SwitchOffWOL = null;
                    break;
            }
            Mock<IEcmWolProvider> provider = CreateProvider();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                new EcmWolBinding(context, node, provider.Object))!;

            Assert.That(error.Message, Does.StartWith("The ECM graph is missing a required "));
            Assert.That(packet.Value.ToArray(), Is.EqualTo(new byte[] { 9, 8, 7, 6, 5, 4 }));
            Assert.That(method.OnCallAsync, Is.Null);
            provider.Verify(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The standalone graph exposes exact MAC metadata and independent Call and device status.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task StandaloneWolRetainsMetadataAndControlStatus(int scenario)
        {
            uint status = scenario switch
            {
                0 => StatusCodes.Good,
                1 => StatusCodes.Uncertain,
                _ => StatusCodes.BadUserAccessDenied
            };
            byte code = scenario == 1 ? (byte)0x54 : (byte)0;
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            var result = StatusCode.IsBad(status) ?
                new EcmControlResult(status, null) :
                new EcmControlResult(status, code, StatusCode.IsGood(status) ? (byte)0xFE : (byte)0,
                    StatusCode.IsGood(status) ? 1000 : 0,
                    StatusCode.IsGood(status) ? 2000 : 0,
                    StatusCode.IsGood(status) ? 3000 : 0);
            provider.Setup(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<EcmControlResult>(result));
            await using var binding = new EcmWolBinding(context, node, provider.Object,
                authorize: static (_, _) => new ValueTask<bool>(true));

            var call = await Required(node.SwitchOffWOL).OnCallAsync!(
                context, Required(node.SwitchOffWOL), node.NodeId, CancellationToken.None);

            Assert.That(call.ServiceResult.StatusCode, Is.EqualTo(new StatusCode(status)));
            Assert.That(call.ReturnCode, Is.EqualTo(code));
            Assert.That(call.ModeID, Is.EqualTo(result.ModeId));
            Assert.That(call.CurrentTimeToDestination, Is.EqualTo(result.CurrentTimeToDestination));
            Assert.That(call.RegularTimeToOperate, Is.EqualTo(result.RegularTimeToOperate));
            Assert.That(call.TimeMinLengthOfStay, Is.EqualTo(result.TimeMinLengthOfStay));
            Assert.That(Required(node.WOLMagicPacket).Value.ToArray(), Is.EqualTo(new byte[] { 2, 3, 4, 5, 6, 7 }));
            Assert.That(Required(node.RegularTimeToOperate).Value, Is.EqualTo(2000));
            Assert.That(Required(node.TimeMinPause).Value, Is.EqualTo(6000));
            Assert.That(Required(node.ModePowerConsumption).Value, Is.EqualTo(3u));
            Assert.That(binding.PowerOff, Is.SameAs(node));
            Assert.That(node.Parent, Is.Null);
            provider.Verify(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// The default role policy denies an unidentified caller without invoking the provider.
        /// </summary>
        [Test]
        public async Task MissingRoleDoesNotInvokeWolProvider()
        {
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            await using var binding = new EcmWolBinding(context, node, provider.Object);

            var call = await Required(node.SwitchOffWOL).OnCallAsync!(
                context, Required(node.SwitchOffWOL), node.NodeId, CancellationToken.None);

            Assert.That(call.ServiceResult.StatusCode,
                Is.EqualTo(new StatusCode(StatusCodes.BadUserAccessDenied)));
            provider.Verify(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Standalone WOL checks session ownership for its own resource without renewing the DI lease.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StandaloneWolChecksItsOwnResourceOwnership(bool owns)
        {
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            provider.Setup(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<EcmControlResult>(new EcmControlResult(StatusCodes.Good, 0, 0xFE)));
            var locks = new Mock<ILockService>(MockBehavior.Strict);
            Mock<ILockOwnershipService> ownership = locks.As<ILockOwnershipService>();
            ownership.Setup(value => value.IsLockOwner(context, node.NodeId)).Returns(owns);
            await using var binding = new EcmWolBinding(context, node, provider.Object, locks.Object,
                static (_, _) => new ValueTask<bool>(true));

            var call = await Required(node.SwitchOffWOL).OnCallAsync!(
                context, Required(node.SwitchOffWOL), node.NodeId, CancellationToken.None);

            Assert.That(call.ServiceResult.StatusCode, Is.EqualTo(new StatusCode(
                owns ? StatusCodes.Good : StatusCodes.BadUserAccessDenied)));
            ownership.Verify(value => value.IsLockOwner(context, node.NodeId), Times.Once);
            ownership.VerifyNoOtherCalls();
            locks.VerifyNoOtherCalls();
            provider.Verify(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()),
                owns ? Times.Once() : Times.Never());
        }

        /// <summary>
        /// A failed provider does not retain the serialization gate or fabricate accepted outputs.
        /// </summary>
        [Test]
        public async Task ProviderFailureReleasesOperationForNextCommand()
        {
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            provider.SetupSequence(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Device unavailable."))
                .Returns(new ValueTask<EcmControlResult>(new EcmControlResult(StatusCodes.Good, 0, 0xFE)));
            await using var binding = new EcmWolBinding(context, node, provider.Object,
                authorize: static (_, _) => new ValueTask<bool>(true));

            var failed = await Required(node.SwitchOffWOL).OnCallAsync!(
                context, Required(node.SwitchOffWOL), node.NodeId, CancellationToken.None);
            var accepted = await Required(node.SwitchOffWOL).OnCallAsync!(
                context, Required(node.SwitchOffWOL), node.NodeId, CancellationToken.None);

            Assert.That(failed.ServiceResult.StatusCode, Is.EqualTo(new StatusCode(StatusCodes.BadUnexpectedError)));
            Assert.That(failed.ModeID, Is.Zero);
            Assert.That(failed.CurrentTimeToDestination, Is.Zero);
            Assert.That(accepted.ServiceResult.StatusCode, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(accepted.ModeID, Is.EqualTo(0xFE));
            provider.Verify(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// Caller cancellation is observed and releases admission and serialization for later commands.
        /// </summary>
        [Test]
        public async Task CallerCancellationReleasesOperationForNextCommand()
        {
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            provider.Setup(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => ReadAsync(token));
            await using var binding = new EcmWolBinding(context, node, provider.Object,
                authorize: static (_, _) => new ValueTask<bool>(true));
            using var cancellation = new CancellationTokenSource();
            var pending = Required(node.SwitchOffWOL).OnCallAsync!(
                context, Required(node.SwitchOffWOL), node.NodeId, cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                cancellation.Cancel();
            }
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(10)));

            var accepted = await Required(node.SwitchOffWOL).OnCallAsync!(
                context, Required(node.SwitchOffWOL), node.NodeId, CancellationToken.None);

            Assert.That(accepted.ServiceResult.StatusCode, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(accepted.ModeID, Is.EqualTo(0xFE));
            Assert.That(calls, Is.EqualTo(2));

            async ValueTask<EcmControlResult> ReadAsync(CancellationToken token)
            {
                if (++calls == 1)
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                }
                return new EcmControlResult(StatusCodes.Good, 0, 0xFE);
            }
        }

        /// <summary>
        /// Cancellation observed during authorization prevents a subsequently permitted physical request.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationDuringAuthorizationDoesNotInvokeWolProvider(bool authorized)
        {
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            using var cancellation = new CancellationTokenSource();
            int checks = 0;
            await using var binding = new EcmWolBinding(context, node, provider.Object,
                authorize: (_, _) =>
                {
                    checks++;
                    cancellation.Cancel();
                    return new ValueTask<bool>(authorized);
                });

            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await Required(node.SwitchOffWOL).OnCallAsync!(
                    context, Required(node.SwitchOffWOL), node.NodeId, cancellation.Token));

            Assert.That(checks, Is.EqualTo(1));
            provider.Verify(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The provider must report a WOL destination on acceptance and no destination on rejection.
        /// </summary>
        [TestCase(false, (byte)0)]
        [TestCase(false, (byte)1)]
        [TestCase(false, (byte)0xFF)]
        [TestCase(true, (byte)1)]
        [TestCase(true, (byte)0xFE)]
        public async Task IncorrectDestinationReportsProviderError(bool uncertain, byte mode)
        {
            uint status = uncertain ? StatusCodes.Uncertain : StatusCodes.Good;
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            provider.Setup(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<EcmControlResult>(new EcmControlResult(
                    status, StatusCode.IsGood(status) ? (byte)0 : (byte)0x54, mode)));
            await using var binding = new EcmWolBinding(context, node, provider.Object,
                authorize: static (_, _) => new ValueTask<bool>(true));

            var call = await Required(node.SwitchOffWOL).OnCallAsync!(
                context, Required(node.SwitchOffWOL), node.NodeId, CancellationToken.None);

            Assert.That(call.ServiceResult.StatusCode, Is.EqualTo(new StatusCode(StatusCodes.BadUnexpectedError)));
            Assert.That(call.ModeID, Is.Zero);
            Assert.That(call.CurrentTimeToDestination, Is.Zero);
            provider.Verify(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Disposal drains an uncooperative active provider, cancels queued work and rejects new operations.
        /// </summary>
        [Test]
        public async Task DisposalDrainsActiveAndQueuedWolCommands()
        {
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<EcmControlResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            provider.Setup(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask<EcmControlResult>(release.Task);
                });
            await using var binding = new EcmWolBinding(context, node, provider.Object,
                authorize: static (_, _) => new ValueTask<bool>(true));
            var method = Required(node.SwitchOffWOL);
            var callback = method.OnCallAsync!;
            var first = callback(context, method, node.NodeId, CancellationToken.None).AsTask();
            Task? stopping = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var queued = callback(context, method, node.NodeId, CancellationToken.None).AsTask();
                stopping = binding.DisposeAsync().AsTask();
                Assert.That(stopping.IsCompleted, Is.False);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await queued.WaitAsync(TimeSpan.FromSeconds(10)));
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await callback(context, method, node.NodeId, CancellationToken.None));
            }
            finally
            {
                release.TrySetResult(new EcmControlResult(StatusCodes.Good, 0, 0xFE));
                await first.WaitAsync(TimeSpan.FromSeconds(10));
                if (stopping != null)
                {
                    await stopping.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            Assert.That(method.OnCallAsync, Is.Null);
            provider.Verify(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Invalid metadata does not install a callback on an unreturned binding.
        /// </summary>
        [TestCase(0)]
        [TestCase(5)]
        [TestCase(7)]
        public void InvalidMacDoesNotInstallCallback(int length)
        {
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            provider.SetupGet(value => value.MacAddress).Returns(ByteString.From(new byte[length].AsSpan()));

            Assert.Throws<ArgumentException>(() => new EcmWolBinding(context, node, provider.Object));

            Assert.That(Required(node.SwitchOffWOL).OnCallAsync, Is.Null);
            provider.Verify(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Nonfinite or negative WOL durations cannot be published as valid provider metadata.
        /// </summary>
        [TestCase(false, -1d)]
        [TestCase(false, double.NaN)]
        [TestCase(false, double.PositiveInfinity)]
        [TestCase(false, double.NegativeInfinity)]
        [TestCase(true, -1d)]
        [TestCase(true, double.NaN)]
        [TestCase(true, double.PositiveInfinity)]
        [TestCase(true, double.NegativeInfinity)]
        public void InvalidDurationDoesNotInstallCallback(bool recovery, double value)
        {
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            if (recovery)
            {
                provider.SetupGet(item => item.RegularTimeToOperate).Returns(value);
            }
            else
            {
                provider.SetupGet(item => item.TimeMinPause).Returns(value);
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => new EcmWolBinding(context, node, provider.Object));

            Assert.That(Required(node.SwitchOffWOL).OnCallAsync, Is.Null);
            provider.Verify(item => item.SwitchOffWolAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Zero durations and the maximum UInt32 power retain the model's full metadata representation.
        /// </summary>
        [Test]
        public async Task ZeroDurationsAndMaximumPowerAreRepresentable()
        {
            SystemContext context = CreateContext();
            EnergyDevicePowerOffState node = CreateNode(context);
            Mock<IEcmWolProvider> provider = CreateProvider();
            provider.SetupGet(value => value.RegularTimeToOperate).Returns(0);
            provider.SetupGet(value => value.TimeMinPause).Returns(0);
            provider.SetupGet(value => value.ModePowerConsumption).Returns(uint.MaxValue);
            await using var binding = new EcmWolBinding(context, node, provider.Object);

            Assert.That(Required(node.RegularTimeToOperate).Value, Is.Zero);
            Assert.That(Required(node.TimeMinPause).Value, Is.Zero);
            Assert.That(Required(node.ModePowerConsumption).Value, Is.EqualTo(uint.MaxValue));
            provider.Verify(value => value.SwitchOffWolAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Supplies only metadata; a strict mock rejects any unconfigured control.
        /// </summary>
        private static Mock<IEcmWolProvider> CreateProvider()
        {
            var provider = new Mock<IEcmWolProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.MacAddress)
                .Returns(ByteString.From(new byte[] { 2, 3, 4, 5, 6, 7 }.AsSpan()));
            provider.SetupGet(value => value.RegularTimeToOperate).Returns(2000);
            provider.SetupGet(value => value.TimeMinPause).Returns(6000);
            provider.SetupGet(value => value.ModePowerConsumption).Returns(3);
            return provider;
        }

        /// <summary>
        /// Creates a generated WOL object with no standby parent.
        /// </summary>
        private static EnergyDevicePowerOffState CreateNode(SystemContext context)
        {
            return context.CreateInstanceOfEnergyDevicePowerOffType(null, new QualifiedName("Sleep", 4));
        }

        /// <summary>
        /// Supplies codecs and deterministic instance identifiers without a server or sockets.
        /// </summary>
        private static SystemContext CreateContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var message = ServiceMessageContext.Create(telemetry);
            message.NamespaceUris.Append(Di.Namespaces.OpcUaDi);
            message.NamespaceUris.Append(IA.Namespaces.IA);
            message.NamespaceUris.Append(Namespaces.ECM);
            ushort instances = message.NamespaceUris.GetIndexOrAppend("urn:ecm-tests:wol-only");
            message.Factory.Builder.AddOpcUaECM().Commit();
            return new SystemContext(telemetry)
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory,
                NodeIdFactory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, instances)
            };
        }
    }
}

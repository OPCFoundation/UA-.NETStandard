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
using Opc.Ua.PADIM.Server;

namespace Opc.Ua.PADIM.Tests
{
    public sealed partial class PadimRuntimeTests
    {
        /// <summary>
        /// Cancelling a queued operation leaves the active request running and never calls its authorizer or provider.
        /// </summary>
        [Test]
        public async Task CancelledQueuedOperationDoesNotInterruptTheActiveProvider()
        {
            PadimDevice device = await CreateDeviceAsync("QueuedOperationCancellation");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IPadimOperationProvider>(MockBehavior.Strict);
            provider.Setup(p => p.FactoryResetAsync(ResetModeEnum.Application, It.IsAny<CancellationToken>()))
                .Returns(async (ResetModeEnum _, CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct);
                    return StatusCodes.Good;
                });
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, It.IsAny<NodeId>(), "FactoryReset", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            device.WithFactoryReset(provider.Object, authorizer.Object);
            MethodState method = device.State.FactoryReset!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var queuedCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            Task<ServiceResult> active = InvokeAsync(timeout.Token);
            try
            {
                await entered.Task.WaitAsync(timeout.Token);
                Task<ServiceResult> queued = InvokeAsync(queuedCancellation.Token);
                Assert.That(queued.IsCompleted, Is.False);
                await queuedCancellation.CancelAsync();

                await Assert.CatchAsync<OperationCanceledException>(async () => await queued);

                Assert.That(active.IsCompleted, Is.False);
                provider.Verify(p => p.FactoryResetAsync(
                    ResetModeEnum.Application, It.IsAny<CancellationToken>()), Times.Once);
                authorizer.Verify(p => p.AuthorizeAsync(
                    device.Context, method.NodeId, "FactoryReset", It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                await active;
            }
            Assert.That((await active).StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That((await InvokeAsync(timeout.Token)).StatusCode, Is.EqualTo(StatusCodes.Good));
            provider.Verify(p => p.FactoryResetAsync(
                ResetModeEnum.Application, It.IsAny<CancellationToken>()), Times.Exactly(2));
            authorizer.Verify(p => p.AuthorizeAsync(
                device.Context, method.NodeId, "FactoryReset", It.IsAny<CancellationToken>()), Times.Exactly(2));

            async Task<ServiceResult> InvokeAsync(CancellationToken ct)
            {
                return await method.OnCallMethod2Async!(
                    device.Context, method, device.State.NodeId, [Variant.From(1)], [], ct);
            }
        }

        /// <summary>
        /// A provider exception reaches the caller and releases serialization for the next operation.
        /// </summary>
        [Test]
        public async Task FailedOperationReleasesTheProviderPipelineForTheNextRequest()
        {
            PadimDevice device = await CreateDeviceAsync("FailedOperationRecovery");
            var provider = new Mock<IPadimOperationProvider>(MockBehavior.Strict);
            provider.SetupSequence(p => p.FactoryResetAsync(ResetModeEnum.Application, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("The device rejected its current operation state."))
                .ReturnsAsync(StatusCodes.BadInvalidState);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, It.IsAny<NodeId>(), "FactoryReset", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            device.WithFactoryReset(provider.Object, authorizer.Object);
            MethodState method = device.State.FactoryReset!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await method.OnCallMethod2Async!(
                    device.Context, method, device.State.NodeId, [Variant.From(1)], [], timeout.Token));

            Assert.That(error!.Message, Is.EqualTo("The device rejected its current operation state."));
            ServiceResult result = await method.OnCallMethod2Async!(
                device.Context, method, device.State.NodeId, [Variant.From(1)], [], timeout.Token);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            provider.Verify(p => p.FactoryResetAsync(
                ResetModeEnum.Application, It.IsAny<CancellationToken>()), Times.Exactly(2));
            authorizer.Verify(p => p.AuthorizeAsync(
                device.Context, method.NodeId, "FactoryReset", It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// Rejects missing, excessive and incorrectly typed operation arguments before calling a physical provider.
        /// </summary>
        [TestCase("reset", "missing")]
        [TestCase("reset", "extra")]
        [TestCase("reset", "type")]
        [TestCase("positioner", "missing")]
        [TestCase("positioner", "extra")]
        [TestCase("positioner", "type")]
        [TestCase("zero", "extra")]
        public async Task InvalidOperationArgumentShapesDoNotInvokeProvider(string operation, string shape)
        {
            PadimDevice device = await CreateDeviceAsync($"OperationShape{operation}{shape}");
            var provider = new Mock<IPadimOperationProvider>(MockBehavior.Strict);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            MethodState method;
            NodeId owner;
            switch (operation)
            {
                case "reset":
                    device.WithFactoryReset(provider.Object, authorizer.Object);
                    method = device.State.FactoryReset!;
                    owner = device.State.NodeId;
                    break;
                case "positioner":
                    var control = (ControlSignalObjectState)await device.AddSignalAsync(
                        Name("Position"), PadimSignalKind.Control, "CV-SHAPE");
                    device.WithAutoAdjustPositioner(control, "position", provider.Object, authorizer.Object);
                    method = control.AutoAdjustPositioner!;
                    owner = control.NodeId;
                    break;
                default:
                    var analog = (AnalogSignalObjectState)await device.AddSignalAsync(
                        Name("Measurement"), PadimSignalKind.Analog, "TT-SHAPE");
                    device.WithZeroPointAdjustment(analog, "measurement", provider.Object, authorizer.Object);
                    method = analog.ZeroPointAdjustment!;
                    owner = analog.NodeId;
                    break;
            }
            ArrayOf<Variant> inputs = shape switch
            {
                "missing" => [],
                "type" => [Variant.From("Start")],
                _ => [Variant.From(1), Variant.From(2)]
            };

            ServiceResult result = await method.OnCallMethod2Async!(
                device.Context, method, owner, inputs, [], CancellationToken.None);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            provider.VerifyNoOtherCalls();
            authorizer.Verify(p => p.AuthorizeAsync(
                device.Context, method.NodeId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}

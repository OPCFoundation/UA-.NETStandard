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

namespace Opc.Ua.PlasticsRubber.Runtime.Tests
{
    public sealed partial class CommonBindingTests
    {
        /// <summary>
        /// Setting and clearing a composed MES message preserve severity rules and commit ordering.
        /// </summary>
        [Test]
        public async Task ComposedMesMessageUsesExactSetAndClearSemanticsAsync()
        {
            MachineMESStatusState node = m_context.CreateInstanceOfMachineMESStatusType(
                Parent(), new QualifiedName("Mes", 1));
            var provider = new Mock<IMesMessageProvider>(MockBehavior.Strict);
            provider.Setup(value => value.SetMessageAsync(
                It.IsAny<PlasticsRubberRequestContext>(), "id", "message", 667, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            provider.Setup(value => value.ClearMessageAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            await using var binding = new MesMessageBinding(
                node, node.MESMessage!, node.SetMESMessage, node.ClearMESMessage, m_context, provider.Object,
                    AllowAccess());

            ServiceResult result = await node.SetMESMessage!.CallAsync(
                m_context, node.NodeId,
                [Variant.From("id"), Variant.From("message"), Variant.From((ushort)667)], [], [])
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(node.MESMessage!.Id!.Value, Is.EqualTo("id"));
                Assert.That(node.MESMessage.Message!.Value, Is.EqualTo("message"));
                Assert.That(node.MESMessage.Severity!.Value, Is.EqualTo(667));
            });
            result = await node.ClearMESMessage!.CallAsync(m_context, node.NodeId, [], [], []).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(node.MESMessage!.Id!.Value, Is.Empty);
                Assert.That(node.MESMessage.Message!.Value, Is.Empty);
                Assert.That(node.MESMessage.Severity!.Value, Is.Zero);
            });
            provider.VerifyAll();
        }

        /// <summary>
        /// Invalid event severities never reach the device authority.
        /// </summary>
        [TestCase((ushort)0)]
        [TestCase((ushort)1001)]
        public async Task InvalidMesSeverityIsRejectedAsync(ushort severity)
        {
            MachineMESStatusState node = m_context.CreateInstanceOfMachineMESStatusType(
                Parent(), new QualifiedName("Mes", 1));
            var provider = new Mock<IMesMessageProvider>(MockBehavior.Strict);
            await using var binding = new MesMessageBinding(node, m_context, provider.Object, AllowAccess());
            ServiceResult result = await node.SetMESMessage!.CallAsync(
                m_context, node.NodeId, [Variant.From("id"), Variant.From("text"), Variant.From(severity)], [], [])
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Watchdog operations verify the controlling MES before changing its authoritative deadline.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(60)]
        public async Task WatchdogDelegatesOnlyAfterControlOwnershipAsync(int seconds)
        {
            ProductionControlState node = m_context.CreateInstanceOfProductionControlType(
                Parent(), new QualifiedName("Production", 1));
            node.AddSetWatchDogTime(m_context);
            node.AddResetWatchDog(m_context);
            var provider = new Mock<IProductionControlProvider>(MockBehavior.Strict);
            var sequence = new MockSequence();
            provider.InSequence(sequence).Setup(value => value.EnsureMesControlAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            provider.InSequence(sequence).Setup(value => value.SetWatchdogTimeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), seconds, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            provider.InSequence(sequence).Setup(value => value.EnsureMesControlAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            provider.InSequence(sequence).Setup(value => value.ResetWatchdogAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            await using var binding = new ProductionControlBinding(node, m_context, provider.Object, AllowAccess());
            SetWatchDogTimeMethodStateResult set = await node.SetWatchDogTime!.OnCallAsync!(
                m_context, node.SetWatchDogTime, node.NodeId, seconds, CancellationToken.None).ConfigureAwait(false);
            ServiceResult reset = await node.ResetWatchDog!.CallAsync(
                m_context, node.NodeId, [], [], []).ConfigureAwait(false);
            Assert.That(set.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(reset.StatusCode, Is.EqualTo(StatusCodes.Good));
            provider.VerifyAll();
        }

        /// <summary>
        /// Values below the disable sentinel are invalid, not silently clamped.
        /// </summary>
        [Test]
        public async Task InvalidWatchdogIntervalDoesNotAcquireControlAsync()
        {
            ProductionControlState node = m_context.CreateInstanceOfProductionControlType(
                Parent(), new QualifiedName("Production", 1));
            node.AddSetWatchDogTime(m_context);
            var provider = new Mock<IProductionControlProvider>(MockBehavior.Strict);
            await using var binding = new ProductionControlBinding(node, m_context, provider.Object, AllowAccess());
            SetWatchDogTimeMethodStateResult result = await node.SetWatchDogTime!.OnCallAsync!(
                m_context, node.SetWatchDogTime, node.NodeId, -2, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A denied MES controller cannot reset another controller's watchdog.
        /// </summary>
        [Test]
        public async Task ForeignMesCannotResetWatchdogAsync()
        {
            ProductionControlState node = m_context.CreateInstanceOfProductionControlType(
                Parent(), new QualifiedName("Production", 1));
            node.AddResetWatchDog(m_context);
            var provider = new Mock<IProductionControlProvider>(MockBehavior.Strict);
            provider.Setup(value => value.EnsureMesControlAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                .Throws(ServiceResultException.Create(StatusCodes.BadUserAccessDenied,
                    "A different MES owns control."));
            await using var binding = new ProductionControlBinding(node, m_context, provider.Object, AllowAccess());
            ServiceResult result = await node.ResetWatchDog!.CallAsync(
                m_context, node.NodeId, [], [], []).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            provider.Verify(value => value.ResetWatchdogAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Active job requests do not fabricate job status or increment any production counter.
        /// </summary>
        [Test]
        public async Task ActiveJobRequestsPreserveObservedStateAsync()
        {
            ActiveCyclicJobValuesState node = m_context.CreateInstanceOfActiveCyclicJobValuesType(
                Parent(), new QualifiedName("Job", 1));
            node.JobStatus!.Value = JobStatusEnumeration.SET_UP_FINISHED;
            node.JobCycleCounter!.Value = 9;
            var provider = new Mock<IActiveJobProvider>(MockBehavior.Strict);
            provider.Setup(value => value.ExecuteAsync(
                It.IsAny<PlasticsRubberRequestContext>(), ActiveJobOperation.Start, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            await using var binding = new ActiveJobBinding(node, m_context, provider.Object, AllowAccess());
            ServiceResult result = await node.StartJob!.CallAsync(
                m_context, node.NodeId, [], [], []).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(node.JobStatus.Value, Is.EqualTo(JobStatusEnumeration.SET_UP_FINISHED));
                Assert.That(node.JobCycleCounter.Value, Is.EqualTo(9));
            });
        }

        /// <summary>
        /// Production release prevents a job start before the provider sees the request.
        /// </summary>
        [Test]
        public async Task UnreleasedProductionRejectsJobStartAsync()
        {
            ActiveJobValuesState node = m_context.CreateInstanceOfActiveJobValuesType(
                Parent(), new QualifiedName("Job", 1));
            ProductionControlState control = m_context.CreateInstanceOfProductionControlType(
                Parent(), new QualifiedName("Control", 1));
            control.ProductionReleasedByMES!.Value = false;
            var provider = new Mock<IActiveJobProvider>(MockBehavior.Strict);
            await using var binding = new ActiveJobBinding(node, m_context, provider.Object, AllowAccess(), control);
            ServiceResult result = await node.StartJob!.CallAsync(
                m_context, node.NodeId, [], [], []).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Machine time and timezone are forwarded together without changing the host's clock.
        /// </summary>
        [Test]
        public async Task MachineClockIsAnAuthorizedAtomicProviderOperationAsync()
        {
            MachineConfigurationState node = m_context.CreateInstanceOfMachineConfigurationType(
                Parent(), new QualifiedName("Configuration", 1));
            DateTimeUtc time = (DateTimeUtc)new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var zone = new TimeZoneDataType { Offset = 120, DaylightSavingInOffset = true };
            var provider = new Mock<IMachineConfigurationProvider>(MockBehavior.Strict);
            provider.Setup(value => value.SetMachineTimeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), time, zone, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            await using var binding = new MachineConfigurationBinding(node, m_context, provider.Object, AllowAccess());
            SetMachineTimeMethodStateResult result = await node.SetMachineTime!.OnCallAsync!(
                m_context, node.SetMachineTime, node.NodeId, time, zone, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(node.TimeZoneOffset!.Value, Is.SameAs(zone));
            provider.VerifyAll();
        }

        /// <summary>
        /// Missing page data and oversized pages are explicit protocol errors.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(5, false)]
        [TestCase(4, true)]
        public async Task PageImagesRespectConfiguredBoundsAsync(int length, bool accepted)
        {
            MachineConfigurationState node = m_context.CreateInstanceOfMachineConfigurationType(
                Parent(), new QualifiedName("Configuration", 1));
            node.AddGetPage(m_context);
            var provider = new Mock<IMachineConfigurationProvider>(MockBehavior.Strict);
            provider.Setup(value => value.GetPageAsync(
                It.IsAny<PlasticsRubberRequestContext>(), "page", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ByteString>(ByteString.From(new byte[length])));
            await using var binding = new MachineConfigurationBinding(node, m_context, provider.Object,
                AllowAccess(), 4);
            GetPageMethodStateResult result = await node.GetPage!.OnCallAsync!(
                m_context, node.GetPage, node.NodeId, "page", CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.EqualTo(accepted));
            if (accepted)
            {
                Assert.That(result.Page.Span.Length, Is.EqualTo(length));
            }
            else
            {
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(length == 0
                    ? StatusCodes.BadNoDataAvailable : StatusCodes.BadEncodingLimitsExceeded));
            }
        }
    }
}

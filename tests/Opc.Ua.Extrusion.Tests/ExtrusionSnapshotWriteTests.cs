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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Extrusion.Extruder;
using Opc.Ua.Extrusion.Server;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.Extrusion.Tests
{
    /// <summary>
    /// Verifies that common parameter commands cannot replace externally committed observations.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ExtrusionSnapshotWriteTests
    {
        /// <summary>
        /// Starts the real model owner used by the attribute and method pipelines.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new ExtrusionFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Releases every binding and the fixture-owned server.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// A completed older command cannot replace a newer externally committed observation.
        /// </summary>
        [Test]
        public async Task CommonWriteCannotReplaceANewerProviderObservationAsync()
        {
            ExtrusionFixture fixture = m_fixture;
            ExtrusionDeviceOptions options = ExtrusionFixture.Options("common-write-review");
            options.Features.Include("Throughput/SetValue");
            ExtrusionDevice<Extruder_InterfaceState>? device = null;
            var parameters = new Mock<IMonitoredParameterProvider>(MockBehavior.Strict);
            parameters.Setup(value => value.WriteSettingAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), MonitoredParameterSetting.SetValue, 180.0,
                    It.IsAny<CancellationToken>()))
                .Returns(async (PlasticsRubberRequestContext _, MonitoredParameterSetting _, double _,
                    CancellationToken ct) =>
                {
                    await device!.ApplySnapshotAsync(new ExtrusionSnapshot(1,
                    [
                        new ExtrusionValue(device.Node.Throughput!.SetValue!.NodeId,
                            new DataValue(Variant.From(182.0), StatusCodes.Good, ExtrusionFixture.Timestamp))
                    ]), ct).ConfigureAwait(false);
                });
            options.Common.Parameters = parameters.Object;
            device = await fixture.Runtime.CreateExtruderAsync(options).ConfigureAwait(false);
            BaseVariableState setting = device.Node.Throughput!.SetValue!;

            ServiceResult write = await setting.WriteAttributeAsync(
                fixture.Runtime.SystemContext, Attributes.Value, NumericRange.Null,
                new DataValue(Variant.From(180.0))).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(write), Is.True, write.ToString());
            (ServiceResult result, DataValue actual) = await setting.ReadAttributeAsync(
                fixture.Runtime.SystemContext, Attributes.Value, NumericRange.Null,
                QualifiedName.Null, new DataValue()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(actual.WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(182.0));
            Assert.That(actual.SourceTimestamp, Is.EqualTo(ExtrusionFixture.Timestamp));
        }

        /// <summary>
        /// A successful acknowledgment or refusal cannot manufacture a value before its observation arrives.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CommonWritesRetainTheLastObservationUntilPublicationAsync(bool reject)
        {
            ExtrusionDeviceOptions options = ParameterOptions("deferred-" + reject);
            var provider = new Mock<IMonitoredParameterProvider>(MockBehavior.Strict);
            provider.Setup(value => value.WriteSettingAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), MonitoredParameterSetting.SetValue, 180.0,
                    It.IsAny<CancellationToken>()))
                .Returns(() => reject
                    ? throw new ServiceResultException(StatusCodes.BadResourceUnavailable)
                    : default(ValueTask));
            options.Common.Parameters = provider.Object;
            var device = await m_fixture.Runtime.CreateExtruderAsync(options).ConfigureAwait(false);
            MonitoredParameterState parameter = device.Node.Throughput!;
            await PublishInitialAsync(device, parameter).ConfigureAwait(false);
            await parameter.SetValue!.ClearChangeMasksAsync(m_fixture.Runtime.SystemContext, false)
                .ConfigureAwait(false);

            ServiceResult result = await WriteAsync(parameter.SetValue, 180.0).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(
                reject ? StatusCodes.BadResourceUnavailable : StatusCodes.Good));
            await AssertValueAsync(parameter.SetValue, 175.0, ExtrusionFixture.Timestamp,
                StatusCodes.UncertainLastUsableValue).ConfigureAwait(false);
            Assert.That(parameter.SetValue.Value, Is.EqualTo(175.0), "The node cache must also remain authoritative.");
            Assert.That(parameter.SetValue.Timestamp, Is.EqualTo(ExtrusionFixture.Timestamp));
            Assert.That(parameter.SetValue.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(NodeStateChangeMasks.None));

            await device.ApplySnapshotAsync(new ExtrusionSnapshot(2,
            [
                new ExtrusionValue(parameter.SetValue.NodeId,
                    new DataValue(Variant.From(182.0), StatusCodes.Good, s_laterTimestamp))
            ])).ConfigureAwait(false);
            await AssertValueAsync(parameter.SetValue, 182.0, s_laterTimestamp, StatusCodes.Good)
                .ConfigureAwait(false);
            provider.Verify(value => value.WriteSettingAsync(It.IsAny<PlasticsRubberRequestContext>(),
                MonitoredParameterSetting.SetValue, 180.0, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Validation uses committed limits, not unobserved requests or mutable node-cache fields.
        /// </summary>
        [Test]
        public async Task ParameterValidationUsesOneCommittedSettingsImageAsync()
        {
            ExtrusionDeviceOptions options = ParameterOptions("committed-limits");
            var provider = new Mock<IMonitoredParameterProvider>(MockBehavior.Strict);
            provider.Setup(value => value.WriteSettingAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), MonitoredParameterSetting.Minimum, 170.0,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            provider.Setup(value => value.WriteSettingAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), MonitoredParameterSetting.Maximum, 150.0,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            options.Common.Parameters = provider.Object;
            var device = await m_fixture.Runtime.CreateExtruderAsync(options).ConfigureAwait(false);
            MonitoredParameterState parameter = device.Node.Throughput!;
            await PublishInitialAsync(device, parameter).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(await WriteAsync(parameter.MinValue!, 170.0).ConfigureAwait(false)),
                Is.True);
            Assert.That(parameter.MinValue!.Value, Is.EqualTo(100.0));
            parameter.MinValue.Value = 999.0;
            await AssertValueAsync(parameter.MinValue, 100.0, ExtrusionFixture.Timestamp, StatusCodes.Good)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(await WriteAsync(parameter.MaxValue!, 150.0).ConfigureAwait(false)),
                Is.True, "The pending request and local cache cannot replace the committed minimum of 100.");

            await device.ApplySnapshotAsync(new ExtrusionSnapshot(2,
            [
                new ExtrusionValue(parameter.MinValue.NodeId,
                    new DataValue(Variant.From(170.0), StatusCodes.Good, s_laterTimestamp))
            ])).ConfigureAwait(false);
            ServiceResult denied = await WriteAsync(parameter.MaxValue!, 150.0).ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            provider.Verify(value => value.WriteSettingAsync(It.IsAny<PlasticsRubberRequestContext>(),
                MonitoredParameterSetting.Maximum, 150.0, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Mode and calibration acknowledgments cannot change published settings or their write access early.
        /// </summary>
        [Test]
        public async Task AutomaticMonitoringAndResetWaitForCommittedSettingsAsync()
        {
            ExtrusionDeviceOptions options = ParameterOptions("committed-monitoring");
            options.Features.Include("Throughput/AutomaticMonitoring")
                .Include("Throughput/LowerTolerance")
                .Include("Throughput/UpperTolerance")
                .Include("Throughput/ResetMonitoring");
            var provider = new Mock<IMonitoredParameterProvider>(MockBehavior.Strict);
            provider.Setup(value => value.SetAutomaticMonitoringAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), true, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            provider.Setup(value => value.ResetMonitoringAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MonitoredParameterLimits>(new MonitoredParameterLimits
                {
                    SetValue = 175,
                    Minimum = 100,
                    Maximum = 250,
                    LowerTolerance = -1,
                    UpperTolerance = 1
                }));
            options.Common.Parameters = provider.Object;
            var device = await m_fixture.Runtime.CreateExtruderAsync(options).ConfigureAwait(false);
            MonitoredParameterState parameter = device.Node.Throughput!;
            await PublishInitialAsync(device, parameter).ConfigureAwait(false);
            await device.ApplySnapshotAsync(new ExtrusionSnapshot(2,
            [
                new ExtrusionValue(parameter.AutomaticMonitoring!.NodeId,
                    new DataValue(Variant.From(false), StatusCodes.Good, ExtrusionFixture.Timestamp)),
                new ExtrusionValue(parameter.LowerTolerance!.NodeId,
                    new DataValue(Variant.From(-5.0), StatusCodes.Good, ExtrusionFixture.Timestamp)),
                new ExtrusionValue(parameter.UpperTolerance!.NodeId,
                    new DataValue(Variant.From(5.0), StatusCodes.Good, ExtrusionFixture.Timestamp))
            ])).ConfigureAwait(false);

            ServiceResult automatic = await parameter.AutomaticMonitoring.WriteAttributeAsync(
                m_fixture.Runtime.SystemContext, Attributes.Value, NumericRange.Null,
                new DataValue(Variant.From(true))).ConfigureAwait(false);
            ServiceResult reset = await parameter.ResetMonitoring!.CallAsync(
                m_fixture.Runtime.SystemContext, parameter.NodeId, [], new List<ServiceResult>(), new List<Variant>())
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(automatic), Is.True);
            Assert.That(ServiceResult.IsGood(reset), Is.True);
            Assert.That(parameter.AutomaticMonitoring.Value, Is.False);
            Assert.That(parameter.LowerTolerance.Value, Is.EqualTo(-5));
            Assert.That(parameter.LowerTolerance.AccessLevel & AccessLevels.CurrentWrite, Is.Not.Zero);
            await device.ApplySnapshotAsync(new ExtrusionSnapshot(3,
            [
                new ExtrusionValue(parameter.AutomaticMonitoring.NodeId,
                    new DataValue(Variant.From(true), StatusCodes.Good, s_laterTimestamp)),
                new ExtrusionValue(parameter.LowerTolerance.NodeId,
                    new DataValue(Variant.From(-1.0), StatusCodes.Good, s_laterTimestamp))
            ])).ConfigureAwait(false);
            Assert.That(parameter.LowerTolerance.Value, Is.EqualTo(-1));
            Assert.That(parameter.LowerTolerance.AccessLevel & AccessLevels.CurrentWrite, Is.Zero);
            Assert.That((await WriteAsync(parameter.LowerTolerance, -2.0).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNotWritable));
        }

        /// <summary>
        /// Reversed committed bounds are rejected before replacing any published value.
        /// </summary>
        [Test]
        public async Task InvalidCommittedLimitsDoNotPartiallyPublishAsync()
        {
            var device = await m_fixture.Runtime.CreateExtruderAsync(ParameterOptions("invalid-limits"))
                .ConfigureAwait(false);
            MonitoredParameterState parameter = device.Node.Throughput!;
            await PublishInitialAsync(device, parameter).ConfigureAwait(false);
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(() =>
                device.ApplySnapshotAsync(new ExtrusionSnapshot(2,
                [
                    new ExtrusionValue(parameter.MinValue!.NodeId,
                        new DataValue(Variant.From(300.0), StatusCodes.Good, s_laterTimestamp)),
                    new ExtrusionValue(parameter.SetValue!.NodeId,
                        new DataValue(Variant.From(180.0), StatusCodes.Good, s_laterTimestamp))
                ])).AsTask()))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            await AssertValueAsync(parameter.MinValue!, 100.0, ExtrusionFixture.Timestamp, StatusCodes.Good)
                .ConfigureAwait(false);
            await AssertValueAsync(parameter.SetValue!, 175.0, ExtrusionFixture.Timestamp,
                StatusCodes.UncertainLastUsableValue).ConfigureAwait(false);
        }

        /// <summary>
        /// Selects only the parameter settings required by ownership and validation checks.
        /// </summary>
        private static ExtrusionDeviceOptions ParameterOptions(string serial)
        {
            ExtrusionDeviceOptions options = ExtrusionFixture.Options(serial);
            options.Features.Include("Throughput/SetValue")
                .Include("Throughput/MinValue").Include("Throughput/MaxValue");
            return options;
        }

        /// <summary>
        /// Publishes known initial settings with source metadata distinct from write completion.
        /// </summary>
        private static ValueTask PublishInitialAsync(
            ExtrusionDevice<Extruder_InterfaceState> device, MonitoredParameterState parameter)
        {
            return device.ApplySnapshotAsync(new ExtrusionSnapshot(1,
            [
                new ExtrusionValue(parameter.ActualValue!.NodeId,
                    new DataValue(Variant.From(175.0), StatusCodes.Good, ExtrusionFixture.Timestamp)),
                new ExtrusionValue(parameter.SetValue!.NodeId,
                    new DataValue(Variant.From(175.0), StatusCodes.UncertainLastUsableValue,
                        ExtrusionFixture.Timestamp)),
                new ExtrusionValue(parameter.MinValue!.NodeId,
                    new DataValue(Variant.From(100.0), StatusCodes.Good, ExtrusionFixture.Timestamp)),
                new ExtrusionValue(parameter.MaxValue!.NodeId,
                    new DataValue(Variant.From(250.0), StatusCodes.Good, ExtrusionFixture.Timestamp))
            ]));
        }

        /// <summary>
        /// Executes a client-style scalar Write rather than calling the callback directly.
        /// </summary>
        private ValueTask<ServiceResult> WriteAsync(BaseVariableState variable, double value)
        {
            return variable.WriteAttributeAsync(m_fixture.Runtime.SystemContext, Attributes.Value,
                NumericRange.Null, new DataValue(Variant.From(value)));
        }

        /// <summary>
        /// Checks the public read result, quality and original source time.
        /// </summary>
        private async Task AssertValueAsync(
            BaseVariableState variable, double expected, DateTimeUtc timestamp, StatusCode quality)
        {
            (ServiceResult result, DataValue actual) = await variable.ReadAttributeAsync(
                m_fixture.Runtime.SystemContext, Attributes.Value, NumericRange.Null,
                QualifiedName.Null, new DataValue()).ConfigureAwait(false);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(actual.WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
            Assert.That(actual.StatusCode, Is.EqualTo(quality));
            Assert.That(actual.SourceTimestamp, Is.EqualTo(timestamp));
        }

        /// <summary>The sole test address-space owner.</summary>
        private ExtrusionFixture m_fixture = null!;

        /// <summary>The later observation's source time.</summary>
        private static readonly DateTimeUtc s_laterTimestamp =
            (DateTimeUtc)ExtrusionFixture.Timestamp.ToDateTime().AddSeconds(5);
    }
}

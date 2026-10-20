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
using NUnit.Framework;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.CranesHoists.Server;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Tests exact input partitions and the bounded reference provider independently of server composition.
    /// </summary>
    [TestFixture]
    public sealed class CraneContractTests
    {
        /// <summary>
        /// Observation limits are inclusive and rejected before subscription creation.
        /// </summary>
        [TestCase(0, 1, 1, false)]
        [TestCase(1, 1, 1, true)]
        [TestCase(60000, 100, 1048576, true)]
        [TestCase(60001, 100, 1048576, false)]
        [TestCase(1, 0, 1, false)]
        [TestCase(1, 101, 1, false)]
        [TestCase(1, 1, 0, false)]
        [TestCase(1, 1, 1048577, false)]
        public void ObservationBoundsAreEnforced(int milliseconds, int notifications, int bytes, bool valid)
        {
            var options = new CraneObservationOptions
            {
                Window = TimeSpan.FromMilliseconds(milliseconds),
                MaximumNotifications = notifications,
                MaximumPayloadBytes = bytes
            };
            if (valid)
            {
                Assert.DoesNotThrow(options.Validate);
            }
            else
            {
                Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
            }
        }

        /// <summary>
        /// Inventory and page limits have exact lower and upper bounds.
        /// </summary>
        [TestCase(1, 1u, 1, true)]
        [TestCase(1024, 256u, 128, true)]
        [TestCase(0, 1u, 1, false)]
        [TestCase(1025, 1u, 1, false)]
        [TestCase(1, 0u, 1, false)]
        [TestCase(1, 257u, 1, false)]
        [TestCase(1, 1u, 0, false)]
        [TestCase(1, 1u, 129, false)]
        public void DiscoveryBoundsAreEnforced(int nodes, uint pageSize, int pages, bool valid)
        {
            var options = new CranesHoistsClientOptions
            {
                MaximumNodes = nodes,
                BrowsePageSize = pageSize,
                MaximumBrowsePages = pages
            };
            if (valid)
            {
                Assert.DoesNotThrow(options.Validate);
            }
            else
            {
                Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
            }
        }

        /// <summary>
        /// Every enum discriminator is validated as a wire Int32 with exact published bounds.
        /// </summary>
        [TestCase(CraneSignal.OperationalMode, 5)]
        [TestCase(CraneSignal.ExternalControlRequest, 3)]
        [TestCase(CraneSignal.SafetyOperationalMode, 4)]
        public void EnumerationValidationCoversEveryPublishedValue(CraneSignal signal, int maximum)
        {
            for (int value = 0; value <= maximum; value++)
            {
                Assert.That(CraneSystemHandle.ValidateValue(signal, Variant.From(value)), Is.EqualTo(StatusCodes.Good));
            }
            Assert.That(CraneSystemHandle.ValidateValue(signal, Variant.From(-1)),
                Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(CraneSystemHandle.ValidateValue(signal, Variant.From(maximum + 1)),
                Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(CraneSystemHandle.ValidateValue(signal, Variant.From(1.0)),
                Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        /// <summary>
        /// Standard functions remain a UInt16 array, including empty selection and reserved-value rejection.
        /// </summary>
        [Test]
        public void AxisFunctionsUseArrayAndPublishedValuesRatherThanAnInventedEnum()
        {
            Assert.That(CraneSystemHandle.ValidateValue(CraneSignal.AxisFunction, Variant.From(ArrayOf<ushort>.Empty)),
                Is.EqualTo(StatusCodes.Good));
            Assert.That(CraneSystemHandle.ValidateValue(CraneSignal.AxisFunction,
                Variant.From(ArrayOf.Wrapped<ushort>([0, 1, 2, 3, 4, 5, 6, 7, 8]))), Is.EqualTo(StatusCodes.Good));
            Assert.That(CraneSystemHandle.ValidateValue(CraneSignal.AxisFunction,
                Variant.From(ArrayOf.Wrapped<ushort>([9]))), Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(CraneSystemHandle.ValidateValue(CraneSignal.AxisFunction, Variant.From((ushort)0)),
                Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(CraneSystemHandle.ValidateValue(CraneSignal.AxisFunction,
                Variant.From(new ushort[65].ToArrayOf())),
                Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        /// <summary>
        /// Measurement validation rejects wrong types and non-finite values, not legitimate signed positions.
        /// </summary>
        [TestCase(CraneSignal.ActualPosition)]
        [TestCase(CraneSignal.MotorTemperature)]
        [TestCase(CraneSignal.DesignedWorkingPeriod)]
        [TestCase(CraneSignal.SpeedOverride)]
        [TestCase(CraneSignal.LoadMass)]
        public void NumericMeasurementsRetainTheirExactWireTypes(CraneSignal signal)
        {
            Assert.That(CraneSystemHandle.ValidateValue(signal, Variant.From(12.0)), Is.EqualTo(StatusCodes.Good));
            Assert.That(CraneSystemHandle.ValidateValue(signal, Variant.From(12)),
                Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(CraneSystemHandle.ValidateValue(signal, Variant.From(double.NaN)),
                Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(CraneSystemHandle.ValidateValue(signal, Variant.From(-1.0)), Is.EqualTo(
                signal is CraneSignal.LoadMass or CraneSignal.SpeedOverride
                    ? StatusCodes.BadOutOfRange : StatusCodes.Good));
        }

        /// <summary>
        /// Load geometry requires the inherited structures, not scalar or unrelated structure payloads.
        /// </summary>
        [Test]
        public void LoadGeometryRequiresTheDeclaredStructures()
        {
            Assert.That(CraneSystemHandle.ValidateValue(CraneSignal.LoadInertia,
                Variant.FromStructure(new ThreeDVector())), Is.EqualTo(StatusCodes.Good));
            Assert.That(CraneSystemHandle.ValidateValue(CraneSignal.LoadCenterOfMass,
                Variant.FromStructure(new ThreeDFrame())), Is.EqualTo(StatusCodes.Good));
            Assert.That(CraneSystemHandle.ValidateValue(CraneSignal.LoadInertia, Variant.From(1)),
                Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(CraneSystemHandle.ValidateValue(CraneSignal.LoadCenterOfMass,
                Variant.FromStructure(new ThreeDVector())), Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        /// <summary>
        /// Sample contracts retain the original immutable DataValue and reject anonymous provider identities.
        /// </summary>
        [Test]
        public void SamplesRetainQualityTimeAndIdentity()
        {
            var value = new DataValue(Variant.From(12.5), StatusCodes.Uncertain, CraneRuntimeTestData.SourceTime);
            var sample = new CraneSample(new("Axis", CraneSignal.ActualPosition), value);
            var observed = new CraneObservedValue(new NodeId("Axis", 1), value);
            Assert.That(sample.Value, Is.EqualTo(value));
            Assert.That(sample.Address.ComponentId, Is.EqualTo("Axis"));
            Assert.That(observed.Value, Is.EqualTo(value));
            Assert.That(observed.NodeId, Is.EqualTo(new NodeId("Axis", 1)));
            Assert.Throws<ArgumentException>(() => new CraneSample(new(string.Empty, CraneSignal.SystemOn), value));
            Assert.Throws<ArgumentException>(() => new CraneSample(default, value));
        }

        /// <summary>
        /// The simulator does not consume reports or synthesize physical changes for accepted requests.
        /// </summary>
        [Test]
        public async Task SimulationRequestsAreBoundedAndIndependentFromAcquiredSamples()
        {
            ArrayOf<CraneSample> samples =
            [
                CraneRuntimeTestData.Sample("Axis", CraneSignal.SpeedLimitDirPlus, Variant.From(61.0))
            ];
            await using var provider = new SimulatedCraneProvider(samples, true, maximumSamples: 1, maximumRequests: 1);
            Assert.That(await provider.ReadAsync().ConfigureAwait(false), Is.EqualTo(samples));
            Assert.That(await provider.ReadAsync().ConfigureAwait(false), Is.EqualTo(samples));
            var request = new CraneLimitRequest("System", new("Axis", CraneSignal.SpeedLimitDirPlusSetpoint),
                Variant.From(27.0));
            Assert.That(await provider.RequestAsync(request).ConfigureAwait(false), Is.EqualTo(StatusCodes.Good));
            Assert.That(await provider.ReadAsync().ConfigureAwait(false), Is.EqualTo(samples));
            Assert.That(provider.AcceptedRequests, Is.EqualTo(new[] { request }));
            Assert.That(await provider.RequestAsync(request).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadResourceUnavailable));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await provider.SetSamplesAsync([samples[0], samples[0]]).ConfigureAwait(false));
            Assert.That(await provider.ReadAsync().ConfigureAwait(false), Is.EqualTo(samples));
        }

        /// <summary>
        /// The adapter's default cannot silently accept an equipment-changing request.
        /// </summary>
        [Test]
        public async Task SimulationRejectsByDefaultAndHonorsCancellationAndDisposal()
        {
            await using var provider = new SimulatedCraneProvider([]);
            var request = new CraneLimitRequest("System", new("Axis", CraneSignal.SpeedLimitEnabledDirPlus),
                Variant.From(true));
            Assert.That(await provider.RequestAsync(request).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadNotSupported));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await provider.ReadAsync(canceled.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await provider.RequestAsync(request, canceled.Token).ConfigureAwait(false));
            await provider.DisposeAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await provider.ReadAsync().ConfigureAwait(false));
            Assert.Throws<ObjectDisposedException>(() => _ = provider.AcceptedRequests);
        }

        /// <summary>
        /// Invalid simulator configuration never creates a partially initialized adapter.
        /// </summary>
        [Test]
        public void SimulationConfigurationRejectsUnboundedOrDuplicateInputs()
        {
            CraneSample sample = CraneRuntimeTestData.Sample("System", CraneSignal.SystemOn, Variant.From(true));
            Assert.Throws<ArgumentNullException>(() => new SimulatedCraneProvider(default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulatedCraneProvider([], maximumSamples: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulatedCraneProvider([], maximumSamples: 1025));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulatedCraneProvider([], maximumRequests: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulatedCraneProvider([], maximumRequests: 1025));
            Assert.Throws<ArgumentException>(() => new SimulatedCraneProvider([sample, sample]));
            Assert.Throws<ArgumentException>(() => new SimulatedCraneProvider([null!]));
        }
    }
}

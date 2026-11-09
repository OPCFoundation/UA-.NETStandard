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
using Opc.Ua.CranesHoists.Server;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Exercises physical composition, failure partitions and cancellation/draining through public runtime interfaces.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CraneLifetimeAndTopologyTests
    {
        /// <summary>
        /// Starts a shared existing owner for independent test systems.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_server = new CraneServerFixture();
            await m_server.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Removes only this fixture's completed systems.
        /// </summary>
        [TearDown]
        public async Task RemoveAsync()
        {
            foreach (CraneSystemHandle handle in m_handles)
            {
                await handle.RemoveAsync().ConfigureAwait(false);
            }
            m_handles.Clear();
        }

        /// <summary>
        /// Stops the existing owner.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_server.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Physical composition adds only the directed standard relationship and preserves Robotics containment.
        /// </summary>
        [Test]
        public async Task PhysicalCompositionDoesNotReparentTheMotionDevices()
        {
            const string name = "PhysicalCrane";
            var samples = new List<CraneSample>(CraneRuntimeTestData.Samples(name).ToArray()!)
            {
                CraneRuntimeTestData.Sample("Trolley", CraneSignal.SpeedOverride, Variant.From(100.0)),
                CraneRuntimeTestData.Sample("TrolleyMotor", CraneSignal.MotorTemperature, Variant.From(31.0))
            };
            await using var provider = new SimulatedCraneProvider(samples.ToArrayOf());
            CraneSystemBuilder builder = Definition(name, provider);
            builder.AddDevice("Trolley", CraneMotionDeviceCategoryEnum.TROLLEY_TRAVERSE, device =>
                device.WithIdentification(data => CraneRuntimeTestData.Identify(data, "Trolley"))
                    .WithPhysicalComponent("Hoist")
                    .AddAxis("TrolleyAxis", axis => axis.AddMotor("TrolleyMotor")));
            CraneSystemHandle crane = await BuildAsync(builder).ConfigureAwait(false);
            BaseObjectState trolley = crane.GetElement("Trolley");
            BaseObjectState hoist = crane.GetElement("Hoist");
            Assert.That(trolley.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.HasPhysicalComponent, false, hoist.NodeId), Is.True);
            Assert.That(hoist.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.HasPhysicalComponent, true, trolley.NodeId), Is.True);
            Assert.That(hoist.Parent, Is.SameAs(crane.State.MotionDevices));
            Assert.That(trolley.Parent, Is.SameAs(crane.State.MotionDevices));
            Assert.That(crane.ReadSignal(new("TrolleyMotor", CraneSignal.MotorTemperature)).WrappedValue.GetDouble(),
                Is.EqualTo(31));
        }

        /// <summary>
        /// Invalid physical targets fail before publishing or reserving an address-space node.
        /// </summary>
        [TestCase("Missing")]
        [TestCase("Trolley")]
        public async Task InvalidPhysicalCompositionFailsSetup(string child)
        {
            var provider = new Mock<ICraneDataProvider>(MockBehavior.Strict);
            CraneSystemBuilder builder = Definition("BadPhysical", provider.Object);
            builder.AddDevice("Trolley", CraneMotionDeviceCategoryEnum.TROLLEY_TRAVERSE, device =>
                device.WithIdentification(data => CraneRuntimeTestData.Identify(data, "Trolley"))
                    .WithPhysicalComponent(child).AddAxis("TrolleyAxis", axis => axis.AddMotor("TrolleyMotor")));
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await builder.BuildAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(m_server.ReservedCount, Is.Zero);
            provider.Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// ProtectsAxis cannot refer to another system or repeat the same target.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidProtectionReferencesFailSetup(bool duplicate)
        {
            var provider = new Mock<ICraneDataProvider>(MockBehavior.Strict);
            CraneSystemBuilder builder = Definition("BadProtection", provider.Object);
            builder.AddProtectiveFunction("Protection", duplicate ? ["Lift", "Lift"] : ["OtherSystemAxis"]);
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await builder.BuildAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(m_server.ReservedCount, Is.Zero);
            provider.Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A disabled, inactive protection may still describe direction capabilities.
        /// </summary>
        [Test]
        public async Task DirectionCapabilitiesAreIndependentFromActiveProtectionState()
        {
            const string name = "InactiveProtection";
            ArrayOf<CraneSample> samples = CraneRuntimeTestData.Replace(CraneRuntimeTestData.Samples(name, true),
                CraneRuntimeTestData.Sample("Overload", CraneSignal.Enabled, Variant.From(false)));
            await using var provider = new SimulatedCraneProvider(samples, true);
            CraneSystemHandle crane = await BuildAsync(Definition(name, provider, true)).ConfigureAwait(false);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.Enabled)).WrappedValue.GetBoolean(), Is.False);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.Active)).WrappedValue.GetBoolean(), Is.False);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.DirMinusStop)).WrappedValue.GetBoolean(), Is.True);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.DirMinusSlowdown)).WrappedValue.GetBoolean(),
                Is.True);
        }

        /// <summary>
        /// Any invalid value rejects the entire report, not just the malformed field.
        /// </summary>
        [Test]
        public async Task InvalidReportCannotPartiallyUpdateSystemState()
        {
            const string name = "AtomicCrane";
            ArrayOf<CraneSample> samples = CraneRuntimeTestData.Samples(name);
            await using var provider = new SimulatedCraneProvider(samples);
            CraneSystemHandle crane = await BuildAsync(Definition(name, provider)).ConfigureAwait(false);
            ArrayOf<CraneSample> invalid = CraneRuntimeTestData.Replace(samples,
                CraneRuntimeTestData.Sample(name, CraneSignal.SystemOn, Variant.From(false)));
            invalid = CraneRuntimeTestData.Replace(invalid,
                CraneRuntimeTestData.Sample("Hoist", CraneSignal.SpeedOverride, Variant.From("wrong type")));
            await provider.SetSamplesAsync(invalid).ConfigureAwait(false);
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await crane.RefreshAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(crane.ReadSignal(new(name, CraneSignal.SystemOn)).WrappedValue.GetBoolean(), Is.True);
            Assert.That(crane.ReadSignal(new("Hoist", CraneSignal.SpeedOverride)).WrappedValue.GetDouble(),
                Is.EqualTo(83.25));
        }

        /// <summary>
        /// Inherited speed override is a percentage in the inclusive range zero through one hundred.
        /// Rejected acquisition must preserve every value, quality and timestamp in the previous report.
        /// </summary>
        [TestCase(0.0, true)]
        [TestCase(100.0, true)]
        [TestCase(100.0001, false)]
        [TestCase(101.0, false)]
        [TestCase(-0.0001, false)]
        [TestCase(double.NaN, false)]
        [TestCase(double.PositiveInfinity, false)]
        [TestCase(double.NegativeInfinity, false)]
        public async Task SpeedOverrideAcquisitionHonorsPercentageBoundsAtomicallyAsync(double speed, bool valid)
        {
            const string name = "SpeedOverrideBounds";
            ArrayOf<CraneSample> initial = CraneRuntimeTestData.Samples(name);
            await using var provider = new SimulatedCraneProvider(initial);
            CraneSystemHandle crane = await BuildAsync(Definition(name, provider)).ConfigureAwait(false);
            ArrayOf<CraneSignalAddress> signals = crane.TelemetrySignals;
            var before = new DataValue[signals.Count];
            for (int index = 0; index < signals.Count; index++)
            {
                before[index] = crane.ReadSignal(signals[index]);
            }
            ArrayOf<CraneSample> next = CraneRuntimeTestData.Replace(initial,
                CraneRuntimeTestData.Sample(name, CraneSignal.SystemOn, Variant.From(false)));
            next = CraneRuntimeTestData.Replace(next,
                CraneRuntimeTestData.Sample("Hoist", CraneSignal.SpeedOverride, Variant.From(speed)));
            await provider.SetSamplesAsync(next).ConfigureAwait(false);

            if (valid)
            {
                await crane.RefreshAsync().ConfigureAwait(false);
                Assert.That(crane.ReadSignal(new("Hoist", CraneSignal.SpeedOverride)).WrappedValue.GetDouble(),
                    Is.EqualTo(speed));
                Assert.That(crane.ReadSignal(new(name, CraneSignal.SystemOn)).WrappedValue.GetBoolean(), Is.False);
            }
            else
            {
                ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await crane.RefreshAsync().ConfigureAwait(false)))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                for (int index = 0; index < signals.Count; index++)
                {
                    Assert.That(crane.ReadSignal(signals[index]), Is.EqualTo(before[index]), signals[index].ToString());
                }
            }
        }

        /// <summary>
        /// An owned provider is disposed only after its canceled in-flight callback exits.
        /// </summary>
        [Test]
        public async Task ShutdownCancelsAndDrainsTheOwnedProviderWithoutHoldingItsCallbackGate()
        {
            const string name = "DrainingCrane";
            ArrayOf<CraneSample> samples = CraneRuntimeTestData.Samples(name);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            var provider = new Mock<ICraneDataProvider>(MockBehavior.Strict);
            provider.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken cancellationToken) => ReadAsync(cancellationToken));
            Mock<IAsyncDisposable> disposable = provider.As<IAsyncDisposable>();
            disposable.Setup(value => value.DisposeAsync())
                .Callback(() => Assert.That(exited.Task.IsCompleted, Is.True))
                .Returns(default(ValueTask));
            CraneSystemHandle crane = await BuildAsync(
                Definition(name, provider.Object).WithProvider(provider.Object, ownsProvider: true))
                .ConfigureAwait(false);
            Task refresh = crane.RefreshAsync().AsTask();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await crane.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await Assert.CatchAsync<OperationCanceledException>(async () => await refresh.ConfigureAwait(false));
            Assert.That(crane.IsDisposed, Is.True);
            Assert.That(crane.ReadSignal(new(name, CraneSignal.SystemOn)).StatusCode,
                Is.EqualTo(StatusCodes.BadShutdown));
            disposable.Verify(value => value.DisposeAsync(), Times.Once);
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await crane.RefreshAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadShutdown));

            async ValueTask<ArrayOf<CraneSample>> ReadAsync(CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref reads) == 1)
                {
                    return samples;
                }
                started.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    return samples;
                }
                finally
                {
                    exited.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// An equipment callback can read the last complete state without deadlocking publication.
        /// </summary>
        [Test]
        public async Task ProviderCallbackCanInspectTheLastPublishedSnapshot()
        {
            const string name = "CallbackCrane";
            ArrayOf<CraneSample> samples = CraneRuntimeTestData.Samples(name);
            var provider = new Mock<ICraneDataProvider>(MockBehavior.Strict);
            provider.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(samples);
            CraneSystemHandle crane = await BuildAsync(Definition(name, provider.Object)).ConfigureAwait(false);
            provider.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Assert.That(crane.ReadSignal(new(name, CraneSignal.SystemOn)).WrappedValue.GetBoolean(), Is.True);
                    return new ValueTask<ArrayOf<CraneSample>>(samples);
                });
            await crane.RefreshAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(crane.IsDisposed, Is.False);
        }

        /// <summary>
        /// A selected configuration request without a provider/authorizer fails before publication.
        /// </summary>
        [Test]
        public async Task SelectedLimitRequestsRequireBothProvidersAtSetup()
        {
            var provider = new Mock<ICraneDataProvider>(MockBehavior.Strict);
            CraneSystemBuilder definition = Definition("UnbackedLimits", provider.Object).WithGlobalSpeedLimit();
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await definition.BuildAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(m_server.ReservedCount, Is.Zero);
            provider.Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Invalid categories and identifiers fail at the public builder interface.
        /// </summary>
        [Test]
        public void BuilderRejectsUnknownEnumsDuplicateNamesAndInvalidBounds()
        {
            var context = new CranesHoistsBuildContext(m_server.RoboticsContext(), m_server.MachineryContext());
            Assert.Throws<ArgumentException>(() => context.AddCraneSystem(string.Empty));
            Assert.Throws<ArgumentException>(() => context.AddCraneSystem(new string('x', 129)));
            CraneSystemBuilder builder = context.AddCraneSystem("Guards");
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddDevice("Device",
                (CraneMotionDeviceCategoryEnum)8, _ => { }));
            builder.AddSafetyState("Safety");
            Assert.Throws<ServiceResultException>(() => builder.AddSafetyState("Safety"));
            builder.AddDevice("Device", CraneMotionDeviceCategoryEnum.HOIST, device =>
                device.AddAxis("Axis", axis =>
                {
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => axis.WithRatedSpeed(double.NaN, new EUInformation()));
                    Assert.Throws<ArgumentOutOfRangeException>(() => axis.WithRatedSpeed(0, new EUInformation()));
                    Assert.Throws<ArgumentNullException>(() => axis.WithRatedSpeed(1, null!));
                }));
        }

        /// <summary>
        /// Every inherited mandatory topology obligation is validated before a usable instance can be returned.
        /// </summary>
        [TestCase("provider")]
        [TestCase("identification")]
        [TestCase("controller")]
        [TestCase("safety")]
        [TestCase("device")]
        [TestCase("device-identification")]
        [TestCase("axis")]
        [TestCase("motor")]
        [TestCase("software")]
        [TestCase("task")]
        public async Task MissingMandatoryObligationFailsAndReleasesReservations(string missing)
        {
            string name = "Missing-" + missing;
            await using var provider = new SimulatedCraneProvider(CraneRuntimeTestData.Samples(name));
            var context = new CranesHoistsBuildContext(m_server.RoboticsContext(), m_server.MachineryContext());
            CraneSystemBuilder builder = context.AddCraneSystem(name);
            if (missing != "provider")
            {
                builder.WithProvider(provider);
            }
            if (missing != "identification")
            {
                builder.WithIdentification(data => CraneRuntimeTestData.Identify(data, name));
            }
            if (missing != "controller")
            {
                builder.AddController("Controller", controller =>
                {
                    if (missing != "software")
                    {
                        controller.AddSoftware("Software");
                    }
                    if (missing != "task")
                    {
                        controller.AddTaskControl("Task");
                    }
                });
            }
            if (missing != "safety")
            {
                builder.AddSafetyState("Safety");
            }
            if (missing != "device")
            {
                builder.AddDevice("Hoist", CraneMotionDeviceCategoryEnum.HOIST, device =>
                {
                    if (missing != "device-identification")
                    {
                        device.WithIdentification(data => CraneRuntimeTestData.Identify(data, name + "-hoist"));
                    }
                    if (missing != "axis")
                    {
                        device.AddAxis("Lift", axis =>
                        {
                            if (missing != "motor")
                            {
                                axis.AddMotor("LiftMotor");
                            }
                        });
                    }
                });
            }
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await builder.BuildAsync().ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(context.ConformanceUnits, Is.Empty);
            Assert.That(m_server.ReservedCount, Is.Zero, m_server.ReservedNames);
        }

        /// <summary>
        /// Creates an independent definition on the existing owner.
        /// </summary>
        private CraneSystemBuilder Definition(string name, ICraneDataProvider provider, bool full = false)
        {
            return CraneRuntimeTestData.Define(
                new CranesHoistsBuildContext(m_server.RoboticsContext(), m_server.MachineryContext()),
                name, provider, full);
        }

        /// <summary>
        /// Registers each successful system for deterministic teardown.
        /// </summary>
        private async Task<CraneSystemHandle> BuildAsync(CraneSystemBuilder builder)
        {
            CraneSystemHandle result = await builder.BuildAsync().ConfigureAwait(false);
            m_handles.Add(result);
            return result;
        }

        private CraneServerFixture m_server = null!;
        private readonly List<CraneSystemHandle> m_handles = [];
    }
}

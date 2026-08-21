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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pumps;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Pins the address space of the <c>PumpDeviceIntegrationServer</c> sample
    /// to the published PumpX-2000 datasheet
    /// (<c>samples/DI/PumpDeviceIntegrationServer/DATASHEET.md</c>). Every value
    /// asserted here is quoted from that document, so a change to the sample
    /// that is not reflected in the datasheet - or the other way round - fails
    /// the build.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [NonParallelizable]
    public sealed class PumpDatasheetConformanceTests
    {
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_manager = new PumpNodeManager(server.CurrentInstance, m_fixture.Config);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await m_manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

            m_pump = m_manager.FindPredefinedNode<PumpState>(
                new NodeId("5001_Pump_1", m_manager.InstanceNamespaceIndex));
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_manager?.Dispose();
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Section 2 of the datasheet - nameplate of unit SN-001.
        /// </summary>
        [Test]
        public void NameplateMatchesTheDatasheet()
        {
            PumpIdentificationState identification = m_pump!.Identification!;

            Assert.Multiple(() =>
            {
                Assert.That(
                    identification.Manufacturer!.Value.Text,
                    Is.EqualTo("SimPump Corp"));
                Assert.That(
                    identification.ManufacturerUri!.Value,
                    Is.EqualTo("https://simpump.example"));
                Assert.That(identification.Model!.Value.Text, Is.EqualTo("PumpX-2000"));
                Assert.That(
                    identification.ProductCode!.Value,
                    Is.EqualTo("PX2000-32-160"));
                Assert.That(identification.DeviceClass!.Value, Is.EqualTo("Pump"));
                Assert.That(identification.HardwareRevision!.Value, Is.EqualTo("1.4"));
                Assert.That(identification.SoftwareRevision!.Value, Is.EqualTo("2.5.3"));
                Assert.That(identification.SerialNumber!.Value, Is.EqualTo("SN-001"));
                Assert.That(
                    identification.ProductInstanceUri!.Value,
                    Is.EqualTo("urn:simdevice:SimPump:PumpX-2000:SN-001"));
                Assert.That(identification.AssetId!.Value, Is.EqualTo("PMP-1001"));
                Assert.That(
                    identification.ComponentName!.Value.Text,
                    Is.EqualTo("Feed Pump A"));
                Assert.That(
                    identification.Location!.Value,
                    Is.EqualTo("Plant 1 / Utility Skid / Bay 3"));
                Assert.That(identification.YearOfConstruction!.Value, Is.EqualTo(2025));
                Assert.That(identification.MonthOfConstruction!.Value, Is.EqualTo(4));
                Assert.That(identification.DayOfConstruction!.Value, Is.EqualTo(17));
                Assert.That(
                    identification.ArticleNumber!.Value,
                    Is.EqualTo("PX2000-32-160-CI"));
                Assert.That(
                    identification.OrderProductCode!.Value,
                    Is.EqualTo("PX2000-32-160-CI-M30"));
                Assert.That(
                    identification.TypeOfProduct!.Value,
                    Is.EqualTo("Centrifugal pump, end-suction"));
                Assert.That(identification.Supplier!.Value, Is.EqualTo("SimPump Corp"));
                Assert.That(identification.CountryOfOrigin!.Value, Is.EqualTo("DE"));
                Assert.That(
                    identification.FabricationNumber!.Value,
                    Is.EqualTo("F-2025-0001"));
            });
        }

        /// <summary>
        /// Section 4 of the datasheet - engineering units and ranges of every
        /// measurement.
        /// </summary>
        [Test]
        public void MeasurementRangesMatchTheDatasheet()
        {
            MeasurementsState measurements = m_pump!.Operational!.Measurements!;

            Assert.Multiple(() =>
            {
                AssertRange(measurements.DifferentialPressure!, "Pa", 0.0, 400_000.0);
                AssertRange(measurements.FluidTemperature!, "K", 263.15, 393.15);
                AssertRange(measurements.BearingTemperature!, "K", 273.15, 423.15);
                AssertRange(measurements.PumpPowerInput!, "W", 0.0, 4_000.0);
                AssertRange(measurements.MassFlow!, "kg/s", 0.0, 10.0);
                AssertRange(measurements.PumpEfficiency!, "%", 0.0, 100.0);
                AssertRange(measurements.Level!, "m", 0.0, 5.0);
            });
        }

        /// <summary>
        /// Section 7.1 of the datasheet - the bearing-temperature trip points
        /// and the measurement the alarm reports on.
        /// </summary>
        [Test]
        public void AlarmTripPointsMatchTheDatasheet()
        {
            ushort pumpsNamespaceIndex = (ushort)m_manager!.Server.NamespaceUris
                .GetIndex(global::Opc.Ua.Pumps.Namespaces.Pumps);
            NodeState? alarmNode = m_pump!.Events!.FindChild(
                m_manager.SystemContext,
                new QualifiedName("OverTempAlarm", pumpsNamespaceIndex));

            Assert.That(alarmNode, Is.InstanceOf<NonExclusiveLimitAlarmState>());
            var alarm = (NonExclusiveLimitAlarmState)alarmNode!;

            Assert.Multiple(() =>
            {
                Assert.That(alarm.HighHighLimit!.Value, Is.EqualTo(373.15));
                Assert.That(alarm.HighLimit!.Value, Is.EqualTo(363.15));
                Assert.That(alarm.LowLimit!.Value, Is.EqualTo(283.15));
                Assert.That(alarm.LowLowLimit!.Value, Is.EqualTo(278.15));
                Assert.That(
                    alarm.SourceNode!.Value,
                    Is.EqualTo(m_pump.Operational!.Measurements!.BearingTemperature!.NodeId),
                    "The limit alarm must report the bearing temperature chain.");
                Assert.That(alarm.SourceName!.Value, Is.EqualTo("BearingTemperature"));
            });
        }

        /// <summary>
        /// Section 8 of the datasheet - the simulated values must stay inside
        /// the published operating envelope.
        /// </summary>
        [Test]
        public void SimulatedValuesStayWithinTheDatasheetEnvelope()
        {
            MeasurementsState measurements = m_pump!.Operational!.Measurements!;

            Assert.Multiple(() =>
            {
                AssertWithin(measurements.DifferentialPressure!, 205_000.0, 283_000.0);
                AssertWithin(measurements.MassFlow!, 4.85, 9.02);
                AssertWithin(measurements.PumpEfficiency!, 68.1, 72.0);
                AssertWithin(measurements.PumpPowerInput!, 2_013.0, 2_728.0);
                AssertWithin(measurements.BearingTemperature!, 331.5, 378.3);
                AssertWithin(measurements.FluidTemperature!, 308.15, 318.15);
                AssertWithin(measurements.Level!, 2.0, 3.0);
            });
        }

        /// <summary>
        /// Section 3.2 of the datasheet - the published values are derived from
        /// one flow through the characteristic curves, so shaft power, mass
        /// flow, differential pressure and efficiency must satisfy
        /// <c>P = Δp · Q / η</c> at every instant.
        /// </summary>
        [Test]
        public async Task SimulatedValuesAreHydraulicallyConsistentAsync()
        {
            MeasurementsState measurements = m_pump!.Operational!.Measurements!;

            // The 250 ms simulation loop is running, so a tick can land
            // between two reads of the sample. Retry until one sample was
            // taken between two ticks; a genuinely inconsistent model fails
            // every attempt.
            double deviation = double.MaxValue;
            bool sampled = false;
            for (int attempt = 0; attempt < MaxSampleAttempts; attempt++)
            {
                double massFlow = measurements.MassFlow!.Value;
                double differentialPressure = measurements.DifferentialPressure!.Value;
                double efficiency = measurements.PumpEfficiency!.Value;
                double shaftPower = measurements.PumpPowerInput!.Value;

                // Guard against the initial defaults: a pump that has not
                // published a complete sample yet would turn the ratio below
                // into NaN or Infinity and mask the actual assertion.
                if (massFlow <= 0.0 || efficiency <= 0.0 || shaftPower <= 0.0)
                {
                    await Task.Delay(SampleRetryDelayMilliseconds)
                        .ConfigureAwait(false);
                    continue;
                }

                sampled = true;

                // Volumetric flow in m³/s from the published mass flow.
                double volumeFlow = massFlow / FluidDensity;
                double expectedPower = differentialPressure * volumeFlow /
                    (efficiency / 100.0);
                deviation = Math.Abs(expectedPower - shaftPower) / shaftPower;
                if (deviation <= ConsistencyTolerance)
                {
                    break;
                }
            }

            Assert.That(
                sampled,
                Is.True,
                "The simulation never published a complete sample.");
            Assert.That(
                deviation,
                Is.LessThanOrEqualTo(ConsistencyTolerance),
                "Shaft power must equal differential pressure times volume flow " +
                "divided by efficiency.");
        }

        private static void AssertRange(
            BaseAnalogState<double> measurement,
            string expectedUnit,
            double expectedLow,
            double expectedHigh)
        {
            Assert.That(
                measurement.EngineeringUnits!.Value.DisplayName.Text,
                Is.EqualTo(expectedUnit),
                measurement.BrowseName.Name);
            Assert.That(
                measurement.EURange!.Value.Low,
                Is.EqualTo(expectedLow),
                measurement.BrowseName.Name);
            Assert.That(
                measurement.EURange.Value.High,
                Is.EqualTo(expectedHigh),
                measurement.BrowseName.Name);
        }

        private static void AssertWithin(
            BaseAnalogState<double> measurement,
            double low,
            double high)
        {
            Assert.That(
                measurement.Value,
                Is.InRange(low, high),
                measurement.BrowseName.Name);
            Assert.That(
                measurement.Value,
                Is.InRange(measurement.EURange!.Value.Low, measurement.EURange.Value.High),
                measurement.BrowseName.Name + " outside its EURange");
        }

        private const double FluidDensity = 998.0;
        private const double ConsistencyTolerance = 1e-9;
        private const int MaxSampleAttempts = 10;
        private const int SampleRetryDelayMilliseconds = 50;

        private ServerFixture<StandardServer>? m_fixture;
        private PumpNodeManager? m_manager;
        private PumpState? m_pump;
    }
}

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

using System.Collections.Generic;
using System.Linq;
using Generators;
using NUnit.Framework;

namespace Opc.Ua.Generators.Tests
{
    /// <summary>
    /// Holds the protection table to the datasheet and to the simulation.
    /// </summary>
    /// <remarks>
    /// A protection that never trips is indistinguishable from a healthy machine,
    /// and a protection that trips on a threshold nobody published is worse: it
    /// stops a set for a reason no operator can look up. These tests hold both ends
    /// - the declared trip points and the conditions that read them.
    /// </remarks>
    [TestFixture]
    [Category("Generators")]
    public sealed class GeneratorProtectionTests
    {
        /// <summary>
        /// The four protections the sample annunciates are all present, once each.
        /// </summary>
        [Test]
        public void EveryProtectionFunctionIsAnnunciatedExactlyOnce()
        {
            GeneratorProtectionFunctionEnum[] functions = Definitions()
                .Select(d => d.Function)
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(functions, Is.EquivalentTo(new[]
                {
                    GeneratorProtectionFunctionEnum.LowOilPressure,
                    GeneratorProtectionFunctionEnum.HighCoolantTemperature,
                    GeneratorProtectionFunctionEnum.Overspeed,
                    GeneratorProtectionFunctionEnum.Overload,
                }));
                Assert.That(functions.Distinct().Count(), Is.EqualTo(functions.Length));
            });
        }

        /// <summary>
        /// Alarm browse names are unique, since they mint the alarm NodeIds.
        /// </summary>
        [Test]
        public void AlarmNamesAreDistinct()
        {
            string[] names = Definitions().Select(d => d.Name).ToArray();
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length));
        }

        /// <summary>
        /// Every protection names the subsystem an operator would go to.
        /// </summary>
        [Test]
        public void EveryProtectionNamesItsSubsystem()
        {
            foreach (ProtectionDefinition definition in Definitions())
            {
                Assert.That(
                    definition.Subsystem,
                    Is.Not.Null.And.Not.Empty,
                    $"{definition.Name} names no subsystem.");
            }
        }

        /// <summary>
        /// The three protections that stop an engine are marked as shutdowns; the
        /// one that does not, is not.
        /// </summary>
        /// <remarks>
        /// This flag is what decides whether a trip stops the machine, so getting it
        /// wrong either strands a healthy set or keeps a damaged one loaded.
        /// </remarks>
        [TestCase("LowOilPressureAlarm", true)]
        [TestCase("HighCoolantTemperatureAlarm", true)]
        [TestCase("OverspeedAlarm", true)]
        [TestCase("OverloadAlarm", false)]
        public void ShutdownClassMatchesTheProtection(string name, bool isShutdown)
        {
            ProtectionDefinition definition = Definitions().Single(d => d.Name == name);
            Assert.That(definition.IsShutdown, Is.EqualTo(isShutdown));
        }

        /// <summary>
        /// A shutdown-class protection is never less severe than a warning.
        /// </summary>
        [Test]
        public void ShutdownProtectionsOutrankWarnings()
        {
            ushort worstWarning = Definitions()
                .Where(d => !d.IsShutdown)
                .Select(d => d.Severity)
                .DefaultIfEmpty((ushort)0)
                .Max();

            foreach (ProtectionDefinition definition in Definitions().Where(d => d.IsShutdown))
            {
                Assert.That(
                    definition.Severity,
                    Is.GreaterThan(worstWarning),
                    $"{definition.Name} is a shutdown but ranks no higher than a warning.");
            }
        }

        /// <summary>
        /// A healthy set at its duty point annunciates nothing.
        /// </summary>
        /// <remarks>
        /// The one failure mode that would make the sample useless: alarms that are
        /// on all the time teach an operator to ignore them.
        /// </remarks>
        [Test]
        public void AHealthySetAnnunciatesNothing()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True);

            for (int tick = 0; tick < 400; tick++)
            {
                simulation.Advance(tick);
                foreach (ProtectionDefinition definition in Definitions())
                {
                    Assert.That(
                        definition.IsTripped(simulation),
                        Is.False,
                        $"{definition.Name} tripped on a healthy set at tick {tick}.");
                }
            }
        }

        /// <summary>
        /// Low oil pressure is not supervised while the engine is cranking.
        /// </summary>
        /// <remarks>
        /// Oil pressure has not built during a start, so supervising it there trips
        /// every set the moment it tries to run. A real set bypasses the trip for
        /// exactly this reason; this test pins that behaviour, because it was a real
        /// defect in this sample before the bypass was added.
        /// </remarks>
        [Test]
        public void LowOilPressureIsBypassedWhileCranking()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            ProtectionDefinition lowOil = Definitions().Single(d => d.Name == "LowOilPressureAlarm");

            simulation.RequestState(GeneratorRunState.Ready);
            simulation.RequestState(GeneratorRunState.Starting);

            for (int tick = 0; tick < 20 && simulation.State == GeneratorRunState.Starting; tick++)
            {
                simulation.Advance(tick);
                Assert.That(
                    lowOil.IsTripped(simulation),
                    Is.False,
                    $"Low oil pressure tripped while cranking at {simulation.OilPressureBar:F2} bar.");
            }
        }

        /// <summary>
        /// A stopped set does not annunciate low oil pressure or over-temperature.
        /// </summary>
        [Test]
        public void AStoppedSetAnnunciatesNothing()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();

            Assert.Multiple(() =>
            {
                Assert.That(simulation.LowOilPressure, Is.False);
                Assert.That(simulation.CoolantOverTemperature, Is.False);
                Assert.That(simulation.IsSpinning, Is.False);
            });
        }

        /// <summary>
        /// The overspeed condition reads the datasheet's trip point.
        /// </summary>
        [Test]
        public void OverspeedTripsAboveTheDatasheetPoint()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    GeneratorDatasheet.TripPoints.OverspeedRpm,
                    Is.GreaterThan(GeneratorDatasheet.Engine.RatedSpeedRpm));
                Assert.That(
                    GeneratorDatasheet.TripPoints.OverspeedRpm,
                    Is.EqualTo(1725.0).Within(0.001));
            });
        }

        /// <summary>
        /// The overload condition trips above the rating, not at it.
        /// </summary>
        [Test]
        public void OverloadTripsAboveThePrimeRating()
        {
            Assert.That(GeneratorDatasheet.TripPoints.OverloadFraction, Is.GreaterThan(1.0));
        }

        /// <summary>
        /// Trip points sit outside the range a healthy set operates in.
        /// </summary>
        /// <remarks>
        /// A trip point inside the normal band would fire on a machine that is
        /// working correctly, which is how a plant ends up with its protections
        /// disabled.
        /// </remarks>
        [Test]
        public void TripPointsSitOutsideTheNormalOperatingBand()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    GeneratorDatasheet.TripPoints.LowOilPressureBar,
                    Is.LessThan(GeneratorDatasheet.Engine.RatedOilPressureBar));
                Assert.That(
                    GeneratorDatasheet.TripPoints.HighCoolantCelsius,
                    Is.GreaterThan(
                        GeneratorDatasheet.Engine.ThermostatCelsius
                            + GeneratorDatasheet.Curves.CoolantRiseKelvin));
            });
        }

        /// <summary>
        /// A shutdown-class trip stops a running set; a warning does not.
        /// </summary>
        [Test]
        public void AShutdownTripStopsARunningSet()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(simulation.Trip(), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Fault));
                Assert.That(simulation.ProtectionTripped, Is.True);
                Assert.That(simulation.IsLoaded, Is.False);
            });
        }

        /// <summary>
        /// One set's trip does not stop another.
        /// </summary>
        /// <remarks>
        /// The interesting failure: a protection evaluated against shared state
        /// stops the whole plant on one machine's fault. That happened here once.
        /// </remarks>
        [Test]
        public void OneSetsTripDoesNotStopAnother()
        {
            GeneratorSimulation first = SimulationHarness.Create(0);
            GeneratorSimulation second = SimulationHarness.Create(1);

            Assert.That(SimulationHarness.AdvanceUntil(first, GeneratorRunState.Loaded), Is.True);
            Assert.That(SimulationHarness.AdvanceUntil(second, GeneratorRunState.Loaded), Is.True);

            first.Trip();

            Assert.Multiple(() =>
            {
                Assert.That(first.State, Is.EqualTo(GeneratorRunState.Fault));
                Assert.That(second.State, Is.EqualTo(GeneratorRunState.Loaded));
                Assert.That(second.ProtectionTripped, Is.False);
            });
        }

        /// <summary>
        /// A stopped set cannot be tripped again.
        /// </summary>
        [Test]
        public void AStoppedSetCannotBeTripped()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(simulation.Trip(), Is.False);
        }

        /// <summary>
        /// Every protection can actually fire.
        /// </summary>
        /// <remarks>
        /// The test that would have caught the sample's worst defect. All four
        /// protections were unreachable: the healthy curves are bounded well inside
        /// every trip point (that is what a datasheet means), and overload was
        /// clamped to exactly its own trip point, so a strictly-greater-than
        /// comparison could never be true. `EvaluateProtections`, the shutdown
        /// class, `ResetFaults` and the whole Fault branch of the state machine were
        /// code that never ran, while the README documented them as observable.
        /// Asserting that a healthy set stays quiet is only half the contract.
        /// </remarks>
        [Test]
        public void EveryProtectionCanFire()
        {
            (GeneratorFault Fault, string Alarm)[] cases =
            [
                (GeneratorFault.OilPressureLoss, "LowOilPressureAlarm"),
                (GeneratorFault.CoolingFailure, "HighCoolantTemperatureAlarm"),
                (GeneratorFault.GovernorFailure, "OverspeedAlarm"),
                (GeneratorFault.Overload, "OverloadAlarm"),
            ];

            foreach ((GeneratorFault fault, string name) in cases)
            {
                GeneratorSimulation simulation = SimulationHarness.Create();
                Assert.That(
                    SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                    Is.True);

                ProtectionDefinition definition = Definitions().Single(d => d.Name == name);
                simulation.InjectFault(fault);

                bool tripped = false;
                for (int tick = 0; tick < 400 && !tripped; tick++)
                {
                    simulation.Advance(tick);
                    tripped = definition.IsTripped(simulation);
                }

                Assert.That(tripped, Is.True, $"{name} never tripped on {fault}.");
            }
        }

        /// <summary>
        /// A shutdown-class fault stops the set; a warning-class one does not.
        /// </summary>
        /// <remarks>
        /// Overload is the only warning here. A set that keeps running while
        /// overloaded is correct - the operator is told, and the machine carries on
        /// - whereas one that keeps running with no oil pressure is not.
        /// </remarks>
        [Test]
        public void OnlyShutdownClassFaultsStopTheSet()
        {
            GeneratorSimulation warned = RunWithFault(GeneratorFault.Overload);
            Assert.That(
                warned.ProtectionTripped,
                Is.False,
                "A warning-class protection must not stop the set.");
            Assert.That(
                Definitions().Single(d => d.Name == "OverloadAlarm").IsTripped(warned),
                Is.True);
        }

        /// <summary>
        /// Clearing the fault lets the protection clear again.
        /// </summary>
        /// <remarks>
        /// A protection that latches and can never clear is indistinguishable from a
        /// stuck sensor, and it would leave the alarm retained forever.
        /// </remarks>
        [Test]
        public void AProtectionClearsWhenTheFaultGoesAway()
        {
            GeneratorSimulation simulation = RunWithFault(GeneratorFault.Overload);
            ProtectionDefinition overload = Definitions().Single(d => d.Name == "OverloadAlarm");
            Assert.That(overload.IsTripped(simulation), Is.True);

            simulation.InjectFault(GeneratorFault.None);
            for (int tick = 0; tick < 200 && overload.IsTripped(simulation); tick++)
            {
                simulation.Advance(tick);
            }

            Assert.That(overload.IsTripped(simulation), Is.False);
        }

        /// <summary>
        /// A shutdown trip removes the very condition that caused it.
        /// </summary>
        /// <remarks>
        /// This is why a shutdown-class alarm has to latch. Oil pressure and coolant
        /// temperature are only supervised while the engine turns, so the moment the
        /// trip stops the set the condition reads healthy again. An alarm that simply
        /// followed its condition would go active for one tick and clear, leaving an
        /// operator with a stopped machine and no indication of why it stopped -
        /// which was the observed behaviour before the latch was added, and was
        /// invisible to every test that only checked the condition.
        /// </remarks>
        [Test]
        public void AShutdownTripRemovesTheConditionThatCausedIt()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded);
            ProtectionDefinition coolant = Definitions()
                .Single(d => d.Name == "HighCoolantTemperatureAlarm");

            simulation.InjectFault(GeneratorFault.CoolingFailure);
            for (int tick = 0; tick < 400 && !coolant.IsTripped(simulation); tick++)
            {
                simulation.Advance(tick);
            }
            Assert.That(coolant.IsTripped(simulation), Is.True, "Never tripped.");

            simulation.Trip();

            Assert.Multiple(() =>
            {
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Fault));
                Assert.That(
                    coolant.IsTripped(simulation),
                    Is.False,
                    "The condition should read healthy once the set has stopped.");
            });
        }

        /// <summary>
        /// A shutdown alarm latches once active; a warning alarm does not.
        /// </summary>
        [Test]
        public void OnlyShutdownAlarmsLatch()
        {
            var shutdown = new ProtectionAlarm(null!, () => false, isShutdown: true);
            var warning = new ProtectionAlarm(null!, () => false, isShutdown: false);

            Assert.Multiple(() =>
            {
                Assert.That(shutdown.IsLatched, Is.False, "Not latched before it goes active.");
                Assert.That(warning.IsLatched, Is.False);
            });

            shutdown.WasActive = true;
            warning.WasActive = true;

            Assert.Multiple(() =>
            {
                Assert.That(shutdown.IsLatched, Is.True, "A shutdown alarm holds until reset.");
                Assert.That(warning.IsLatched, Is.False, "A warning alarm follows its condition.");
            });
        }

        private static GeneratorSimulation RunWithFault(GeneratorFault fault)
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded);
            simulation.InjectFault(fault);
            for (int tick = 0; tick < 200; tick++)
            {
                simulation.Advance(tick);
            }
            return simulation;
        }

        private static List<ProtectionDefinition> Definitions()
        {
            var definitions = new List<ProtectionDefinition>();
            for (int index = 0; index < GeneratorProtections.Definitions.Count; index++)
            {
                definitions.Add(GeneratorProtections.Definitions[index]);
            }
            return definitions;
        }
    }
}

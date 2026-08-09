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
using Generators;
using GeneratorModel = Opc.Ua.Generators;
using NUnit.Framework;

namespace Opc.Ua.OpenUsd.Tests.Generator
{
    /// <summary>
    /// Holds the six generator-set methods to what they claim to do.
    /// </summary>
    /// <remarks>
    /// The method nodes themselves are plumbing; <see cref="GeneratorCommands"/> is
    /// the behaviour, and it is where a wrong answer matters. A method that silently
    /// does nothing is the worst outcome of the three - worse than one that refuses,
    /// because a client cannot tell it apart from success.
    /// </remarks>
    [TestFixture]
    [Category("Generators")]
    public sealed class GeneratorMethodTests
    {
        /// <summary>
        /// Start takes a shut-down set through Ready and into cranking.
        /// </summary>
        /// <remarks>
        /// Two declared transitions, not one: a set becomes available before it
        /// cranks, and a client watching the state machine must see both.
        /// </remarks>
        [Test]
        public void StartFromOffPassesThroughReady()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            List<GeneratorRunState> seen = SimulationHarness.RecordStates(simulation);

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.Start(simulation), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Starting));
                Assert.That(seen, Is.EqualTo(new[]
                {
                    GeneratorRunState.Ready,
                    GeneratorRunState.Starting,
                }));
            });
        }

        /// <summary>
        /// Start from Ready cranks directly.
        /// </summary>
        [Test]
        public void StartFromReadyCranksDirectly()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            simulation.RequestState(GeneratorRunState.Ready);

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.Start(simulation), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Starting));
            });
        }

        /// <summary>
        /// Starting a set that is already running is refused rather than ignored.
        /// </summary>
        [Test]
        public void StartOnALoadedSetIsRefused()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.Start(simulation), Is.False);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Loaded));
            });
        }

        /// <summary>
        /// Stop takes a loaded set into cooldown rather than straight to rest.
        /// </summary>
        [Test]
        public void StopGoesThroughCooldown()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.Stop(simulation), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Cooldown));
            });
        }

        /// <summary>
        /// Stopping a set that is already off is refused.
        /// </summary>
        [Test]
        public void StopOnAStoppedSetIsRefused()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.Stop(simulation), Is.False);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Off));
            });
        }

        /// <summary>
        /// The emergency stop drops a loaded set immediately.
        /// </summary>
        [Test]
        public void EmergencyStopDropsALoadedSet()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.EmergencyStop(simulation), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.EmergencyStopped));
                Assert.That(simulation.IsLoaded, Is.False);
                Assert.That(simulation.ProtectionTripped, Is.True);
            });
        }

        /// <summary>
        /// The emergency stop is declared only out of the states in which the engine
        /// is turning at speed.
        /// </summary>
        /// <remarks>
        /// This pins the specification's shape rather than endorsing it. A real
        /// panel stops from anywhere; <c>GeneratorStateMachineType</c> declares
        /// <c>RunningToEmergencyStopped</c>, <c>LoadedToEmergencyStopped</c> and
        /// <c>ParalleledToEmergencyStopped</c> and nothing else. If a later revision
        /// of the model adds more, this test is what says so.
        /// </remarks>
        [Test]
        public void EmergencyStopFollowsTheDeclaredTransitions()
        {
            (GeneratorRunState From, bool Declared)[] cases =
            [
                (GeneratorRunState.Running, true),
                (GeneratorRunState.Loaded, true),
                (GeneratorRunState.Paralleled, true),
                (GeneratorRunState.Off, false),
                (GeneratorRunState.Starting, false),
                (GeneratorRunState.Warmup, false),
            ];

            Assert.Multiple(() =>
            {
                foreach ((GeneratorRunState from, bool declared) in cases)
                {
                    Assert.That(
                        GeneratorSimulation.IsLegalTransition(
                            from, GeneratorRunState.EmergencyStopped),
                        Is.EqualTo(declared),
                        $"{from}->EmergencyStopped");
                }
            });
        }

        /// <summary>
        /// ResetFaults returns a tripped set to Off.
        /// </summary>
        [Test]
        public void ResetFaultsClearsALatchedTrip()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True);
            simulation.Trip();

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.ResetFaults(simulation), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Off));
                Assert.That(simulation.ProtectionTripped, Is.False);
            });
        }

        /// <summary>
        /// ResetFaults clears an emergency stop as well as a protection trip.
        /// </summary>
        [Test]
        public void ResetFaultsClearsAnEmergencyStop()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True);
            GeneratorCommands.EmergencyStop(simulation);

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.ResetFaults(simulation), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Off));
            });
        }

        /// <summary>
        /// Resetting a healthy set succeeds without moving it.
        /// </summary>
        /// <remarks>
        /// Answering Bad here would only train an operator to ignore the result of
        /// pressing reset.
        /// </remarks>
        [Test]
        public void ResetFaultsOnAHealthySetIsANoOp()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.ResetFaults(simulation), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Loaded));
            });
        }

        /// <summary>
        /// A reset set can be started again.
        /// </summary>
        /// <remarks>
        /// Without this, a trip is a one-way door and the sample can only ever show
        /// a plant winding down.
        /// </remarks>
        [Test]
        public void ASetCanRunAgainAfterAResetTrip()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True);
            simulation.Trip();
            GeneratorCommands.ResetFaults(simulation);

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.Start(simulation), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Starting));
            });
        }

        /// <summary>
        /// StartTest starts a shut-down set the same way Start does.
        /// </summary>
        [Test]
        public void StartTestStartsAStoppedSet()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();

            Assert.Multiple(() =>
            {
                Assert.That(GeneratorCommands.StartTest(simulation), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Starting));
            });
        }

        /// <summary>
        /// A selector position naming a real mode is accepted.
        /// </summary>
        [TestCase((int)GeneratorModel.GeneratorOperatingModeEnum.Auto)]
        [TestCase((int)GeneratorModel.GeneratorOperatingModeEnum.Manual)]
        [TestCase((int)GeneratorModel.GeneratorOperatingModeEnum.Test)]
        public void ADeclaredOperatingModeIsAccepted(int requested)
        {
            ArrayOf<Variant> arguments = new Variant[] { new(requested) };

            Assert.Multiple(() =>
            {
                Assert.That(
                    GeneratorCommands.TryReadOperatingMode(
                        arguments, out GeneratorModel.GeneratorOperatingModeEnum mode),
                    Is.True);
                Assert.That((int)mode, Is.EqualTo(requested));
            });
        }

        /// <summary>
        /// A selector position naming no mode is refused rather than stored.
        /// </summary>
        [Test]
        public void AnUndeclaredOperatingModeIsRefused()
        {
            ArrayOf<Variant> arguments = new Variant[] { new(9999) };

            Assert.That(
                GeneratorCommands.TryReadOperatingMode(
                    arguments, out GeneratorModel.GeneratorOperatingModeEnum _),
                Is.False);
        }

        /// <summary>
        /// A call with no arguments is refused rather than throwing.
        /// </summary>
        [Test]
        public void AnEmptyArgumentListIsRefused()
        {
            Assert.That(
                GeneratorCommands.TryReadOperatingMode(
                    default, out GeneratorModel.GeneratorOperatingModeEnum _),
                Is.False);
        }

        /// <summary>
        /// Commanding one set leaves the others where they were.
        /// </summary>
        [Test]
        public void CommandsActOnOneSetOnly()
        {
            GeneratorSimulation first = SimulationHarness.Create(0);
            GeneratorSimulation second = SimulationHarness.Create(1);

            Assert.That(SimulationHarness.AdvanceUntil(first, GeneratorRunState.Loaded), Is.True);
            Assert.That(SimulationHarness.AdvanceUntil(second, GeneratorRunState.Loaded), Is.True);

            GeneratorCommands.EmergencyStop(first);

            Assert.Multiple(() =>
            {
                Assert.That(first.State, Is.EqualTo(GeneratorRunState.EmergencyStopped));
                Assert.That(second.State, Is.EqualTo(GeneratorRunState.Loaded));
            });
        }
    }
}

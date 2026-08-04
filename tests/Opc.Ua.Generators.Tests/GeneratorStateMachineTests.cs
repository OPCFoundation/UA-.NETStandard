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
using System.Collections.Generic;
using System.Linq;
using Generators;
using NUnit.Framework;

namespace Opc.Ua.Generators.Tests
{
    /// <summary>
    /// Holds the simulated operating state machine to what the model declares.
    /// </summary>
    /// <remarks>
    /// The sample keeps two descriptions of the same state machine: a C# enum plus
    /// <see cref="GeneratorSimulation.IsLegalTransition"/>, which the physics uses,
    /// and <see cref="GeneratorStateMap"/>, which translates it onto the numbers
    /// <c>GeneratorStateMachineType</c> declares. Two descriptions of one thing
    /// drift, and when they do, a server reports a machine as running while its
    /// physics say it is stopped and nothing at run time notices. These tests are
    /// what stops that.
    /// </remarks>
    [TestFixture]
    [Category("Generators")]
    public sealed class GeneratorStateMachineTests
    {
        /// <summary>
        /// Every simulated state has a number in the model.
        /// </summary>
        [Test]
        public void EveryStateHasAModelStateNumber()
        {
            foreach (GeneratorRunState state in Enum.GetValues(typeof(GeneratorRunState))
                .Cast<GeneratorRunState>())
            {
                Assert.That(
                    GeneratorStateMap.StateNumbers.ContainsKey(state),
                    Is.True,
                    $"{state} has no state number.");
            }
        }

        /// <summary>
        /// No two states share a number, or a client cannot tell them apart.
        /// </summary>
        [Test]
        public void StateNumbersAreDistinct()
        {
            Assert.That(
                GeneratorStateMap.StateNumbers.Values.Distinct().Count(),
                Is.EqualTo(GeneratorStateMap.StateNumbers.Count));
        }

        /// <summary>
        /// The two descriptions of the state machine agree in both directions.
        /// </summary>
        /// <remarks>
        /// A transition the physics permits but the map lacks would move a machine
        /// without telling a client; one the map holds but the physics refuses would
        /// be dead weight that looks supported.
        /// </remarks>
        [Test]
        public void LegalTransitionsAndModelTransitionsAgree()
        {
            var states = Enum.GetValues(typeof(GeneratorRunState))
                .Cast<GeneratorRunState>()
                .ToArray();

            var missingFromMap = new List<string>();
            var missingFromSimulation = new List<string>();

            foreach (GeneratorRunState from in states)
            {
                foreach (GeneratorRunState to in states)
                {
                    bool legal = GeneratorSimulation.IsLegalTransition(from, to);
                    bool mapped = GeneratorStateMap.TransitionNumbers.ContainsKey((from, to));

                    if (legal && !mapped)
                    {
                        missingFromMap.Add($"{from}->{to}");
                    }
                    if (mapped && !legal)
                    {
                        missingFromSimulation.Add($"{from}->{to}");
                    }
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    missingFromMap,
                    Is.Empty,
                    "Transitions the simulation permits but the model map lacks.");
                Assert.That(
                    missingFromSimulation,
                    Is.Empty,
                    "Transitions the model map holds but the simulation refuses.");
            });
        }

        /// <summary>
        /// No two transitions share a number.
        /// </summary>
        [Test]
        public void TransitionNumbersAreDistinct()
        {
            Assert.That(
                GeneratorStateMap.TransitionNumbers.Values.Distinct().Count(),
                Is.EqualTo(GeneratorStateMap.TransitionNumbers.Count));
        }

        /// <summary>
        /// Every state can be reached from Off by legal moves.
        /// </summary>
        /// <remarks>
        /// A state no path reaches is a state the model declares and the sample
        /// never shows, which is worse than not declaring it.
        /// </remarks>
        [Test]
        public void EveryStateIsReachableFromOff()
        {
            var reached = new HashSet<GeneratorRunState> { GeneratorRunState.Off };
            var queue = new Queue<GeneratorRunState>();
            queue.Enqueue(GeneratorRunState.Off);

            GeneratorRunState[] states = Enum.GetValues(typeof(GeneratorRunState))
                .Cast<GeneratorRunState>()
                .ToArray();

            while (queue.Count > 0)
            {
                GeneratorRunState from = queue.Dequeue();
                foreach (GeneratorRunState to in states)
                {
                    if (GeneratorSimulation.IsLegalTransition(from, to) && reached.Add(to))
                    {
                        queue.Enqueue(to);
                    }
                }
            }

            Assert.That(reached, Is.EquivalentTo(states));
        }

        /// <summary>
        /// Every state except Off can get back to Off, so no set can be stranded.
        /// </summary>
        [Test]
        public void EveryStateCanReturnToOff()
        {
            GeneratorRunState[] states = Enum.GetValues(typeof(GeneratorRunState))
                .Cast<GeneratorRunState>()
                .ToArray();

            var canReturn = new HashSet<GeneratorRunState> { GeneratorRunState.Off };
            bool grew;
            do
            {
                grew = false;
                foreach (GeneratorRunState from in states)
                {
                    if (canReturn.Contains(from))
                    {
                        continue;
                    }
                    foreach (GeneratorRunState to in states)
                    {
                        if (GeneratorSimulation.IsLegalTransition(from, to) &&
                            canReturn.Contains(to))
                        {
                            canReturn.Add(from);
                            grew = true;
                            break;
                        }
                    }
                }
            }
            while (grew);

            Assert.That(canReturn, Is.EquivalentTo(states));
        }

        /// <summary>
        /// Every state the model declares is reached by the running simulation.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="EveryStateIsReachableFromOff"/>, which walks the
        /// declared transition table. This one drives the actual simulation, and it
        /// is the test that would have caught Synchronizing and Paralleled being
        /// declared, drawn in the README and never entered: a client browsing the
        /// state machine saw two states it could never observe.
        /// </remarks>
        [Test]
        public void EveryStateIsEnteredByTheRunningSimulation()
        {
            var seen = new HashSet<GeneratorRunState>();

            // Profile 0 energises a dead bus; a later profile synchronises onto a
            // live one, so both paths have to run to cover the model.
            foreach (int profile in new[] { 0, 1 })
            {
                GeneratorSimulation simulation = SimulationHarness.Create(profile);
                simulation.StateChanged = (from, to) => seen.Add(to);
                seen.Add(simulation.State);

                for (int tick = 0; tick < 4000; tick++)
                {
                    simulation.Advance(tick);
                }
            }

            // A protection trip and the emergency stop are commanded, not part of
            // the free-running cycle.
            GeneratorSimulation tripped = SimulationHarness.Create();
            SimulationHarness.AdvanceUntil(tripped, GeneratorRunState.Loaded);
            tripped.StateChanged = (from, to) => seen.Add(to);
            tripped.Trip();

            GeneratorSimulation stopped = SimulationHarness.Create();
            SimulationHarness.AdvanceUntil(stopped, GeneratorRunState.Loaded);
            stopped.StateChanged = (from, to) => seen.Add(to);
            stopped.RequestState(GeneratorRunState.EmergencyStopped);

            var missing = new List<GeneratorRunState>();
            foreach (GeneratorRunState state in Enum.GetValues(typeof(GeneratorRunState))
                .Cast<GeneratorRunState>())
            {
                if (!seen.Contains(state))
                {
                    missing.Add(state);
                }
            }

            Assert.That(missing, Is.Empty, "States the model declares but nothing enters.");
        }

        /// <summary>
        /// A start commanded through a method is counted, not just an automatic one.
        /// </summary>
        /// <remarks>
        /// The counter used to be incremented only in the automatic cycle, so the
        /// more a client used Start or StartTest the further NumberOfStarts drifted
        /// from the truth - the opposite of what a start counter is for.
        /// </remarks>
        [Test]
        public void ACommandedStartIsCounted()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();

            Assert.That(GeneratorCommands.Start(simulation), Is.True);
            simulation.Advance(0);

            Assert.That(
                SimulationHarness.LastStartCount(simulation),
                Is.EqualTo(1u),
                "A commanded start was not counted.");
        }

        /// <summary>
        /// The automatic cycle counts its starts exactly once each.
        /// </summary>
        [Test]
        public void AnAutomaticStartIsCountedOnce()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True);

            Assert.That(SimulationHarness.LastStartCount(simulation), Is.EqualTo(1u));
        }

        /// <summary>
        /// A set starts shut down rather than in whatever the enum's default is.
        /// </summary>
        [Test]
        public void ASimulationStartsOff()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Off));
        }

        /// <summary>
        /// A legal request is accepted and moves the set.
        /// </summary>
        [Test]
        public void ALegalRequestIsAccepted()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();

            Assert.Multiple(() =>
            {
                Assert.That(simulation.RequestState(GeneratorRunState.Ready), Is.True);
                Assert.That(simulation.State, Is.EqualTo(GeneratorRunState.Ready));
            });
        }

        /// <summary>
        /// An illegal request is refused and leaves the set where it was.
        /// </summary>
        [Test]
        public void AnIllegalRequestIsRefusedAndChangesNothing()
        {
            GeneratorRunState[] unreachableFromOff =
            [
                GeneratorRunState.Loaded,
                GeneratorRunState.Running,
                GeneratorRunState.Paralleled,
                GeneratorRunState.EmergencyStopped,
            ];

            Assert.Multiple(() =>
            {
                foreach (GeneratorRunState requested in unreachableFromOff)
                {
                    GeneratorSimulation simulation = SimulationHarness.Create();
                    Assert.That(
                        simulation.RequestState(requested),
                        Is.False,
                        $"Off->{requested} was accepted.");
                    Assert.That(
                        simulation.State,
                        Is.EqualTo(GeneratorRunState.Off),
                        $"Off->{requested} moved the set.");
                }
            });
        }

        /// <summary>
        /// Every state change is announced exactly once, in order.
        /// </summary>
        /// <remarks>
        /// The address-space state machine follows this callback, so a change that
        /// is not announced is a change a client never sees.
        /// </remarks>
        [Test]
        public void EveryStateChangeIsAnnouncedOnce()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            List<GeneratorRunState> seen = SimulationHarness.RecordStates(simulation);

            simulation.RequestState(GeneratorRunState.Ready);
            simulation.RequestState(GeneratorRunState.Starting);
            simulation.RequestState(GeneratorRunState.Warmup);
            simulation.RequestState(GeneratorRunState.Running);

            Assert.That(seen, Is.EqualTo(new[]
            {
                GeneratorRunState.Ready,
                GeneratorRunState.Starting,
                GeneratorRunState.Warmup,
                GeneratorRunState.Running,
            }));
        }

        /// <summary>
        /// A refused request announces nothing.
        /// </summary>
        [Test]
        public void ARefusedRequestAnnouncesNothing()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();
            List<GeneratorRunState> seen = SimulationHarness.RecordStates(simulation);

            simulation.RequestState(GeneratorRunState.Loaded);

            Assert.That(seen, Is.Empty);
        }

        /// <summary>
        /// Left alone, a set runs itself up to load rather than sitting at Off.
        /// </summary>
        /// <remarks>
        /// The sample's whole point is a plant that looks alive without a client
        /// driving it, so the automatic cycle is part of the contract.
        /// </remarks>
        [Test]
        public void ASetRunsItselfUpToLoad()
        {
            GeneratorSimulation simulation = SimulationHarness.Create();

            Assert.That(
                SimulationHarness.AdvanceUntil(simulation, GeneratorRunState.Loaded),
                Is.True,
                $"Stalled in {simulation.State}.");
        }

        /// <summary>
        /// Two sets advance independently of one another.
        /// </summary>
        [Test]
        public void SetsAdvanceIndependently()
        {
            GeneratorSimulation first = SimulationHarness.Create(0);
            GeneratorSimulation second = SimulationHarness.Create(1);

            first.RequestState(GeneratorRunState.Ready);

            Assert.Multiple(() =>
            {
                Assert.That(first.State, Is.EqualTo(GeneratorRunState.Ready));
                Assert.That(second.State, Is.EqualTo(GeneratorRunState.Off));
            });
        }
    }
}

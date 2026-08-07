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
using System.Runtime.CompilerServices;
using Generators;
using GeneratorModel = Opc.Ua.Generators;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.OpenUsd.Tests.Generator
{
    /// <summary>
    /// Records what a simulation publishes, so a test can build a
    /// <see cref="GeneratorSimulation"/> without an address space behind it.
    /// </summary>
    /// <typeparam name="TValue">Type of the published value.</typeparam>
    internal sealed class RecordingUpdater<TValue> : IValueUpdater<TValue>
    {
        /// <summary>
        /// Gets the last value published, if any.
        /// </summary>
        public TValue? Last { get; private set; }

        /// <summary>
        /// Gets the number of values published.
        /// </summary>
        public int Writes { get; private set; }

        /// <inheritdoc/>
        public void SetValue(TValue value)
        {
            Last = value;
            Writes++;
        }

        /// <inheritdoc/>
        public void SetValue(TValue value, StatusCode statusCode)
        {
            SetValue(value);
        }

        /// <inheritdoc/>
        public void SetValue(TValue value, StatusCode statusCode, DateTime sourceTimestamp)
        {
            SetValue(value);
        }

        /// <inheritdoc/>
        public void NotifyChange()
        {
        }
    }

    /// <summary>
    /// Builds generator simulations wired to recording updaters.
    /// </summary>
    /// <remarks>
    /// The simulation is the part of the sample that decides what state a set is in
    /// and which protections have tripped, so it is the part worth testing. Standing
    /// up a whole server to reach it would make these tests slow and flaky for no
    /// extra coverage of the logic that can actually be wrong.
    /// </remarks>
    internal static class SimulationHarness
    {
        /// <summary>
        /// The tick interval the sample's node manager uses.
        /// </summary>
        public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

        private static readonly ConditionalWeakTable<GeneratorSimulation, RecordingUpdater<uint>>
            s_startCounters = new();

        /// <summary>
        /// Creates a simulation for one set.
        /// </summary>
        /// <param name="profile">Zero-based per-set profile index.</param>
        /// <returns>A simulation publishing into recording updaters.</returns>
        public static GeneratorSimulation Create(int profile = 0)
        {
            var starts = new RecordingUpdater<uint>();
            var simulation = new GeneratorSimulation(
                profile,
                Interval,
                new EngineUpdaters(
                    Double(), Double(), Double(), Double(), Double(), Double(), Double(),
                    starts),
                new AlternatorUpdaters(
                    Double(), Double(), Double(), Double(), Double(), Double(), Double(), Double(),
                    [Phase(), Phase(), Phase()]),
                new PlantUpdaters(
                    Double(), Double(), Double(), Double(), Double(),
                    new RecordingUpdater<bool>(), new RecordingUpdater<bool>()));

            s_startCounters.Add(simulation, starts);
            return simulation;
        }

        /// <summary>
        /// Returns the start count a simulation last published.
        /// </summary>
        /// <param name="simulation">A simulation created by this harness.</param>
        /// <returns>The last published value of <c>Engine/NumberOfStarts</c>.</returns>
        /// <remarks>
        /// Read from the updater the set actually publishes through rather than from
        /// a field, so the test sees what a client would see.
        /// </remarks>
        public static uint LastStartCount(GeneratorSimulation simulation)
        {
            return s_startCounters.TryGetValue(simulation, out RecordingUpdater<uint>? starts)
                ? starts.Last
                : 0u;
        }

        /// <summary>
        /// Advances a simulation until it reaches a state, or gives up.
        /// </summary>
        /// <param name="simulation">The simulation to advance.</param>
        /// <param name="target">The state to wait for.</param>
        /// <param name="maxTicks">How many ticks to allow.</param>
        /// <returns><see langword="true"/> when the state was reached.</returns>
        public static bool AdvanceUntil(
            GeneratorSimulation simulation,
            GeneratorRunState target,
            int maxTicks = 2000)
        {
            for (int tick = 0; tick < maxTicks; tick++)
            {
                if (simulation.State == target)
                {
                    return true;
                }
                simulation.Advance(tick);
            }
            return simulation.State == target;
        }

        /// <summary>
        /// Records every state a simulation enters.
        /// </summary>
        /// <param name="simulation">The simulation to observe.</param>
        /// <returns>The list the observer appends to.</returns>
        public static List<GeneratorRunState> RecordStates(GeneratorSimulation simulation)
        {
            var seen = new List<GeneratorRunState>();
            simulation.StateChanged = (from, to) => seen.Add(to);
            return seen;
        }

        private static RecordingUpdater<double> Double()
        {
            return new RecordingUpdater<double>();
        }

        private static PhaseUpdaters Phase()
        {
            return new PhaseUpdaters(Double(), Double(), Double(), Double());
        }
    }
}

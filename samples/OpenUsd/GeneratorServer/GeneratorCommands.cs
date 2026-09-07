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
using Opc.Ua;
using Opc.Ua.Generators;

namespace Generators
{
    /// <summary>
    /// What each generator-set method means, expressed against the simulation
    /// rather than against address-space nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method nodes are plumbing; this is the behaviour. Keeping the two apart
    /// means the semantics can be held to account by a test without standing up a
    /// server, and it keeps the node manager's handlers down to one line each.
    /// </para>
    /// <para>
    /// Every command routes through <see cref="GeneratorSimulation.RequestState"/>,
    /// so legality is decided in exactly one place. A caller cannot drive a machine
    /// from <c>Off</c> straight to <c>Loaded</c> by picking the right method.
    /// </para>
    /// </remarks>
    internal static class GeneratorCommands
    {
        /// <summary>
        /// Starts a set that is shut down or waiting.
        /// </summary>
        /// <param name="simulation">The set's simulation.</param>
        /// <returns><see langword="true"/> when the set began starting.</returns>
        /// <remarks>
        /// From <c>Off</c> this is two declared transitions, not one: a set becomes
        /// available before it cranks. Collapsing them would publish a machine that
        /// never passed through <c>Ready</c>.
        /// </remarks>
        public static bool Start(GeneratorSimulation simulation)
        {
            return simulation.State switch
            {
                GeneratorRunState.Off =>
                    simulation.RequestState(GeneratorRunState.Ready)
                        && simulation.RequestState(GeneratorRunState.Starting),
                GeneratorRunState.Ready => simulation.RequestState(GeneratorRunState.Starting),
                _ => false,
            };
        }

        /// <summary>
        /// Stops a running set through its cooldown.
        /// </summary>
        /// <param name="simulation">The set's simulation.</param>
        /// <returns><see langword="true"/> when the set began stopping.</returns>
        /// <remarks>
        /// A normal stop always goes through cooldown; dropping a hot engine
        /// straight to rest is what <c>EmergencyStop</c> is for.
        /// </remarks>
        public static bool Stop(GeneratorSimulation simulation)
        {
            return simulation.RequestState(GeneratorRunState.Cooldown);
        }

        /// <summary>
        /// Trips a set on the emergency stop.
        /// </summary>
        /// <param name="simulation">The set's simulation.</param>
        /// <returns><see langword="true"/> when the set was stopped.</returns>
        /// <remarks>
        /// The model declares an emergency stop only out of the three states in
        /// which the engine is turning at speed, so this is refused from
        /// <c>Starting</c> and <c>Warmup</c>. That is the specification's shape, not
        /// this sample's choice, and it is left visible rather than papered over.
        /// </remarks>
        public static bool EmergencyStop(GeneratorSimulation simulation)
        {
            return simulation.RequestState(GeneratorRunState.EmergencyStopped);
        }

        /// <summary>
        /// Clears a latched shutdown.
        /// </summary>
        /// <param name="simulation">The set's simulation.</param>
        /// <returns><see langword="true"/> in every case that is not an error.</returns>
        /// <remarks>
        /// Resetting a healthy set is a no-op rather than an error. An operator
        /// pressing reset on a machine that is running has not done anything wrong,
        /// and answering <c>Bad</c> would only train them to ignore the result.
        /// </remarks>
        public static bool ResetFaults(GeneratorSimulation simulation)
        {
            return simulation.State is GeneratorRunState.Fault
                    or GeneratorRunState.EmergencyStopped
                ? simulation.RequestState(GeneratorRunState.Off)
                : true;
        }

        /// <summary>
        /// Starts a set for a scheduled exercise run.
        /// </summary>
        /// <param name="simulation">The set's simulation.</param>
        /// <returns><see langword="true"/> when the set began starting.</returns>
        /// <remarks>
        /// The same move as <see cref="Start"/>; the difference a client observes is
        /// the operating mode the caller publishes alongside it.
        /// </remarks>
        public static bool StartTest(GeneratorSimulation simulation)
        {
            return Start(simulation);
        }

        /// <summary>
        /// Reads a selector position out of a method's input arguments.
        /// </summary>
        /// <param name="arguments">The call's input arguments.</param>
        /// <param name="mode">Receives the requested mode.</param>
        /// <returns><see langword="true"/> when the argument named a real mode.</returns>
        public static bool TryReadOperatingMode(
            ArrayOf<Variant> arguments,
            out GeneratorOperatingModeEnum mode)
        {
            mode = default;
            if (arguments.Count < 1 || !arguments[0].TryGetValue(out int requested))
            {
                return false;
            }
            if (!Enum.IsDefined(typeof(GeneratorOperatingModeEnum), requested))
            {
                return false;
            }
            mode = (GeneratorOperatingModeEnum)requested;
            return true;
        }
    }
}

/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.PackML.Server
{
    /// <summary>
    /// A concrete equipment mode and its supported state subset.
    /// </summary>
    public sealed record PackMLMode
    {
        /// <summary>
        /// Gets the concrete enumeration value and administration array index.
        /// </summary>
        public int Value { get; init; } = 1;

        /// <summary>
        /// Gets the enumeration display name.
        /// </summary>
        public string Name { get; init; } = "Produce";

        /// <summary>
        /// Gets supported PackML state numbers. Empty selects the complete graph.
        /// </summary>
        public ArrayOf<uint> States { get; init; }

        /// <summary>
        /// Gets the stable state entered when a mode change completes.
        /// </summary>
        public PackMLInitialState InitialState { get; init; } = PackMLInitialState.Stopped;
    }

    /// <summary>
    /// Configuration captured before a PackML unit is published.
    /// </summary>
    public sealed class PackMLUnitOptions
    {
        /// <summary>
        /// Gets or sets configured modes. Concrete enum values are bounded to
        /// keep the specification's mode-indexed arrays finite.
        /// </summary>
        public ArrayOf<PackMLMode> Modes { get; set; } = [new PackMLMode()];

        /// <summary>
        /// Gets or sets the initial concrete mode.
        /// </summary>
        public int InitialMode { get; set; } = 1;

        /// <summary>
        /// Gets or sets states from which a mode change is permitted.
        /// </summary>
        public ArrayOf<uint> ModeChangeStates { get; set; } =
            [PackMLStateNumbers.Aborted, PackMLStateNumbers.Stopped];

        /// <summary>
        /// Gets or sets the optional remote interface capability.
        /// </summary>
        public bool EnableRemoteCommand { get; set; }

        /// <summary>
        /// Gets or sets the number of material readiness/interlock slots.
        /// Zero omits the optional operation and its status fields.
        /// </summary>
        public int MaterialCount { get; set; }

        /// <summary>
        /// Gets or sets an optional equipment identifier.
        /// </summary>
        public string? TagId { get; set; }

        /// <summary>
        /// Gets or sets whether to publish the PackML version property.
        /// </summary>
        public bool IncludeVersion { get; set; } = true;

        /// <summary>
        /// Gets or sets the permitted maximum speed.
        /// </summary>
        public float MaximumSpeed { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the clock used for timers and accepted setpoint timestamps.
        /// </summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        /// <summary>
        /// Gets or sets optional administrative timer persistence. Equipment
        /// production counters and alarm records remain provider-owned.
        /// </summary>
        public IPackMLAdministrationStore? AdministrationStore { get; set; }

        /// <summary>
        /// Gets or sets the maximum populations accepted from administration data.
        /// </summary>
        public PackMLAdministrationExtents AdministrationExtents { get; set; } = new();

        /// <summary>
        /// Gets or sets permissions applied to Admin and every administrative
        /// variable. Defaults to SecurityAdmin; samples can explicitly allow reads.
        /// Requires at least one explicit role entry; empty and null arrays are rejected.
        /// </summary>
        public ArrayOf<RolePermissionType> AdministrationRolePermissions { get; set; } =
        [
            new RolePermissionType
            {
                RoleId = Ua.ObjectIds.WellKnownRole_SecurityAdmin,
                Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.ReadRolePermissions)
            }
        ];

        /// <summary>
        /// Validates and freezes the mutable configuration for a unit.
        /// </summary>
        internal PackMLUnitOptions Capture()
        {
            if (Modes.Count == 0 || MaterialCount < 0 || MaterialCount > 4096 ||
                float.IsNaN(MaximumSpeed) || float.IsInfinity(MaximumSpeed) || MaximumSpeed <= 0 ||
                TimeProvider is null)
            {
                throw new ArgumentException("PackML modes, clock, material count or speed limits are invalid.");
            }
            var values = new HashSet<int>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (PackMLMode mode in Modes)
            {
                if (mode is null || mode.Value <= 0 || mode.Value > 255 ||
                    string.IsNullOrWhiteSpace(mode.Name) || !values.Add(mode.Value) || !names.Add(mode.Name))
                {
                    throw new ArgumentException("PackML modes require unique values (1-255) and names.");
                }
#if NET8_0_OR_GREATER
                bool defined = Enum.IsDefined(mode.InitialState);
#else
                bool defined = Enum.IsDefined(typeof(PackMLInitialState), mode.InitialState);
#endif
                if (!defined)
                {
                    throw new ArgumentException("The initial PackML state is unknown.");
                }
                ValidateStates(mode);
            }
            if (!values.Contains(InitialMode))
            {
                throw new ArgumentException("The initial PackML mode is not configured.");
            }
            foreach (uint state in ModeChangeStates)
            {
                if (state is not (PackMLStateNumbers.Aborted or PackMLStateNumbers.Stopped or
                    PackMLStateNumbers.Idle or PackMLStateNumbers.Held or PackMLStateNumbers.Suspended or
                    PackMLStateNumbers.Execute or PackMLStateNumbers.Complete))
                {
                    throw new ArgumentException("Mode changes require a stable configured state.");
                }
            }
            if (AdministrationRolePermissions.Count == 0)
            {
                throw new ArgumentException("Administration permissions require at least one explicit role entry.");
            }
            return new PackMLUnitOptions
            {
                Modes = Modes.ConvertAll(mode => mode with { States = mode.States.Span.ToArray().ToArrayOf() }),
                InitialMode = InitialMode,
                ModeChangeStates = ModeChangeStates.Span.ToArray().ToArrayOf(),
                EnableRemoteCommand = EnableRemoteCommand,
                MaterialCount = MaterialCount,
                TagId = TagId,
                IncludeVersion = IncludeVersion,
                MaximumSpeed = MaximumSpeed,
                TimeProvider = TimeProvider,
                AdministrationStore = AdministrationStore,
                AdministrationExtents = AdministrationExtents
                    ?? throw new ArgumentException("Administration capacities must be configured."),
                AdministrationRolePermissions = AdministrationRolePermissions.ConvertAll(
                    permission => (RolePermissionType)permission.Clone())
            };
        }

        /// <summary>
        /// Rejects invalid future modes before any equipment request can be accepted.
        /// </summary>
        private static void ValidateStates(PackMLMode mode)
        {
            if (mode.States.Count == 0)
            {
                return;
            }
            var states = new HashSet<uint>();
            foreach (uint state in mode.States)
            {
                if (state is < PackMLStateNumbers.Clearing or > PackMLStateNumbers.Cleared || !states.Add(state))
                {
                    throw new ArgumentException("PackML state subsets require unique standard state numbers.");
                }
            }
            uint[] initial = mode.InitialState switch
            {
                PackMLInitialState.Aborted => [PackMLStateNumbers.Aborted],
                PackMLInitialState.Stopped => [PackMLStateNumbers.Cleared, PackMLStateNumbers.Stopped],
                _ => [PackMLStateNumbers.Cleared, PackMLStateNumbers.Running, PackMLStateNumbers.Idle]
            };
            foreach (uint state in initial)
            {
                if (!states.Contains(state))
                {
                    throw new ArgumentException("A PackML mode excludes its initial state or parent.");
                }
            }
            foreach (uint state in states)
            {
                bool baseState = state is PackMLStateNumbers.Aborting or PackMLStateNumbers.Aborted or
                    PackMLStateNumbers.Cleared;
                bool machineState = state is PackMLStateNumbers.Clearing or PackMLStateNumbers.Stopped or
                    PackMLStateNumbers.Stopping or PackMLStateNumbers.Running;
                if ((!baseState && !states.Contains(PackMLStateNumbers.Cleared)) ||
                    (!baseState && !machineState && !states.Contains(PackMLStateNumbers.Running)))
                {
                    throw new ArgumentException("A PackML state subset excludes a required parent state.");
                }
            }
            (uint Acting, uint Stable)[] completions =
            [
                (PackMLStateNumbers.Clearing, PackMLStateNumbers.Stopped),
                (PackMLStateNumbers.Aborting, PackMLStateNumbers.Aborted),
                (PackMLStateNumbers.Stopping, PackMLStateNumbers.Stopped),
                (PackMLStateNumbers.Resetting, PackMLStateNumbers.Idle),
                (PackMLStateNumbers.Starting, PackMLStateNumbers.Execute),
                (PackMLStateNumbers.Holding, PackMLStateNumbers.Held),
                (PackMLStateNumbers.Unholding, PackMLStateNumbers.Execute),
                (PackMLStateNumbers.Suspending, PackMLStateNumbers.Suspended),
                (PackMLStateNumbers.Unsuspending, PackMLStateNumbers.Execute),
                (PackMLStateNumbers.Completing, PackMLStateNumbers.Complete)
            ];
            foreach ((uint acting, uint stable) in completions)
            {
                if (states.Contains(acting) && !states.Contains(stable))
                {
                    throw new ArgumentException("An acting state requires its equipment-completion destination.");
                }
            }
        }
    }
}

// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using Opc.Ua.Generators;

namespace Generators
{
    /// <summary>
    /// Translates the simulated operating state onto the numbers
    /// <c>GeneratorStateMachineType</c> declares.
    /// </summary>
    /// <remarks>
    /// Kept apart from the node manager because it is a statement about the model,
    /// not about how this server happens to publish it, and because the tests need
    /// to hold it against <see cref="GeneratorSimulation.IsLegalTransition"/>. Two
    /// tables that disagree produce a server whose state machine reports a machine
    /// as running while its physics say it is stopped, and nothing at run time
    /// notices.
    /// </remarks>
    internal static class GeneratorStateMap
    {
        /// <summary>
        /// Gets the model's state <em>node id</em> for every simulated
        /// state — the identifier of the StateType node the model
        /// declares, which is what <c>CurrentState/Id</c> must carry
        /// (the state <em>number</em> is a different value published by
        /// the StateNumber property).
        /// </summary>
        public static IReadOnlyDictionary<GeneratorRunState, uint> StateIds { get; } =
            new Dictionary<GeneratorRunState, uint>
            {
                [GeneratorRunState.Off] = GeneratorStateMachineTypeIds.StateIds.Off,
                [GeneratorRunState.Ready] = GeneratorStateMachineTypeIds.StateIds.Ready,
                [GeneratorRunState.Starting] = GeneratorStateMachineTypeIds.StateIds.Starting,
                [GeneratorRunState.Warmup] = GeneratorStateMachineTypeIds.StateIds.Warmup,
                [GeneratorRunState.Running] = GeneratorStateMachineTypeIds.StateIds.Running,
                [GeneratorRunState.Loaded] = GeneratorStateMachineTypeIds.StateIds.Loaded,
                [GeneratorRunState.Synchronizing] =
                    GeneratorStateMachineTypeIds.StateIds.Synchronizing,
                [GeneratorRunState.Paralleled] = GeneratorStateMachineTypeIds.StateIds.Paralleled,
                [GeneratorRunState.Cooldown] = GeneratorStateMachineTypeIds.StateIds.Cooldown,
                [GeneratorRunState.Stopping] = GeneratorStateMachineTypeIds.StateIds.Stopping,
                [GeneratorRunState.Fault] = GeneratorStateMachineTypeIds.StateIds.Fault,
                [GeneratorRunState.EmergencyStopped] =
                    GeneratorStateMachineTypeIds.StateIds.EmergencyStopped,
            };

        /// <summary>
        /// Gets the model's state number for every simulated state.
        /// </summary>
        public static IReadOnlyDictionary<GeneratorRunState, uint> StateNumbers { get; } =
            new Dictionary<GeneratorRunState, uint>
            {
                [GeneratorRunState.Off] = GeneratorStateMachineTypeIds.StateNumbers.Off,
                [GeneratorRunState.Ready] = GeneratorStateMachineTypeIds.StateNumbers.Ready,
                [GeneratorRunState.Starting] = GeneratorStateMachineTypeIds.StateNumbers.Starting,
                [GeneratorRunState.Warmup] = GeneratorStateMachineTypeIds.StateNumbers.Warmup,
                [GeneratorRunState.Running] = GeneratorStateMachineTypeIds.StateNumbers.Running,
                [GeneratorRunState.Loaded] = GeneratorStateMachineTypeIds.StateNumbers.Loaded,
                [GeneratorRunState.Synchronizing] =
                    GeneratorStateMachineTypeIds.StateNumbers.Synchronizing,
                [GeneratorRunState.Paralleled] = GeneratorStateMachineTypeIds.StateNumbers.Paralleled,
                [GeneratorRunState.Cooldown] = GeneratorStateMachineTypeIds.StateNumbers.Cooldown,
                [GeneratorRunState.Stopping] = GeneratorStateMachineTypeIds.StateNumbers.Stopping,
                [GeneratorRunState.Fault] = GeneratorStateMachineTypeIds.StateNumbers.Fault,
                [GeneratorRunState.EmergencyStopped] =
                    GeneratorStateMachineTypeIds.StateNumbers.EmergencyStopped,
            };

        /// <summary>
        /// Gets the model's transition number for every legal move.
        /// </summary>
        /// <remarks>
        /// Every entry is a transition the specification declares. A pair that is
        /// not here is not a legal move, which is why this table and
        /// <see cref="GeneratorSimulation.IsLegalTransition"/> are held against each
        /// other by <c>GeneratorStateMachineTests</c>.
        /// </remarks>
        public static IReadOnlyDictionary<(GeneratorRunState From, GeneratorRunState To), uint>
            TransitionNumbers
        { get; } = new Dictionary<(GeneratorRunState, GeneratorRunState), uint>
        {
            [(GeneratorRunState.Off, GeneratorRunState.Ready)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.OffToReady,
            [(GeneratorRunState.Ready, GeneratorRunState.Starting)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.ReadyToStarting,
            [(GeneratorRunState.Ready, GeneratorRunState.Off)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.ReadyToOff,
            [(GeneratorRunState.Starting, GeneratorRunState.Warmup)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.StartingToWarmup,
            [(GeneratorRunState.Starting, GeneratorRunState.Fault)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.StartingToFault,
            [(GeneratorRunState.Warmup, GeneratorRunState.Running)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.WarmupToRunning,
            [(GeneratorRunState.Running, GeneratorRunState.Loaded)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.RunningToLoaded,
            [(GeneratorRunState.Running, GeneratorRunState.Synchronizing)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.RunningToSynchronizing,
            [(GeneratorRunState.Running, GeneratorRunState.Cooldown)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.RunningToCooldown,
            [(GeneratorRunState.Running, GeneratorRunState.Fault)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.RunningToFault,
            [(GeneratorRunState.Running, GeneratorRunState.EmergencyStopped)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.RunningToEmergencyStopped,
            [(GeneratorRunState.Synchronizing, GeneratorRunState.Paralleled)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.SynchronizingToParalleled,
            [(GeneratorRunState.Synchronizing, GeneratorRunState.Running)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.SynchronizingToRunning,
            [(GeneratorRunState.Paralleled, GeneratorRunState.Loaded)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.ParalleledToLoaded,
            [(GeneratorRunState.Paralleled, GeneratorRunState.Fault)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.ParalleledToFault,
            [(GeneratorRunState.Paralleled, GeneratorRunState.EmergencyStopped)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.ParalleledToEmergencyStopped,
            [(GeneratorRunState.Loaded, GeneratorRunState.Cooldown)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.LoadedToCooldown,
            [(GeneratorRunState.Loaded, GeneratorRunState.Fault)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.LoadedToFault,
            [(GeneratorRunState.Loaded, GeneratorRunState.EmergencyStopped)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.LoadedToEmergencyStopped,
            [(GeneratorRunState.Cooldown, GeneratorRunState.Stopping)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.CooldownToStopping,
            [(GeneratorRunState.Stopping, GeneratorRunState.Off)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.StoppingToOff,
            [(GeneratorRunState.Fault, GeneratorRunState.Off)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.FaultToOff,
            [(GeneratorRunState.EmergencyStopped, GeneratorRunState.Off)] =
                GeneratorStateMachineTypeIds.TransitionNumbers.EmergencyStoppedToOff,
        };
    }
}

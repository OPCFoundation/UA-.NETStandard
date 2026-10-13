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
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// The three standard OPC 40084-1 executing substates.
    /// </summary>
    public enum ExtrusionExecutingState
    {
        /// <summary>
        /// Tempering is ready, but the device is not moving.
        /// </summary>
        ReadyToRun,

        /// <summary>
        /// The device is running with manually selected parameters.
        /// </summary>
        ManualRun,

        /// <summary>
        /// The device is running with controlled parameters.
        /// </summary>
        ControlledRun
    }

    /// <summary>
    /// An absolute state-machine commit from the authoritative device.
    /// </summary>
    /// <param name="Revision">The device's committed snapshot revision.</param>
    /// <param name="ItemState">The Machinery item state.</param>
    /// <param name="OperationMode">The independent Machinery operation mode.</param>
    /// <param name="ExecutingState">The substate, inactive unless ItemState is Executing.</param>
    /// <param name="SourceTimestamp">The authority's source timestamp, not a locally advanced clock.</param>
    public sealed record ExtrusionOperatingSnapshot(
        ulong Revision,
        MachineryItemStateValue ItemState,
        MachineryOperationModeValue OperationMode,
        ExtrusionExecutingState ExecutingState,
        DateTimeUtc SourceTimestamp);

    /// <summary>
    /// Projects the actual two Machinery machines and the extrusion executing submachine.
    /// </summary>
    /// <remarks>
    /// Uses the stack's Part 16 dispatcher on unregistered generated staging states, then
    /// publishes all changed values atomically. State numbers and NodeIds are read from the
    /// loaded model; this module does not create a replacement state machine.
    /// </remarks>
    public sealed class ExtrusionStateController : IAsyncDisposable
    {
        /// <summary>
        /// Prepares staging states from the exact generated model types.
        /// </summary>
        internal ExtrusionStateController(
            ExtrusionRuntime runtime,
            ExtrusionDeviceState root,
            Func<ExtrusionSnapshot, CancellationToken, ValueTask> publish)
        {
            m_runtime = runtime;
            m_publish = publish;
            m_item = CreateProjection(root, "MachineryBuildingBlocks/MachineryItemState");
            m_mode = CreateProjection(root, "MachineryBuildingBlocks/MachineryOperationMode");
            m_executing = CreateProjection(root,
                "MachineryBuildingBlocks/MachineryItemState/ExtrusionExecutingSubState");
        }

        /// <summary>
        /// Applies one externally committed state, never simulating production or changing a job counter.
        /// </summary>
        public ValueTask ApplyAsync(
            ExtrusionOperatingSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            return ApplyAsync(snapshot, [], cancellationToken);
        }

        /// <summary>
        /// Atomically publishes operating state and other values from the same authoritative commit.
        /// </summary>
        public async ValueTask ApplyAsync(
            ExtrusionOperatingSnapshot snapshot,
            ArrayOf<ExtrusionValue> additionalValues,
            CancellationToken cancellationToken = default)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if ((uint)snapshot.ItemState > 3 || (uint)snapshot.OperationMode > 3 ||
                (uint)snapshot.ExecutingState > 2 || snapshot.SourceTimestamp == DateTimeUtc.MinValue)
            {
                throw new ArgumentOutOfRangeException(nameof(snapshot));
            }
            using ExtrusionOperationLifetime.Operation pending = m_lifetime.Begin(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                m_lifetime.ThrowIfStopped();
                var values = new List<ExtrusionValue>();
                Stage(m_item, snapshot.ItemState.ToString(), true, snapshot.SourceTimestamp, values);
                Stage(m_mode, snapshot.OperationMode.ToString(), true, snapshot.SourceTimestamp, values);
                Stage(m_executing, snapshot.ExecutingState.ToString(),
                    snapshot.ItemState == MachineryItemStateValue.Executing, snapshot.SourceTimestamp, values);
                values.AddRange(additionalValues);
                await m_publish(new ExtrusionSnapshot(snapshot.Revision, values.ToArrayOf()), pending.Token)
                    .ConfigureAwait(false);
                m_item.Previous = snapshot.ItemState.ToString();
                m_mode.Previous = snapshot.OperationMode.ToString();
                m_executing.Previous = snapshot.ItemState == MachineryItemStateValue.Executing
                    ? snapshot.ExecutingState.ToString() : null;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref m_disposalStarted, 1, 0) == 0)
            {
                try
                {
                    try
                    {
                        await m_lifetime.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_gate.Dispose();
                    }
                    m_disposed.TrySetResult(true);
                }
                catch (Exception failure)
                {
                    m_disposed.TrySetException(failure);
                }
            }
            await m_disposed.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Constructs a registered-factory staging instance and the model's actual numeric entries.
        /// </summary>
        private Projection CreateProjection(ExtrusionDeviceState root, string path)
        {
            var actual = ExtrusionNodeAccess.Find(m_runtime.SystemContext, root, path) as FiniteStateMachineState
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Required extrusion state machine '{0}' is missing.", path);
            var staged = m_runtime.SystemContext.NodeStateFactory.CreateInstance(
                m_runtime.SystemContext, null, NodeClass.Object, actual.BrowseName,
                actual.ReferenceTypeId, actual.TypeDefinitionId) as FiniteStateMachineState
                ?? throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "State factory is missing.");
            staged.Create(m_runtime.SystemContext, actual);
            staged.CreateAsPredefinedNode(m_runtime.SystemContext);
            NodeState definition = m_runtime.Manager.FindPredefinedNode(actual.TypeDefinitionId)
                ?? throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                    "State definition is missing.");
            var states = new List<FiniteStateMachineEntry>();
            var transitions = new List<FiniteStateMachineEntry>();
            foreach (BaseInstanceState node in ExtrusionNodeAccess.Children(m_runtime.SystemContext, definition))
            {
                if (!node.NodeId.TryGetValue(out uint identifier))
                {
                    continue;
                }
                if (node is StateMachineStateState state && state.StateNumber != null)
                {
                    states.Add(new FiniteStateMachineEntry(identifier, state.StateNumber.Value, node.BrowseName.Name!));
                }
                else if (node is StateMachineTransitionState transition && transition.TransitionNumber != null)
                {
                    transitions.Add(new FiniteStateMachineEntry(
                        identifier, transition.TransitionNumber.Value, node.BrowseName.Name!));
                }
            }
            if (states.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                    "No declared states were loaded.");
            }
            return new Projection(actual, staged, new FiniteStateMachineDispatcher(
                actual.TypeDefinitionId.NamespaceIndex, states.ToArrayOf(), transitions.ToArrayOf()),
                states.ToArrayOf(), transitions.ToArrayOf());
        }

        /// <summary>
        /// Stages a model-defined state and transition before collecting its complete Value metadata.
        /// </summary>
        private void Stage(
            Projection projection,
            string name,
            bool active,
            DateTimeUtc timestamp,
            List<ExtrusionValue> values)
        {
            FiniteStateMachineEntry state = projection.States.Find(entry => entry.Name == name);
            if (state.Id == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "The state '{0}' is not declared.", name);
            }
            if (active)
            {
                projection.Dispatcher.ApplyState(projection.Staged, state.Id, m_runtime.SystemContext);
                string transitionName = "From" + projection.Previous + "To" + name;
                FiniteStateMachineEntry transition = projection.Transitions
                    .Find(entry => entry.Name == transitionName);
                if (projection.Previous != null && transition.Id != 0)
                {
                    projection.Dispatcher.ApplyTransition(
                        projection.Staged, transition.Id, m_runtime.SystemContext);
                }
            }
            foreach ((string path, BaseInstanceState node) in
                ExtrusionNodeAccess.Walk(m_runtime.SystemContext, projection.Actual))
            {
                if (node is not BaseVariableState variable ||
                    (!path.StartsWith("CurrentState", StringComparison.Ordinal) &&
                        !path.StartsWith("LastTransition", StringComparison.Ordinal)))
                {
                    continue;
                }
                if (ExtrusionNodeAccess.Find(m_runtime.SystemContext, projection.Staged, path)
                    is not BaseVariableState staged)
                {
                    continue;
                }
                Variant value = path.EndsWith("TransitionTime", StringComparison.Ordinal)
                    ? Variant.From(timestamp) : staged.WrappedValue;
                values.Add(new ExtrusionValue(variable.NodeId, new DataValue(
                    value, active ? StatusCodes.Good : StatusCodes.BadStateNotActive, timestamp)));
            }
        }

        /// <summary>
        /// Holds the generated real/staging instances and their model-derived dispatcher.
        /// </summary>
        private sealed class Projection(
            FiniteStateMachineState actual,
            FiniteStateMachineState staged,
            FiniteStateMachineDispatcher dispatcher,
            ArrayOf<FiniteStateMachineEntry> states,
            ArrayOf<FiniteStateMachineEntry> transitions)
        {
            /// <summary>
            /// Gets the live generated machine.
            /// </summary>
            internal FiniteStateMachineState Actual { get; } = actual;

            /// <summary>
            /// Gets the unregistered staging machine.
            /// </summary>
            internal FiniteStateMachineState Staged { get; } = staged;

            /// <summary>
            /// Gets the shared stack dispatcher.
            /// </summary>
            internal FiniteStateMachineDispatcher Dispatcher { get; } = dispatcher;

            /// <summary>
            /// Gets the real model states.
            /// </summary>
            internal ArrayOf<FiniteStateMachineEntry> States { get; } = states;

            /// <summary>
            /// Gets the real model transitions.
            /// </summary>
            internal ArrayOf<FiniteStateMachineEntry> Transitions { get; } = transitions;

            /// <summary>
            /// Gets or sets the last successfully committed state name.
            /// </summary>
            internal string? Previous { get; set; }
        }

        /// <summary>The hosting runtime and model resolver.</summary>
        private readonly ExtrusionRuntime m_runtime;

        /// <summary>The atomic absolute-state publisher.</summary>
        private readonly Func<ExtrusionSnapshot, CancellationToken, ValueTask> m_publish;

        /// <summary>The Machinery item state.</summary>
        private readonly Projection m_item;

        /// <summary>The independent Machinery operation mode.</summary>
        private readonly Projection m_mode;

        /// <summary>The extrusion-specific execution submachine.</summary>
        private readonly Projection m_executing;

        /// <summary>Serializes access to unregistered staging state.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Counts and cancels publications before they can wait for staging access.</summary>
        private readonly ExtrusionOperationLifetime m_lifetime = new(nameof(ExtrusionStateController));

        /// <summary>Claims the single terminal cleanup.</summary>
        private int m_disposalStarted;

        /// <summary>Shares completion after all staging access and resource release.</summary>
        private readonly TaskCompletionSource<bool> m_disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

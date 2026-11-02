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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.LADS.Server.StateMachines;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The handle of a functional unit.
    /// </summary>
    internal sealed class LadsFunctionalUnit : ILadsFunctionalUnit, IAsyncDisposable
    {
        /// <summary>
        /// Creates the handle and binds the methods of the functional unit state machine.
        /// </summary>
        public LadsFunctionalUnit(LadsDevice device, FunctionalUnitObjectState node)
        {
            Device = device;
            Node = node;
            ISystemContext context = Runtime.Context;
            FunctionalUnitStateMachineState stateMachine = node.FunctionalUnitState ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The functional unit state machine is missing.");
            stateMachine.AddStart(context);
            stateMachine.AddStop(context).AddAbort(context).AddClear(context).AddRunningStateMachine(context);
            Functional = new LadsStateMachine<LadsFunctionalStateValue>(
                stateMachine,
                context,
                LadsStateMaps.Functional,
                LadsFunctionalStateValue.Stopped);
            Running = new LadsStateMachine<LadsRunningStateValue>(
                stateMachine.RunningStateMachine!,
                context,
                LadsStateMaps.Running,
                LadsRunningStateValue.Idle);
            Functional.StateChanged += state =>
            {
                if (Runtime.Logger.IsEnabled(LogLevel.Debug))
                {
                    Runtime.Logger.StateChanged(Name, state);
                }
                StateChanged?.Invoke(state);
                Device.OnFunctionalUnitStateChanged();
            };
            Runtime.Bind(stateMachine.Start, (_, ct) => StartAsync(ct));
            Runtime.Bind(stateMachine.Stop, (_, ct) => StopAsync(ct));
            Runtime.Bind(stateMachine.Abort, (_, ct) => AbortAsync(ct));
            Runtime.Bind(stateMachine.Clear, (_, ct) => ClearAsync(ct));
        }

        /// <inheritdoc/>
        public string Name => Node.BrowseName.Name ?? string.Empty;

        /// <inheritdoc/>
        public NodeId NodeId => Node.NodeId;

        /// <inheritdoc/>
        ILadsDevice ILadsFunctionalUnit.Device => Device;

        /// <summary>
        /// Gets the device.
        /// </summary>
        public LadsDevice Device { get; }

        /// <summary>
        /// Gets the node of the functional unit.
        /// </summary>
        public FunctionalUnitObjectState Node { get; }

        /// <summary>
        /// Gets the runtime.
        /// </summary>
        public LadsRuntime Runtime => Device.Runtime;

        /// <summary>
        /// Gets the functional unit state machine.
        /// </summary>
        public LadsStateMachine<LadsFunctionalStateValue> Functional { get; }

        /// <summary>
        /// Gets the running state machine.
        /// </summary>
        public LadsStateMachine<LadsRunningStateValue> Running { get; }

        /// <summary>
        /// Gets the gate that serializes the commands of the functional unit and its programs.
        /// </summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>
        /// Gets the provider of the functions.
        /// </summary>
        public ILadsFunctionProvider FunctionProvider => m_functionProvider ?? Runtime.FunctionProvider;

        /// <inheritdoc/>
        public LadsFunctionalStateValue State => Functional.Current;

        /// <inheritdoc/>
        public LadsRunningStateValue RunningState => Running.Current;

        /// <inheritdoc/>
        public ArrayOf<ILadsFunction> Functions
        {
            get
            {
                lock (m_lock)
                {
                    var functions = new List<ILadsFunction>(m_functions.Count);
                    foreach (LadsFunction function in m_functions.Values)
                    {
                        if (!function.Path.Contains('/', StringComparison.Ordinal))
                        {
                            functions.Add(function);
                        }
                    }
                    return functions.ToArray();
                }
            }
        }

        /// <inheritdoc/>
        public ILadsProgramManager? Programs => m_programs;

        /// <summary>
        /// Gets the program manager.
        /// </summary>
        internal LadsProgramManager? ProgramManager => m_programs;

        /// <inheritdoc/>
        public event Action<LadsFunctionalStateValue>? StateChanged;

        /// <inheritdoc/>
        public TFunction GetFunction<TFunction>(string name)
            where TFunction : class, ILadsFunction
        {
            if (TryGetFunction(name, out TFunction? function))
            {
                return function;
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNotFound,
                "The functional unit '{0}' has no function '{1}' of type {2}.",
                Name,
                name,
                typeof(TFunction).Name);
        }

        /// <inheritdoc/>
        public bool TryGetFunction<TFunction>(string name, [NotNullWhen(true)] out TFunction? function)
            where TFunction : class, ILadsFunction
        {
            lock (m_lock)
            {
                if (name != null && m_functions.TryGetValue(name, out LadsFunction? found) &&
                    found is TFunction typed)
                {
                    function = typed;
                    return true;
                }
            }
            function = null;
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Device.RequireOperate();
                Functional.Require(LadsFunctionalStateValue.Stopped);
                await TransitionAsync(Functional, cancellationToken, LadsFunctionalStateValue.Running)
                    .ConfigureAwait(false);
                Running.SetState(LadsRunningStateValue.Idle);
                await Running.TransitionThroughAsync(
                    cancellationToken,
                    LadsRunningStateValue.Starting,
                    LadsRunningStateValue.Execute).ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            return EndAsync(
                LadsProgramOutcome.Stopped,
                LadsFunctionalStateValue.Stopping,
                LadsFunctionalStateValue.Stopped,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask AbortAsync(CancellationToken cancellationToken = default)
        {
            return EndAsync(
                LadsProgramOutcome.Aborted,
                LadsFunctionalStateValue.Aborting,
                LadsFunctionalStateValue.Aborted,
                cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Functional.Require(LadsFunctionalStateValue.Aborted);
                await TransitionAsync(
                    Functional,
                    cancellationToken,
                    LadsFunctionalStateValue.Clearing,
                    LadsFunctionalStateValue.Stopped).ConfigureAwait(false);
                Running.SetState(LadsRunningStateValue.Idle);
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Stops or aborts the functional unit, ending the active program run first.
        /// </summary>
        private async ValueTask EndAsync(
            LadsProgramOutcome outcome,
            LadsFunctionalStateValue transient,
            LadsFunctionalStateValue final,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                if (m_programs != null &&
                    await m_programs.EndActiveRunAsync(outcome, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
                await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (m_programs?.HasActiveRun == true)
                    {
                        continue;
                    }
                    Functional.Require(LadsFunctionalStateValue.Running);
                    await TransitionAsync(Functional, cancellationToken, transient, final).ConfigureAwait(false);
                    Running.SetState(LadsRunningStateValue.Idle);
                    return;
                }
                finally
                {
                    Gate.Release();
                }
            }
        }

        /// <summary>
        /// Applies transitions and rejects a vetoed transition.
        /// </summary>
        /// <typeparam name="TEnum">The enum type.</typeparam>
        internal async ValueTask TransitionAsync<TEnum>(
            LadsStateMachine<TEnum> machine,
            CancellationToken cancellationToken,
            params TEnum[] states)
            where TEnum : struct, Enum
        {
            if (!await machine.TransitionThroughAsync(cancellationToken, states).ConfigureAwait(false))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "A transition of the functional unit '{0}' was vetoed.",
                    Name);
            }
        }

        /// <summary>
        /// Returns whether a function with the path exists.
        /// </summary>
        internal bool HasFunction(string path)
        {
            lock (m_lock)
            {
                return m_functions.ContainsKey(path);
            }
        }

        /// <summary>
        /// Registers a function.
        /// </summary>
        internal void AddFunction(LadsFunction function)
        {
            lock (m_lock)
            {
                m_functions.Add(function.Path, function);
            }
        }

        /// <summary>
        /// Creates the function set on first use.
        /// </summary>
        internal NodeState EnsureFunctionSet()
        {
            if (Node.FunctionSet == null)
            {
                Node.AddFunctionSet(Runtime.Context);
            }
            return Node.FunctionSet!;
        }

        /// <summary>
        /// Creates the program manager on first use.
        /// </summary>
        internal LadsProgramManager EnsureProgramManager()
        {
            if (m_programs == null)
            {
                ISystemContext context = Runtime.Context;
                Node.AddProgramManager(context);
                FunctionalUnitStateMachineState stateMachine = Node.FunctionalUnitState!;
                stateMachine.AddStartProgram(context);
                m_programs = new LadsProgramManager(this, Node.ProgramManager!);
                Runtime.Bind(stateMachine.StartProgram, m_programs.OnStartProgramAsync);
            }
            return m_programs;
        }

        /// <summary>
        /// Adds a supported property.
        /// </summary>
        internal void AddSupportedProperty(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A supported property needs a name.", nameof(name));
            }
            ISystemContext context = Runtime.Context;
            if (Node.SupportedPropertiesSet == null)
            {
                Node.AddSupportedPropertiesSet(context);
            }
            NodeState set = Node.SupportedPropertiesSet!;
            if (Runtime.FindChild<BaseInstanceState>(set, name) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The supported property '{0}' already exists.",
                    name);
            }
            set.AddChild(LadsRuntime.Named(
                context.CreateInstanceOfSupportedPropertyType(set, Runtime.InstanceName(name)),
                global::Opc.Ua.ReferenceTypeIds.HasComponent));
        }

        /// <summary>
        /// Sets the provider of the functions.
        /// </summary>
        internal void SetFunctionProvider(ILadsFunctionProvider provider)
        {
            m_functionProvider = provider;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_programs != null)
            {
                await m_programs.DisposeAsync().ConfigureAwait(false);
            }
            LadsFunction[] functions;
            lock (m_lock)
            {
                functions = [.. m_functions.Values];
            }
            foreach (LadsFunction function in functions)
            {
                function.Dispose();
            }
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, LadsFunction> m_functions = new(StringComparer.Ordinal);
        private ILadsFunctionProvider? m_functionProvider;
        private LadsProgramManager? m_programs;
    }
}

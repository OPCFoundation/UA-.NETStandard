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
using Opc.Ua.Programs;

namespace Opc.Ua.Server.Programs
{
    /// <summary>
    /// Describes a concrete Program type and its explicitly selected execution and lifetime policy.
    /// </summary>
    public sealed class ProgramDefinition
    {
        /// <summary>
        /// Creates a definition. UInt32.MaxValue denotes unlimited limits by SDK convention.
        /// </summary>
        public ProgramDefinition(
            ExpandedNodeId typeId,
            Func<NodeState?, ProgramStateMachineState> nodeFactory,
            IProgramProvider provider,
            ArrayOf<ProgramControlMethod> controls,
            ProgramState initialState,
            uint maxInstanceCount,
            uint maxRecycleCount)
        {
            if (typeId.IsNull)
            {
                throw new ArgumentNullException(nameof(typeId));
            }
            if (initialState is not (ProgramState.Ready or ProgramState.Halted))
            {
                throw new ArgumentOutOfRangeException(nameof(initialState));
            }
            var unique = new HashSet<ProgramControlMethod>();
            foreach (ProgramControlMethod control in controls)
            {
                if (control is not (ProgramControlMethod.Start or ProgramControlMethod.Suspend or
                    ProgramControlMethod.Resume or ProgramControlMethod.Halt or ProgramControlMethod.Reset))
                {
                    throw new ArgumentOutOfRangeException(nameof(controls));
                }
                if (!unique.Add(control))
                {
                    throw new ArgumentException("Program controls must be unique.", nameof(controls));
                }
            }
            TypeId = typeId;
            NodeFactory = nodeFactory ?? throw new ArgumentNullException(nameof(nodeFactory));
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            Controls = controls;
            InitialState = initialState;
            MaxInstanceCount = maxInstanceCount;
            MaxRecycleCount = maxRecycleCount;
        }

        /// <summary>
        /// Gets the namespace-independent identity of the concrete Program type.
        /// </summary>
        public ExpandedNodeId TypeId { get; }

        /// <summary>
        /// Gets the generated node-state factory.
        /// </summary>
        public Func<NodeState?, ProgramStateMachineState> NodeFactory { get; }

        /// <summary>
        /// Gets the provider that creates independently owned invocation resources.
        /// </summary>
        public IProgramProvider Provider { get; }

        /// <summary>
        /// Gets the supported control methods.
        /// </summary>
        public ArrayOf<ProgramControlMethod> Controls { get; }

        /// <summary>
        /// Gets the initial state, Ready or Halted.
        /// </summary>
        public ProgramState InitialState { get; }

        /// <summary>
        /// Gets the maximum number of live invocations of this type.
        /// </summary>
        public uint MaxInstanceCount { get; }

        /// <summary>
        /// Gets the maximum number of restarts after the first accepted Start.
        /// </summary>
        public uint MaxRecycleCount { get; }

        /// <summary>
        /// Gets whether an authorized client may create invocations.
        /// </summary>
        public bool Creatable { get; init; }

        /// <summary>
        /// Gets whether an authorized client may delete Halted invocations.
        /// </summary>
        public bool Deletable { get; init; }

        /// <summary>
        /// Gets whether execution termination removes the invocation after event capture.
        /// </summary>
        public bool AutoDelete { get; init; }

        /// <summary>
        /// Gets whether the modern ProgramDiagnostic structure is exposed.
        /// </summary>
        public bool DiagnosticsEnabled { get; init; } = true;

        /// <summary>
        /// Gets the concrete event factory selected for each transition identifier.
        /// </summary>
        public Func<NodeId, ProgramTransitionEventState> TransitionEventFactory { get; init; } =
            static _ => new ManagedProgramTransitionEventState(null);

        /// <summary>
        /// Gets the concrete audit event factory selected for each transition identifier.
        /// </summary>
        public Func<NodeId, AuditProgramTransitionEventState> AuditEventFactory { get; init; } =
            static _ => new ManagedProgramAuditEventState(null);

        /// <summary>
        /// Gets an adapter for an application-defined Create signature. Null selects the SDK signature.
        /// </summary>
        public IProgramCreationAdapter? CreationAdapter { get; init; }
    }

    /// <summary>
    /// Creates an execution resource for each Program invocation.
    /// </summary>
    public interface IProgramProvider
    {
        /// <summary>
        /// Creates resources; ownership transfers to the runtime on successful return.
        /// </summary>
        ValueTask<IProgramExecution> CreateAsync(
            ProgramInvocationContext context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Application execution behind a Program. Control accepts a command, not the entire workload.
    /// </summary>
    public interface IProgramExecution : IAsyncDisposable
    {
        /// <summary>
        /// Accepts or rejects a serialized control. The lifetime token is independent of the call token.
        /// </summary>
        ValueTask<ProgramControlResult> ControlAsync(
            ProgramControlContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Produces ordered internal transitions for the lifetime of this invocation.
        /// </summary>
        IAsyncEnumerable<ProgramExecutionUpdate> ObserveAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Identifies an invocation without exposing mutable nodes or synchronization.
    /// </summary>
    public sealed record ProgramInvocationContext(
        NodeId InvocationId,
        NodeId TypeId,
        TimeProvider TimeProvider,
        ITelemetryContext Telemetry,
        CancellationToken Lifetime,
        ArrayOf<Variant> CreationArguments = default);

    /// <summary>
    /// Supplies a Create adapter with the authenticated request and its allowed hosting scope.
    /// </summary>
    public sealed record ProgramCreationContext(
        ISystemContext SystemContext,
        NodeId TypeId,
        NodeId RootId,
        ushort NamespaceIndex);

    /// <summary>
    /// Normalizes application-specific creation arguments before common authorization and admission.
    /// </summary>
    public sealed record ProgramCreationRequest(
        NodeId ParentId,
        QualifiedName BrowseName,
        ArrayOf<Variant> ProviderArguments = default);

    /// <summary>
    /// Adapts a declared Create signature without bypassing Program lifetime ownership.
    /// </summary>
    public interface IProgramCreationAdapter
    {
        /// <summary>
        /// Interprets validated inputs without creating resources or starting execution.
        /// </summary>
        ValueTask<ProgramCreationRequest> ParseAsync(
            ProgramCreationContext context,
            ArrayOf<Variant> inputArguments,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Builds the declared outputs. Failure causes the newly created invocation to be removed.
        /// </summary>
        ValueTask<ArrayOf<Variant>> CompleteAsync(
            NodeId invocationId,
            ArrayOf<Variant> inputArguments,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Captures a control request and the generation to put on subsequent execution updates.
    /// </summary>
    public sealed record ProgramControlContext(
        ProgramControlMethod Control,
        ProgramState State,
        long RunId,
        ArrayOf<Variant> InputArguments,
        NodeId SessionId,
        CancellationToken Lifetime);

    /// <summary>
    /// Returns the provider's acceptance status and method outputs.
    /// </summary>
    public sealed record ProgramControlResult(
        ServiceResult Result,
        ArrayOf<Variant> OutputArguments,
        Variant IntermediateResult = default);

    /// <summary>
    /// Assigns a published final-result Variable by its invocation-relative qualified path.
    /// </summary>
    public sealed record ProgramResultValue(ArrayOf<QualifiedName> Path, DataValue Value);

    /// <summary>
    /// An internal transition tagged with the run that produced it.
    /// </summary>
    /// <remarks>
    /// For a nested transition, set SubStateMachineId and SubStateTransitionId. TargetState is then ignored.
    /// Initial resource-availability updates use RunId zero. An autonomous Ready-to-Running transition
    /// uses the next RunId; subsequent updates use that run's identifier. The base transition graph applies.
    /// </remarks>
    public sealed record ProgramExecutionUpdate(
        long RunId,
        ProgramState TargetState,
        Variant IntermediateResult = default,
        ArrayOf<ProgramResultValue> FinalResults = default,
        NodeId SubStateMachineId = default,
        uint SubStateTransitionId = 0);

    /// <summary>
    /// Adapts an application factory without requiring inheritance.
    /// </summary>
    public sealed class DelegateProgramProvider : IProgramProvider
    {
        /// <summary>
        /// Creates an adapter around the supplied asynchronous factory.
        /// </summary>
        public DelegateProgramProvider(
            Func<ProgramInvocationContext, CancellationToken, ValueTask<IProgramExecution>> factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <inheritdoc/>
        public ValueTask<IProgramExecution> CreateAsync(
            ProgramInvocationContext context,
            CancellationToken cancellationToken = default)
        {
            return m_factory(context, cancellationToken);
        }

        private readonly Func<ProgramInvocationContext, CancellationToken, ValueTask<IProgramExecution>> m_factory;
    }
}

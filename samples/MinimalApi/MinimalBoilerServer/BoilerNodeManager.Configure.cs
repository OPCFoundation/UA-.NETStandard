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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server.Fluent;

namespace Boiler
{
    /// <summary>
    /// Sibling partial that wires per-node callbacks for the
    /// source-generated <see cref="BoilerNodeManager"/> using the fluent
    /// builder.
    /// </summary>
    /// <remarks>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to
    /// source generation: the generator emits a sibling partial that
    /// derives from <c>CustomNodeManager2</c>, owns the predefined-node
    /// load, and calls back into <see cref="Configure"/> below. No
    /// MSBuild flag is required; the attribute alone selects the class
    /// and (since this project carries a single design) the single
    /// matching design file.
    /// <c>Configure</c> runs once,
    /// <em>after</em> <c>base.CreateAddressSpace</c> has materialized the
    /// predefined Boiler instance, so all browse paths into the
    /// <c>Boilers/Boiler #1</c> sub-tree are addressable here.
    /// <para>
    /// The wiring below is intentionally a mix of the four addressing
    /// styles — string browse path, absolute <see cref="NodeId"/>,
    /// type-definition lookup, and the new typed
    /// <see cref="IVariableBuilder{TValue}"/> surface — to demonstrate
    /// that the legacy and source-generator-friendly APIs interoperate.
    /// </para>
    /// </remarks>
    [NodeManager(NamespaceUri = "http://opcfoundation.org/UA/Boiler/")]
    public partial class BoilerNodeManager
    {
        private long m_drumLevelTicks;
        private long m_pipeFlowTicks;
        private long m_inputFlowTicks;
        private long m_drumHeartbeatTicks;

        partial void Configure(INodeManagerBuilder builder)
        {
            // (1) Legacy browse-path addressing with the lower-level
            // ref-Variant callback. Use this when you need full control
            // over the StatusCode / SourceTimestamp returned per read.
            builder
                .Node("Boilers/Boiler #1/DrumX001/LIX001/Output")
                .OnRead(GenerateDrumLevel);

            // (2) Absolute NodeId addressing using the strongly-typed
            // identifier table generated from the NodeSet2.
            builder
                .Node(ExpandedNodeId.ToNodeId(
                    VariableIds.Boilers_Boiler__1_PipeX001_FTX001_Output,
                    Server.NamespaceUris))
                .OnRead(GeneratePipeFlow);

            // (3) New typed IVariableBuilder<T> via the absolute NodeId
            // table — the simple Func<double> overload removes the
            // ref-Variant boilerplate from the lambda and runs through
            // the same sync read path as (1).
            builder
                .Variable<double>(ExpandedNodeId.ToNodeId(
                    VariableIds.Boilers_Boiler__1_FCX001_Measurement,
                    Server.NamespaceUris))
                .OnRead(GenerateInputFlow);

            // (4) New typed async IVariableBuilder<T> overload — the
            // handler runs OUTSIDE the NodeState lock (lock-released
            // semantics in BaseVariableState.ReadAttributeAsync), so the
            // lambda may freely await without tying up a thread-pool
            // thread. Hooked to the second pipe's flow output to show
            // the routing end-to-end through AsyncCustomNodeManager.
            builder
                .Variable<double>(ExpandedNodeId.ToNodeId(
                    VariableIds.Boilers_Boiler__1_PipeX002_FTX002_Output,
                    Server.NamespaceUris))
                .OnRead(GenerateOutputFlowAsync);

            // (5) TypeDefinitionId addressing — robust for well-known
            // singletons, independent of browse-path layout.
            builder
                .NodeFromTypeId(ExpandedNodeId.ToNodeId(ObjectTypeIds.BoilerType, Server.NamespaceUris))
                .OnNodeAdded((context, node) => Server.Telemetry.CreateLogger<BoilerNodeManager>()
                    .BoilerInstanceMaterialized(node.NodeId, node.BrowseName));
        }

        /// <summary>
        /// Source-generator-emitted typed builder partial. The fluent
        /// surface here walks the model's predefined-instance tree
        /// directly: each segment is a generated property whose return
        /// type is the typed wrapper for the next node. Browse paths,
        /// NodeIds, and namespace-index lookups are eliminated at the
        /// callsite — IntelliSense surfaces every legal child, and
        /// typos are compile-time errors.
        /// </summary>
        /// <remarks>
        /// This partial coexists with <see cref="Configure(INodeManagerBuilder)"/>;
        /// the generated <c>CreateAddressSpaceAsync</c> override invokes
        /// both. Wiring the same node from both partials is illegal and
        /// will throw at startup, so the targets here are deliberately
        /// disjoint from the ones in the non-typed partial above.
        /// </remarks>
        partial void Configure(IBoilerNodeManagerBuilder builder)
        {
            // (6) Typed traversal — the LCX001 level controller measurement
            // is reached via generated accessors with no string paths or
            // NodeIds in sight. The Func<double> handler is the same shape
            // as wiring (3) but the resolution is fully type-checked.
            // The trailing .Historize() opts this variable in to Part 11
            // historical access; with no prior UseHistorian() call the
            // fluent surface lazily installs an in-memory engine and
            // registers it as the server-wide default. Subsequent
            // .Historize() calls in this manager would reuse the same
            // binding.
            builder.Boilers.Boiler__1.LCX001.Measurement
                .OnRead(GenerateLevelControlMeasurement)
                .Historize();

            // (7) Typed traversal of a method node — the Halt method is
            // bound to an async lambda. The generator emits the typed
            // OnCall(Func<CancellationToken, ValueTask>) overload that
            // erases the (ISystemContext, MethodState, NodeId, ArrayOf,
            // List, CancellationToken)/ServiceResult plumbing entirely.
            builder.Boilers.Boiler__1.Simulation.Halt
                .OnCall(HaltSimulationAsync);

            // (8) Event publish source — the source-generated typed
            // wrapper for DrumX001 exposes Publish<TEvent> because the
            // model declares EventNotifier=SubscribeToEvents on this
            // node. The factory iterator runs lazily: the registry
            // activates it the first time a client subscribes to events
            // on the drum (or any ancestor that walks via inverse
            // HasNotifier/HasEventSource references) and cancels it once
            // the last interested monitored item disappears. The
            // registry auto-populates EventId/EventType/Time/SourceNode
            // so the iterator only fills the user-meaningful fields.
            builder.Boilers.Boiler__1.DrumX001
                .Publish(GenerateDrumHeartbeatAsync);
        }

        private long m_levelMeasurementTicks;

        private double GenerateLevelControlMeasurement()
        {
            long t = Interlocked.Increment(ref m_levelMeasurementTicks);
            return 50.0 + (10.0 * Math.Cos(t * 0.05));
        }

        private async ValueTask HaltSimulationAsync(CancellationToken cancellationToken)
        {
            // Token-aware async work to demonstrate the end-to-end async
            // method call path through AsyncCustomNodeManager.CallAsync.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Server.Telemetry.CreateLogger<BoilerNodeManager>()
                .BoilerSimulationHalted();
        }

        /// <summary>
        /// Lazily emits a synthetic heartbeat <see cref="BaseEventState"/>
        /// every 500ms while at least one client is monitoring events on
        /// the drum notifier. Cancellation tears the iterator down on the
        /// last unsubscribe (or on manager disposal). The registry fills
        /// in <c>EventId</c>, <c>EventType</c>, <c>SourceNode</c>,
        /// <c>SourceName</c>, <c>Time</c>, and <c>ReceiveTime</c> on the
        /// way out, so the iterator only sets the user-meaningful fields.
        /// </summary>
        private async IAsyncEnumerable<BaseEventState> GenerateDrumHeartbeatAsync(
            BaseObjectState notifier,
            ISystemContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = Task.Delay(
                    TimeSpan.FromMilliseconds(500), cancellationToken);
                try
                {
                    await delay.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                long sequence = Interlocked.Increment(ref m_drumHeartbeatTicks);
                var ev = new BaseEventState(parent: notifier);
                ev.Severity = PropertyState<ushort>.With<VariantBuilder>(
                    ev, (ushort)EventSeverity.Medium);
                ev.Message = PropertyState<LocalizedText>.With<VariantBuilder>(
                    ev,
                    new LocalizedText(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Drum heartbeat #{0}",
                        sequence)));
                yield return ev;
            }
        }

        private ServiceResult GenerateDrumLevel(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            // Slow saw-tooth between 40% and 60% to give clients something
            // to plot without needing a background timer in this single
            // file. Each Read advances the wave; suitable for a quickstart.
            long t = Interlocked.Increment(ref m_drumLevelTicks);
            value = new Variant(50.0 + (10.0 * Math.Sin(t * 0.05)));
            statusCode = StatusCodes.Good;
            timestamp = DateTimeUtc.Now;
            return ServiceResult.Good;
        }

        private ServiceResult GeneratePipeFlow(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            long t = Interlocked.Increment(ref m_pipeFlowTicks);
            value = new Variant(100.0 + (25.0 * Math.Cos(t * 0.07)));
            statusCode = StatusCodes.Good;
            timestamp = DateTimeUtc.Now;
            return ServiceResult.Good;
        }

        private double GenerateInputFlow()
        {
            long t = Interlocked.Increment(ref m_inputFlowTicks);
            return 80.0 + (15.0 * Math.Sin(t * 0.09));
        }

        private async ValueTask<double> GenerateOutputFlowAsync(CancellationToken cancellationToken)
        {
            // Token-aware no-op delay simulates an out-of-process source
            // (a database round-trip, a remote sensor read, etc.) without
            // pulling in a real I/O dependency. Cancellation correctness
            // here flows all the way back to AsyncCustomNodeManager.ReadAsync.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            long t = Interlocked.Increment(ref m_pipeFlowTicks);
            return 105.0 + (25.0 * Math.Cos(t * 0.07));
        }
    }

    internal static partial class BoilerNodeManagerLog
    {
        [LoggerMessage(EventId = MinimalBoilerServerEventIds.BoilerNodeManager + 0,
            Level = LogLevel.Information,
            Message = "Boiler instance materialized: {NodeId} ({BrowseName})")]
        public static partial void BoilerInstanceMaterialized(
            this ILogger logger,
            NodeId nodeId,
            QualifiedName browseName);

        [LoggerMessage(EventId = MinimalBoilerServerEventIds.BoilerNodeManager + 1,
            Level = LogLevel.Information,
            Message = "Boiler simulation halted.")]
        public static partial void BoilerSimulationHalted(this ILogger logger);
    }
}

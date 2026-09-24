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
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace FlatTagServer
{
    /// <summary>
    /// Creates flat-tag node managers from the configured source options.
    /// </summary>
    public sealed class FlatTagNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Initializes the factory.
        /// </summary>
        public FlatTagNodeManagerFactory(FlatTagServerOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            NamespacesUris = [options.SourceNamespaceUri];
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris { get; }

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            _ = configuration;
            _ = cancellationToken;
            // CA2000 cannot model ownership transfer through ValueTask<IAsyncNodeManager>.
            // TODO: Remove this suppression when CA2000 recognizes factory ownership transfer.
#pragma warning disable CA2000
            IAsyncNodeManager nodeManager = new FlatTagNodeManager(server, m_options);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }

        private readonly FlatTagServerOptions m_options;
    }

    /// <summary>
    /// Minimal async address space exposing one half of the aggregate Pump tags.
    /// </summary>
    public sealed class FlatTagNodeManager : FluentNodeManagerBase
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public FlatTagNodeManager(
            IServerInternal server,
            FlatTagServerOptions options)
            : base(server, options.SourceNamespaceUri)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Mints the dotted tag paths this sample is addressed by.
        /// </summary>
        /// <remarks>
        /// The identifiers are part of the sample's contract rather than an
        /// implementation detail: the aggregating client's Thing Descriptions
        /// spell out <c>s=Pump1.Identification.Manufacturer</c>, so a staged
        /// node keeps its browse path as its identifier instead of taking the
        /// hashed one the default factory would mint.
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            ushort namespaceIndex = NamespaceIndexes[0];

            // An identifier the caller has already chosen here is kept, on the
            // same terms as the default factory: the supervision signals name
            // their tag and their condition explicitly, and NodeState.Create
            // runs its assignment pass over a subtree that already carries
            // them. A staged node arrives with its NodeId cleared, so it falls
            // through to the browse path below.
            if (node is not null &&
                !node.NodeId.IsNull &&
                (node.NodeId.NamespaceIndex == namespaceIndex ||
                    node is not BaseInstanceState { Parent: not null }))
            {
                return node.NodeId;
            }

            string? browseName = node?.BrowseName.Name;
            if (string.IsNullOrEmpty(browseName))
            {
                return base.New(context, node!);
            }

            if (node is BaseInstanceState instance &&
                instance.Parent is NodeState parent &&
                parent.NodeId.NamespaceIndex == namespaceIndex &&
                parent.NodeId.IdType == IdType.String)
            {
                return new NodeId(
                    parent.NodeId.IdentifierAsString + "." + browseName,
                    namespaceIndex);
            }

            return new NodeId(browseName!, namespaceIndex);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            if (externalReferences is null)
            {
                throw new ArgumentNullException(nameof(externalReferences));
            }

            NodeManagerBuilder builder = CreateFluentBuilder(NamespaceIndexes[0]);
            await AddPumpAsync(builder, "Pump1", m_options.Values, cancellationToken)
                .ConfigureAwait(false);
            await AddPumpAsync(builder, "Pump2", m_options.Pump2Values, cancellationToken)
                .ConfigureAwait(false);

            // Registering the staged pumps and then re-running the reverse
            // reference pass is what puts each pump's inverse Organizes
            // reference into externalReferences[ObjectsFolder]; the manager used
            // to maintain that entry by hand.
            await RegisterAuthoredNodesAsync(builder, cancellationToken).ConfigureAwait(false);
            await CompleteConfigureAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);
            await SealConfigurationAsync(builder, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask AddPumpAsync(
            INodeManagerBuilder builder,
            string pumpName,
            FlatTagValues values,
            CancellationToken cancellationToken)
        {
            // Unparented, so the staged node organises itself under the Objects
            // folder and its NodeId comes out as the bare pump name.
            INodeBuilder<BaseObjectState> pump = AddGroup(builder, pumpName);
            NodeId pumpId = pump.Node.NodeId;

            NodeId identification = AddGroup(builder, "Identification", pumpId).Node.NodeId;
            AddTag<LocalizedText>(
                builder, identification, "Manufacturer",
                Variant.From(new LocalizedText(values.Manufacturer)), property: true);
            AddTag<string>(
                builder, identification, "SerialNumber",
                Variant.From(values.SerialNumber), property: true);
            AddTag<string>(
                builder, identification, "ProductInstanceUri",
                Variant.From(values.ProductInstanceUri), property: true);

            NodeId operational = AddGroup(builder, "Operational", pumpId).Node.NodeId;
            NodeId measurements = AddGroup(builder, "Measurements", operational).Node.NodeId;
            INodeBuilder<BaseObjectState> events = AddGroup(builder, "Events", pumpId);

            // The pump is the notifier an aggregating server subscribes to. Its
            // conditions live under the supervision Objects below it, and OPC
            // 10000-3 only delivers their events to a client that can reach a
            // notifier, so the pump both carries the bit and is registered as a
            // root notifier with the server.
            pump.Node.EventNotifier = EventNotifiers.SubscribeToEvents;
            events.Node.EventNotifier = EventNotifiers.SubscribeToEvents;
            await AddRootNotifierAsync(pump.Node, cancellationToken).ConfigureAwait(false);

            var signals = new List<SupervisionSignal>();
            AddManagementMethods(builder, pumpId, signals);

            if (m_options.SourceNamespaceUri == FlatTagServerOptions.SourceANamespaceUri)
            {
                AddSourceAVariables(builder, measurements, events.Node.NodeId, values, signals);
            }
            else
            {
                AddSourceBVariables(builder, measurements, events.Node.NodeId, values, signals);
            }
        }

        private void AddSourceAVariables(
            INodeManagerBuilder builder,
            NodeId measurements,
            NodeId events,
            FlatTagValues values,
            List<SupervisionSignal> signals)
        {
            AddTag<double>(
                builder, measurements, "DifferentialPressure",
                Variant.From(values.DifferentialPressure));
            AddTag<double>(
                builder, measurements, "FluidTemperature",
                Variant.From(values.FluidTemperature));
            AddTag<double>(
                builder, measurements, "MassFlow",
                Variant.From(values.MassFlow));
            AddTag<double>(
                builder, measurements, "Level",
                Variant.From(values.Level));

            BaseObjectState supervision = AddGroup(
                builder, "SupervisionProcessFluid", events).Node;
            supervision.EventNotifier = EventNotifiers.SubscribeToEvents;
            signals.Add(new SupervisionSignal(
                SystemContext,
                Server.Telemetry,
                supervision,
                "Cavitation",
                "CavitationAlarm",
                severity: 700,
                initiallyActive: values.Cavitation));
        }

        private void AddSourceBVariables(
            INodeManagerBuilder builder,
            NodeId measurements,
            NodeId events,
            FlatTagValues values,
            List<SupervisionSignal> signals)
        {
            AddTag<double>(
                builder, measurements, "BearingTemperature",
                Variant.From(values.BearingTemperature));
            AddTag<double>(
                builder, measurements, "PumpPowerInput",
                Variant.From(values.PumpPowerInput));
            AddTag<double>(
                builder, measurements, "PumpEfficiency",
                Variant.From(values.PumpEfficiency));
            AddTag<uint>(
                builder, measurements, "NumberOfStarts",
                Variant.From(values.NumberOfStarts));

            BaseObjectState supervision = AddGroup(
                builder, "SupervisionPumpOperation", events).Node;
            supervision.EventNotifier = EventNotifiers.SubscribeToEvents;
            signals.Add(new SupervisionSignal(
                SystemContext,
                Server.Telemetry,
                supervision,
                "MotorOverheat",
                "MotorOverheatAlarm",
                severity: 800,
                initiallyActive: values.MotorOverheat));
        }

        /// <summary>
        /// Adds the Methods an operator uses to manage the pump. They are the
        /// members the asset's management group projects, and <c>Reset</c> is
        /// also what returns a tripped supervision signal to normal.
        /// </summary>
        private static void AddManagementMethods(
            INodeManagerBuilder builder,
            NodeId pumpId,
            List<SupervisionSignal> signals)
        {
            BaseVariableState running = AddTag<bool>(
                builder, pumpId, "Running", Variant.From(true), readBack: false);

            AddCommand(
                builder,
                pumpId,
                "Start",
                (context, _, _, _, _, _) =>
                {
                    SetRunning(context, running, value: true);
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            AddCommand(
                builder,
                pumpId,
                "Stop",
                (context, _, _, _, _, _) =>
                {
                    SetRunning(context, running, value: false);
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            AddCommand(
                builder,
                pumpId,
                "Reset",
                (context, _, _, _, _, _) =>
                {
                    foreach (SupervisionSignal signal in signals)
                    {
                        if (signal.IsActive)
                        {
                            signal.SetActive(context, active: false);
                        }
                    }
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
        }

        private static void SetRunning(
            ISystemContext context,
            BaseVariableState running,
            bool value)
        {
            running.Value = Variant.From(value);
            running.Timestamp = DateTime.UtcNow;
            running.ClearChangeMasks(context, includeChildren: false);
        }

        /// <summary>
        /// Stages one of the grouping Objects a pump is browsed through.
        /// </summary>
        /// <remarks>
        /// Only the display name is set here: the staged node already carries
        /// the browse name, the type definition and - through
        /// <see cref="New"/> - the dotted NodeId this sample publishes. The
        /// sample tags its display names with a locale, which the fluent
        /// default does not.
        /// </remarks>
        private static INodeBuilder<BaseObjectState> AddGroup(
            INodeManagerBuilder builder,
            string browseName,
            NodeId parentId = default)
        {
            INodeBuilder<BaseObjectState> node = builder.AddObject(browseName, parentId);
            node.Node.DisplayName = new LocalizedText("en", browseName);
            return node;
        }

        /// <summary>
        /// Stages one flat tag holding a constant value.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR type the tag carries; the staged variable takes its DataType
        /// and ValueRank from it.
        /// </typeparam>
        /// <param name="builder">The fluent builder staging the pump.</param>
        /// <param name="parentId">The Object the tag hangs off.</param>
        /// <param name="browseName">Browse name of the tag.</param>
        /// <param name="value">The value the tag reports.</param>
        /// <param name="property">
        /// Whether the tag is a Property rather than a data variable.
        /// </param>
        /// <param name="readBack">
        /// Whether reads are answered from <paramref name="value"/> through an
        /// async callback. Tags the server itself drives - the pump's
        /// <c>Running</c> flag - pass <c>false</c> so a write from the
        /// simulation is what a client sees.
        /// </param>
        private static BaseVariableState AddTag<TValue>(
            INodeManagerBuilder builder,
            NodeId parentId,
            string browseName,
            Variant value,
            bool property = false,
            bool readBack = true)
        {
            IVariableBuilder<TValue> tag = builder.AddVariable<TValue>(browseName, parentId);
            BaseVariableState node = tag.Node;
            node.DisplayName = new LocalizedText("en", browseName);
            if (property)
            {
                node.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                node.TypeDefinitionId = VariableTypeIds.PropertyType;
            }
            node.Value = value;
            node.StatusCode = StatusCodes.Good;
            node.Timestamp = DateTime.UtcNow;
            if (readBack)
            {
                tag.OnRead((_, _, ct) => ReadValueAsync(value, ct));
            }
            return node;
        }

        /// <summary>
        /// Stages one of the argument-less Methods an operator manages the
        /// pump with.
        /// </summary>
        private static void AddCommand(
            INodeManagerBuilder builder,
            NodeId parentId,
            string browseName,
            GenericMethodCalledEventHandler2Async onCall)
        {
            INodeBuilder<MethodState> method = builder.AddMethod(browseName, parentId);
            method.Node.DisplayName = new LocalizedText("en", browseName);
            method.OnCall(onCall);
        }

        private static async ValueTask<AttributeSimpleReadResult> ReadValueAsync(
            Variant value,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return new AttributeSimpleReadResult(ServiceResult.Good, value);
        }

        private readonly FlatTagServerOptions m_options;
    }
}

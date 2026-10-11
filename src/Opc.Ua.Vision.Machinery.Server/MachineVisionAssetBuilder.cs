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
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Vision.MachineVision.AMCM;
using Amcm = Opc.Ua.Vision.MachineVision.AMCM;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Part 2 asset builders on the shared Vision configuration interface.
    /// </summary>
    public static class VisionMachineryBuilderExtensions
    {
        /// <summary>
        /// Starts a Part 2 system asset. No nodes are published until BuildAsync succeeds.
        /// </summary>
        public static MachineVisionAssetBuilder AddMachineVisionAsset(
            this IVisionBuildContext context,
            string browseName,
            TimeProvider? timeProvider = null)
        {
            return new MachineVisionAssetBuilder(context, browseName, timeProvider);
        }
    }

    /// <summary>
    /// Builds the entire OPC 40100-2 asset surface using generated types and
    /// the shared Machinery identification contract.
    /// </summary>
    public sealed class MachineVisionAssetBuilder
    {
        /// <summary>
        /// Creates a staged asset in the Vision context's application namespace.
        /// </summary>
        public MachineVisionAssetBuilder(
            IVisionBuildContext context,
            string browseName,
            TimeProvider? timeProvider = null)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(browseName))
            {
                throw new ArgumentException("An asset browse name is required.", nameof(browseName));
            }
            if (context.Context.NamespaceUris.GetIndex(MachineVisionModels.AssetNamespaceUri) < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Enable MachineVisionParts.Assets before building an asset.");
            }
            m_clock = timeProvider ?? TimeProvider.System;
            var name = new QualifiedName(browseName, context.InstanceNamespaceIndex);
            if (context.Root.FindChild(context.Context, name) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated, "The asset name already exists.");
            }
            State = context.Context.CreateInstanceOfVisionSystemAssetType(context.Root, name);
            State.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            Nodes = new MachineVisionNodeBuilder(context.Manager, State, m_clock);
        }

        /// <summary>
        /// Gets the generated system asset state.
        /// </summary>
        public VisionSystemAssetState State { get; }

        /// <summary>
        /// Gets fluent access to every declared identification, maintenance, and health member.
        /// </summary>
        public MachineVisionNodeBuilder Nodes { get; }

        /// <summary>
        /// Sets the mandatory machine nameplate.
        /// </summary>
        public MachineVisionAssetBuilder WithIdentification(MachineryIdentificationData identification)
        {
            EnsureMutable();
            Nodes.WithIdentification(identification);
            return this;
        }

        /// <summary>
        /// Exposes the same component instances through the Machinery Components add-in.
        /// </summary>
        public MachineVisionAssetBuilder WithComponents()
        {
            EnsureMutable();
            m_includeComponents = true;
            return this;
        }

        /// <summary>
        /// Adds an image receiver, transmitter, or transceiver aspect without inventing control methods.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public MachineVisionNodeBuilder AddImageHandlingAspect(string name, bool receives, bool transmits)
        {
            EnsureMutable();
            if (!receives && !transmits)
            {
                throw new ArgumentException("An image aspect must receive or transmit images.");
            }
            FolderState folder = VisionMachineryNodes.Require<FolderState>(
                m_context.Manager, State, "ImageHandlingAspects");
            QualifiedName browseName = ItemName(name);
            if (folder.FindChild(m_context.Context, browseName) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated, "The aspect name already exists.");
            }
            BaseObjectState aspect = (receives, transmits) switch
            {
                (true, true) => m_context.Context.CreateInstanceOfVisionAspectImageTransceiverType(folder, browseName),
                (true, false) => m_context.Context.CreateInstanceOfVisionAspectImageReceiverType(folder, browseName),
                _ => m_context.Context.CreateInstanceOfVisionAspectImageTransmitterType(folder, browseName)
            };
            aspect.ReferenceTypeId = ReferenceTypeIds.Organizes;
            folder.AddChild(aspect);
            return new MachineVisionNodeBuilder(m_context.Manager, aspect, m_clock);
        }

        /// <summary>
        /// Adds one component of a defined category. The returned builder exposes
        /// every optional member of its generated type.
        /// </summary>
        public MachineVisionNodeBuilder AddItem(MachineVisionItemKind kind, string name)
        {
            EnsureMutable();
            VisionItemFolderState folder = GetFolder(kind);
            BaseObjectState item = CreateItem(kind, folder, ItemName(name));
            return AddItemCore(folder, item);
        }

        /// <summary>
        /// Adopts an existing generated/vendor component, preserving its type definition.
        /// Use this for IA calibration targets, Robotics motion devices, or DI software components.
        /// Already parented or registered components are linked, not reparented or owned by this asset.
        /// Fluent value configuration on an existing component updates that component directly.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="item"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public MachineVisionNodeBuilder AddItem(MachineVisionItemKind kind, BaseObjectState item)
        {
            EnsureMutable();
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            if (item.NodeId.IsNull || item.TypeDefinitionId.IsNull)
            {
                throw new ArgumentException("The adopted item must have instance and type identifiers.", nameof(item));
            }
            bool borrowed = item.Parent != null || m_context.Manager.FindPredefinedNode<NodeState>(item.NodeId) != null;
            return AddItemCore(GetFolder(kind), item, borrowed);
        }

        /// <summary>
        /// Adds a directed optical path between two components.
        /// </summary>
        public MachineVisionAssetBuilder HasOpticalPathTo(NodeState source, NodeState target)
        {
            Link(source, target, Amcm.ReferenceTypeIds.HasOpticalPathTo);
            return this;
        }

        /// <summary>
        /// Adds a directed data-transmission path between two components.
        /// </summary>
        public MachineVisionAssetBuilder TransmitsDataTo(NodeState source, NodeState target)
        {
            Link(source, target, Amcm.ReferenceTypeIds.TransmitsDataTo);
            return this;
        }

        /// <summary>
        /// Links the asset to the Part 1 object describing the same physical system.
        /// </summary>
        public MachineVisionAssetBuilder RepresentsSameSystem(NodeState controlSystem)
        {
            Link(State, controlSystem, ReferenceTypeIds.RepresentsSameEntityAs);
            return this;
        }

        /// <summary>
        /// Publishes the validated asset and organizes it into Machinery's Machines folder.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<MachineVisionAssetHandle> BuildAsync(CancellationToken cancellationToken = default)
        {
            EnsureMutable();
            cancellationToken.ThrowIfCancellationRequested();
            ValidateIdentity(m_context, State, machine: true);
            foreach (BaseObjectState item in m_items)
            {
                ValidateIdentity(m_context, item, machine: false);
            }
            var groups = new List<BaseInstanceState>();
            State.GetChildren(m_context.Context, groups);
            foreach (BaseInstanceState child in groups)
            {
                if (child is VisionItemFolderState folder)
                {
                    var items = new List<BaseInstanceState>();
                    folder.GetChildren(m_context.Context, items);
                    if (!items.Exists(static item => item is BaseObjectState) &&
                        !m_borrowed.Exists(item => ReferenceEquals(item.Folder, folder)))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError, "A materialized vision category must contain an item.");
                    }
                }
            }
            if (m_includeComponents)
            {
                BaseObjectState components = VisionMachineryNodes.Require<BaseObjectState>(
                    m_context.Manager, State, "Components");
                foreach (BaseObjectState item in m_items)
                {
                    m_links.Add((components, item, ReferenceTypeIds.Organizes));
                }
            }
            var machinesId = ExpandedNodeId.ToNodeId(
                Machinery.ObjectIds.Machines, m_context.Context.NamespaceUris);
            NodeState machines = m_context.Manager.FindPredefinedNode<NodeState>(machinesId)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "The Machinery Machines folder is not loaded.");
            MachineVisionNodes.AttachRoot(m_context, State);
            try
            {
                await m_context.Manager.AddPredefinedNodeAsync(State, cancellationToken).ConfigureAwait(false);
                foreach ((VisionItemFolderState _, BaseObjectState item) in m_borrowed)
                {
                    await m_context.Manager.AddPredefinedNodeAsync(item, cancellationToken).ConfigureAwait(false);
                    await item.ClearChangeMasksAsync(m_context.Context, true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                ApplyReference(machines, ReferenceTypeIds.Organizes, false, State.NodeId);
                ApplyReference(State, ReferenceTypeIds.Organizes, true, machines.NodeId);
                foreach ((NodeState source, NodeState target, NodeId type) in m_links)
                {
                    ApplyReference(source, type, false, target.NodeId);
                    ApplyReference(target, type, true, source.NodeId);
                }
                m_built = true;
                var handle = new MachineVisionAssetHandle(m_context, State, m_clock);
                if (m_context is VisionBuildContext vision)
                {
                    vision.Registry.AdoptMachineVisionResource(handle);
                }
                return handle;
            }
            catch
            {
                foreach ((NodeState source, NodeId type, bool inverse, NodeId target) in m_appliedLinks)
                {
                    source.RemoveReference(type, inverse, target);
                }
                m_appliedLinks.Clear();
                m_context.Root.RemoveChild(State);
                await m_context.Manager.DeleteNodeAsync(
                    m_context.Manager.SystemContext, State.NodeId, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        private MachineVisionNodeBuilder AddItemCore(
            VisionItemFolderState folder, BaseObjectState item, bool borrowed = false)
        {
            if (folder.FindChild(m_context.Context, item.BrowseName) != null ||
                m_borrowed.Exists(other =>
                    ReferenceEquals(other.Folder, folder) && other.Item.BrowseName == item.BrowseName))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated, "The component name already exists in its category.");
            }
            if (borrowed)
            {
                m_borrowed.Add((folder, item));
                m_links.Add((folder, item, ReferenceTypeIds.Organizes));
            }
            else
            {
                item.ReferenceTypeId = ReferenceTypeIds.Organizes;
                folder.AddChild(item);
            }
            var infoType = ExpandedNodeId.ToNodeId(
                Amcm.ObjectTypeIds.IVisionInfoType, m_context.Context.NamespaceUris);
            if (!item.ReferenceExists(ReferenceTypeIds.HasInterface, false, infoType))
            {
                item.AddReference(ReferenceTypeIds.HasInterface, false, infoType);
            }
            if (item.FindChild(m_context.Context, new QualifiedName(
                "Identification",
                m_context.Context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi))) ==
                null)
            {
                VisionComponentIdentificationState identification =
                    m_context.Context.CreateInstanceOfVisionComponentIdentificationType(
                        item, new QualifiedName("Identification",
                            m_context.Context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi)));
                identification.ReferenceTypeId = ReferenceTypeIds.HasAddIn;
                item.AddChild(identification);
            }
            m_items.Add(item);
            return new MachineVisionNodeBuilder(m_context.Manager, item, m_clock);
        }

        private VisionItemFolderState GetFolder(MachineVisionItemKind kind)
        {
            string name = MachineVisionAssetConformance.CategoryName(kind);
            return VisionMachineryNodes.Require<VisionItemFolderState>(m_context.Manager, State, name);
        }

        private QualifiedName ItemName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A component browse name is required.", nameof(name));
            }
            return new QualifiedName(name, m_context.InstanceNamespaceIndex);
        }

        private BaseObjectState CreateItem(MachineVisionItemKind kind, NodeState parent, QualifiedName name)
        {
            ISystemContext context = m_context.Context;
            return kind switch
            {
                MachineVisionItemKind.ComputingDevice =>
                    context.CreateInstanceOfVisionComputingDeviceType(parent, name),
                MachineVisionItemKind.DisplayUnit => context.CreateInstanceOfVisionDisplayUnitType(parent, name),
                MachineVisionItemKind.PhysicalInterface =>
                    context.CreateInstanceOfVisionPhysicalInterfaceType(parent, name),
                MachineVisionItemKind.ImageSensor => context.CreateInstanceOfVisionImageSensorType(parent, name),
                MachineVisionItemKind.FrameGrabber => context.CreateInstanceOfVisionFrameGrabberType(parent, name),
                MachineVisionItemKind.Lens => context.CreateInstanceOfVisionLensType(parent, name),
                MachineVisionItemKind.LensController => context.CreateInstanceOfVisionLensControllerType(parent, name),
                MachineVisionItemKind.OpticalFilter => context.CreateInstanceOfVisionOpticalFilterType(parent, name),
                MachineVisionItemKind.OtherOpticalEquipment =>
                    context.CreateInstanceOfVisionOtherOpticalEquipmentType(parent, name),
                MachineVisionItemKind.WayEncoder => context.CreateInstanceOfVisionWayEncoderType(parent, name),
                MachineVisionItemKind.TriggerSensor => context.CreateInstanceOfVisionTriggerSensorType(parent, name),
                MachineVisionItemKind.Lamp => context.CreateInstanceOfVisionLampType(parent, name),
                MachineVisionItemKind.LightingController =>
                    context.CreateInstanceOfVisionLightingControllerType(parent, name),
                MachineVisionItemKind.PatternGenerator =>
                    context.CreateInstanceOfVisionPatternGeneratorType(parent, name),
                MachineVisionItemKind.AcquisitionBackground =>
                    context.CreateInstanceOfVisionAcquisitionBackgroundType(parent, name),
                MachineVisionItemKind.SurroundingEnvironment =>
                    context.CreateInstanceOfVisionSurroundingEnvironmentType(parent, name),
                MachineVisionItemKind.Housing => context.CreateInstanceOfVisionHousingType(parent, name),
                MachineVisionItemKind.PowerSupply => context.CreateInstanceOfVisionPowerSupplyType(parent, name),
                MachineVisionItemKind.ClimateController =>
                    context.CreateInstanceOfVisionClimateControllerType(parent, name),
                MachineVisionItemKind.License => context.CreateInstanceOfVisionLicenseType(parent, name),
                MachineVisionItemKind.NetworkDevice => context.CreateInstanceOfVisionNetworkDeviceType(parent, name),
                MachineVisionItemKind.Cable => context.CreateInstanceOfVisionCableType(parent, name),
                MachineVisionItemKind.CalibrationTarget or MachineVisionItemKind.MotionDevice or
                    MachineVisionItemKind.SoftwareComponent => CreateGenericItem(parent, name),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        private BaseObjectState CreateGenericItem(NodeState parent, QualifiedName name)
        {
            var item = new BaseObjectState(parent);
            item.Create(m_context.Context, NodeId.Null, name, new LocalizedText(name.Name), true);
            MachineVisionNodes.Rebase(m_context.Context, item);
            return item;
        }

        private void Link(NodeState source, NodeState target, ExpandedNodeId referenceType)
        {
            EnsureMutable();
            if (source == null || source.NodeId.IsNull)
            {
                throw new ArgumentException("The source must be an initialized node.", nameof(source));
            }
            if (target == null || target.NodeId.IsNull)
            {
                throw new ArgumentException("The target must be an initialized node.", nameof(target));
            }
            var type = ExpandedNodeId.ToNodeId(referenceType, m_context.Context.NamespaceUris);
            m_links.Add((source, target, type));
        }

        /// <summary>
        /// Validates the common identification invariant before initial publication or a telemetry update.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void ValidateIdentity(IVisionBuildContext context, NodeState node, bool machine)
        {
            BaseInstanceState nameplate = VisionMachineryNodes.Require<BaseInstanceState>(
                context.Manager, node, "Identification");
            BaseVariableState manufacturer = VisionMachineryNodes.Require<BaseVariableState>(
                context.Manager, nameplate, "Manufacturer");
            BaseVariableState serial = VisionMachineryNodes.Require<BaseVariableState>(
                context.Manager, nameplate, "SerialNumber");
            if (!manufacturer.WrappedValue.TryGetValue(out LocalizedText value) ||
                value.IsNullOrEmpty ||
                !serial.WrappedValue.TryGetValue(out string text) ||
                string.IsNullOrWhiteSpace(text))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Every vision item requires Manufacturer and SerialNumber.");
            }
            if (machine)
            {
                BaseVariableState product = VisionMachineryNodes.Require<BaseVariableState>(
                    context.Manager, nameplate, "ProductInstanceUri");
                if (!product.WrappedValue.TryGetValue(out string uri) ||
                    !Uri.TryCreate(uri, UriKind.Absolute, out _))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError, "A vision system requires an absolute ProductInstanceUri.");
                }
            }
        }

        private void EnsureMutable()
        {
            if (m_built)
            {
                throw new InvalidOperationException("The asset has already been published.");
            }
        }

        private void ApplyReference(NodeState source, NodeId type, bool inverse, NodeId target)
        {
            if (!source.ReferenceExists(type, inverse, target))
            {
                source.AddReference(type, inverse, target);
                m_appliedLinks.Add((source, type, inverse, target));
            }
        }

        private readonly IVisionBuildContext m_context;
        private readonly TimeProvider m_clock;
        private readonly List<BaseObjectState> m_items = [];
        private readonly List<(NodeState Source, NodeState Target, NodeId Type)> m_links = [];
        private readonly List<(VisionItemFolderState Folder, BaseObjectState Item)> m_borrowed = [];
        private readonly List<(NodeState Source, NodeId Type, bool Inverse, NodeId Target)> m_appliedLinks = [];
        private bool m_built;
        private bool m_includeComponents;
    }

    /// <summary>
    /// Updates a published Part 2 asset through the same declared-member interface used during setup.
    /// </summary>
    public sealed class MachineVisionAssetHandle : IConformanceContributor, IAsyncDisposable
    {
        /// <summary>
        /// Owns the serialized telemetry-update lifetime of a published asset.
        /// </summary>
        internal MachineVisionAssetHandle(IVisionBuildContext context, VisionSystemAssetState state, TimeProvider clock)
        {
            m_context = context;
            State = state;
            m_clock = clock;
        }

        /// <summary>
        /// Gets the published, generated asset state.
        /// </summary>
        public VisionSystemAssetState State { get; }

        /// <summary>
        /// Gets the published asset's specification-named conformance units.
        /// Optional maintenance, health, categories, and path units require actual instances or references.
        /// </summary>
        public ArrayOf<QualifiedName> ConformanceUnits => Volatile.Read(ref m_disposed) != 0
            ? [] : MachineVisionAssetConformance.Asset(m_context, State);

        /// <summary>
        /// No unverified OPC Foundation profile URI is advertised.
        /// </summary>
        public ArrayOf<string> ServerProfiles => [];

        /// <summary>
        /// Applies telemetry changes, registers newly materialized optional members,
        /// and notifies data-change subscribers. The callback edits an isolated snapshot;
        /// invalid values or a thrown callback leave the published tree unchanged.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="update"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="AggregateException"></exception>
        public async ValueTask UpdateAsync(
            Action<MachineVisionNodeBuilder> update,
            CancellationToken cancellationToken = default)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }
            ThrowIfDisposed();
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime.Token);
            await m_gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                VisionSystemAssetState snapshot = CoreUtils.Clone(State) ??
                    throw new InvalidOperationException("The asset snapshot could not be created.");
                update(new MachineVisionNodeBuilder(m_context.Manager, snapshot, m_clock));
                MachineVisionAssetBuilder.ValidateIdentity(m_context, snapshot, machine: true);
                var values = new List<(BaseVariableState Target, BaseVariableState Value)>();
                var additions = new List<(NodeState Parent, BaseInstanceState Value)>();
                var visited = new HashSet<NodeState>();
                PlanUpdate(State, snapshot, values, additions, false, visited);
                var created = new List<BaseInstanceState>();
                try
                {
                    for (int ii = 0; ii < additions.Count; ii++)
                    {
                        lifetime.Token.ThrowIfCancellationRequested();
                        (NodeState parent, BaseInstanceState proposed) = additions[ii];
                        BaseInstanceState child = VisionMachineryNodes.Require<BaseInstanceState>(
                            m_context.Manager, parent, proposed.BrowseName.Name!);
                        created.Add(child);
                        PlanUpdate(child, proposed, values, additions, true, visited);
                    }
                    await m_context.Manager.AddPredefinedNodeAsync(State, lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    var errors = new List<Exception> { failure };
                    for (int ii = created.Count - 1; ii >= 0; ii--)
                    {
                        BaseInstanceState child = created[ii];
                        child.Parent?.RemoveChild(child);
                        try
                        {
                            await m_context.Manager.DeleteNodeAsync(
                                m_context.Manager.SystemContext, child.NodeId, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception rollback)
                        {
                            errors.Add(rollback);
                        }
                    }
                    if (errors.Count > 1)
                    {
                        throw new AggregateException("The asset update and registration rollback failed.", errors);
                    }
                    throw;
                }
                foreach ((BaseVariableState target, BaseVariableState value) in values)
                {
                    target.WrappedValue = CoreUtils.Clone(value.WrappedValue);
                    target.StatusCode = value.StatusCode;
                    target.Timestamp = value.Timestamp;
                }
                await State.ClearChangeMasksAsync(m_context.Context, true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            await m_lifetime.CancelAsync().ConfigureAwait(false);
            await m_gate.WaitAsync().ConfigureAwait(false);
            m_gate.Release();
            m_gate.Dispose();
            m_lifetime.Dispose();
        }

        private void PlanUpdate(
            NodeState target,
            NodeState proposed,
            List<(BaseVariableState Target, BaseVariableState Value)> values,
            List<(NodeState Parent, BaseInstanceState Value)> additions,
            bool newNode,
            HashSet<NodeState> visited)
        {
            if (!visited.Add(proposed))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument, "An asset subtree cannot contain a cycle.");
            }
            if ((!newNode && target.NodeId != proposed.NodeId) ||
                target.BrowseName != proposed.BrowseName ||
                target.NodeClass != proposed.NodeClass ||
                (target is BaseInstanceState actual &&
                    proposed is BaseInstanceState copy &&
                    (actual.TypeDefinitionId != copy.TypeDefinitionId ||
                        actual.ReferenceTypeId != copy.ReferenceTypeId)))
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument,
                    "An asset telemetry update cannot replace existing node identity or types.");
            }
            if (target is BaseVariableState variable && proposed is BaseVariableState value)
            {
                if (variable.DataType != value.DataType || variable.ValueRank != value.ValueRank)
                {
                    throw ServiceResultException.Create(StatusCodes.BadInvalidArgument,
                        "An asset telemetry update cannot change a variable's data type or rank.");
                }
                MachineVisionNodes.ValidateValue(m_context.Context, variable, value.WrappedValue);
                if (newNode ||
                    variable.WrappedValue != value.WrappedValue ||
                    variable.StatusCode != value.StatusCode ||
                    variable.Timestamp != value.Timestamp)
                {
                    values.Add((variable, value));
                }
            }
            var children = new List<BaseInstanceState>();
            proposed.GetChildren(m_context.Context, children);
            if (proposed is VisionItemFolderState folder && children.Count == 0)
            {
                var links = new List<IReference>();
                folder.GetReferences(m_context.Context, links, ReferenceTypeIds.Organizes, false);
                if (links.Count == 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                        "A materialized vision category must contain an item.");
                }
            }
            foreach (BaseInstanceState child in children)
            {
                BaseInstanceState? existing = target.FindChild(m_context.Context, child.BrowseName);
                if (existing == null)
                {
                    additions.Add((target, child));
                }
                else
                {
                    PlanUpdate(existing, child, values, additions, newNode, visited);
                }
            }
            var existingChildren = new List<BaseInstanceState>();
            target.GetChildren(m_context.Context, existingChildren);
            foreach (BaseInstanceState existing in existingChildren)
            {
                if (proposed.FindChild(m_context.Context, existing.BrowseName) == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadInvalidArgument,
                        "An asset telemetry update cannot remove existing nodes.");
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidState, "The asset handle has been disposed.");
            }
        }

        private readonly IVisionBuildContext m_context;
        private readonly TimeProvider m_clock;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly CancellationTokenSource m_lifetime = new();
        private int m_disposed;
    }
}

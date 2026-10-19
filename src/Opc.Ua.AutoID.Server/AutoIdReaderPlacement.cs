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
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Places a reader in the address space of its Device Integration owner so that it stays
    /// reachable from the DI <c>DeviceSet</c> (OPC 10000-100 4.9), and makes it a component of a
    /// machine (OPC 40001-1 11) on request.
    /// </summary>
    internal sealed class AutoIdReaderPlacement
    {
        /// <summary>
        /// Creates the placement helper of a node manager.
        /// </summary>
        public AutoIdReaderPlacement(DiNodeManager manager)
        {
            m_manager = manager;
        }

        /// <summary>
        /// Gets the DI <c>DeviceSet</c> of the node manager, or <see langword="null"/> when the
        /// manager does not publish one.
        /// </summary>
        public NodeState? FindDeviceSet()
        {
            return m_manager.FindPredefinedNode(NodeId.Create(
                Di.Objects.DeviceSet,
                DiNodeManager.DiNamespaceUri,
                m_manager.Server.NamespaceUris));
        }

        /// <summary>
        /// Resolves the <c>Components</c> add-in of <paramref name="machine"/> that a reader becomes
        /// a component of, and fails before anything is registered when the reader cannot be one.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <c>Bad_ConfigurationError</c>: the owner has no Machinery model, the machine is not a
        /// node of the owner or it publishes no <c>Components</c> add-in.
        /// </exception>
        public NodeState ResolveComponents(BaseObjectState machine, QualifiedName readerName)
        {
            ServerSystemContext context = m_manager.SystemContext;
            int machineryNamespace = context.NamespaceUris.GetIndex(Machinery.Namespaces.Machinery);
            NodeId componentsTypeId = machineryNamespace < 0
                ? NodeId.Null
                : ExpandedNodeId.ToNodeId(Machinery.ObjectTypeIds.MachineComponentsType, context.NamespaceUris);
            if (componentsTypeId.IsNull || m_manager.FindPredefinedNode(componentsTypeId) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The reader '{0}' cannot be a component of a machine: AutoID is not composed with " +
                    "Machinery. Register it with AddAutoIdToMachinery, or load the Machinery model into " +
                    "the Device Integration owner.",
                    readerName);
            }
            if (!ReferenceEquals(m_manager.FindPredefinedNode(machine.NodeId), machine))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The machine '{0}' of the reader '{1}' is not a node of the AutoID owner's node manager.",
                    machine.BrowseName,
                    readerName);
            }
            NodeState? components = machine.FindChild(
                context,
                new QualifiedName(Machinery.BrowseNames.Components, (ushort)machineryNamespace));
            if (components is not BaseObjectState componentsObject ||
                !m_manager.Server.TypeTree.IsTypeOf(componentsObject.TypeDefinitionId, componentsTypeId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The machine '{0}' publishes no Components add-in for the reader '{1}'; build the " +
                    "machine with WithComponents (OPC 40001-1 11).",
                    machine.BrowseName,
                    readerName);
            }
            return componentsObject;
        }

        /// <summary>
        /// Checks the members OPC 40001-1 8.3 makes mandatory for the identification of a
        /// component before anything is registered.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <c>Bad_ConfigurationError</c>: <c>Manufacturer</c> or <c>SerialNumber</c> is not set.
        /// </exception>
        public static void ValidateComponentNameplate(DeviceIdentificationData? nameplate, QualifiedName readerName)
        {
            var missing = new List<string>();
            if (nameplate == null || nameplate.Manufacturer.IsNull || string.IsNullOrEmpty(nameplate.Manufacturer.Text))
            {
                missing.Add("Manufacturer");
            }
            if (string.IsNullOrEmpty(nameplate?.SerialNumber))
            {
                missing.Add("SerialNumber");
            }
            if (missing.Count > 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The reader '{0}' is a component of a machine and needs the mandatory identification " +
                    "{1} (OPC 40001-1 8.3); set it with WithIdentification.",
                    readerName,
                    string.Join(" and ", missing));
            }
        }

        /// <summary>
        /// Returns whether <c>DeviceSet</c> reaches <paramref name="target"/> through forward
        /// hierarchical references of nodes of the node manager.
        /// </summary>
        public bool IsReachableFrom(NodeState deviceSet, NodeState target)
        {
            if (ReferenceEquals(deviceSet, target))
            {
                return true;
            }
            ServerSystemContext context = m_manager.SystemContext;
            TypeTable typeTree = m_manager.Server.TypeTree;
            var visited = new HashSet<NodeId> { deviceSet.NodeId };
            var pending = new Queue<NodeState>();
            pending.Enqueue(deviceSet);
            var children = new List<BaseInstanceState>();
            var references = new List<IReference>();
            while (pending.Count > 0)
            {
                NodeState node = pending.Dequeue();
                children.Clear();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    if (ReferenceEquals(child, target))
                    {
                        return true;
                    }
                    if (child.NodeClass != NodeClass.Method && visited.Add(child.NodeId))
                    {
                        pending.Enqueue(child);
                    }
                }
                references.Clear();
                node.GetReferences(context, references);
                foreach (IReference reference in references)
                {
                    if (reference.IsInverse ||
                        reference.TargetId.IsAbsolute ||
                        !typeTree.IsTypeOf(
                            reference.ReferenceTypeId,
                            Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences))
                    {
                        continue;
                    }
                    var targetId = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                    NodeState? next = targetId.IsNull ? null : m_manager.FindPredefinedNode(targetId);
                    if (next == null)
                    {
                        continue;
                    }
                    if (ReferenceEquals(next, target))
                    {
                        return true;
                    }
                    if (visited.Add(next.NodeId))
                    {
                        pending.Enqueue(next);
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Organizes <paramref name="device"/> from <c>DeviceSet</c>, in both directions.
        /// </summary>
        public static void Organize(NodeState deviceSet, NodeState device)
        {
            deviceSet.AddReference(Opc.Ua.Types.ReferenceTypeIds.Organizes, false, device.NodeId);
            device.AddReference(Opc.Ua.Types.ReferenceTypeIds.Organizes, true, deviceSet.NodeId);
        }

        /// <summary>
        /// Removes what <see cref="Organize"/> added.
        /// </summary>
        public static void RemoveOrganize(NodeState deviceSet, NodeState device)
        {
            deviceSet.RemoveReference(Opc.Ua.Types.ReferenceTypeIds.Organizes, false, device.NodeId);
            device.RemoveReference(Opc.Ua.Types.ReferenceTypeIds.Organizes, true, deviceSet.NodeId);
        }

        /// <summary>
        /// Adds the OPC 40001-1 <c>Identification</c> add-in of a component to the registered
        /// reader. It is typed <c>MachineryComponentIdentificationType</c> and references the
        /// reader's DI nameplate properties rather than copying them (OPC 40001-1 7.3, Annex B.2).
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <c>Bad_ConfigurationError</c>: the reader has an <c>Identification</c> child already.
        /// </exception>
        public async ValueTask AddComponentIdentificationAsync(
            AutoIdDeviceState device,
            CancellationToken cancellationToken)
        {
            ServerSystemContext context = m_manager.SystemContext;
            var browseName = new QualifiedName(Di.BrowseNames.Identification, m_manager.DiNamespaceIndex);
            if (device.FindChild(context, browseName) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The reader '{0}' has an Identification already.",
                    device.BrowseName);
            }
            var identification = new BaseObjectState(device)
            {
                SymbolicName = Di.BrowseNames.Identification,
                BrowseName = browseName,
                DisplayName = new LocalizedText(Di.BrowseNames.Identification),
                TypeDefinitionId = ExpandedNodeId.ToNodeId(
                    Machinery.ObjectTypeIds.MachineryComponentIdentificationType,
                    context.NamespaceUris),
                ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasAddIn
            };
            identification.NodeId = context.NodeIdFactory!.New(context, identification);
            device.AddChild(identification);
            foreach (BaseVariableState property in NameplateOf(device))
            {
                identification.AddReference(Opc.Ua.Types.ReferenceTypeIds.HasProperty, false, property.NodeId);
                property.AddReference(Opc.Ua.Types.ReferenceTypeIds.HasProperty, true, identification.NodeId);
            }
            await m_manager.AddPredefinedNodeAsync(identification, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// The nameplate properties of the reader that its component identification references:
        /// the members of <c>MachineryComponentIdentificationType</c> that the reader publishes
        /// with a value. <c>DeviceManual</c> and <c>RevisionCounter</c> are left out, as
        /// OPC 40001-1 10.2 recommends; the user-writable <c>AssetId</c> and <c>ComponentName</c>
        /// are referenced whenever the reader publishes them.
        /// </summary>
        private static IEnumerable<BaseVariableState> NameplateOf(AutoIdDeviceState device)
        {
            foreach (PropertyState<LocalizedText>? text in new[] { device.Manufacturer, device.Model })
            {
                if (text != null && !text.Value.IsNull && !string.IsNullOrEmpty(text.Value.Text))
                {
                    yield return text;
                }
            }
            foreach (PropertyState<string>? value in new[]
            {
                device.SerialNumber,
                device.ManufacturerUri,
                device.ProductCode,
                device.HardwareRevision,
                device.SoftwareRevision,
                device.DeviceRevision,
                device.DeviceClass,
                device.ProductInstanceUri
            })
            {
                if (value != null && !string.IsNullOrEmpty(value.Value))
                {
                    yield return value;
                }
            }
            if (device.AssetId != null)
            {
                yield return device.AssetId;
            }
            if (device.ComponentName != null)
            {
                yield return device.ComponentName;
            }
        }

        private readonly DiNodeManager m_manager;
    }
}

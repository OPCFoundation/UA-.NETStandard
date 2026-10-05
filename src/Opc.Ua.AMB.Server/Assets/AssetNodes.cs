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

using Opc.Ua.Di;

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// Adds nodes to an asset in the node manager that owns it.
    /// </summary>
    internal static class AssetNodes
    {
        /// <summary>
        /// Gives a node that registering created an identifier of the owner
        /// and registers it there.
        /// </summary>
        /// <param name="handle">The asset.</param>
        /// <param name="node">The node, a descendant of the asset.</param>
        public static void Register(AssetHandle handle, NodeState node)
        {
            // A child a generated factory created carries the NodeId of its
            // declaration in the model namespace until the owner hands out
            // its own.
            if (node.NodeId.IsNull ||
                node.NodeId.NamespaceIndex == handle.DiNamespaceIndex ||
                handle.Context.NamespaceUris.GetString(node.NodeId.NamespaceIndex) == Namespaces.AMB)
            {
                node.NodeId = handle.Owner.New(handle.Context, node);
            }
            handle.Owner.AddNode(node);
        }

        /// <summary>
        /// References an interface from the asset unless it does already.
        /// </summary>
        /// <param name="handle">The asset.</param>
        /// <param name="interfaceId">The interface type.</param>
        public static void AddInterface(AssetHandle handle, ExpandedNodeId interfaceId)
        {
            NodeId id = ExpandedNodeId.ToNodeId(interfaceId, handle.Context.NamespaceUris);
            if (!handle.Asset.ReferenceExists(Ua.ReferenceTypeIds.HasInterface, false, id))
            {
                handle.Asset.AddReference(Ua.ReferenceTypeIds.HasInterface, false, id);
            }
        }

        /// <summary>
        /// Finds or creates the <c>2:DeviceHealthAlarms</c> folder, which
        /// lists the health alarms and maintenance conditions of the asset
        /// (OPC 10000-110 §9.3, §12.1).
        /// </summary>
        /// <param name="handle">The asset.</param>
        /// <returns>The folder.</returns>
        public static FolderState EnsureDeviceHealthAlarms(AssetHandle handle)
        {
            ISystemContext context = handle.Context;
            BaseObjectState asset = handle.Asset;
            var browseName = new QualifiedName(Opc.Ua.Di.BrowseNames.DeviceHealthAlarms, handle.DiNamespaceIndex);
            if (asset.FindChildWithQualifiedName(context, browseName) is FolderState existing)
            {
                return existing;
            }

            FolderState folder;
            if (asset is DeviceState device)
            {
                device.AddDeviceHealthAlarms(context);
                folder = device.DeviceHealthAlarms!;
            }
            else
            {
                folder = new FolderState(asset)
                {
                    SymbolicName = Opc.Ua.Di.BrowseNames.DeviceHealthAlarms,
                    BrowseName = browseName,
                    DisplayName = new LocalizedText(Opc.Ua.Di.BrowseNames.DeviceHealthAlarms),
                    TypeDefinitionId = Ua.ObjectTypeIds.FolderType,
                    ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
                };
                asset.AddChild(folder);
                AddInterface(handle, Opc.Ua.Di.ObjectTypeIds.IDeviceHealthType);
            }
            Register(handle, folder);
            return folder;
        }

        /// <summary>
        /// Lists a condition in a folder; the condition stays a child of the
        /// asset, which is its source.
        /// </summary>
        public static void Organize(FolderState folder, NodeState condition)
        {
            folder.AddReference(Ua.ReferenceTypeIds.Organizes, false, condition.NodeId);
            condition.AddReference(Ua.ReferenceTypeIds.Organizes, true, folder.NodeId);
        }

        /// <summary>
        /// Reports the current state of a condition as a new event.
        /// </summary>
        public static void ReportEvent(ISystemContext context, ConditionState condition)
        {
            if (condition.EnabledState?.Id?.Value != true)
            {
                return;
            }
            condition.EventId!.Value = Uuid.NewUuid().ToByteString();
            condition.Time!.Value = DateTimeUtc.Now;
            condition.ReceiveTime!.Value = condition.Time.Value;
            condition.ClearChangeMasks(context, true);

            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(context, condition);
            condition.ReportEvent(context, snapshot);
        }
    }
}

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

using System.Collections.Generic;
using Opc.Ua.AMB.Server.Assets;

namespace Opc.Ua.AMB.Server.Structure
{
    /// <summary>
    /// Adds properties and folders to an asset in the node manager that owns
    /// it.
    /// </summary>
    internal static class AssetProperties
    {
        /// <summary>
        /// Finds or creates an OPC 10000-100 property, on the asset or in its
        /// <c>2:Identification</c> group.
        /// </summary>
        /// <param name="handle">The asset.</param>
        /// <param name="name">The DI browse name.</param>
        /// <param name="dataType">The data type of a created property.</param>
        /// <param name="value">
        /// The value to write; <see cref="Variant.Null"/> keeps the value of an
        /// existing property and gives a created one <paramref name="initial"/>.
        /// No caller clears a DI property, so the null value is free to mean
        /// "no update".
        /// </param>
        /// <param name="initial">The value of a created property without <paramref name="value"/>.</param>
        /// <param name="interfaceId">
        /// The DI interface that declares the property, referenced from the
        /// asset when its type has no slot for it; <see cref="ExpandedNodeId.Null"/>
        /// for none.
        /// </param>
        /// <returns>The property.</returns>
        public static BaseVariableState EnsureDi(
            AssetHandle handle,
            string name,
            NodeId dataType,
            Variant value,
            Variant initial,
            ExpandedNodeId interfaceId)
        {
            BaseVariableState? existing = AssetIdentification.FindProperty(
                handle.Context,
                handle.Asset,
                handle.DiNamespaceIndex,
                name);
            if (existing != null)
            {
                if (!value.IsNull)
                {
                    existing.WrappedValue = value;
                    existing.Timestamp = DateTimeUtc.Now;
                    existing.ClearChangeMasks(handle.Context, false);
                }
                return existing;
            }

            var browseName = new QualifiedName(name, handle.DiNamespaceIndex);
            BaseVariableState variable;
            if (handle.Asset.CreateChild(handle.Context, browseName) is BaseVariableState declared)
            {
                variable = declared;
                Shape(variable, dataType);
            }
            else
            {
                variable = Create(handle, browseName, dataType);
                if (!interfaceId.IsNull)
                {
                    AssetNodes.AddInterface(handle, interfaceId);
                }
            }
            variable.WrappedValue = value.IsNull ? initial : value;
            variable.Timestamp = DateTimeUtc.Now;
            AssetNodes.Register(handle, variable);
            return variable;
        }

        /// <summary>
        /// Finds or creates a property directly on the asset.
        /// </summary>
        /// <param name="handle">The asset.</param>
        /// <param name="browseName">The browse name.</param>
        /// <param name="dataType">The data type of a created property.</param>
        /// <param name="value">The value.</param>
        /// <returns>The property.</returns>
        public static BaseVariableState Ensure(
            AssetHandle handle,
            QualifiedName browseName,
            NodeId dataType,
            Variant value)
        {
            if (handle.Asset.FindChildWithQualifiedName(handle.Context, browseName) is BaseVariableState existing)
            {
                existing.WrappedValue = value;
                existing.Timestamp = DateTimeUtc.Now;
                return existing;
            }

            BaseVariableState variable = Create(handle, browseName, dataType);
            variable.WrappedValue = value;
            variable.Timestamp = DateTimeUtc.Now;
            AssetNodes.Register(handle, variable);
            return variable;
        }

        /// <summary>
        /// Adds a <c>Requirements</c> or <c>Capabilities</c> folder with its
        /// entries (OPC 10000-110 §10.6, §10.7).
        /// </summary>
        /// <param name="handle">The asset.</param>
        /// <param name="ambNamespaceIndex">The index of the AMB namespace.</param>
        /// <param name="name">The browse name of the folder.</param>
        /// <param name="entries">The entries.</param>
        /// <param name="dictionaryReferences">
        /// Receives the variables that reference a dictionary entry of this
        /// server, which gets the inverse reference.
        /// </param>
        public static FolderState AddFolder(
            AssetHandle handle,
            ushort ambNamespaceIndex,
            string name,
            AssetEntriesRequest entries,
            List<(NodeId Source, NodeId Entry)> dictionaryReferences)
        {
            var browseName = new QualifiedName(name, ambNamespaceIndex);
            if (handle.Asset.FindChildWithQualifiedName(handle.Context, browseName) is not FolderState folder)
            {
                folder = new FolderState(handle.Asset)
                {
                    SymbolicName = name,
                    BrowseName = browseName,
                    DisplayName = new LocalizedText(name),
                    TypeDefinitionId = Ua.ObjectTypeIds.FolderType,
                    ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
                };
                handle.Asset.AddChild(folder);
                AssetNodes.Register(handle, folder);
            }

            ushort ns = handle.Asset.NodeId.NamespaceIndex;
            foreach (AssetEntry entry in entries.Entries)
            {
                var variable = new BaseDataVariableState(folder)
                {
                    SymbolicName = entry.Name,
                    BrowseName = new QualifiedName(entry.Name, ns),
                    DisplayName = new LocalizedText(entry.Name),
                    TypeDefinitionId = Ua.VariableTypeIds.BaseDataVariableType,
                    ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                    DataType = TypeInfo.GetDataTypeId(entry.Value, handle.Context.NamespaceUris),
                    ValueRank = entry.Value.TypeInfo.ValueRank,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    WrappedValue = entry.Value
                };
                // An entry of this server is referenced by its local NodeId,
                // as the classification of the asset is.
                NodeId local = entry.DictionaryEntry.IsNull
                    ? NodeId.Null
                    : ExpandedNodeId.ToNodeId(entry.DictionaryEntry, handle.Context.NamespaceUris);
                if (!local.IsNull)
                {
                    variable.AddReference(Ua.ReferenceTypeIds.HasDictionaryEntry, false, local);
                }
                else if (!entry.DictionaryEntry.IsNull)
                {
                    variable.AddReference(Ua.ReferenceTypeIds.HasDictionaryEntry, false, entry.DictionaryEntry);
                }
                folder.AddChild(variable);
                AssetNodes.Register(handle, variable);
                if (!local.IsNull)
                {
                    dictionaryReferences.Add((variable.NodeId, local));
                }
            }
            return folder;
        }

        private static PropertyState Create(AssetHandle handle, QualifiedName browseName, NodeId dataType)
        {
            var property = new PropertyState(handle.Asset)
            {
                SymbolicName = browseName.Name ?? string.Empty,
                BrowseName = browseName,
                DisplayName = new LocalizedText(browseName.Name)
            };
            Shape(property, dataType);
            handle.Asset.AddChild(property);
            return property;
        }

        /// <summary>
        /// Fills in what a property created through a generated slot leaves
        /// unset: without a reference type the owner does not list it when
        /// the parent is browsed, so a client cannot reach it by browse path.
        /// </summary>
        internal static void Shape(BaseVariableState variable, NodeId dataType)
        {
            if (variable.DataType.IsNull || variable.DataType == Ua.DataTypeIds.BaseDataType)
            {
                variable.DataType = dataType;
                variable.ValueRank = ValueRanks.Scalar;
            }
            if (variable.TypeDefinitionId.IsNull)
            {
                variable.TypeDefinitionId = Ua.VariableTypeIds.PropertyType;
            }
            if (variable.ReferenceTypeId.IsNull)
            {
                variable.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
            }
            variable.ModellingRuleId = NodeId.Null;
            variable.AccessLevel |= AccessLevels.CurrentRead;
            variable.UserAccessLevel |= AccessLevels.CurrentRead;
        }
    }
}

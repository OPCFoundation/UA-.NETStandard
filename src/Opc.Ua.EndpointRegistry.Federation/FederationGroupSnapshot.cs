/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Globalization;
using System.Text;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Federation
{
    /// <summary>
    /// A provider-verified Group view. Immutable origin, collection, GroupId and native target
    /// pins are independent of its relocatable transport address.
    /// </summary>
    public sealed class FederationGroupSnapshot
    {
        /// <summary>
        /// Captures independently verified Group metadata and provider evidence.
        /// </summary>
        public FederationGroupSnapshot(
            RegistryEntityReferenceDataType source,
            FederationTrustBinding binding,
            FederationNodeObservation? registry,
            FederationNodeObservation? group,
            RegistryRecordDataType metadata,
            uint epoch,
            FederationGroupSnapshot? previous = null)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (binding is null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            if (metadata is null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }
            string xid = source.Xid ?? throw new ArgumentException("A Group Xid is required.");
            string[] parts = xid.Split('/');
            if (source.Role != "Group" || parts.Length != 3 || parts[0].Length != 0 ||
                parts[1] is not ("endpoints" or "messagegroups") || parts[2].Length == 0 ||
                parts[2] is "." or ".." || epoch == 0 ||
                !binding.Origin.Equals(new RegistryOriginKey(source)) || !binding.Authorizes(source.Locator!))
            {
                throw new ArgumentException("The Group collection, identity, origin or locator contradicts trust.");
            }
            ExpandedNodeId expectedType = parts[1] == "endpoints"
                ? ObjectTypeIds.EndpointGroupType : ObjectTypeIds.MessageDefinitionGroupType;
            if (binding.ApplicationUri.Length > 0)
            {
                if (registry is null || group is null || registry.Node != binding.RegistryRoot ||
                    registry.NodeClass != NodeClass.Object || group.NodeClass != NodeClass.Object ||
                    !registry.HasType(XRegistry.ObjectTypeIds.RegistryType) ||
                    !registry.HasType(binding.RequiredRegistryType) ||
                    !group.HasType(XRegistry.ObjectTypeIds.GroupType) || !group.HasType(expectedType) ||
                    !source.HasNativeTarget || source.NativeTarget != group.Node || group.Node == registry.Node)
                {
                    throw new ArgumentException("The authenticated Group/root/type/target evidence contradicts trust.");
                }
            }
            else if (registry is not null || group is not null || source.HasNativeTarget || !source.NativeTarget.IsNull)
            {
                throw new ArgumentException("An HTTP Group observation cannot invent native targets.");
            }
            if (parts[1] == "endpoints" && metadata is not EndpointDataType ||
                parts[1] == "messagegroups" && metadata is not MessageGroupDataType)
            {
                throw new ArgumentException("The Group metadata has another domain type.");
            }
            RegistryValueDataType expectedEpoch = RegistryValues.Parse(
                Encoding.UTF8.GetBytes(epoch.ToString(CultureInfo.InvariantCulture)));
            bool matches = metadata switch
            {
                EndpointDataType endpoint => endpoint.PresentFields.Contains("EndpointId") &&
                    endpoint.EndpointId == parts[2] &&
                    (!endpoint.PresentFields.Contains("Epoch") || RegistryValues.Identical(endpoint.Epoch, expectedEpoch)),
                MessageGroupDataType messages => messages.PresentFields.Contains("MessageGroupId") &&
                    messages.MessageGroupId == parts[2] &&
                    (!messages.PresentFields.Contains("Epoch") || RegistryValues.Identical(messages.Epoch, expectedEpoch)),
                _ => false
            };
            if (!matches)
            {
                throw new ArgumentException("The native Group snapshot contradicts the observed identity or epoch.");
            }
            if (previous is not null &&
                (!previous.Origin.Equals(binding.Origin) || previous.m_source.Xid != xid ||
                    previous.m_source.NativeTarget != source.NativeTarget ||
                    previous.ApplicationUri != binding.ApplicationUri || previous.RegistryRoot != binding.RegistryRoot))
            {
                throw new ArgumentException("Group relocation changes an immutable identity pin.");
            }
            m_source = (RegistryEntityReferenceDataType)source.Clone();
            m_metadata = (RegistryRecordDataType)metadata.Clone();
            Origin = binding.Origin;
            ApplicationUri = binding.ApplicationUri;
            RegistryRoot = binding.RegistryRoot;
            Epoch = epoch;
        }

        /// <summary>
        /// Gets the immutable origin key.
        /// </summary>
        public RegistryOriginKey Origin { get; }

        /// <summary>
        /// Gets the pinned authenticated ApplicationUri.
        /// </summary>
        public string ApplicationUri { get; }

        /// <summary>
        /// Gets the pinned portable registry root.
        /// </summary>
        public ExpandedNodeId RegistryRoot { get; }

        /// <summary>
        /// Gets the committed remote Group epoch.
        /// </summary>
        public uint Epoch { get; }

        /// <summary>
        /// Returns a copy of the verified Group selection.
        /// </summary>
        public RegistryEntityReferenceDataType Source => (RegistryEntityReferenceDataType)m_source.Clone();

        /// <summary>
        /// Returns a complete named native Group view without JSON parsing.
        /// </summary>
        public RegistryRecordDataType Metadata => (RegistryRecordDataType)m_metadata.Clone();

        private readonly RegistryEntityReferenceDataType m_source;
        private readonly RegistryRecordDataType m_metadata;
    }
}

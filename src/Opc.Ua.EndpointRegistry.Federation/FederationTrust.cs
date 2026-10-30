/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Federation
{
    /// <summary>Server configuration, never a resolution request's assertion of trust.</summary>
    public sealed class FederationTrustBinding
    {
        /// <summary>Creates an immutable authorized origin and route binding.</summary>
        public FederationTrustBinding(
            RegistryEntityReferenceDataType origin,
            string applicationUri,
            ExpandedNodeId registryRoot,
            ArrayOf<string> authorizedLocators,
            ExpandedNodeId requiredTargetType = default,
            ExpandedNodeId requiredRegistryType = default)
        {
            Origin = new RegistryOriginKey(origin);
            ApplicationUri = applicationUri ?? throw new ArgumentNullException(nameof(applicationUri));
            RegistryRoot = registryRoot;
            if ((ApplicationUri.Length > 0) != !registryRoot.IsNull)
            {
                throw new ArgumentException("An OPC UA binding requires both application and root.");
            }
            if (ApplicationUri.Length > 0)
            {
                Absolute(ApplicationUri);
                Portable(registryRoot);
            }
            else if (Origin.OriginUri.Length == 0)
            {
                throw new ArgumentException("A non-UA provider requires a stable OriginUri.");
            }
            if (Origin.OriginUri.Length == 0 &&
                (Origin.ApplicationUri != ApplicationUri || Origin.RegistryNode != registryRoot))
            {
                throw new ArgumentException("The configured application/root contradicts the paired origin.");
            }
            if (authorizedLocators.Count == 0)
            {
                throw new ArgumentException("At least one authorized locator is required.");
            }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string locator in authorizedLocators)
            {
                Absolute(locator);
                if (!unique.Add(locator))
                {
                    throw new ArgumentException("Duplicate authorized locator.");
                }
            }
            m_locators = [.. authorizedLocators];
            RequiredTargetType = requiredTargetType.IsNull ? ObjectTypeIds.MessageDefinitionType : requiredTargetType;
            RequiredRegistryType = requiredRegistryType.IsNull ? ObjectTypeIds.EndpointRegistryType : requiredRegistryType;
            Portable(RequiredTargetType);
            Portable(RequiredRegistryType);
        }

        /// <summary>Gets the exact immutable origin key.</summary>
        public RegistryOriginKey Origin { get; }

        /// <summary>Gets the expected authenticated application, empty for HTTP.</summary>
        public string ApplicationUri { get; }

        /// <summary>Gets the expected portable registry root, null for HTTP.</summary>
        public ExpandedNodeId RegistryRoot { get; }

        /// <summary>Gets the pinned required domain target type.</summary>
        public ExpandedNodeId RequiredTargetType { get; }

        /// <summary>Gets the pinned required domain registry type.</summary>
        public ExpandedNodeId RequiredRegistryType { get; }

        /// <summary>Gets a copy of the exact authorized routes.</summary>
        public ArrayOf<string> AuthorizedLocators => [.. m_locators];

        /// <summary>Tests an exact route without URI normalization.</summary>
        public bool Authorizes(string locator)
        {
            foreach (string item in m_locators)
            {
                if (string.Equals(item, locator, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        internal static void Portable(ExpandedNodeId node)
        {
            if (node.IsNull || node.NamespaceIndex != 0 || node.ServerIndex != 0 ||
                string.IsNullOrEmpty(node.NamespaceUri))
            {
                throw new ArgumentException("A non-null portable NodeId without Session indexes is required.");
            }
            Absolute(node.NamespaceUri);
        }

        internal static void Absolute(string text)
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute URI is required.");
            }
        }

        private readonly ArrayOf<string> m_locators;
    }

    /// <summary>Semantic source identity. Hash codes are never persisted or used as keys.</summary>
    public sealed class FederationSourceKey : IEquatable<FederationSourceKey>
    {
        /// <summary>Captures the exact origin, Resource/Version role and concrete Xid.</summary>
        public FederationSourceKey(RegistryEntityReferenceDataType source)
        {
            Origin = new RegistryOriginKey(source);
            Xid = source.Xid ?? throw new ArgumentException("A concrete Xid is required.");
            Role = source.Role ?? throw new ArgumentException("A metadata role is required.");
            LogicalXid = ValidateXid(Xid, Role);
        }

        /// <summary>Gets the immutable origin.</summary>
        public RegistryOriginKey Origin { get; }

        /// <summary>Gets the exact role.</summary>
        public string Role { get; }

        /// <summary>Gets the exact Xid.</summary>
        public string Xid { get; }

        /// <summary>Gets the logical Resource Xid of either alias.</summary>
        public string LogicalXid { get; }

        /// <inheritdoc/>
        public bool Equals(FederationSourceKey? other) => other is not null && Origin.Equals(other.Origin) &&
            Role == other.Role && Xid == other.Xid;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as FederationSourceKey);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Origin, Role, Xid);

        internal static string ValidateXid(string xid, string role)
        {
            try
            {
                EndpointRegistryRules.ValidateMessageReference(xid, "Reference");
            }
            catch (RegistryRuleException error)
            {
                throw new ArgumentException("A concrete metadata Xid is required.", nameof(xid), error);
            }
            string[] parts = xid.Split('/');
            if (parts.Length != (role == "MetadataResource" ? 5 : role == "MetadataVersion" ? 7 : 0) ||
                parts[0].Length != 0 || parts[3] != "messages" ||
                role == "MetadataVersion" && parts[5] != "versions")
            {
                throw new ArgumentException("Metadata Xid does not identify the required Resource collection and role.");
            }
            return string.Join("/", parts, 0, 5);
        }
    }

    /// <summary>Independently read Node evidence, not fields supplied by the resolution client.</summary>
    public sealed class FederationNodeObservation
    {
        /// <summary>Captures one portable Node, class, structural role and actual type ancestry.</summary>
        public FederationNodeObservation(
            ExpandedNodeId node, NodeClass nodeClass, string role, ArrayOf<ExpandedNodeId> typeAncestors)
        {
            FederationTrustBinding.Portable(node);
            Node = node;
            NodeClass = nodeClass;
            Role = role ?? throw new ArgumentNullException(nameof(role));
            var seen = new HashSet<ExpandedNodeId>();
            foreach (ExpandedNodeId type in typeAncestors)
            {
                FederationTrustBinding.Portable(type);
                if (!seen.Add(type))
                {
                    throw new ArgumentException("Ambiguous type ancestry.");
                }
            }
            m_types = [.. typeAncestors];
        }

        /// <summary>Gets the observed portable Node.</summary>
        public ExpandedNodeId Node { get; }

        /// <summary>Gets its observed class.</summary>
        public NodeClass NodeClass { get; }

        /// <summary>Gets its structural role.</summary>
        public string Role { get; }

        /// <summary>Gets a copy of the observed type chain.</summary>
        public ArrayOf<ExpandedNodeId> TypeAncestors => [.. m_types];

        internal bool HasType(ExpandedNodeId type)
        {
            foreach (ExpandedNodeId item in m_types)
            {
                if (item == type)
                {
                    return true;
                }
            }
            return false;
        }

        private readonly ArrayOf<ExpandedNodeId> m_types;
    }

    /// <summary>
    /// Provider-owned evidence from an authenticated Session or a separately authorized HTTP observation.
    /// No claim field or Boolean in MessageResolutionRequestDataType can construct trust.
    /// </summary>
    public sealed class FederationMetadataObservation
    {
        /// <summary>Captures independently observed identity and metadata-only constraints.</summary>
        public FederationMetadataObservation(
            RegistryEntityReferenceDataType origin,
            string locator,
            string applicationUri,
            FederationNodeObservation? registry,
            FederationNodeObservation? target,
            ExpandedNodeId owningRegistry,
            string logicalXid,
            string versionId,
            bool hasDocument,
            uint maxVersions)
        {
            Origin = new RegistryOriginKey(origin);
            Locator = locator ?? throw new ArgumentNullException(nameof(locator));
            ApplicationUri = applicationUri ?? throw new ArgumentNullException(nameof(applicationUri));
            Registry = registry;
            Target = target;
            OwningRegistry = owningRegistry;
            LogicalXid = logicalXid ?? throw new ArgumentNullException(nameof(logicalXid));
            VersionId = versionId ?? throw new ArgumentNullException(nameof(versionId));
            HasDocument = hasDocument;
            MaxVersions = maxVersions;
        }

        /// <summary>Gets the independently observed origin.</summary>
        public RegistryOriginKey Origin { get; }

        /// <summary>Gets the authenticated route.</summary>
        public string Locator { get; }

        /// <summary>Gets the authenticated ApplicationUri, empty for non-UA observations.</summary>
        public string ApplicationUri { get; }

        /// <summary>Gets the independently observed registry root.</summary>
        public FederationNodeObservation? Registry { get; }

        /// <summary>Gets the independently observed logical metadata Object.</summary>
        public FederationNodeObservation? Target { get; }

        /// <summary>Gets the registry established by browsed ownership references.</summary>
        public ExpandedNodeId OwningRegistry { get; }

        /// <summary>Gets the independently read logical Xid.</summary>
        public string LogicalXid { get; }

        /// <summary>Gets the independently read sole VersionId.</summary>
        public string VersionId { get; }

        /// <summary>Gets the independently read HasDocument value.</summary>
        public bool HasDocument { get; }

        /// <summary>Gets the independently read MaxVersions value.</summary>
        public uint MaxVersions { get; }
    }

    /// <summary>
    /// Historical identity pins, not live transport authentication. A new route always needs fresh evidence.
    /// </summary>
    public sealed class FederationReferenceSnapshot
    {
        /// <summary>Captures a previously validated selection and its domain pins.</summary>
        public FederationReferenceSnapshot(
            RegistryEntityReferenceDataType source,
            FederationTrustBinding binding,
            FederationMetadataObservation evidence)
        {
            new FederationMetadataSelector().Select(source, binding, evidence);
            Source = new FederationSourceKey(source);
            Evidence = evidence;
            TargetType = binding.RequiredTargetType;
            RegistryType = binding.RequiredRegistryType;
        }

        /// <summary>Gets the pinned exact origin, reference role and Xid.</summary>
        public FederationSourceKey Source { get; }

        /// <summary>Gets the historical independent observations.</summary>
        public FederationMetadataObservation Evidence { get; }

        /// <summary>Gets the pinned required target domain.</summary>
        public ExpandedNodeId TargetType { get; }

        /// <summary>Gets the pinned required registry domain.</summary>
        public ExpandedNodeId RegistryType { get; }
    }

    /// <summary>Fail-closed selection of Message metadata without any I/O.</summary>
    public sealed class FederationMetadataSelector
    {
        /// <summary>Validates a claim against server configuration and independent provider observations.</summary>
        public RegistryEntityReferenceDataType Select(
            RegistryEntityReferenceDataType reference,
            FederationTrustBinding binding,
            FederationMetadataObservation observation,
            FederationReferenceSnapshot? previous = null)
        {
            var key = new FederationSourceKey(reference);
            if (!key.Origin.Equals(binding.Origin) || !key.Origin.Equals(observation.Origin))
            {
                throw new ArgumentException("Origin claim has no matching configured/observed trust binding.");
            }
            if (!binding.Authorizes(reference.Locator!) || observation.Locator != reference.Locator)
            {
                throw new ArgumentException("Metadata locator is not authorized.");
            }
            if (observation.ApplicationUri != binding.ApplicationUri || observation.LogicalXid != key.LogicalXid)
            {
                throw new ArgumentException("Metadata application or Xid contradicts trust.");
            }
            if (observation.HasDocument || observation.MaxVersions != 1)
            {
                throw new ArgumentException("Metadata Resource must have no document and one retained Version.");
            }
            if (observation.VersionId.Length == 0 || key.Role == "MetadataVersion" &&
                key.Xid != key.LogicalXid + "/versions/" + observation.VersionId)
            {
                throw new ArgumentException("The requested sole Version does not match the observed committed Version.");
            }
            FederationSourceKey.ValidateXid(key.LogicalXid + "/versions/" + observation.VersionId, "MetadataVersion");
            if (binding.ApplicationUri.Length > 0)
            {
                FederationNodeObservation root = observation.Registry ??
                    throw new ArgumentException("An authenticated registry root observation is required.");
                FederationNodeObservation target = observation.Target ??
                    throw new ArgumentException("An authenticated metadata target observation is required.");
                if (root.NodeClass != NodeClass.Object || root.Role != "RegistryRoot" ||
                    root.Node != binding.RegistryRoot || !root.HasType(XRegistry.ObjectTypeIds.RegistryType) ||
                    !root.HasType(binding.RequiredRegistryType))
                {
                    throw new ArgumentException("Metadata registry root or domain ancestry contradicts trust.");
                }
                if (target.NodeClass != NodeClass.Object || target.Role != "MetadataResource" ||
                    !target.HasType(XRegistry.ObjectTypeIds.MetadataResourceType) ||
                    !target.HasType(binding.RequiredTargetType) || target.Node == root.Node ||
                    observation.OwningRegistry != root.Node || !reference.HasNativeTarget ||
                    reference.NativeTarget != target.Node)
                {
                    throw new ArgumentException("Metadata target type, native target or registry ownership disagrees.");
                }
                FederationTrustBinding.Portable(reference.NativeTarget);
            }
            else if (reference.HasNativeTarget || !reference.NativeTarget.IsNull ||
                observation.Registry is not null || observation.Target is not null || !observation.OwningRegistry.IsNull)
            {
                throw new ArgumentException("A non-UA provider must not invent UA targets or Sessions.");
            }
            if (previous is not null && (!previous.Source.Equals(key) ||
                previous.TargetType != binding.RequiredTargetType || previous.RegistryType != binding.RequiredRegistryType ||
                !previous.Evidence.Origin.Equals(observation.Origin) ||
                previous.Evidence.ApplicationUri != observation.ApplicationUri ||
                previous.Evidence.LogicalXid != observation.LogicalXid ||
                previous.Evidence.OwningRegistry != observation.OwningRegistry ||
                !SameNode(previous.Evidence.Registry, observation.Registry) ||
                !SameNode(previous.Evidence.Target, observation.Target)))
            {
                throw new ArgumentException("Locator-only relocation cannot change immutable source, application, root or type/target pins.");
            }
            return (RegistryEntityReferenceDataType)reference.Clone();
        }

        private static bool SameNode(FederationNodeObservation? first, FederationNodeObservation? second)
        {
            if (first is null || second is null)
            {
                return first == second;
            }
            if (first.Node != second.Node || first.NodeClass != second.NodeClass || first.Role != second.Role ||
                first.TypeAncestors.Count != second.TypeAncestors.Count)
            {
                return false;
            }
            foreach (ExpandedNodeId type in first.TypeAncestors)
            {
                if (!second.HasType(type))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

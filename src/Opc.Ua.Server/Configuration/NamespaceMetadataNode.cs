// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A <c>NamespaceMetadataType</c> Object under <c>Server/Namespaces</c>
    /// (OPC 10000-5 §6.3.13) together with the properties namespace default
    /// permissions are read from. The Object is either a generated
    /// <see cref="NamespaceMetadataState"/>, whose property slots are read on
    /// every access, or an untyped <see cref="BaseObjectState"/> such as the
    /// one an untyped NodeSet import creates, whose properties are resolved
    /// by BrowseName when the Object is discovered.
    /// </summary>
    internal sealed class NamespaceMetadataNode
    {
        /// <summary>
        /// Creates the view of an untyped metadata node with its resolved properties.
        /// </summary>
        /// <param name="node">The metadata Object.</param>
        /// <param name="namespaceUri">The <c>NamespaceUri</c> property.</param>
        /// <param name="defaultAccessRestrictions">The <c>DefaultAccessRestrictions</c> property.</param>
        /// <param name="defaultRolePermissions">The <c>DefaultRolePermissions</c> property.</param>
        /// <param name="defaultUserRolePermissions">The <c>DefaultUserRolePermissions</c> property.</param>
        /// <param name="isIncomplete">
        /// Whether a <c>HasProperty</c> target could not be resolved while a property is missing.
        /// </param>
        public NamespaceMetadataNode(
            BaseObjectState node,
            BaseVariableState? namespaceUri,
            BaseVariableState? defaultAccessRestrictions,
            BaseVariableState? defaultRolePermissions,
            BaseVariableState? defaultUserRolePermissions,
            bool isIncomplete = false)
        {
            Node = node;
            m_namespaceUri = namespaceUri;
            m_defaultAccessRestrictions = defaultAccessRestrictions;
            m_defaultRolePermissions = defaultRolePermissions;
            m_defaultUserRolePermissions = defaultUserRolePermissions;
            IsIncomplete = isIncomplete;
        }

        /// <summary>
        /// Creates the view of a generated metadata node.
        /// </summary>
        private NamespaceMetadataNode(NamespaceMetadataState state)
        {
            Node = state;
            TypedState = state;
        }

        /// <summary>
        /// The metadata Object.
        /// </summary>
        public BaseObjectState Node { get; }

        /// <summary>
        /// The metadata Object when it is a generated <see cref="NamespaceMetadataState"/>,
        /// otherwise <c>null</c>.
        /// </summary>
        public NamespaceMetadataState? TypedState { get; }

        /// <summary>
        /// <c>true</c> when a <c>HasProperty</c> target of the untyped Object could not be
        /// resolved while one of its properties is still missing: the unresolved target may
        /// be a default, so the defaults of the namespace are unknown, or the
        /// <c>NamespaceUri</c>, so the namespace the Object describes is unknown. Permission
        /// checks must fail closed until the Object is resolved again.
        /// </summary>
        public bool IsIncomplete { get; }

        /// <summary>
        /// The namespace the Object describes, read from its <c>NamespaceUri</c> property;
        /// <c>null</c> while that property holds no URI, including an empty one.
        /// </summary>
        public string? NamespaceUri
        {
            get
            {
                string? namespaceUri = null;
                if (TypedState != null)
                {
                    namespaceUri = TypedState.NamespaceUri?.Value;
                }
                else if (m_namespaceUri != null && m_namespaceUri.Value.TryGetValue(out string? value))
                {
                    namespaceUri = value;
                }

                return string.IsNullOrEmpty(namespaceUri) ? null : namespaceUri;
            }
        }

        /// <summary>
        /// The <c>DefaultAccessRestrictions</c> property, if present.
        /// </summary>
        public BaseVariableState? DefaultAccessRestrictions =>
            TypedState != null ? TypedState.DefaultAccessRestrictions : m_defaultAccessRestrictions;

        /// <summary>
        /// The <c>DefaultRolePermissions</c> property, if present.
        /// </summary>
        public BaseVariableState? DefaultRolePermissions =>
            TypedState != null ? TypedState.DefaultRolePermissions : m_defaultRolePermissions;

        /// <summary>
        /// The <c>DefaultUserRolePermissions</c> property, if present.
        /// </summary>
        public BaseVariableState? DefaultUserRolePermissions =>
            TypedState != null ? TypedState.DefaultUserRolePermissions : m_defaultUserRolePermissions;

        /// <summary>
        /// Returns the view of a generated metadata node. The same view is
        /// returned for the same node, so views can be compared by reference.
        /// </summary>
        public static NamespaceMetadataNode? From(NamespaceMetadataState? state)
        {
            return state == null ? null : s_typed.GetValue(state, static s => new NamespaceMetadataNode(s));
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="typeDefinitionId"/> is
        /// <c>NamespaceMetadataType</c> or one of its subtypes.
        /// </summary>
        public static bool IsNamespaceMetadataType(ISystemContext context, NodeId typeDefinitionId)
        {
            if (typeDefinitionId.IsNull)
            {
                return false;
            }

            return typeDefinitionId == ObjectTypeIds.NamespaceMetadataType ||
                (context.TypeTable?.IsTypeOf(typeDefinitionId, ObjectTypeIds.NamespaceMetadataType) ?? false);
        }

        private readonly BaseVariableState? m_namespaceUri;
        private readonly BaseVariableState? m_defaultAccessRestrictions;
        private readonly BaseVariableState? m_defaultRolePermissions;
        private readonly BaseVariableState? m_defaultUserRolePermissions;
        private static readonly ConditionalWeakTable<NamespaceMetadataState, NamespaceMetadataNode> s_typed = new();
    }
}

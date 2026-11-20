// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// Extends reference description with helper functionality
    /// </summary>
    public static class ReferenceDescriptionExtensions
    {
        /// <summary>
        /// Sets the reference type for the reference.
        /// </summary>
        public static void SetReferenceType(
            this ReferenceDescription referenceDescription,
            BrowseResultMask resultMask,
            NodeId referenceTypeId,
            bool isForward)
        {
            if (((int)resultMask & (int)BrowseResultMask.ReferenceTypeId) != 0)
            {
                referenceDescription.ReferenceTypeId = referenceTypeId;
            }
            else
            {
                referenceDescription.ReferenceTypeId = default;
            }

            if (((int)resultMask & (int)BrowseResultMask.IsForward) != 0)
            {
                referenceDescription.IsForward = isForward;
            }
            else
            {
                referenceDescription.IsForward = false;
            }
        }

        /// <summary>
        /// Sets the target attributes for the reference.
        /// </summary>
        public static void SetTargetAttributes(
            this ReferenceDescription referenceDescription,
            BrowseResultMask resultMask,
            NodeClass nodeClass,
            QualifiedName browseName,
            LocalizedText displayName,
            ExpandedNodeId typeDefinition)
        {
            if (((int)resultMask & (int)BrowseResultMask.NodeClass) != 0)
            {
                referenceDescription.NodeClass = nodeClass;
            }
            else
            {
                referenceDescription.NodeClass = 0;
            }

            if (((int)resultMask & (int)BrowseResultMask.BrowseName) != 0)
            {
                referenceDescription.BrowseName = browseName;
            }
            else
            {
                referenceDescription.BrowseName = default;
            }

            if (((int)resultMask & (int)BrowseResultMask.DisplayName) != 0)
            {
                referenceDescription.DisplayName = displayName;
            }
            else
            {
                referenceDescription.DisplayName = default;
            }

            if (((int)resultMask & (int)BrowseResultMask.TypeDefinition) != 0)
            {
                referenceDescription.TypeDefinition = typeDefinition;
            }
            else
            {
                referenceDescription.TypeDefinition = default;
            }
        }
    }
}

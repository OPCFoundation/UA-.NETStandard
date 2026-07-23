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

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Resolves and classifies generated OPC 40010 Robotics ObjectTypes and
    /// ReferenceTypes against a server namespace table.
    /// </summary>
    /// <remarks>
    /// The generated <see cref="ObjectTypeIds"/> and <see cref="ReferenceTypeIds"/>
    /// classes are the source of truth for the model. The numeric constants in
    /// this class are retained for compatibility with earlier package versions.
    /// </remarks>
    public static class RoboticsModel
    {
        /// <summary>
        /// The numeric identifier of the generated MotionDeviceSystemType.
        /// </summary>
        public const uint MotionDeviceSystemType = 1002;

        /// <summary>
        /// The numeric identifier of the generated ControllerType.
        /// </summary>
        public const uint ControllerType = 1003;

        /// <summary>
        /// The numeric identifier of the generated MotionDeviceType.
        /// </summary>
        public const uint MotionDeviceType = 1004;

        /// <summary>
        /// The numeric identifier of the generated AxisType.
        /// </summary>
        public const uint AxisType = 16601;

        /// <summary>
        /// Resolves a numeric Robotics identifier into the Robotics namespace
        /// in the supplied namespace table.
        /// </summary>
        /// <param name="identifier">
        /// A Robotics numeric identifier, such as <see cref="MotionDeviceType"/>.
        /// </param>
        /// <param name="namespaceUris">The server's namespace table.</param>
        /// <returns>The resolved NodeId.</returns>
        public static NodeId TypeNodeId(uint identifier, NamespaceTable namespaceUris)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            return NodeId.Create(identifier, RoboticsNamespaces.Robotics, namespaceUris);
        }

        /// <summary>
        /// Resolves a generated Robotics ObjectType identifier against a server
        /// namespace table.
        /// </summary>
        /// <param name="identifier">
        /// An identifier from the generated <see cref="ObjectTypeIds"/> class.
        /// </param>
        /// <param name="namespaceUris">The server's namespace table.</param>
        /// <returns>The resolved NodeId, or <see cref="NodeId.Null"/> when unresolved.</returns>
        public static NodeId ObjectTypeNodeId(
            ExpandedNodeId identifier,
            NamespaceTable namespaceUris)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            if (!ObjectTypeIds.TryGetBrowseName(identifier, out _))
            {
                return NodeId.Null;
            }

            return ResolveNodeId(identifier, namespaceUris);
        }

        /// <summary>
        /// Resolves a generated Robotics ReferenceType identifier against a
        /// server namespace table.
        /// </summary>
        /// <param name="identifier">
        /// An identifier from the generated <see cref="ReferenceTypeIds"/> class.
        /// </param>
        /// <param name="namespaceUris">The server's namespace table.</param>
        /// <returns>The resolved NodeId, or <see cref="NodeId.Null"/> when unresolved.</returns>
        public static NodeId ReferenceTypeNodeId(
            ExpandedNodeId identifier,
            NamespaceTable namespaceUris)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            if (!ReferenceTypeIds.TryGetBrowseName(identifier, out _))
            {
                return NodeId.Null;
            }

            return ResolveNodeId(identifier, namespaceUris);
        }

        /// <summary>
        /// Resolves a generated Robotics ObjectType by browse name.
        /// </summary>
        /// <param name="typeName">
        /// The generated browse name, with or without the trailing <c>Type</c>.
        /// </param>
        /// <param name="namespaceUris">The server's namespace table.</param>
        /// <param name="typeId">The resolved ObjectType NodeId.</param>
        /// <returns>
        /// <c>true</c> when the name is generated by the Robotics model and its
        /// namespace is present in <paramref name="namespaceUris"/>.
        /// </returns>
        public static bool TryGetObjectTypeNodeId(
            string typeName,
            NamespaceTable namespaceUris,
            out NodeId typeId)
        {
            if (typeName is null)
            {
                throw new ArgumentNullException(nameof(typeName));
            }

            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            string browseName = typeName.EndsWith("Type", StringComparison.Ordinal) ?
                typeName :
                typeName + "Type";
            if (ObjectTypeIds.TryGetValue(browseName, out ExpandedNodeId identifier))
            {
                typeId = ResolveNodeId(identifier, namespaceUris);
                return !typeId.IsNull;
            }

            typeId = NodeId.Null;
            return false;
        }

        /// <summary>
        /// Resolves a generated Robotics ReferenceType by browse name.
        /// </summary>
        /// <param name="referenceTypeName">The generated ReferenceType browse name.</param>
        /// <param name="namespaceUris">The server's namespace table.</param>
        /// <param name="referenceTypeId">The resolved ReferenceType NodeId.</param>
        /// <returns>
        /// <c>true</c> when the name is generated by the Robotics model and its
        /// namespace is present in <paramref name="namespaceUris"/>.
        /// </returns>
        public static bool TryGetReferenceTypeNodeId(
            string referenceTypeName,
            NamespaceTable namespaceUris,
            out NodeId referenceTypeId)
        {
            if (referenceTypeName is null)
            {
                throw new ArgumentNullException(nameof(referenceTypeName));
            }

            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            if (ReferenceTypeIds.TryGetValue(
                referenceTypeName,
                out ExpandedNodeId identifier))
            {
                referenceTypeId = ResolveNodeId(identifier, namespaceUris);
                return !referenceTypeId.IsNull;
            }

            referenceTypeId = NodeId.Null;
            return false;
        }

        /// <summary>
        /// Classifies a NodeId as any ObjectType generated from the Robotics
        /// NodeSet and returns its friendly type name.
        /// </summary>
        /// <param name="typeDefinition">The TypeDefinition NodeId to classify.</param>
        /// <param name="namespaceUris">The server's namespace table.</param>
        /// <param name="name">
        /// The generated browse name without the trailing <c>Type</c>.
        /// </param>
        /// <returns><c>true</c> when the NodeId is a generated Robotics ObjectType.</returns>
        public static bool TryGetRoboticsTypeName(
            NodeId typeDefinition,
            NamespaceTable namespaceUris,
            out string? name)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            name = null;
            int namespaceIndex = namespaceUris.GetIndex(RoboticsNamespaces.Robotics);
            if (namespaceIndex < 0 ||
                typeDefinition.NamespaceIndex != namespaceIndex ||
                !typeDefinition.TryGetValue(out uint identifier))
            {
                return false;
            }

            var expandedNodeId = new ExpandedNodeId(identifier, RoboticsNamespaces.Robotics);
            if (!ObjectTypeIds.TryGetBrowseName(expandedNodeId, out string? browseName) ||
                string.IsNullOrEmpty(browseName))
            {
                return false;
            }

            name = browseName.EndsWith("Type", StringComparison.Ordinal) ?
                browseName.Substring(0, browseName.Length - 4) :
                browseName;
            return true;
        }

        /// <summary>
        /// Classifies a NodeId as any ReferenceType generated from the Robotics
        /// NodeSet and returns its browse name.
        /// </summary>
        /// <param name="referenceTypeId">The ReferenceType NodeId to classify.</param>
        /// <param name="namespaceUris">The server's namespace table.</param>
        /// <param name="name">The generated ReferenceType browse name.</param>
        /// <returns><c>true</c> when the NodeId is a generated Robotics ReferenceType.</returns>
        public static bool TryGetRoboticsReferenceTypeName(
            NodeId referenceTypeId,
            NamespaceTable namespaceUris,
            out string? name)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            name = null;
            int namespaceIndex = namespaceUris.GetIndex(RoboticsNamespaces.Robotics);
            if (namespaceIndex < 0 ||
                referenceTypeId.NamespaceIndex != namespaceIndex ||
                !referenceTypeId.TryGetValue(out uint identifier))
            {
                return false;
            }

            var expandedNodeId = new ExpandedNodeId(identifier, RoboticsNamespaces.Robotics);
            return ReferenceTypeIds.TryGetBrowseName(expandedNodeId, out name);
        }

        private static NodeId ResolveNodeId(
            ExpandedNodeId identifier,
            NamespaceTable namespaceUris)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            return ExpandedNodeId.ToNodeId(identifier, namespaceUris);
        }
    }
}

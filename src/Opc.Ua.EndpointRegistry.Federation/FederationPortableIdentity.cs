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
    /// <summary>Explicit conversion at the authenticated provider boundary, not during resolution.</summary>
    public static class FederationPortableIdentity
    {
        /// <summary>Resolves a transport NodeId using an independently obtained namespace table.</summary>
        public static ExpandedNodeId FromNode(NodeId node, NamespaceTable namespaces)
        {
            if (node.IsNull)
            {
                throw new ArgumentException("A non-null observed NodeId is required.");
            }
            string uri = namespaces.GetString(node.NamespaceIndex) ??
                throw new ArgumentException("Unresolved Node namespace.");
            FederationTrustBinding.Absolute(uri);
            return new ExpandedNodeId(node.WithNamespaceIndex(0), uri, 0);
        }

        /// <summary>
        /// Resolves the referencing Server's namespace/server indexes before selecting a remote Session.
        /// The result retains exact origin, local portable identity, role and Xid.
        /// </summary>
        public static RegistryEntityReferenceDataType ResolveReference(
            RegistryEntityReferenceDataType reference,
            NamespaceTable sourceNamespaces,
            StringTable sourceServers,
            FederationTrustBinding binding)
        {
            if (!reference.HasNativeTarget)
            {
                throw new ArgumentException("An OPC UA external target is required.");
            }
            ValidateTable(sourceNamespaces.ToArray(), true);
            ValidateTable(sourceServers.ToArray(), false);
            ExpandedNodeId external = reference.NativeTarget;
            if (external.ServerIndex >= sourceServers.Count ||
                sourceServers.GetString(external.ServerIndex) != binding.ApplicationUri)
            {
                throw new ArgumentException("ExternalReference ServerIndex does not identify the trusted ApplicationUri.");
            }
            ExpandedNodeId portable = string.IsNullOrEmpty(external.NamespaceUri)
                ? FromNode(external.InnerNodeId, sourceNamespaces)
                : new ExpandedNodeId(external.InnerNodeId.WithNamespaceIndex(0), external.NamespaceUri, 0);
            FederationTrustBinding.Portable(portable);
            var result = (RegistryEntityReferenceDataType)reference.Clone();
            result.NativeTarget = portable;
            return result;
        }

        internal static void ValidateTable(ArrayOf<string> values, bool namespaces)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (values.Count == 0 || namespaces && values[0] != Ua.Namespaces.OpcUa)
            {
                throw new ArgumentException("Missing Core namespace or empty authenticated URI table.");
            }
            foreach (string value in values)
            {
                FederationTrustBinding.Absolute(value);
                if (!seen.Add(value))
                {
                    throw new ArgumentException("Ambiguous authenticated URI table.");
                }
            }
        }
    }
}

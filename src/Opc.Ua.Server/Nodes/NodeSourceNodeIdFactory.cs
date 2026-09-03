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
using System.Globalization;
using System.Text;

namespace Opc.Ua.Server.Nodes
{
    /// <summary>
    /// Formats deterministic child NodeIds for compositional authoring.
    /// </summary>
    internal static class NodeSourceNodeIdFactory
    {
        public static NodeId CreateChildNodeId(
            NodeId parentNodeId,
            QualifiedName browseName,
            ushort namespaceIndex,
            NamespaceTable namespaceUris)
        {
            if (parentNodeId.IsNull)
            {
                throw new ArgumentException("The parent NodeId is null.", nameof(parentNodeId));
            }
            if (browseName.IsNull)
            {
                throw new ArgumentException("The browse name is null.", nameof(browseName));
            }
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            var identifierBuffer = new StringBuilder();
            NodeId.Format(
                CultureInfo.InvariantCulture,
                identifierBuffer,
                parentNodeId.IdentifierAsString,
                parentNodeId.IdType,
                namespaceIndex: 0);
            string identifierText = identifierBuffer.ToString();
            string parentText;
            if (parentNodeId.NamespaceIndex != namespaceIndex)
            {
                string? namespaceUri = namespaceUris.GetString(
                    parentNodeId.NamespaceIndex);
                if (string.IsNullOrEmpty(namespaceUri))
                {
                    throw new InvalidOperationException(
                        $"Parent NodeId '{parentNodeId}' has no namespace URI.");
                }
                parentText = string.Concat(
                    "u:",
                    namespaceUri.Length.ToString(CultureInfo.InvariantCulture),
                    ":",
                    namespaceUri,
                    ":",
                    identifierText.Length.ToString(CultureInfo.InvariantCulture),
                    ":",
                    identifierText);
            }
            else
            {
                parentText = string.Concat(
                    "l:",
                    identifierText.Length.ToString(CultureInfo.InvariantCulture),
                    ":",
                    identifierText);
            }
            string browseNameText;
            if (browseName.NamespaceIndex == namespaceIndex)
            {
                browseNameText = string.Concat(
                    "l:",
                    browseName.Name!.Length.ToString(CultureInfo.InvariantCulture),
                    ":",
                    browseName.Name);
            }
            else if (browseName.NamespaceIndex == 0)
            {
                browseNameText = string.Concat(
                    "z:",
                    browseName.Name!.Length.ToString(CultureInfo.InvariantCulture),
                    ":",
                    browseName.Name);
            }
            else
            {
                string? browseNamespaceUri = namespaceUris.GetString(
                    browseName.NamespaceIndex);
                if (string.IsNullOrEmpty(browseNamespaceUri))
                {
                    throw new InvalidOperationException(
                        $"Browse name '{browseName}' has no namespace URI.");
                }
                browseNameText = string.Concat(
                    "u:",
                    browseNamespaceUri.Length.ToString(CultureInfo.InvariantCulture),
                    ":",
                    browseNamespaceUri,
                    ":",
                    browseName.Name!.Length.ToString(CultureInfo.InvariantCulture),
                    ":",
                    browseName.Name);
            }
            string identifier = string.Concat(
                "v1:",
                parentText.Length.ToString(CultureInfo.InvariantCulture),
                ":",
                parentText,
                ":",
                browseNameText.Length.ToString(CultureInfo.InvariantCulture),
                ":",
                browseNameText);
            return new NodeId(identifier, namespaceIndex);
        }
    }
}

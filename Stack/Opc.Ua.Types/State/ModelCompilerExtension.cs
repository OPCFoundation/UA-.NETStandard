/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;

namespace Opc.Ua
{
    public partial class NodeState
    {
        /// <summary>
        /// The specification that defines the node.
        /// </summary>
        public string Specification { get; set; }

        /// <summary>
        /// The documentation for the node that is saved in the NodeSet.
        /// </summary>
        public string NodeSetDocumentation { get; set; }

        /// <summary>
        /// The documentation for the node that is saved in the NodeSet.
        /// </summary>
        public bool DesignToolOnly { get; set; }
    }

    public partial class StructureDefinition
    {
        /// <summary>
        /// The first non-inherited field in the structure definition.
        /// </summary>
        public int FirstExplicitFieldIndex { get; set; }
    }

    public partial class EnumDefinition
    {
        /// <summary>
        /// If TRUE the values are bit positions rather than values.
        /// </summary>
        public bool IsOptionSet { get; set; }
    }

    public partial class NodeStateCollection
    {
        /// <summary>
        /// Writes the collection to a stream using the Opc.Ua.Schema.UANodeSet schema.
        /// </summary>
        public void SaveAsNodeSet2(
            ISystemContext context,
            Stream ostrm,
            Export.ModelTableEntry model,
            DateTime lastModified,
            bool outputRedundantNames)
        {
            var nodeSet = new Export.UANodeSet();

            if (lastModified != DateTime.MinValue)
            {
                nodeSet.LastModified = lastModified;
                nodeSet.LastModifiedSpecified = true;
            }

            nodeSet.NamespaceUris = (context.NamespaceUris?.ToArray()
                .Where(x => x != Namespaces.OpcUa)
                .ToArray());
            nodeSet.ServerUris = (context.ServerUris?.ToArray());

            if (nodeSet.NamespaceUris != null && nodeSet.NamespaceUris.Length == 0)
            {
                nodeSet.NamespaceUris = null;
            }

            if (nodeSet.ServerUris != null && nodeSet.ServerUris.Length == 0)
            {
                nodeSet.ServerUris = null;
            }

            if (model != null)
            {
                nodeSet.Models = [model];
            }

            for (int ii = 0; ii < s_aliasesToUse.Length; ii++)
            {
                nodeSet.AddAlias(context, s_aliasesToUse[ii].Alias, s_aliasesToUse[ii].NodeId);
            }

            for (int ii = 0; ii < Count; ii++)
            {
                nodeSet.Export(context, this[ii], outputRedundantNames);
            }

            nodeSet.Write(ostrm);
        }
    }
}

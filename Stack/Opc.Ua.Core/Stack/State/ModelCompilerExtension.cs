/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

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
            Opc.Ua.Export.UANodeSet nodeSet = new Opc.Ua.Export.UANodeSet();

            if (lastModified != DateTime.MinValue)
            {
                nodeSet.LastModified = lastModified;
                nodeSet.LastModifiedSpecified = true;
            }

            nodeSet.NamespaceUris = (context.NamespaceUris != null) ? context.NamespaceUris.ToArray().Where(x => x != Namespaces.OpcUa).ToArray() : null;
            nodeSet.ServerUris = (context.ServerUris != null) ? context.ServerUris.ToArray() : null;

            if (nodeSet.NamespaceUris != null && nodeSet.NamespaceUris.Length == 0) nodeSet.NamespaceUris = null;
            if (nodeSet.ServerUris != null && nodeSet.ServerUris.Length == 0) nodeSet.ServerUris = null;

            if (model != null)
            {
                nodeSet.Models = new Export.ModelTableEntry[] { model };
            }

            for (int ii = 0; ii < s_AliasesToUse.Length; ii++)
            {
                nodeSet.AddAlias(context, s_AliasesToUse[ii].Alias, s_AliasesToUse[ii].NodeId);
            }

            for (int ii = 0; ii < this.Count; ii++)
            {
                nodeSet.Export(context, this[ii], outputRedundantNames);
            }

            nodeSet.Write(ostrm);
        }
    }
}

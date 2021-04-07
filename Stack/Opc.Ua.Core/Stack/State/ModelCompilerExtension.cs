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

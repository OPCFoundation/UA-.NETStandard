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
using System.Collections.Generic;

namespace Opc.Ua.MachineTool
{
    /// <summary>
    /// Identifies the released models and their independently selectable dependency closures.
    /// </summary>
    public static class MachineToolModels
    {
        /// <summary>
        /// Returns the selected non-core namespace URIs in dependency order, without duplicates.
        /// The OPC UA core namespace is implicit at index zero; DI is included explicitly.
        /// </summary>
        /// <param name="parts">The models to select; None returns an empty collection.</param>
        public static ArrayOf<string> GetNamespaceUris(MachineToolParts parts = MachineToolParts.MachineTool)
        {
            Validate(parts);
            if (parts == MachineToolParts.None)
            {
                return ArrayOf<string>.Empty;
            }

            var namespaces = new List<string>
            {
                Di.Namespaces.OpcUaDi,
                IA.Namespaces.IA,
                Machinery.Namespaces.Machinery
            };
            if ((parts & MachineToolParts.MachineTool) != 0)
            {
                namespaces.Add(ISA95.JobControl.V2.Namespaces.ISA95JobControlV2);
                namespaces.Add(Machinery.Jobs.Namespaces.MachineryJobs);
                namespaces.Add(MachineToolNamespaceUri);
            }
            if ((parts & MachineToolParts.Wmtp) != 0)
            {
                namespaces.Add(IRDI.Namespaces.IRDI);
                namespaces.Add(PADIM.Namespaces.PADIM);
                namespaces.Add(Machinery.ProcessValues.Namespaces.MachineryProcessValues);
                namespaces.Add(WmtpNamespaceUri);
            }
            return namespaces.ToArrayOf();
        }

        /// <summary>
        /// Rejects undefined selection flags before model or factory state is changed.
        /// </summary>
        internal static void Validate(MachineToolParts parts)
        {
            if ((parts & ~MachineToolParts.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(parts));
            }
        }

        /// <summary>The OPC 40501-1 namespace.</summary>
        public const string MachineToolNamespaceUri = "http://opcfoundation.org/UA/MachineTool/";

        /// <summary>The independent OPC 40505 namespace.</summary>
        public const string WmtpNamespaceUri = "http://opcfoundation.org/UA/WMTP/";

        /// <summary>The immutable OPCFoundation/UA-Nodeset source revision for both models.</summary>
        public const string SourceCommit = "96a08945425d0295edb3607d2a38f6841ef44046";

        /// <summary>The SHA-256 of the MachineTool NodeSet with LF-normalized line endings.</summary>
        public const string MachineToolNodeSetSha256 =
            "ADA2158CF4B307E571A243D5944422496CC80E93D33831AE92C4162505E3B2C0";

        /// <summary>The SHA-256 of the WMTP NodeSet with LF-normalized line endings.</summary>
        public const string WmtpNodeSetSha256 =
            "421ABA93C231DADD672D10300830BE5C65F214A7325AC18465F66609C56C5EFC";
    }
}

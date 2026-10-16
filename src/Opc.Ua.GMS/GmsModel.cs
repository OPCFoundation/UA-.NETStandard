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

using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS
{
    /// <summary>
    /// Identifies the released OPC 40210 model and its dependency closure, without claiming runtime conformance.
    /// </summary>
    public static class GmsModel
    {
        /// <summary>
        /// Returns the non-core model namespaces in dependency order, without duplicates.
        /// Includes MachineTool's job dependencies and Machinery Result, but not the bundled WMTP model.
        /// </summary>
        public static ArrayOf<string> GetNamespaceUris()
        {
            return
            [
                .. MachineToolModels.GetNamespaceUris(MachineToolParts.MachineTool),
                Machinery.Result.Namespaces.MachineryResult,
                NamespaceUri
            ];
        }

        /// <summary>The namespace of the released Geometric Measurement Systems model.</summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/GMS/";

        /// <summary>The model version declared by the released NodeSet.</summary>
        public const string Version = "1.0.0";

        /// <summary>The OPC 40210 reference edition, distinct from the NodeSet model version.</summary>
        public const string ReferenceEdition = "1.0.1";

        /// <summary>The immutable OPCFoundation/UA-Nodeset revision containing the publication.</summary>
        public const string SourceCommit = "96a08945425d0295edb3607d2a38f6841ef44046";

        /// <summary>The SHA-256 of the UTF-8 NodeSet after CRLF line endings are normalized to LF.</summary>
        public const string NormalizedNodeSetSha256 =
            "9E510C01A2671B732E5EB27561B06A5BC040BB219412884D0A3AFF3C55B88EE0";
    }
}

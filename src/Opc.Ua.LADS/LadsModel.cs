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

namespace Opc.Ua.LADS
{
    /// <summary>
    /// Identifies the released OPC 30500-1 LADS model and its dependency closure,
    /// without claiming runtime conformance.
    /// </summary>
    public static class LadsModel
    {
        /// <summary>
        /// Returns the non-core model namespaces in dependency order, without duplicates:
        /// Device Integration, Industrial Automation, Machinery, Asset Management Basics and LADS.
        /// </summary>
        public static ArrayOf<string> GetNamespaceUris()
        {
            return
            [
                Di.Namespaces.OpcUaDi,
                IA.Namespaces.IA,
                Machinery.Namespaces.Machinery,
                AMB.Namespaces.AMB,
                NamespaceUri
            ];
        }

        /// <summary>The namespace of the released LADS model.</summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/LADS/";

        /// <summary>The model version declared by the released NodeSet.</summary>
        public const string Version = "1.0.0";

        /// <summary>The immutable OPCFoundation/UA-Nodeset revision containing the publication.</summary>
        public const string SourceCommit = "9f529354fc05a261295b4ce3e937a47410f7cced";

        /// <summary>The SHA-256 of the published NodeSet, which uses LF line endings.</summary>
        public const string NodeSetSha256 =
            "15FA32B4952F12A2D696FDDD2466A4C764AFDAC2125D198223547D787AAB6ADB";
    }
}
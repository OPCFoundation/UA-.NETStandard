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

namespace Opc.Ua.AutoID
{
    /// <summary>
    /// Identifies the released OPC 30010 AutoID Devices model and its dependency closure,
    /// without claiming runtime conformance.
    /// </summary>
    public static class AutoIdModel
    {
        /// <summary>
        /// Returns the non-core model namespaces in dependency order, without duplicates:
        /// Device Integration followed by AutoID.
        /// </summary>
        public static ArrayOf<string> GetNamespaceUris()
        {
            return [Di.Namespaces.OpcUaDi, NamespaceUri];
        }

        /// <summary>The namespace of the released AutoID Devices model.</summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/AutoID/";

        /// <summary>The model version declared by the released NodeSet.</summary>
        public const string Version = "1.01";

        /// <summary>The immutable OPCFoundation/UA-Nodeset revision containing the publication.</summary>
        public const string SourceCommit = "9f6c7660b94af24d32c4248d4b13bf60adab0301";

        /// <summary>The SHA-256 of the published NodeSet, which uses LF line endings.</summary>
        public const string NodeSetSha256 =
            "C3D5E77AAEEED03862620E307CDF4974E5998267E8FAA992C1F1A24F9BE6A4BA";
    }
}

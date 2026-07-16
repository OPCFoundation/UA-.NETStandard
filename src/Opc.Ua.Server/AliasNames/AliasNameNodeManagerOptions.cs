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

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Tunables for an <see cref="AliasNameNodeManager"/>.
    /// </summary>
    public sealed class AliasNameNodeManagerOptions
    {
        /// <summary>
        /// The namespace URI the manager registers and exposes its own
        /// category nodes under. Defaults to
        /// <c>http://opcfoundation.org/UA/AliasName/</c>.
        /// </summary>
        public string NamespaceUri { get; set; }
            = "http://opcfoundation.org/UA/AliasName/";

        /// <summary>
        /// When <c>true</c> (default), the manager publishes
        /// <c>Organizes</c> external references from the well-known
        /// <c>Aliases (i=23470)</c> object (Part 17 §9.2) into each of its
        /// root categories. Disable when the store is already serving
        /// standard well-known categories such as <c>TagVariables</c> /
        /// <c>Topics</c> (which are already organised under
        /// <c>Aliases</c>).
        /// </summary>
        public bool LinkToStandardAliasesObject { get; set; } = true;

        /// <summary>
        /// When <c>true</c> (default), the manager requires the calling
        /// user to have the <c>SecurityAdmin</c> well-known role on a
        /// <c>SignAndEncrypt</c> channel before allowing
        /// <c>AddAliasesToCategory</c> or
        /// <c>DeleteAliasesFromCategory</c> to succeed. Read methods
        /// (<c>FindAlias</c> / <c>FindAliasVerbose</c>) are not affected.
        /// </summary>
        public bool RequireSecurityAdminForMutations { get; set; } = true;

        /// <summary>
        /// When <c>true</c> (default), the manager registers its
        /// <see cref="IAliasNameStore"/> with the server-wide
        /// <see cref="IAliasNameStoreRegistry"/> resolved through
        /// <see cref="IAliasNameStoreRegistryProvider"/>. This is what
        /// enables <c>DiagnosticsNodeManager</c>'s late binder to wire the
        /// standard well-known <c>Aliases.FindAlias</c> /
        /// <c>TagVariables.FindAlias</c> / <c>Topics.FindAlias</c> methods
        /// through this manager's store. Disable when an alternate
        /// registration mechanism is in use.
        /// </summary>
        public bool RegisterWithServerRegistry { get; set; } = true;
    }
}

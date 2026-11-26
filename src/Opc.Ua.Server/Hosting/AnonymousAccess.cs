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

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// What an anonymous session may do with the nodes that carry no
    /// <c>RolePermissions</c> of their own, see
    /// <see cref="OpcUaServerOptions.AnonymousAccess"/>.
    /// </summary>
    public enum AnonymousAccess
    {
        /// <summary>
        /// An anonymous session may browse, read and receive events, but not
        /// write, call methods, update history or change the address space.
        /// Authenticated users are not restricted by this setting.
        /// </summary>
        ReadOnly = 0,

        /// <summary>
        /// The server applies no default: a node without
        /// <c>RolePermissions</c> in a namespace without
        /// <c>DefaultRolePermissions</c> is open to every session, including
        /// anonymous ones. Use only for demos, tests and closed networks.
        /// </summary>
        Unrestricted = 1
    }
}

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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional opt-in interface that exposes the role permissions a server
    /// applies to every namespace that configures no
    /// <c>DefaultRolePermissions</c> of its own, without any change to
    /// <see cref="IServerInternal"/>; external/mocked
    /// <see cref="IServerInternal"/> implementations remain unaffected and
    /// apply no server default.
    /// </summary>
    public interface IServerNamespaceDefaultPermissionsProvider
    {
        /// <summary>
        /// The role permissions of a node that has no <c>RolePermissions</c>
        /// in a namespace, other than namespace 0, that has no
        /// <c>DefaultRolePermissions</c>. Empty when the server applies no
        /// default, which leaves such a node unrestricted.
        /// </summary>
        ArrayOf<RolePermissionType> NamespaceDefaultRolePermissions { get; }
    }
}

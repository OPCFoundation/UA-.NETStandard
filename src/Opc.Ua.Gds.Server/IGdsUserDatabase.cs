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

using System.Collections.Generic;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Extends <see cref="IUserDatabase"/> with GDS-specific user
    /// properties required for the <see cref="GdsRole.ApplicationAdmin"/>
    /// privilege (OPC 10000-12 §7.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base <see cref="IUserDatabase"/> stores user credentials and
    /// roles but has no concept of per-user application bindings. A GDS
    /// that supports the <c>ApplicationAdmin</c> privilege needs to
    /// know which <c>ApplicationId</c>s each user may administer.
    /// </para>
    /// <para>
    /// Implement this interface (instead of plain
    /// <see cref="IUserDatabase"/>) and pass it to
    /// <see cref="GlobalDiscoverySampleServer"/> so that the
    /// impersonation handler can populate
    /// <see cref="GdsRoleBasedIdentity.AdministeredApplicationIds"/>
    /// automatically.
    /// </para>
    /// <example>
    /// <code>
    /// public class MyGdsUserDatabase : LinqUserDatabase, IGdsUserDatabase
    /// {
    ///     public IReadOnlyList&lt;NodeId&gt; GetAdministeredApplicationIds(string userName)
    ///     {
    ///         // look up the mapping from your persistent store
    ///         return _appAdminMap.TryGetValue(userName, out var ids) ? ids : null;
    ///     }
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    public interface IGdsUserDatabase : IUserDatabase
    {
        /// <summary>
        /// Returns the set of <c>ApplicationId</c>s the specified user
        /// is allowed to administer, or <c>null</c> / empty when the
        /// user does not hold the <see cref="GdsRole.ApplicationAdmin"/>
        /// privilege.
        /// </summary>
        /// <param name="userName">The user name.</param>
        /// <returns>
        /// A list of <see cref="NodeId"/>s representing the registered
        /// applications the user may administer, or <c>null</c>.
        /// </returns>
        IReadOnlyList<NodeId>? GetAdministeredApplicationIds(string userName);
    }
}

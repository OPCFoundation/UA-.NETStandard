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

#nullable enable

namespace Opc.Ua.Server
{
    /// <summary>
    /// Configures the default <see cref="RoleManager"/> implementation.
    /// </summary>
    /// <remarks>
    /// See OPC UA Part 18 §4.4.4
    /// (<see href="https://reference.opcfoundation.org/Core/Part18/v105/docs/4.4.4"/>)
    /// and the stack migration notes in
    /// <see href="https://github.com/OPCFoundation/UA-.NETStandard/blob/main/Docs/RoleBasedUserManagement.md"/>.
    /// </remarks>
    public sealed class RoleConfigurationOptions
    {
        /// <summary>
        /// Gets or sets whether <see cref="IdentityCriteriaType.Role"/> rules
        /// use the historical OPC UA role NodeId matching behavior.
        /// </summary>
        /// <remarks>
        /// The corrected default (<c>false</c>) evaluates Role criteria against
        /// roles asserted inside an access token via <c>IIdentityClaims.Roles</c>,
        /// including the Part 18 §4.4.4 <c>iss/roleName</c> prefix form.
        /// Set this to <c>true</c> for one-release compatibility only when an
        /// existing deployment intentionally relied on the legacy, spec-incorrect
        /// behavior where Role criteria matched already-granted OPC UA role
        /// NodeIds. Clear the flag after migrating those rules to access-token
        /// role claims. See the Role-Based Security migration notes
        /// (<see href="https://github.com/OPCFoundation/UA-.NETStandard/blob/main/Docs/RoleBasedUserManagement.md"/>).
        /// </remarks>
        public bool LegacyRoleCriteriaMatchesGrantedRoles { get; set; }
    }
}

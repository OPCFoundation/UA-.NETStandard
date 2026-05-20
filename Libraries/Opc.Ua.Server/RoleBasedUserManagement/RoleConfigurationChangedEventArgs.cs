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

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Identifies the kind of role-configuration change that occurred.
    /// </summary>
    public enum RoleConfigurationChangeKind
    {
        /// <summary>An identity-mapping rule was added.</summary>
        IdentityAdded,

        /// <summary>An identity-mapping rule was removed.</summary>
        IdentityRemoved,

        /// <summary>An application URI was added.</summary>
        ApplicationAdded,

        /// <summary>An application URI was removed.</summary>
        ApplicationRemoved,

        /// <summary>An endpoint was added.</summary>
        EndpointAdded,

        /// <summary>An endpoint was removed.</summary>
        EndpointRemoved,

        /// <summary>The <c>ApplicationsExclude</c> flag was changed.</summary>
        ApplicationsExcludeChanged,

        /// <summary>The <c>EndpointsExclude</c> flag was changed.</summary>
        EndpointsExcludeChanged,

        /// <summary>The <c>CustomConfiguration</c> flag was changed.</summary>
        CustomConfigurationChanged,

        /// <summary>A new role was created via <c>AddRole</c>.</summary>
        RoleAdded,

        /// <summary>A role was removed via <c>RemoveRole</c>.</summary>
        RoleRemoved
    }

    /// <summary>
    /// Event payload raised by <see cref="IRoleManager.RoleConfigurationChanged"/>
    /// after any successful mutation. The Part 18 §4.4.1 contract is:
    /// "If the configuration of a Role is changed, the Role assignment to
    /// active Session shall be re-evaluated and applied." — the session
    /// manager subscribes to this event to honour that requirement.
    /// </summary>
    public sealed class RoleConfigurationChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes the event payload.
        /// </summary>
        /// <param name="roleId">The role that was changed.</param>
        /// <param name="kind">The kind of change that occurred.</param>
        public RoleConfigurationChangedEventArgs(NodeId roleId, RoleConfigurationChangeKind kind)
        {
            RoleId = roleId;
            Kind = kind;
        }

        /// <summary>
        /// The role whose configuration changed. For <see cref="RoleConfigurationChangeKind.RoleRemoved"/>
        /// this is the NodeId of the removed role; the role is no longer in the manager.
        /// </summary>
        public NodeId RoleId { get; }

        /// <summary>
        /// The kind of change that occurred.
        /// </summary>
        public RoleConfigurationChangeKind Kind { get; }
    }
}

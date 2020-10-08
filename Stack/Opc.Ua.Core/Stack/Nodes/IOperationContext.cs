/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that describes a node local to the server.
    /// </summary>
    public interface IOperationContext
    {
        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session identifier.</value>
        NodeId SessionId { get; }

        /// <summary>
        /// The identity of the user.
        /// </summary>
        /// <value>The user identity.</value>
        IUserIdentity UserIdentity { get; }

        /// <summary>
        /// The locales to use if available.
        /// </summary>
        /// <value>The preferred locales.</value>
        IList<string> PreferredLocales { get; }

        /// <summary>
        /// The mask to use when collecting any diagnostic information.
        /// </summary>
        /// <value>The diagnostics mask.</value>
        DiagnosticsMasks DiagnosticsMask { get; }

        /// <summary>
        /// The table of strings which is used to store diagnostic string data.
        /// </summary>
        /// <value>The string table.</value>
        StringTable StringTable { get; }

        /// <summary>
        /// When the operation times out.
        /// </summary>
        /// <value>The operation deadline.</value>
        DateTime OperationDeadline { get; }

        /// <summary>
        /// The current status of the the operation (bad if the operation has been aborted).
        /// </summary>
        /// <value>The operation status.</value>
        StatusCode OperationStatus { get; }

        /// <summary>
        /// The audit identifier associated with the operation.
        /// </summary>
        /// <value>The audit entry identifier.</value>
        string AuditEntryId { get; }
    }
}

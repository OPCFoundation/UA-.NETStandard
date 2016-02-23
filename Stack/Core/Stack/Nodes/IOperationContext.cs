/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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

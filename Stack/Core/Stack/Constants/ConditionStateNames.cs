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

namespace Opc.Ua
{
    /// <summary>
    /// Defines the default names for the condition states.
    /// </summary>
    public static partial class ConditionStateNames
    {
		/// <summary>
		/// The name of the Disabled state.
		/// </summary>
        public const string Disabled = "Disabled";

		/// <summary>
		/// The name of the Enabled state.
		/// </summary>
        public const string Enabled = "Enabled";

		/// <summary>
		/// The name of the Inactive state.
		/// </summary>
        public const string Inactive = "Inactive";

		/// <summary>
		/// The name of the Active state.
		/// </summary>
        public const string Active = "Active";

		/// <summary>
		/// The name of the Unacknowledged state.
		/// </summary>
        public const string Unacknowledged = "Unacknowledged";

		/// <summary>
		/// The name of the Acknowledged state.
		/// </summary>
        public const string Acknowledged = "Acknowledged";

		/// <summary>
		/// The name of the Unconfirmed state.
		/// </summary>
        public const string Unconfirmed = "Unconfirmed";

		/// <summary>
		/// The name of the Confirmed state.
		/// </summary>
        public const string Confirmed = "Confirmed";

		/// <summary>
		/// The name of the Unsuppressed state.
		/// </summary>
        public const string Unsuppressed = "Unsuppressed";

		/// <summary>
		/// The name of the Suppressed state.
		/// </summary>
        public const string Suppressed = "Suppressed";

		/// <summary>
		/// The name of the HighHighActive state.
		/// </summary>
        public const string HighHighActive = "HighHighActive";

		/// <summary>
		/// The name of the HighActive state.
		/// </summary>
        public const string HighActive = "HighActive";

		/// <summary>
		/// The name of the LowActive state.
		/// </summary>
        public const string LowActive = "LowActive";

		/// <summary>
		/// The name of the LowLowActive state.
		/// </summary>
        public const string LowLowActive = "LowLowActive";
    }
}

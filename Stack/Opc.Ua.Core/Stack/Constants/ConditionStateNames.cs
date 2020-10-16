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

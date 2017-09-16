/* Copyright (c) 1996-2017, OPC Foundation. All rights reserved.

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

namespace PubSubBase.Definitions
{
    /// <summary>
    /// A reference returned in browse operation.
    /// </summary>
    [Serializable]
    public class ClientReferenceDescription
    {
        #region Public Properties
        /// <summary>
        /// Return the flag indicating whether the reference is a forward reference.
        /// </summary>
        public bool IsForward { get; set; }
        /// <summary>
        /// Node ID of the Target Node
        /// </summary>
        public string NodeId { get; set; }
        /// <summary>
        /// Node Class of the Target Node
        /// </summary>
        public string NodeClass { get; set; }
        /// <summary>
        /// BrowseName of the Target Node
        /// </summary>
        public string BrowseName { get; set; }
        /// <summary>
        /// Display name of the Target Node
        /// </summary>
        public string DisplayName { get; set; }
        /// <summary>
        /// Type Definition of the Target Node
        /// </summary>
        public string TypeDefinition { get; set; }

        #endregion
    }
}

/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
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
	/// Stores context information for message encoding and decoding.
	/// </summary>
	public interface IServiceMessageContext
    {
        #region Public Properties
        /// <summary>
        /// Returns the object used to synchronize access to the context.
        /// </summary>
        object SyncRoot { get; }

        /// <summary>
        /// The maximum length for any string, byte string or xml element.
        /// </summary>
        int MaxStringLength { get; }

        /// <summary>
        /// The maximum length for any array.
        /// </summary>
        int MaxArrayLength { get; }

        /// <summary>
        /// The maximum length for any ByteString.
        /// </summary>
        int MaxByteStringLength { get; }

        /// <summary>
        /// The maximum length for any Message.
        /// </summary>
        int MaxMessageSize { get; }

        /// <summary>
        /// The maximum nesting level accepted while encoding or decoding objects.
        /// </summary>
        uint MaxEncodingNestingLevels { get; }

        /// <summary>
        /// The table of namespaces used by the server.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// The table of servers used by the server.
        /// </summary>
        StringTable ServerUris { get; }

        /// <summary>
        /// The factory used to create encodeable objects.
        /// </summary>
        IEncodeableFactory Factory { get; }
        #endregion
    }
}

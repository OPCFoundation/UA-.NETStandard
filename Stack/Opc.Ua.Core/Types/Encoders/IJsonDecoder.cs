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
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// A Json decoder extension to handle arrays and structures.
    /// </summary>
    public interface IJsonDecoder : IDecoder
    {
        /// <summary>
        /// Push the specified structure on the Read Stack.
        /// </summary>
        /// <param name="fieldName">The name of the object that shall be placed on the Read Stack</param>
        /// <returns>true if successful</returns>
        bool PushStructure(string fieldName);

        /// <summary>
        /// Push an Array item on the Read Stack
        /// </summary>
        /// <param name="fieldName">The array name</param>
        /// <param name="index">The index of the item that shall be placed on the Read Stack</param>
        /// <returns>true if successful</returns>
        bool PushArray(string fieldName, int index);

        /// <summary>
        /// Pop the current object (structure/array) from the Read Stack.
        /// </summary>
        void Pop();

        /// <summary>
        /// Read a decoded JSON field.
        /// </summary>
        /// <param name="fieldName">The name of the field.</param>
        /// <param name="token">The returned object token of the field.</param>
        bool ReadField(string fieldName, out object token);
    }
}

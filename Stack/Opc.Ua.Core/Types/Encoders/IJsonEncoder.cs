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
    /// Interface for extended methods for JSON encoders based on IEncoder.
    /// </summary>
    public interface IJsonEncoder : IEncoder
    {
        /// <summary>
        /// Force the Json encoder to encode namespace URI instead of
        /// namespace Index in NodeIds.
        /// </summary>
        bool ForceNamespaceUri { get; set; }

        /// <summary>
        /// Push the begin of an array on the encoder stack.
        /// </summary>
        /// <param name="fieldName">The name of the array field.</param>
        void PushArray(string fieldName);

        /// <summary>
        /// Push the begin of a structure on the encoder stack.
        /// </summary>
        /// <param name="fieldName">The name of the structure field.</param>
        void PushStructure(string fieldName);

        /// <summary>
        /// Pop the array from the encoder stack.
        /// </summary>
        void PopArray();

        /// <summary>
        /// Pop the structure from the encoder stack.
        /// </summary>
        void PopStructure();

        /// <summary>
        /// Call an IEncoder action where the reversible encoding is applied
        /// before the call to the Action and restored before return.
        /// </summary>
        void UsingReversibleEncoding<T>(Action<string, T> action, string fieldName, T value, bool useReversibleEncoding);
    }
}

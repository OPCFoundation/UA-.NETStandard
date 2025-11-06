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

using System.Text;
#if !NETFRAMEWORK
using Opc.Ua.Security.Certificates;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Service result extensions
    /// </summary>
    public static class ServiceResultExtensions
    {
        /// <summary>
        /// Returns a formatted string with the contents of service result.
        /// </summary>
        public static string ToLongString(this ServiceResult result)
        {
            return new StringBuilder()
                .AppendLong(result)
                .ToString();
        }

        /// <summary>
        /// Returns a formatted string with the contents of exception.
        /// </summary>
        public static string ToLongString(this ServiceResultException result)
        {
            var buffer = new StringBuilder();
            buffer.AppendLine(result.Message);
            buffer.AppendLong(result.Result);
            return buffer.ToString();
        }

        /// <summary>
        /// Append details to string buffer
        /// </summary>
        internal static StringBuilder AppendLong(
            this StringBuilder buffer,
            ServiceResult result)
        {
            buffer.Append("Id: ")
                .Append(StatusCodes.GetBrowseName(result.Code));
            if (!string.IsNullOrEmpty(result.SymbolicId))
            {
                buffer.AppendLine()
                    .Append("SymbolicId: ")
                    .Append(result.SymbolicId);
            }

            if (!LocalizedText.IsNullOrEmpty(result.LocalizedText))
            {
                buffer.AppendLine()
                    .Append("Description: ")
                    .Append(result.LocalizedText);
            }

            if (!string.IsNullOrEmpty(result.AdditionalInfo))
            {
                buffer.AppendLine()
                    .Append(result.AdditionalInfo);
            }

            ServiceResult innerResult = result.InnerResult;

            if (innerResult != null)
            {
                buffer.AppendLine()
                    .AppendLine("===")
                    .Append(innerResult.ToLongString());
            }

            return buffer;
        }
    }
}

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

using System.Text;

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
            buffer.Append("Id: ").Append((StatusCode)result.Code);
            if (!string.IsNullOrEmpty(result.SymbolicId))
            {
                buffer.AppendLine()
                    .Append("SymbolicId: ")
                    .Append(result.SymbolicId);
            }

            if (!result.LocalizedText.IsNullOrEmpty)
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

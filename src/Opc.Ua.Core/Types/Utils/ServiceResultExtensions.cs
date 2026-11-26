// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

            ServiceResult? innerResult = result.InnerResult;

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

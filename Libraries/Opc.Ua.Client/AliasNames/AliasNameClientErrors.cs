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

using System;

namespace Opc.Ua.Client.AliasNames
{
    /// <summary>
    /// Maps the OPC UA <see cref="StatusCode"/>s returned by Part 17
    /// methods to typed .NET exceptions in the spirit of
    /// <c>FileSystemErrors</c>.
    /// </summary>
    internal static class AliasNameClientErrors
    {
        public static Exception Translate(
            StatusCode status,
            string operation,
            NodeId categoryId = default)
        {
            uint code = status.Code;

            if (code == StatusCodes.BadUserAccessDenied)
            {
                return new UnauthorizedAccessException(
                    $"OPC UA Part 17 {operation} was rejected with " +
                    "BadUserAccessDenied" +
                    (!categoryId.IsNull
                        ? " (category=" + categoryId + ")"
                        : "") +
                    ".");
            }
            if (code == StatusCodes.BadNotSupported ||
                code == StatusCodes.BadNotImplemented)
            {
                return new NotSupportedException(
                    $"OPC UA Part 17 {operation} is not supported by this " +
                    "server/category" +
                    (!categoryId.IsNull
                        ? " (category=" + categoryId + ")"
                        : "") +
                    ".");
            }
            return ServiceResultException.Create(
                code,
                "OPC UA Part 17 {0} failed: 0x{1:X8}",
                operation,
                code);
        }
    }
}

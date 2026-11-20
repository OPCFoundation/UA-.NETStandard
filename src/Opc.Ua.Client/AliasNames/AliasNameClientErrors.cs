// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
                        : string.Empty) +
                    ".");
            }
            // BadMethodInvalid is what a server returns when the type-level
            // MethodId is called on a category that does not expose the
            // (optional) method (Part 4 §5.12.2.4), so it means "not supported".
            if (code == StatusCodes.BadNotSupported ||
                code == StatusCodes.BadNotImplemented ||
                code == StatusCodes.BadMethodInvalid)
            {
                return new NotSupportedException(
                    $"OPC UA Part 17 {operation} is not supported by this " +
                    "server/category" +
                    (!categoryId.IsNull
                        ? " (category=" + categoryId + ")"
                        : string.Empty) +
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

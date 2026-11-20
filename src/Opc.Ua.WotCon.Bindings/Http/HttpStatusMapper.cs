/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Net;

namespace Opc.Ua.WotCon.Bindings.Http
{
    /// <summary>
    /// Maps HTTP status codes to OPC UA <see cref="StatusCode"/> values.
    /// </summary>
    internal static class HttpStatusMapper
    {
        public static StatusCode Map(HttpStatusCode status)
        {
            int code = (int)status;
            if (code is >= 200 and < 300)
            {
                return StatusCodes.Good;
            }
            return status switch
            {
                HttpStatusCode.BadRequest => StatusCodes.BadInvalidArgument,
                HttpStatusCode.Unauthorized => StatusCodes.BadUserAccessDenied,
                HttpStatusCode.Forbidden => StatusCodes.BadUserAccessDenied,
                HttpStatusCode.NotFound => StatusCodes.BadNodeIdUnknown,
                HttpStatusCode.MethodNotAllowed => StatusCodes.BadNotSupported,
                HttpStatusCode.RequestTimeout => StatusCodes.BadTimeout,
                HttpStatusCode.Conflict => StatusCodes.BadInvalidState,
                HttpStatusCode.NotImplemented => StatusCodes.BadNotImplemented,
                HttpStatusCode.ServiceUnavailable => StatusCodes.BadServerHalted,
                HttpStatusCode.GatewayTimeout => StatusCodes.BadTimeout,
                _ => code >= 500 ? StatusCodes.BadInternalError : StatusCodes.BadUnexpectedError
            };
        }
    }
}

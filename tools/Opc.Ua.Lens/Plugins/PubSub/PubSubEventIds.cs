/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;

namespace UaLens.Plugins.PubSub;

internal static class UaLensPubSubEventIds
{
    public const int Workspace = 21600;
}

internal static partial class PubSubWorkspaceLog
{
    [LoggerMessage(EventId = UaLensPubSubEventIds.Workspace, Level = LogLevel.Warning,
        Message = "{Operation} failed with status {StatusCode}. Secrets and raw exception text are omitted.")]
    public static partial void OperationFailed(this ILogger logger, string operation, uint statusCode);
}

/// <summary>
/// Expected operational failures are surfaced without copying broker/authority exception
/// messages that may contain credentials. Unexpected programming failures are not swallowed.
/// </summary>
internal static class PubSubFailure
{
    public static bool IsExpected(Exception exception)
    {
        return exception is ArgumentException or InvalidOperationException or NotSupportedException or
            ServiceResultException or PubSubApplicationBuildException or PubSubConfigurationException or
            IOException or SocketException or AuthenticationException or CryptographicException or
            SecurityException or UnauthorizedAccessException or HttpRequestException or
            TimeoutException or JsonException;
    }

    public static StatusCode Status(Exception exception)
    {
        return exception switch
        {
            ServiceResultException service => service.StatusCode,
            AuthenticationException or CryptographicException or SecurityException =>
                StatusCodes.BadSecurityChecksFailed,
            UnauthorizedAccessException => StatusCodes.BadUserAccessDenied,
            TimeoutException => StatusCodes.BadTimeout,
            SocketException or IOException or HttpRequestException => StatusCodes.BadCommunicationError,
            NotSupportedException => StatusCodes.BadNotSupported,
            _ => StatusCodes.BadConfigurationError
        };
    }

    public static string Describe(Exception exception)
    {
        return exception switch
        {
            ServiceResultException => "The stack returned an unsuccessful status. Inspect the component counters.",
            AuthenticationException or CryptographicException or SecurityException =>
                "Security negotiation/key access failed. Verify configured trust, key provider, policy and group.",
            UnauthorizedAccessException =>
                "Access was denied. Check the configured provider, broker or interface privileges.",
            TimeoutException => "The operation timed out without a correlated response. No success is inferred.",
            SocketException or IOException or HttpRequestException =>
                "Communication failed. Check the configured endpoint, interface and external service.",
            NotSupportedException => "The installed provider does not support this profile or operation.",
            JsonException => "The typed configuration is invalid or includes unsupported fields.",
            ArgumentException or InvalidOperationException or
                PubSubApplicationBuildException or PubSubConfigurationException =>
                "Prerequisites are unsatisfied. Validate the displayed setup and provider references.",
            _ => "An unexpected failure occurred."
        };
    }
}

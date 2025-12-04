/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

//
// Portions of this logging abstraction class were derived from:
//
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Logging.Abstractions/src/LoggerExtensions.cs
//

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA2254 // Template should be a static expression
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace Opc.Ua
{
    /// <summary>
    /// Logger Utils methods.
    /// </summary>
    /// <remarks>
    /// To simplify porting from Utils.Trace and to avoid
    /// name collisions with anything that is called 'Log'
    /// the Utils class hosts the Logger class.
    /// </remarks>
    public static partial class Utils
    {
        /// <summary>
        /// The high performance EventSource log interface.
        /// </summary>
        internal static OpcUaCoreEventSource EventLog { get; } = new();

        /// <summary>
        /// Global default logger provider
        /// </summary>
        internal static TraceLoggerProvider LoggerProvider { get; } = new();

        /// <summary>
        /// Fallback logger
        /// </summary>
        [Experimental("UA_NETStandard_1")]
        public static class Fallback
        {
            /// <summary>
            /// Get an instance of the fallback logger. In debug builds
            /// the null logger is returned which emits debug checks on usage.
            /// </summary>
            public static ILogger Logger =>
#if DEBUG
                LoggerUtils.Null.Logger;
#else
                LoggerProvider.CreateLogger(nameof(Fallback));
#endif
        }

        /// <summary>
        /// Shared telemetry context instance (singleton)
        /// </summary>
        private static readonly TraceLoggerTelemetry s_sharedTelemetry = new();

        static Utils()
        {
            TelemetryExtensions.InternalOnly__TelemetryHook = () => s_sharedTelemetry;
        }

        /// <summary>
        /// Trace logger telemetry context
        /// </summary>
        internal class TraceLoggerTelemetry : TelemetryContextBase
        {
            public TraceLoggerTelemetry()
                : base(Microsoft.Extensions.Logging.LoggerFactory.Create(
                    builder => builder.AddProvider(LoggerProvider)))
            {
            }
        }

        /// <summary>
        /// The possible trace output mechanisms.
        /// </summary>
        public enum TraceOutput
        {
            /// <summary>
            /// No tracing
            /// </summary>
            Off = 0,

            /// <summary>
            /// Only write to file (if specified). Default for Release mode.
            /// </summary>
            FileOnly = 1,

            /// <summary>
            /// Write to debug trace listeners and a file (if specified). Default for Debug mode.
            /// </summary>
            DebugAndFile = 2
        }

        /// <summary>
        /// The masks used to filter trace messages.
        /// </summary>
        public static class TraceMasks
        {
            /// <summary>
            /// Do not output any messages.
            /// </summary>
            public const int None = 0x0;

            /// <summary>
            /// Output error messages.
            /// </summary>
            public const int Error = 0x1;

            /// <summary>
            /// Output informational messages.
            /// </summary>
            public const int Information = 0x2;

            /// <summary>
            /// Output stack traces.
            /// </summary>
            public const int StackTrace = 0x4;

            /// <summary>
            /// Output basic messages for service calls.
            /// </summary>
            public const int Service = 0x8;

            /// <summary>
            /// Output detailed messages for service calls.
            /// </summary>
            public const int ServiceDetail = 0x10;

            /// <summary>
            /// Output basic messages for each operation.
            /// </summary>
            public const int Operation = 0x20;

            /// <summary>
            /// Output detailed messages for each operation.
            /// </summary>
            public const int OperationDetail = 0x40;

            /// <summary>
            /// Output messages related to application initialization or shutdown
            /// </summary>
            public const int StartStop = 0x80;

            /// <summary>
            /// Output messages related to a call to an external system.
            /// </summary>
            public const int ExternalSystem = 0x100;

            /// <summary>
            /// Output messages related to security
            /// </summary>
            public const int Security = 0x200;

            /// <summary>
            /// Output all messages.
            /// </summary>
            public const int All = 0x3FF;
        }

        /// <summary>
        /// Format a log string of the certificate
        /// </summary>
        /// <param name="certificate"></param>
        /// <returns></returns>
        public static string AsLogSafeString(this X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                return "(none)";
            }
            var buffer = new StringBuilder();
            buffer
                .Append('[')
                .Append(Redact.Create(certificate.Subject))
                .Append("], [")
                .Append(Redact.Create(certificate.Thumbprint))
                .Append(']');

            if (certificate.Handle == IntPtr.Zero)
            {
                buffer.Append(" !!DISPOSED!!");
            }
            else if (certificate.HasPrivateKey)
            {
                buffer.Append(" (with Private Key)");
            }
            return buffer.ToString();
        }
    }
}

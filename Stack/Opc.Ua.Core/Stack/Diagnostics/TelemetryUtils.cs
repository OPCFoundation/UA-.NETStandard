/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;

#nullable enable

namespace Opc.Ua
{
    /// <summary>
    /// Telemetry utilities
    /// </summary>
    public static partial class Utils
    {
        /// <summary>
        /// Typed null logger
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public sealed class NullLogger<T> : NullLogger, ILogger<T>
        {
            /// <summary>
            /// Get instance to a typed null logger
            /// </summary>
            public static new ILogger<T> Instance { get; } = new NullLogger<T>();
        }

        /// <summary>
        /// Null logger. In debug builds it will assert if used. In retail
        /// it will ensure that no null reference exception occurrs in classes
        /// that initialize a logger field.
        /// </summary>
        public class NullLogger : ILogger
        {
            /// <summary>
            /// Get an instance to the null logger
            /// </summary>
            public static ILogger Instance { get; } = new NullLogger();

            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                DebugCheck();
                return new DummyScope();
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                DebugCheck();
                return false;
            }

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                DebugCheck();
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                return "NullLogger";
            }

            private sealed class DummyScope : IDisposable
            {
                /// <inheritdoc/>
                public void Dispose()
                {
                }
            }

            [Conditional("DEBUG")]
            private static void DebugCheck()
            {
                Debug.Fail("Using a NullLogger");
            }
        }
    }
}

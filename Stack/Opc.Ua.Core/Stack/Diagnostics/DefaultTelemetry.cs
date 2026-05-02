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
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Default telemetry implementation
    /// </summary>
    public sealed class DefaultTelemetry : TelemetryContextBase, IDisposable
    {
        private readonly ILoggerFactory _ownedFactory;

        /// <summary>
        /// Create default telemetry
        /// </summary>
        private DefaultTelemetry(ILoggerFactory ownedFactory)
            : base(ownedFactory)
        {
            _ownedFactory = ownedFactory;
        }

        /// <summary>
        /// Create default telemetry
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static ITelemetryContext Create(Action<ILoggingBuilder> configure)
        {
            ILoggerFactory? factory = null;
            try
            {
                factory = Microsoft.Extensions.Logging.LoggerFactory.Create(configure);
                var result = new DefaultTelemetry(factory);
                factory = null;
                return result;
            }
            finally
            {
                factory?.Dispose();
            }
        }

        /// <summary>
        /// Dispose the owned logger factory.
        /// </summary>
        public void Dispose()
        {
            _ownedFactory?.Dispose();
        }
    }
}

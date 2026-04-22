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

using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// <see cref="ITelemetryContext"/> implementation that adapts the host's
    /// <see cref="ILoggerFactory"/> so a Generic-Host application has a single
    /// logging pipeline shared between user code and the OPC UA stack.
    /// </summary>
    /// <remarks>
    /// The host owns the lifetime of the supplied <see cref="ILoggerFactory"/>;
    /// this adapter does not dispose it.
    /// </remarks>
    public sealed class HostTelemetryContext : TelemetryContextBase
    {
        /// <summary>
        /// Creates a new <see cref="HostTelemetryContext"/> bound to the given
        /// <see cref="ILoggerFactory"/>.
        /// </summary>
        public HostTelemetryContext(ILoggerFactory loggerFactory)
            : base(loggerFactory)
        {
        }
    }
}

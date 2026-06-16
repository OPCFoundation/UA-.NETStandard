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
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Opc.Ua.Core.Diagnostics.Bindings;
using Opc.Ua.Core.Diagnostics.Models;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Capture.Sources
{
    /// <summary>
    /// Default capture source factory for the built-in pcap capture sources.
    /// </summary>
    public sealed class DefaultCaptureSourceFactory : ICaptureSourceFactory
    {
        private readonly IChannelCaptureRegistry m_registry;

        /// <summary>
        /// Constructs a factory that shares the supplied
        /// <see cref="IChannelCaptureRegistry"/> with every in-process
        /// capture source it produces.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registry"/> is <c>null</c>.
        /// </exception>
        public DefaultCaptureSourceFactory(IChannelCaptureRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            m_registry = registry;
        }

        /// <inheritdoc/>
        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "NIC capture is conditionally created and SharpPcap requires dynamic native loading.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "NIC capture is conditionally created and SharpPcap requires dynamic native loading.")]
        public ICaptureSource Create(
            CaptureSourceKind kind,
            string sessionFolder,
            ILoggerFactory loggerFactory)
        {
            return kind switch
            {
                CaptureSourceKind.Nic => new NicCaptureSource(loggerFactory),
                CaptureSourceKind.InProcessClient
                    => new InProcessClientCaptureSource(m_registry, loggerFactory),
                CaptureSourceKind.InProcessServer
                    => new InProcessServerCaptureSource(m_registry, loggerFactory),
                CaptureSourceKind.Replay => new ReplayCaptureSource(loggerFactory),
                _ => throw new PcapDiagnosticsException($"Unsupported capture source kind '{kind}'.")
            };
        }
    }
}

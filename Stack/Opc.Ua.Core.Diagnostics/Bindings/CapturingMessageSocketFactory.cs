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
using Microsoft.Extensions.Logging;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Bindings
{
    /// <summary>
    /// <see cref="IMessageSocketFactory"/> decorator that wraps every
    /// socket created by an inner factory in a
    /// <see cref="CapturingMessageSocket"/>. Installed once at binding
    /// registration time; every channel created through the binding gets
    /// its socket transparently capture-enabled.
    /// </summary>
    public sealed class CapturingMessageSocketFactory : IMessageSocketFactory
    {
        private readonly IMessageSocketFactory m_inner;
        private readonly IChannelCaptureRegistry m_registry;
        private readonly ILoggerFactory? m_loggerFactory;

        /// <summary>
        /// Constructs a capturing factory.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="inner"/> or <paramref name="registry"/> is <c>null</c>.
        /// </exception>
        public CapturingMessageSocketFactory(
            IMessageSocketFactory inner,
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(registry);

            m_inner = inner;
            m_registry = registry;
            m_loggerFactory = loggerFactory;
        }

        /// <inheritdoc/>
        public string Implementation => m_inner.Implementation + "+pcap";

        /// <inheritdoc/>
        public IMessageSocket Create(
            IMessageSink sink,
            BufferManager bufferManager,
            int receiveBufferSize)
        {
            IMessageSocket inner = m_inner.Create(sink, bufferManager, receiveBufferSize);
            return new CapturingMessageSocket(inner, m_registry, sink, m_loggerFactory);
        }
    }
}

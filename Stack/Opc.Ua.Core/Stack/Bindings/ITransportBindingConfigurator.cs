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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Callback that configures an
    /// <see cref="ITransportBindingRegistry"/> at first resolution time.
    /// Every <c>Add*Transport()</c> DI extension adds one of these to
    /// the service collection; the
    /// <see cref="DefaultTransportBindingRegistry"/> factory iterates
    /// them in registration order so a downstream
    /// <c>AddKestrelOpcTcpTransport()</c> after
    /// <c>AddOpcTcpTransport()</c> swaps the raw-socket listener for
    /// the Kestrel-hosted one (last-writer-wins per URI scheme).
    /// </summary>
    public interface ITransportBindingConfigurator
    {
        /// <summary>
        /// Apply this configurator's listener / channel factory
        /// registrations to the supplied <paramref name="registry"/>.
        /// </summary>
        void Configure(ITransportBindingRegistry registry);
    }

    /// <summary>
    /// Adapter that wraps a configuration delegate into an
    /// <see cref="ITransportBindingConfigurator"/>.
    /// </summary>
    public sealed class TransportBindingConfigurator : ITransportBindingConfigurator
    {
        /// <summary>
        /// Constructs a configurator that runs <paramref name="configure"/>
        /// at registry-creation time.
        /// </summary>
        public TransportBindingConfigurator(Action<ITransportBindingRegistry> configure)
        {
            m_configure = configure ?? throw new ArgumentNullException(nameof(configure));
        }

        /// <inheritdoc/>
        public void Configure(ITransportBindingRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            m_configure(registry);
        }

        private readonly Action<ITransportBindingRegistry> m_configure;
    }
}

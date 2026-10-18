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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Explicit native Client/Server composition without installing a native backend or enabling a Session/model.
    /// </summary>
    public static class DataChannelClientServerBuilderExtensions
    {
        /// <summary>
        /// Registers the injectable client. Native transport, identity and resource policies remain separate opt-ins.
        /// </summary>
        public static IOpcUaBuilder AddDataChannelClientServer(
            this IOpcUaBuilder builder, DataChannelClientServerOptions? options = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            options ??= new DataChannelClientServerOptions();
            options.Validate();
            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddSingleton<IDataChannelClientServerClient>(provider =>
                new DataChannelClientServerClient(
                    provider.GetRequiredService<ITelemetryContext>(),
                    provider.GetRequiredService<DataChannelClientServerOptions>(),
                    provider.GetService<IBufferManagerFactory>(),
                    provider.GetService<TimeProvider>()));
            return builder;
        }

        /// <summary>
        /// Registers one application-supplied virtual listener with the host's private transport registry.
        /// This starts neither the native listener nor its connections and does not open any flow.
        /// </summary>
        public static IOpcUaBuilder AddDataChannelClientServerListener(
            this IOpcUaBuilder builder, DataChannelClientServerListener listener)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }
            builder.Services.AddTransportBindingRegistry();
            var factory = new DataChannelClientServerListenerFactory(listener);
            builder.Services.AddSingleton<ITransportBindingConfigurator>(
                new TransportBindingConfigurator(registry => registry.RegisterListenerFactory(factory)));
            return builder;
        }
    }
}

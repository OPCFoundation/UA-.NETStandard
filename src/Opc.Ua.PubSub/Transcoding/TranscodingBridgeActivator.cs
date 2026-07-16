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
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Builds a <see cref="PubSubTranscodingBridge"/> from a
    /// <see cref="TranscodingBridgeDescriptor"/> by resolving the source and
    /// target connections from the running PubSub application and assembling
    /// the transcoder, egress, and security context. Shared by the fluent
    /// single-bridge hosted service and the configuration-driven reload
    /// coordinator so both produce identically wired bridges.
    /// </summary>
    internal sealed class TranscodingBridgeActivator
    {
        private readonly IServiceProvider m_serviceProvider;

        public TranscodingBridgeActivator(IServiceProvider serviceProvider)
        {
            m_serviceProvider = serviceProvider
                ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Creates an unstarted bridge for the supplied descriptor. Callers
        /// invoke <see cref="PubSubTranscodingBridge.Start"/> to attach it.
        /// </summary>
        /// <param name="descriptor">Bridge descriptor.</param>
        /// <returns>The constructed, unstarted bridge.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubTranscodingBridge Create(TranscodingBridgeDescriptor descriptor)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            IPubSubApplication application =
                m_serviceProvider.GetRequiredService<IPubSubApplication>();
            IPubSubConnection source = FindConnection(
                application, descriptor.SourceConnectionName);
            IPubSubConnection target = FindConnection(
                application, descriptor.TargetConnectionName);

            ITelemetryContext telemetry =
                m_serviceProvider.GetRequiredService<ITelemetryContext>();
            TimeProvider clock = m_serviceProvider.GetRequiredService<TimeProvider>();
            Dictionary<string, INetworkMessageEncoder> encoders =
                BuildEncoderMap(m_serviceProvider.GetServices<INetworkMessageEncoder>());

            var messageContext = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(telemetry),
                application.MetaDataRegistry,
                application.Diagnostics,
                clock);
            var context = new TranscodeContext(messageContext, telemetry);
            TranscodeSecurity security = BuildSecurity(target, descriptor);

            var transcoder = new PubSubTranscoder(
                descriptor.Spec, encoders, context, security);
            var egress = new ConnectionTranscodeEgress(target);
            return new PubSubTranscodingBridge(
                source, transcoder, egress, telemetry, descriptor.TopicSelector);
        }

        private TranscodeSecurity BuildSecurity(
            IPubSubConnection target,
            TranscodingBridgeDescriptor descriptor)
        {
            IPubSubSecurityWrapperResolver? resolver =
                m_serviceProvider.GetService<IPubSubSecurityWrapperResolver>();
            PubSubSecurityContext? securityContext = resolver?.Resolve(target.Configuration);
            return new TranscodeSecurity
            {
                TargetWrapper = securityContext?.Wrapper,
                TargetWrapOptions = securityContext?.WrapOptions
                    ?? UadpSecurityWrapOptions.SignAndEncrypt,
                AllowInsecureCrossEncoding = descriptor.AllowInsecureCrossEncoding
            };
        }

        private static Dictionary<string, INetworkMessageEncoder> BuildEncoderMap(
            IEnumerable<INetworkMessageEncoder> encoders)
        {
            var map = new Dictionary<string, INetworkMessageEncoder>(StringComparer.Ordinal);
            foreach (INetworkMessageEncoder encoder in encoders)
            {
                map[encoder.TransportProfileUri] = encoder;
            }
            return map;
        }

        private static IPubSubConnection FindConnection(
            IPubSubApplication application,
            string name)
        {
            foreach (IPubSubConnection connection in application.Connections)
            {
                if (string.Equals(connection.Name, name, StringComparison.Ordinal))
                {
                    return connection;
                }
            }
            throw new InvalidOperationException(
                $"PubSub connection '{name}' was not found for the transcoding bridge.");
        }
    }
}

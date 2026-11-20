/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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

        private static TranscodeSecurity BuildSecurity(
            IPubSubConnection target,
            TranscodingBridgeDescriptor descriptor)
        {
            PubSubSecurityContext? securityContext = (target as PubSubConnection)?.EffectiveSecurityContext;
            if (PubSubSecurityWrapperResolver.TryResolveConnectionSecurity(
                target.Configuration, out MessageSecurityMode mode, out _) &&
                mode is MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt &&
                securityContext is null)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient);
            }
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

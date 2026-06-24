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
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions for the OPC UA PubSub
    /// Security Key Service (SKS) — both the client side
    /// (<see cref="OpcUaSecurityKeyServiceClient"/>) and the
    /// in-process server side
    /// (<see cref="InMemoryPubSubKeyServiceServer"/>).
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.4">
    /// Part 14 §8.4 Security Key Service</see>. The client is what a
    /// publisher / subscriber uses to pull keys from a remote SKS;
    /// the server hosts groups locally for testing or for
    /// single-process scenarios.
    /// </remarks>
    public static class PubSubSecurityServiceCollectionExtensions
    {
        /// <summary>
        /// Registers
        /// <see cref="PullSecurityKeyProviderOptions"/> in the
        /// container so a
        /// <see cref="PullSecurityKeyProvider"/> can be instantiated
        /// later (one per <c>SecurityGroupId</c>).
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="configure">
        /// Optional callback to configure the default
        /// <see cref="PullSecurityKeyProviderOptions"/> instance.
        /// </param>
        public static IOpcUaBuilder AddPubSubSecurityKeyServiceClient(
            this IOpcUaBuilder builder,
            Action<PullSecurityKeyProviderOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                builder.Services.AddOptions<PullSecurityKeyProviderOptions>();
            }
            else
            {
                builder.Services.AddOptions<PullSecurityKeyProviderOptions>().Configure(configure);
            }
            return builder;
        }

        /// <summary>
        /// Registers a SetSecurityKeys push target provider for one SecurityGroup.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        public static IOpcUaBuilder AddPubSubSecurityKeyPushTarget(
            this IOpcUaBuilder builder,
            string securityGroupId)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (string.IsNullOrEmpty(securityGroupId))
            {
                throw new ArgumentException("SecurityGroupId must be non-empty.", nameof(securityGroupId));
            }

            builder.Services.AddSingleton(sp => new PushSecurityKeyProvider(
                securityGroupId,
                sp.GetService<ITelemetryContext>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System));
            builder.Services.AddSingleton<IPubSubSecurityKeyProvider>(
                sp => sp.GetRequiredService<PushSecurityKeyProvider>());
            return builder;
        }

        /// <summary>
        /// Registers an
        /// <see cref="InMemoryPubSubKeyServiceServer"/> as a
        /// singleton in the container. Apply
        /// <paramref name="configure"/> to seed groups at
        /// construction time.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="configure">Optional configuration callback.</param>
        public static IOpcUaBuilder AddPubSubSecurityKeyServiceServer(
            this IOpcUaBuilder builder,
            Action<InMemoryPubSubKeyServiceServer>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<IPubSubSecurityKeyStore, InMemoryPubSubSecurityKeyStore>();
            builder.Services.TryAddSingleton(sp =>
            {
                var server = new InMemoryPubSubKeyServiceServer(
                    sp.GetService<TimeProvider>() ?? TimeProvider.System,
                    sp.GetRequiredService<ITelemetryContext>(),
                    keyStore: sp.GetRequiredService<IPubSubSecurityKeyStore>());
                configure?.Invoke(server);
                return server;
            });
            return builder;
        }
    }
}

// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Convenience extension methods that compose multiple
    /// <see cref="PubSubApplicationBuilder"/> calls into a single,
    /// idiomatic chainable call.
    /// </summary>
    public static class PubSubApplicationBuilderExtensions
    {
        /// <summary>
        /// Registers all standard
        /// <see cref="Encoding.INetworkMessageEncoder"/>
        /// and
        /// <see cref="Encoding.INetworkMessageDecoder"/>
        /// implementations (UADP + JSON) on the builder.
        /// </summary>
        /// <param name="builder">Builder.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static PubSubApplicationBuilder UseAllStandardEncoders(
            this PubSubApplicationBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder
                .AddEncoder(new Encoding.Uadp.UadpEncoder())
                .AddEncoder(new Encoding.Json.JsonEncoder())
                .AddDecoder(new Encoding.Uadp.UadpDecoder())
                .AddDecoder(new Encoding.Json.JsonDecoder());
        }

        /// <summary>
        /// Convenience wrapper around
        /// <see cref="PubSubApplicationBuilder.AddSecurityKeyServiceServer"/>
        /// that exposes a fluent-style name.
        /// </summary>
        /// <param name="builder">Builder.</param>
        /// <param name="configure">Optional configuration callback.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static PubSubApplicationBuilder UseInMemorySks(
            this PubSubApplicationBuilder builder,
            Action<InMemoryPubSubKeyServiceServer>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddSecurityKeyServiceServer(configure);
        }
    }
}

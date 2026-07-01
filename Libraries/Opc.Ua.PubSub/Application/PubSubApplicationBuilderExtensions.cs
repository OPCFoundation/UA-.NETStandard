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
        /// <see cref="Opc.Ua.PubSub.Encoding.INetworkMessageEncoder"/>
        /// and
        /// <see cref="Opc.Ua.PubSub.Encoding.INetworkMessageDecoder"/>
        /// implementations (UADP + JSON) on the builder.
        /// </summary>
        /// <param name="builder">Builder.</param>
        public static PubSubApplicationBuilder UseAllStandardEncoders(
            this PubSubApplicationBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder
                .AddEncoder(new Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder())
                .AddEncoder(new Opc.Ua.PubSub.Encoding.Json.JsonEncoder())
                .AddDecoder(new Opc.Ua.PubSub.Encoding.Uadp.UadpDecoder())
                .AddDecoder(new Opc.Ua.PubSub.Encoding.Json.JsonDecoder());
        }

        /// <summary>
        /// Convenience wrapper around
        /// <see cref="PubSubApplicationBuilder.AddSecurityKeyServiceServer"/>
        /// that exposes a fluent-style name.
        /// </summary>
        /// <param name="builder">Builder.</param>
        /// <param name="configure">Optional configuration callback.</param>
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

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
using Microsoft.Extensions.Hosting;
using Opc.Ua.PubSub.Transcoding;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent extensions that register an in-process PubSub transcoding
    /// bridge on an <see cref="IPubSubBuilder"/>. The bridge observes
    /// messages received on a source connection, transcodes them, and
    /// re-publishes them on a target connection.
    /// </summary>
    public static class PubSubTranscodingBuilderExtensions
    {
        /// <summary>
        /// Registers a transcoding bridge configured through the fluent
        /// <see cref="PubSubTranscoderBuilder"/>. The bridge is started as
        /// a hosted service after the PubSub application, so both the
        /// source and target connections exist when it attaches.
        /// </summary>
        /// <param name="builder">The PubSub builder.</param>
        /// <param name="configure">Bridge configuration callback.</param>
        /// <returns>The original <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="builder"/> or
        /// <paramref name="configure"/> is <see langword="null"/>.
        /// </exception>
        public static IPubSubBuilder AddTranscodingBridge(
            this IPubSubBuilder builder,
            Action<PubSubTranscoderBuilder> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var transcoderBuilder = new PubSubTranscoderBuilder();
            configure(transcoderBuilder);
            TranscodingBridgeDescriptor descriptor = transcoderBuilder.Build();

            builder.Services.AddSingleton<IHostedService>(
                sp => new PubSubTranscodingBridgeHostedService(sp, descriptor));
            return builder;
        }
    }
}

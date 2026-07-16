/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Client.Alarms</c>: register an
    /// <see cref="AlarmClientFactory"/> so that dependency-injected
    /// client hosts can resolve a Part 9 alarm/condition client per
    /// connected session.
    /// </summary>
    public static class OpcUaAlarmsBuilderExtensions
    {
        /// <summary>
        /// Registers the singleton <see cref="AlarmClientFactory"/> so
        /// consumers can resolve it and produce an
        /// <see cref="AlarmClient"/> per <see cref="ISessionClient"/>
        /// (typically the connected <c>ManagedSession</c>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Idempotent (<see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection)"/>):
        /// repeated calls do not add a second descriptor. The root
        /// <c>AddOpcUa()</c> call is invoked to ensure
        /// <see cref="ITelemetryContext"/> is registered even if the
        /// caller skipped chaining through it.
        /// </para>
        /// <para>
        /// The non-DI path
        /// (<c>AlarmClientSessionExtensions.GetAlarmClient</c> extension
        /// and the public <see cref="AlarmClient"/> constructor)
        /// remains available for callers that do not use the
        /// <c>Microsoft.Extensions.DependencyInjection</c> infrastructure.
        /// </para>
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance for
        /// fluent chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// is <c>null</c>.</exception>
        public static IOpcUaBuilder AddAlarms(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<AlarmClientFactory>();
            return builder;
        }

        /// <summary>
        /// Registers the singleton <see cref="AlarmClientFactory"/> and
        /// keeps the client builder chain flowing.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddAlarms(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddAlarms();
            return builder;
        }

        private sealed class BuilderAdapter : IOpcUaBuilder
        {
            public BuilderAdapter(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}

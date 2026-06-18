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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transports;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Default <see cref="IPubSubBuilder"/> implementation. Accumulates
    /// the requested PubSub composition as a set of deferred steps and,
    /// when finalised, registers an <see cref="IPubSubApplication"/>
    /// factory that runs them against a fresh
    /// <see cref="PubSubApplicationBuilder"/>. This supersedes the
    /// default factory registered by
    /// <see cref="OpcUaPubSubBuilderExtensions"/> so callers never have
    /// to hand-roll their own factory before adding the feature.
    /// </summary>
    internal sealed class PubSubBuilder : IPubSubBuilder
    {
        private readonly List<Action<IServiceProvider, PubSubApplicationBuilder>> m_steps = [];
        private bool m_hasDirectConfiguration;
        private bool m_hasConfigureApplication;

        /// <summary>
        /// Initializes a new <see cref="PubSubBuilder"/>.
        /// </summary>
        /// <param name="opcUaBuilder">The central OPC UA builder.</param>
        public PubSubBuilder(IOpcUaBuilder opcUaBuilder)
        {
            OpcUaBuilder = opcUaBuilder
                ?? throw new ArgumentNullException(nameof(opcUaBuilder));
        }

        /// <inheritdoc/>
        public IServiceCollection Services => OpcUaBuilder.Services;

        /// <inheritdoc/>
        public IOpcUaBuilder OpcUaBuilder { get; }

        /// <inheritdoc/>
        public IPubSubBuilder AddPublisher()
        {
            return this;
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddSubscriber()
        {
            return this;
        }

        /// <inheritdoc/>
        public IPubSubBuilder ConfigureApplication(Action<PubSubApplicationBuilder> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_hasConfigureApplication = true;
            m_steps.Add((_, pb) => configure(pb));
            return this;
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddSecurityKeyProvider(IPubSubSecurityKeyProvider keyProvider)
        {
            if (keyProvider is null)
            {
                throw new ArgumentNullException(nameof(keyProvider));
            }
            Services.AddSingleton(keyProvider);
            m_steps.Add((_, pb) => pb.AddSecurityKeyProvider(keyProvider));
            return this;
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddDataSetSource(
            string publishedDataSetName,
            IPublishedDataSetSource source)
        {
            if (string.IsNullOrEmpty(publishedDataSetName))
            {
                throw new ArgumentException(
                    "publishedDataSetName must not be empty.",
                    nameof(publishedDataSetName));
            }
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            m_steps.Add((_, pb) => pb.AddDataSetSource(publishedDataSetName, source));
            return this;
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddDataSetSource(
            string publishedDataSetName,
            Func<IServiceProvider, IPublishedDataSetSource> sourceFactory)
        {
            if (string.IsNullOrEmpty(publishedDataSetName))
            {
                throw new ArgumentException(
                    "publishedDataSetName must not be empty.",
                    nameof(publishedDataSetName));
            }
            if (sourceFactory is null)
            {
                throw new ArgumentNullException(nameof(sourceFactory));
            }
            m_steps.Add((sp, pb) =>
                pb.AddDataSetSource(publishedDataSetName, sourceFactory(sp)));
            return this;
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddSubscribedDataSetSink(
            string dataSetReaderName,
            ISubscribedDataSetSink sink)
        {
            if (string.IsNullOrEmpty(dataSetReaderName))
            {
                throw new ArgumentException(
                    "dataSetReaderName must not be empty.",
                    nameof(dataSetReaderName));
            }
            if (sink is null)
            {
                throw new ArgumentNullException(nameof(sink));
            }
            m_steps.Add((_, pb) => pb.AddSubscribedDataSetSink(dataSetReaderName, sink));
            return this;
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddSubscribedDataSetSink(
            string dataSetReaderName,
            Func<IServiceProvider, ISubscribedDataSetSink> sinkFactory)
        {
            if (string.IsNullOrEmpty(dataSetReaderName))
            {
                throw new ArgumentException(
                    "dataSetReaderName must not be empty.",
                    nameof(dataSetReaderName));
            }
            if (sinkFactory is null)
            {
                throw new ArgumentNullException(nameof(sinkFactory));
            }
            m_steps.Add((sp, pb) =>
                pb.AddSubscribedDataSetSink(dataSetReaderName, sinkFactory(sp)));
            return this;
        }

        /// <inheritdoc/>
        public IPubSubBuilder UseConfiguration(PubSubConfigurationDataType configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            m_hasDirectConfiguration = true;
            m_steps.Add((_, pb) => pb.UseConfiguration(configuration));
            return this;
        }

        /// <inheritdoc/>
        public IPubSubBuilder UseConfigurationFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("path must not be empty.", nameof(path));
            }
            m_hasDirectConfiguration = true;
            m_steps.Add((_, pb) => pb.UseConfigurationFile(path));
            return this;
        }

        /// <inheritdoc/>
        public IPubSubBuilder Configure(Action<PubSubApplicationOptions> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Services.AddOptions<PubSubApplicationOptions>().Configure(configure);
            return this;
        }

        /// <summary>
        /// Registers the <see cref="IPubSubApplication"/> factory that
        /// applies the accumulated composition steps. Called once by the
        /// <c>AddPubSub</c> extension after the configure callback ran.
        /// </summary>
        public void Build()
        {
            List<Action<IServiceProvider, PubSubApplicationBuilder>> steps = m_steps;
            bool applyOptionsConfiguration =
                !m_hasDirectConfiguration && !m_hasConfigureApplication;

            // Supersedes the default IPubSubApplication registered by
            // RegisterCoreServices: a later AddSingleton wins for
            // GetRequiredService.
            Services.AddSingleton<IPubSubApplication>(sp =>
            {
                ITelemetryContext telemetry =
                    sp.GetRequiredService<ITelemetryContext>();
                PubSubApplicationOptions options =
                    sp.GetRequiredService<IOptions<PubSubApplicationOptions>>().Value;
                TimeProvider clock =
                    sp.GetService<TimeProvider>() ?? TimeProvider.System;

                var pb = new PubSubApplicationBuilder(telemetry)
                    .UseAllStandardEncoders()
                    .WithTimeProvider(clock)
                    .WithDiagnosticsLevel(options.DiagnosticsLevel);
                if (!string.IsNullOrEmpty(options.ApplicationId))
                {
                    pb.WithApplicationId(options.ApplicationId!);
                }
                foreach (IPubSubTransportFactory factory
                    in sp.GetServices<IPubSubTransportFactory>())
                {
                    pb.AddTransportFactory(factory);
                }
                if (applyOptionsConfiguration)
                {
                    if (!string.IsNullOrEmpty(options.ConfigurationFilePath))
                    {
                        pb.UseConfigurationFile(options.ConfigurationFilePath!);
                    }
                    else
                    {
                        pb.UseConfiguration(
                            options.InlineConfiguration ?? new PubSubConfigurationDataType());
                    }
                }
                foreach (Action<IServiceProvider, PubSubApplicationBuilder> step in steps)
                {
                    step(sp, pb);
                }
                return pb.Build();
            });
        }
    }
}

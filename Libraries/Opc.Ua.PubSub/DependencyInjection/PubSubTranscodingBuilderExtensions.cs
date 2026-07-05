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
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transcoding;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent extensions that register in-process PubSub transcoding bridges
    /// on an <see cref="IPubSubBuilder"/>. A bridge observes messages
    /// received on a source connection, transcodes them, and re-publishes
    /// them on a target connection. Bridges can be declared fluently or bound
    /// from configuration with hot reload.
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

        /// <summary>
        /// Registers declarative, reloadable transcoding routes bound from
        /// <paramref name="configuration"/>. Routes are watched through an
        /// <c>IOptionsMonitor</c>; when the configuration changes, only the
        /// added, removed, or modified routes are reconfigured while
        /// unchanged routes keep running.
        /// </summary>
        /// <param name="builder">The PubSub builder.</param>
        /// <param name="configuration">
        /// Configuration section binding a
        /// <see cref="PubSubTranscodingOptions"/> (a <c>Routes</c> array).
        /// </param>
        /// <returns>The original <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="builder"/> or
        /// <paramref name="configuration"/> is <see langword="null"/>.
        /// </exception>
        public static IPubSubBuilder AddTranscoding(
            this IPubSubBuilder builder,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            builder.Services.AddSingleton<IOptionsChangeTokenSource<PubSubTranscodingOptions>>(
                new ConfigurationChangeTokenSource<PubSubTranscodingOptions>(
                    string.Empty, configuration));
            builder.Services.AddSingleton<IConfigureOptions<PubSubTranscodingOptions>>(
                new ConfigureOptions<PubSubTranscodingOptions>(
                    options => BindOptions(options, configuration)));

            builder.Services.TryAddSingleton(
                sp => new PubSubTranscodingReloadCoordinator(
                    sp,
                    sp.GetRequiredService<IOptionsMonitor<PubSubTranscodingOptions>>(),
                    sp.GetRequiredService<ITelemetryContext>()));
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, PubSubTranscodingReloadHostedService>());
            return builder;
        }

        private static void BindOptions(
            PubSubTranscodingOptions options,
            IConfiguration configuration)
        {
            options.Routes.Clear();
            IConfigurationSection routes = configuration.GetSection(
                nameof(PubSubTranscodingOptions.Routes));
            foreach (IConfigurationSection route in routes.GetChildren())
            {
                options.Routes.Add(BindRoute(route));
            }
        }

        private static TranscodeRouteOptions BindRoute(IConfigurationSection section)
        {
            var route = new TranscodeRouteOptions
            {
                Name = section[nameof(TranscodeRouteOptions.Name)],
                Source = section[nameof(TranscodeRouteOptions.Source)],
                Target = section[nameof(TranscodeRouteOptions.Target)],
                Topic = section[nameof(TranscodeRouteOptions.Topic)],
                PromotedFieldPrefix = section[nameof(TranscodeRouteOptions.PromotedFieldPrefix)]
            };
            BindEnum<TranscodeEncoding>(
                section,
                nameof(TranscodeRouteOptions.TargetEncoding),
                value => route.TargetEncoding = value);
            BindEnum<PubSubFieldEncoding>(
                section,
                nameof(TranscodeRouteOptions.FieldEncoding),
                value => route.FieldEncoding = value);
            BindBoolean(
                section,
                nameof(TranscodeRouteOptions.JsonSingleMessage),
                value => route.JsonSingleMessage = value);
            BindBoolean(
                section,
                nameof(TranscodeRouteOptions.PreserveMetaDataVersion),
                value => route.PreserveMetaDataVersion = value);
            BindBoolean(
                section,
                nameof(TranscodeRouteOptions.AllowInsecureCrossEncoding),
                value => route.AllowInsecureCrossEncoding = value);
            BindBoolean(
                section,
                nameof(TranscodeRouteOptions.DropKeepAlive),
                value => route.DropKeepAlive = value);
            route.RenameFields = BindStringMap(
                section.GetSection(nameof(TranscodeRouteOptions.RenameFields)));
            route.SelectFields = BindStringList(
                section.GetSection(nameof(TranscodeRouteOptions.SelectFields)));
            route.ExcludeFields = BindStringList(
                section.GetSection(nameof(TranscodeRouteOptions.ExcludeFields)));
            route.PromoteFields = BindStringList(
                section.GetSection(nameof(TranscodeRouteOptions.PromoteFields)));
            route.KeepMessageTypes = BindEnumList<PubSubDataSetMessageType>(
                section.GetSection(nameof(TranscodeRouteOptions.KeepMessageTypes)));
            route.RemapIds = BindRemap(
                section.GetSection(nameof(TranscodeRouteOptions.RemapIds)));
            return route;
        }

        private static TranscodeIdRemapOptions? BindRemap(IConfigurationSection section)
        {
            if (!section.Exists())
            {
                return null;
            }
            var remap = new TranscodeIdRemapOptions
            {
                PublisherId = section[nameof(TranscodeIdRemapOptions.PublisherId)],
                DataSetClassId = section[nameof(TranscodeIdRemapOptions.DataSetClassId)]
            };
            BindUInt64(
                section,
                nameof(TranscodeIdRemapOptions.PublisherIdNumber),
                value => remap.PublisherIdNumber = value);
            BindUInt16(
                section,
                nameof(TranscodeIdRemapOptions.WriterGroupId),
                value => remap.WriterGroupId = value);
            remap.DataSetWriterIds = BindUInt16Map(
                section.GetSection(nameof(TranscodeIdRemapOptions.DataSetWriterIds)));
            return remap;
        }

        private static Dictionary<string, string>? BindStringMap(IConfigurationSection section)
        {
            Dictionary<string, string>? map = null;
            foreach (IConfigurationSection child in section.GetChildren())
            {
                if (child.Value is not null)
                {
                    map ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    map[child.Key] = child.Value;
                }
            }
            return map;
        }

        private static Dictionary<ushort, ushort>? BindUInt16Map(IConfigurationSection section)
        {
            Dictionary<ushort, ushort>? map = null;
            foreach (IConfigurationSection child in section.GetChildren())
            {
                if (child.Value is null)
                {
                    continue;
                }
                if (!ushort.TryParse(child.Key, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out ushort key)
                    || !ushort.TryParse(child.Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out ushort value))
                {
                    throw new InvalidOperationException(
                        $"DataSetWriterIds entry '{child.Key}'='{child.Value}' is not a valid "
                        + "ushort mapping.");
                }
                map ??= [];
                map[key] = value;
            }
            return map;
        }

        private static List<string>? BindStringList(IConfigurationSection section)
        {
            List<string>? list = null;
            foreach (IConfigurationSection child in section.GetChildren())
            {
                if (child.Value is not null)
                {
                    list ??= [];
                    list.Add(child.Value);
                }
            }
            return list;
        }

        private static List<TEnum>? BindEnumList<TEnum>(IConfigurationSection section)
            where TEnum : struct, Enum
        {
            List<TEnum>? list = null;
            foreach (IConfigurationSection child in section.GetChildren())
            {
                if (child.Value is null)
                {
                    continue;
                }
                if (!Enum.TryParse(child.Value, ignoreCase: true, out TEnum parsed))
                {
                    throw new InvalidOperationException(
                        $"Configuration value '{child.Value}' is not a valid "
                        + $"{typeof(TEnum).Name}.");
                }
                list ??= [];
                list.Add(parsed);
            }
            return list;
        }

        private static void BindEnum<TEnum>(
            IConfiguration configuration,
            string key,
            Action<TEnum> assign)
            where TEnum : struct, Enum
        {
            string? value = configuration[key];
            if (value is null)
            {
                return;
            }
            if (!Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
            {
                throw new InvalidOperationException(
                    $"Configuration value '{value}' for '{key}' is not a valid "
                    + $"{typeof(TEnum).Name}.");
            }
            assign(parsed);
        }

        private static void BindBoolean(
            IConfiguration configuration,
            string key,
            Action<bool> assign)
        {
            string? value = configuration[key];
            if (value is null)
            {
                return;
            }
            if (!bool.TryParse(value, out bool parsed))
            {
                throw new InvalidOperationException(
                    $"Configuration value '{value}' for '{key}' is not a valid boolean.");
            }
            assign(parsed);
        }

        private static void BindUInt16(
            IConfiguration configuration,
            string key,
            Action<ushort> assign)
        {
            string? value = configuration[key];
            if (value is null)
            {
                return;
            }
            if (!ushort.TryParse(value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out ushort parsed))
            {
                throw new InvalidOperationException(
                    $"Configuration value '{value}' for '{key}' is not a valid ushort.");
            }
            assign(parsed);
        }

        private static void BindUInt64(
            IConfiguration configuration,
            string key,
            Action<ulong> assign)
        {
            string? value = configuration[key];
            if (value is null)
            {
                return;
            }
            if (!ulong.TryParse(value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out ulong parsed))
            {
                throw new InvalidOperationException(
                    $"Configuration value '{value}' for '{key}' is not a valid ulong.");
            }
            assign(parsed);
        }
    }
}

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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    public static partial class OpcUaServerBuilderExtensions
    {
        /// <summary>
        /// Configures server-wide resource isolation without replacing existing rate limiters.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureResourceIsolation(
            this IOpcUaServerBuilder builder,
            Action<ServerResourceIsolationOptions> configure)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            builder.Services.AddOptions<OpcUaServerOptions>()
                .Configure(options => configure(options.ResourceIsolation));
            return builder;
        }

        /// <summary>
        /// Installs an explicit trusted ingress and verified-owner mapping.
        /// </summary>
        /// <typeparam name="TClassifier">The host's trusted classification implementation.</typeparam>
        public static IOpcUaServerBuilder AddResourceIsolationClassifier<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClassifier>(
            this IOpcUaServerBuilder builder)
            where TClassifier : class, IResourceIsolationClassifier
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddSingleton<IResourceIsolationClassifier, TClassifier>();
            return builder;
        }

        private static void BindResourceIsolationOptions(
            ServerResourceIsolationOptions options,
            IConfiguration section)
        {
            options.Mode = ParseIsolationEnum<ServerResourceIsolationMode>(section, nameof(options.Mode), options.Mode);
            options.MaxTrackedOwners = checked((int)(ReadIsolationLong(section, nameof(options.MaxTrackedOwners)) ??
                options.MaxTrackedOwners));
            options.MaxOwnerKeyLength = checked((int)(ReadIsolationLong(section, nameof(options.MaxOwnerKeyLength)) ??
                options.MaxOwnerKeyLength));
            options.DefaultWeight = checked((int)(ReadIsolationLong(section, nameof(options.DefaultWeight)) ??
                options.DefaultWeight));
            options.BootstrapReservedBytes = ReadIsolationLong(section, nameof(options.BootstrapReservedBytes));
            options.ReconnectReservedBytes = ReadIsolationLong(section, nameof(options.ReconnectReservedBytes));
            options.MaxRetainedMessageBytes = ReadIsolationLong(section, nameof(options.MaxRetainedMessageBytes));
            if (section[nameof(options.HandshakeTimeout)] is { } timeout)
            {
                options.HandshakeTimeout = TimeSpan.Parse(timeout, CultureInfo.InvariantCulture);
            }
            var stages = new List<ResourceIsolationStageOptions>();
            foreach (IConfigurationSection item in section.GetSection(nameof(options.Stages)).GetChildren())
            {
                stages.Add(new ResourceIsolationStageOptions
                {
                    Stage = ParseIsolationEnum<ResourceIsolationStage>(item, "Stage"),
                    Capacity = ReadIsolationLong(item, "Capacity"),
                    BootstrapReserved = ReadIsolationLong(item, "BootstrapReserved"),
                    ReconnectReserved = ReadIsolationLong(item, "ReconnectReserved"),
                    ControlReserved = ReadIsolationLong(item, "ControlReserved"),
                    OwnerHardLimit = ReadIsolationLong(item, "OwnerHardLimit")
                });
            }
            options.Stages = stages.ToArray();
            var owners = new List<TrustedResourceOwnerOptions>();
            foreach (IConfigurationSection item in section.GetSection(nameof(options.TrustedOwners)).GetChildren())
            {
                var reservations = new List<TrustedResourceReservation>();
                foreach (IConfigurationSection reservation in item.GetSection("Reservations").GetChildren())
                {
                    reservations.Add(new TrustedResourceReservation(
                        ParseIsolationEnum<ResourceIsolationStage>(reservation, "Stage"),
                        ReadIsolationLong(reservation, "Reserved") ??
                            throw new ArgumentException("A trusted reservation requires Reserved."),
                        ReadIsolationLong(reservation, "HardLimit")));
                }
                owners.Add(new TrustedResourceOwnerOptions
                {
                    Key = item["Key"] ?? throw new ArgumentException("A trusted owner requires Key."),
                    Weight = checked((int)(ReadIsolationLong(item, "Weight") ?? 1)),
                    Reservations = reservations.ToArray()
                });
            }
            options.TrustedOwners = owners.ToArray();
        }

        private static long? ReadIsolationLong(IConfiguration section, string name)
        {
            string? text = section[name];
            if (text == null)
            {
                return null;
            }
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                throw new ArgumentException($"Resource isolation option {name} must be an Int64.");
            }
            return value;
        }

        private static TEnum ParseIsolationEnum<TEnum>(
            IConfiguration section,
            string name,
            TEnum? fallback = null)
            where TEnum : struct, Enum
        {
            string? text = section[name];
            if (text == null && fallback.HasValue)
            {
                return fallback.Value;
            }
            if (text == null || !Enum.TryParse(text, true, out TEnum value))
            {
                throw new ArgumentException($"Invalid resource isolation {name}.");
            }
#if NET8_0_OR_GREATER
            bool defined = Enum.IsDefined(value);
#else
            bool defined = Enum.IsDefined(typeof(TEnum), value);
#endif
            if (!defined)
            {
                throw new ArgumentException($"Invalid resource isolation {name}.");
            }
            return value;
        }
    }
}

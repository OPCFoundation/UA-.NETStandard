/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Base telemetry context implementation
    /// </summary>
    public class TelemetryContextBase : ITelemetryContext
    {
        /// <inheritdoc/>
        public ILoggerFactory LoggerFactory { get; }

        /// <summary>
        /// Create default telemetry
        /// </summary>
        protected TelemetryContextBase(ILoggerFactory loggerFactory)
        {
            LoggerFactory = loggerFactory;

            // Set the default Id format to W3C
            Activity.DefaultIdFormat = ActivityIdFormat.W3C;
            Activity.ForceDefaultIdFormat = true;
        }

        /// <inheritdoc/>
        // GetCallingAssembly has to be called from the public entry point:
        // inside a private helper it only ever reports this assembly, so every
        // meter and activity source was named after Opc.Ua.Types.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Meter CreateMeter()
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.GetCallingAssembly();
            }
            catch (PlatformNotSupportedException)
            {
                assembly = typeof(TelemetryContextBase).Assembly;
            }
            return CreateMeter(assembly);
        }

        /// <inheritdoc/>
        public Meter CreateMeter(Assembly assembly)
        {
            (string name, string version) = GetAssemblyInfo(
                assembly ?? throw new ArgumentNullException(nameof(assembly)));
            return new Meter(name, version);
        }

        /// <inheritdoc/>
        public ActivitySource ActivitySource
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                Assembly assembly;
                try
                {
                    assembly = Assembly.GetCallingAssembly();
                }
                catch (PlatformNotSupportedException)
                {
                    assembly = typeof(TelemetryContextBase).Assembly;
                }
                return GetActivitySource(assembly);
            }
        }

        /// <inheritdoc/>
        public ActivitySource GetActivitySource(Assembly assembly)
        {
            return s_sources.GetOrAdd(
                GetAssemblyInfo(assembly ?? throw new ArgumentNullException(nameof(assembly))),
                key => new ActivitySource(key.Item1, key.Item2));
        }

        private static (string, string) GetAssemblyInfo(Assembly assembly)
        {
            return s_cache.GetOrAdd(assembly, GetAssemblyInfoCore);
            static (string, string) GetAssemblyInfoCore(Assembly assembly)
            {
                string version = assembly
                    .GetCustomAttribute<AssemblyFileVersionAttribute>()?
                    .Version ??
                    "1.0.0";
                string name = assembly.FullName ?? "Opc.Ua";
                return (name, version);
            }
        }

        private static readonly ConcurrentDictionary<(string, string), ActivitySource> s_sources = [];
        private static readonly ConcurrentDictionary<Assembly, (string, string)> s_cache = [];
    }
}

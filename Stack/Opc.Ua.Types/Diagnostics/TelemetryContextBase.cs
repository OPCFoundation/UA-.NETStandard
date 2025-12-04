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

#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
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
        public Meter CreateMeter()
        {
            (string name, string version) = GetAssemblyInfo();
            return new Meter(name, version);
        }

        /// <inheritdoc/>
        public ActivitySource ActivitySource
            => s_sources.GetOrAdd(GetAssemblyInfo(),
                    key => new ActivitySource(key.Item1, key.Item2));

        private static (string, string) GetAssemblyInfo()
        {
            return s_cache.GetOrAdd(Assembly.GetCallingAssembly(), GetAssemblyInfoCore);
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

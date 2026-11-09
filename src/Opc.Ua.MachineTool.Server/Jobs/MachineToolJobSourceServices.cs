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
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Registers the optional reference source as one coherent, independently owned source per DI scope.
    /// Applications can instead construct it directly and pass the same instance to both machine bindings.
    /// </summary>
    public static class MachineToolJobSourceServices
    {
        /// <summary>
        /// Registers the source and its existing provider facets with scoped lifetime. Use one scope per machine;
        /// the scope owns the source, while its TimeProvider and telemetry services remain borrowed.
        /// </summary>
        public static IServiceCollection AddMachineToolJobSource(
            this IServiceCollection services,
            MachineToolJobSourceOptions options,
            MachineToolSnapshot? monitoring = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            services.AddScoped(provider => new MachineToolJobSource(options, monitoring,
                provider.GetService<TimeProvider>(), provider.GetService<ITelemetryContext>()));
            services.AddScoped<IMachineToolProvider>(Resolve);
            services.AddScoped<IIsa95JobOrderReceiverV2>(Resolve);
            services.AddScoped<IIsa95JobResponseProviderV2>(Resolve);
            services.AddScoped<IIsa95JobResponseReceiverV2>(Resolve);
            services.AddScoped<IIsa95JobExecutionController>(Resolve);
            services.AddScoped<IIsa95JobOrderCatalog>(Resolve);
            services.AddScoped<IIsa95JobResponseCatalog>(Resolve);
            services.AddScoped<IIsa95JobStatusSourceV2>(Resolve);
            services.AddScoped<IIsa95JobOrderCatalogChangeSource>(Resolve);
            services.AddScoped<IIsa95JobResponseChangeSource>(Resolve);
            return services;
        }

        /// <summary>
        /// Resolves exactly the same source for every facet within a machine scope.
        /// </summary>
        private static MachineToolJobSource Resolve(IServiceProvider provider) =>
            provider.GetRequiredService<MachineToolJobSource>();
    }
}

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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Aas.V3;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers DPP services for AAS model applications.
    /// </summary>
    public static class AasDppServiceCollectionExtensions
    {
        /// <summary>
        /// Default configuration section used by the <see cref="AddAasDpp(IServiceCollection, IConfiguration)"/>
        /// overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Aas:Dpp";

        /// <summary>
        /// Registers DPP identifier construction, mapping set lookup and disclosure policy services.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional callback used to populate <see cref="AasDppOptions"/>.</param>
        /// <returns>The same <paramref name="services"/> instance for fluent chaining.</returns>
        public static IServiceCollection AddAasDpp(
            this IServiceCollection services,
            Action<AasDppOptions>? configure = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var options = new AasDppOptions();
            configure?.Invoke(options);
            RegisterCoreServices(services, options);

            return services;
        }

        /// <summary>
        /// Registers DPP services with options bound from the default configuration section.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The configuration root containing <c>OpcUa:Aas:Dpp</c>.</param>
        /// <returns>The same <paramref name="services"/> instance for fluent chaining.</returns>
        public static IServiceCollection AddAasDpp(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return services.AddAasDpp(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers DPP services with options bound from the supplied configuration section.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="section">The configuration section to bind.</param>
        /// <returns>The same <paramref name="services"/> instance for fluent chaining.</returns>
        public static IServiceCollection AddAasDpp(
            this IServiceCollection services,
            IConfigurationSection section)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            RegisterCoreServices(services, ReadOptions(section));

            return services;
        }

        private static void RegisterCoreServices(IServiceCollection services, AasDppOptions options)
        {
            services.Replace(ServiceDescriptor.Singleton(options));
            services.TryAddSingleton<IAasDppIdentifierFactory, AasDppIdentifierFactory>();
            services.TryAddSingleton<IAasDppMappingSet, AasDppMappingSetProvider>();
            services.Replace(ServiceDescriptor.Singleton<IAasDisclosurePolicy>(
                sp =>
                {
                    AasDppOptions registeredOptions = sp.GetRequiredService<AasDppOptions>();
                    return new AasDppDisclosurePolicy(
                        registeredOptions.DisclosureRules,
                        registeredOptions.DefaultRegulatoryClass);
                }));
        }

        private static AasDppOptions ReadOptions(IConfigurationSection section)
        {
            var options = new AasDppOptions();
            string? defaultClass = section[nameof(AasDppOptions.DefaultRegulatoryClass)];
            if (!string.IsNullOrEmpty(defaultClass) &&
                Enum.TryParse(defaultClass, ignoreCase: true, out AasDppRegulatoryClass regulatoryClass))
            {
                options.DefaultRegulatoryClass = regulatoryClass;
            }

            var rules = new List<AasDppDisclosureRule>();
            foreach (IConfigurationSection ruleSection in section
                .GetSection(nameof(AasDppOptions.DisclosureRules))
                .GetChildren())
            {
                string? modelType = ruleSection[nameof(AasDppDisclosureRule.ModelType)];
                string? idShort = ruleSection[nameof(AasDppDisclosureRule.IdShort)];
                string? ruleClass = ruleSection[nameof(AasDppDisclosureRule.RegulatoryClass)];
                if (string.IsNullOrEmpty(modelType) ||
                    string.IsNullOrEmpty(ruleClass) ||
                    !Enum.TryParse(ruleClass, ignoreCase: true, out AasDppRegulatoryClass parsedClass))
                {
                    continue;
                }

                rules.Add(new AasDppDisclosureRule(modelType, idShort ?? string.Empty, parsedClass));
            }

            options.DisclosureRules = new Opc.Ua.ArrayOf<AasDppDisclosureRule>(rules.ToArray());
            return options;
        }
    }
}

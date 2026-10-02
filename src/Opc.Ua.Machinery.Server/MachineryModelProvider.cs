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

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Built-in compiled model provider for the configured OPC 40001 parts and
    /// every model they depend on.
    /// </summary>
    public sealed class MachineryModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates a provider for every part of the series.
        /// </summary>
        public MachineryModelProvider()
            : this(MachineryParts.All)
        {
        }

        /// <summary>
        /// Creates a provider for the selected parts.
        /// </summary>
        /// <param name="parts">The OPC 40001 parts to contribute.</param>
        public MachineryModelProvider(MachineryParts parts)
        {
            MachineryPartsValidation.Validate(parts);
            Parts = parts;
        }

        /// <summary>
        /// Gets the parts this provider contributes.
        /// </summary>
        public MachineryParts Parts { get; }

        /// <inheritdoc/>
        public int Order => int.MinValue;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => MachineryServer.GetNamespaceUris(Parts);

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            nodes.AddMachineryTypeSystem(context, Parts);
        }
    }

    /// <summary>
    /// Validates and orders the <see cref="IMachineryModelProvider"/> list a
    /// Machinery node manager is configured with, and derives from it the
    /// namespace URIs the manager and its factory register. The built-in
    /// <see cref="MachineryModelProvider"/> always sorts first, and together
    /// the providers must cover every namespace the selected parts need.
    /// </summary>
    internal static class MachineryModelProviderUtilities
    {
        public static ArrayOf<IMachineryModelProvider> Normalize(
            ArrayOf<IMachineryModelProvider> providers,
            MachineryParts parts)
        {
            if (providers.IsNull)
            {
                throw new ArgumentNullException(nameof(providers));
            }
            if (providers.IsEmpty)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "At least one Machinery model provider must be configured.");
            }

            var entries = new List<ProviderEntry>(providers.Count);
            for (int ii = 0; ii < providers.Count; ii++)
            {
                IMachineryModelProvider provider = providers[ii] ??
                    throw new ArgumentException(
                        "Machinery model providers must not contain null entries.",
                        nameof(providers));
                entries.Add(new ProviderEntry(provider, ii));
            }

            entries.Sort(static (left, right) =>
            {
                bool leftIsBuiltIn = left.Provider is MachineryModelProvider;
                bool rightIsBuiltIn = right.Provider is MachineryModelProvider;
                if (leftIsBuiltIn != rightIsBuiltIn)
                {
                    return leftIsBuiltIn ? -1 : 1;
                }

                int result = left.Provider.Order.CompareTo(right.Provider.Order);
                if (result != 0)
                {
                    return result;
                }

                result = string.Compare(
                    GetStableTypeName(left.Provider),
                    GetStableTypeName(right.Provider),
                    StringComparison.Ordinal);
                return result != 0 ? result : left.OriginalIndex.CompareTo(right.OriginalIndex);
            });

            var normalized = new IMachineryModelProvider[entries.Count];
            for (int ii = 0; ii < entries.Count; ii++)
            {
                normalized[ii] = entries[ii].Provider;
            }

            ValidateRequiredNamespaces(normalized, parts);
            return normalized;
        }

        /// <summary>
        /// The namespaces the node manager itself registers. The Device
        /// Integration namespace is removed because
        /// <c>DiNodeManager</c> always appends it.
        /// </summary>
        public static string[] GetManagerNamespaceUris(
            ArrayOf<IMachineryModelProvider> providers,
            MachineryServerOptions options)
        {
            options = ValidateOptions(options);
            ArrayOf<IMachineryModelProvider> normalized =
                Normalize(providers, options.Parts);
            ValidateInstanceNamespace(options, normalized);
            var namespaceUris = new List<string>();
            AddProviderNamespaces(namespaceUris, normalized);
            AddNamespace(namespaceUris, options.InstanceNamespaceUri);
            namespaceUris.RemoveAll(
                static namespaceUri =>
                    namespaceUri == Opc.Ua.Di.Server.DiNodeManager.DiNamespaceUri);
            return namespaceUris.ToArray();
        }

        /// <summary>
        /// The namespaces the factory announces to the server. The Device
        /// Integration namespace is added back because the manager does
        /// register it — just not through the list above.
        /// </summary>
        public static ArrayOf<string> GetFactoryNamespaceUris(
            ArrayOf<IMachineryModelProvider> providers,
            MachineryServerOptions options)
        {
            options = ValidateOptions(options);
            ArrayOf<IMachineryModelProvider> normalized =
                Normalize(providers, options.Parts);
            ValidateInstanceNamespace(options, normalized);
            var namespaceUris = new List<string>();
            AddProviderNamespaces(namespaceUris, normalized);
            AddNamespace(namespaceUris, options.InstanceNamespaceUri);
            AddNamespace(namespaceUris, Opc.Ua.Di.Server.DiNodeManager.DiNamespaceUri);
            return namespaceUris;
        }

        public static MachineryServerOptions ValidateOptions(MachineryServerOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            options.Validate();
            return options;
        }

        public static MachineryServerOptions ValidateOptions(
            MachineryServerOptions options,
            ArrayOf<IMachineryModelProvider> providers)
        {
            options = ValidateOptions(options);
            ValidateInstanceNamespace(options, providers);
            return options;
        }

        private static void AddProviderNamespaces(
            List<string> namespaceUris,
            ArrayOf<IMachineryModelProvider> providers)
        {
            for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
            {
                ArrayOf<string> providerNamespaces = providers[providerIndex].NamespaceUris;
                for (int namespaceIndex = 0;
                    namespaceIndex < providerNamespaces.Count;
                    namespaceIndex++)
                {
                    AddNamespace(namespaceUris, providerNamespaces[namespaceIndex]);
                }
            }
        }

        private static void ValidateRequiredNamespaces(
            ArrayOf<IMachineryModelProvider> providers,
            MachineryParts parts)
        {
            var advertised = new HashSet<string>(StringComparer.Ordinal);
            for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
            {
                ArrayOf<string> providerNamespaces = providers[providerIndex].NamespaceUris;
                if (providerNamespaces.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Machinery model provider '{0}' returned a null namespace URI list.",
                        GetStableTypeName(providers[providerIndex]));
                }

                for (int namespaceIndex = 0;
                    namespaceIndex < providerNamespaces.Count;
                    namespaceIndex++)
                {
                    string namespaceUri = providerNamespaces[namespaceIndex];
                    if (string.IsNullOrWhiteSpace(namespaceUri))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            "Machinery model provider '{0}' advertised an empty namespace URI.",
                            GetStableTypeName(providers[providerIndex]));
                    }
                    advertised.Add(namespaceUri);
                }
            }

            ArrayOf<string> required = MachineryServer.GetNamespaceUris(parts);
            for (int ii = 0; ii < required.Count; ii++)
            {
                if (!advertised.Contains(required[ii]))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Machinery model providers must collectively advertise the " +
                        "namespace '{0}' required by MachineryParts '{1}'.",
                        required[ii],
                        parts);
                }
            }
        }

        private static void ValidateInstanceNamespace(
            MachineryServerOptions options,
            ArrayOf<IMachineryModelProvider> providers)
        {
            for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
            {
                ArrayOf<string> providerNamespaces = providers[providerIndex].NamespaceUris;
                for (int namespaceIndex = 0;
                    namespaceIndex < providerNamespaces.Count;
                    namespaceIndex++)
                {
                    if (options.InstanceNamespaceUri == providerNamespaces[namespaceIndex])
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            "MachineryServerOptions.InstanceNamespaceUri '{0}' is provided " +
                            "by Machinery model provider '{1}'. Configure a distinct " +
                            "application-owned namespace for Machinery instances.",
                            options.InstanceNamespaceUri,
                            GetStableTypeName(providers[providerIndex]));
                    }
                }
            }
        }

        private static void AddNamespace(List<string> namespaceUris, string namespaceUri)
        {
            if (string.IsNullOrWhiteSpace(namespaceUri))
            {
                throw new ArgumentException(
                    "Machinery model providers must advertise non-empty namespace URIs.",
                    nameof(namespaceUri));
            }

            if (!namespaceUris.Contains(namespaceUri))
            {
                namespaceUris.Add(namespaceUri);
            }
        }

        private static string GetStableTypeName(IMachineryModelProvider provider)
        {
            Type type = provider.GetType();
            return type.FullName ?? type.Name;
        }

        private readonly record struct ProviderEntry(
            IMachineryModelProvider Provider,
            int OriginalIndex);
    }
}

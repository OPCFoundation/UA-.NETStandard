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

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Built-in compiled model provider for DI, IA, and Robotics.
    /// </summary>
    public sealed class RoboticsModelProvider : IRoboticsModelProvider
    {
        /// <inheritdoc/>
        public int Order => int.MinValue;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => new string[]
        {
            Opc.Ua.IA.Namespaces.IA,
            Opc.Ua.Robotics.Namespaces.Robotics
        };

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

            nodes.AddRoboticsTypeSystem(context);
        }
    }

    internal static class RoboticsModelProviderUtilities
    {
        public static ArrayOf<IRoboticsModelProvider> Normalize(
            ArrayOf<IRoboticsModelProvider> providers)
        {
            if (providers.IsNull)
            {
                throw new ArgumentNullException(nameof(providers));
            }
            if (providers.IsEmpty)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "At least one Robotics model provider must be configured.");
            }

            var entries = new List<ProviderEntry>(providers.Count);
            for (int ii = 0; ii < providers.Count; ii++)
            {
                IRoboticsModelProvider provider = providers[ii] ??
                    throw new ArgumentException(
                        "Robotics model providers must not contain null entries.",
                        nameof(providers));
                entries.Add(new ProviderEntry(provider, ii));
            }

            entries.Sort(static (left, right) =>
            {
                bool leftIsBuiltIn = left.Provider is RoboticsModelProvider;
                bool rightIsBuiltIn = right.Provider is RoboticsModelProvider;
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

            var result = new IRoboticsModelProvider[entries.Count];
            for (int ii = 0; ii < entries.Count; ii++)
            {
                result[ii] = entries[ii].Provider;
            }

            ValidateRequiredNamespaces(result);
            return result;
        }

        public static string[] GetManagerNamespaceUris(
            ArrayOf<IRoboticsModelProvider> providers,
            RoboticsServerOptions options)
        {
            options = ValidateOptions(options);
            ArrayOf<IRoboticsModelProvider> normalized = Normalize(providers);
            ValidateInstanceNamespace(options, normalized);
            var namespaceUris = new List<string>();
            AddProviderNamespaces(namespaceUris, normalized);
            AddNamespace(namespaceUris, options.InstanceNamespaceUri);
            namespaceUris.RemoveAll(
                static namespaceUri => namespaceUri == Opc.Ua.Di.Server.DiNodeManager.DiNamespaceUri);
            return namespaceUris.ToArray();
        }

        public static ArrayOf<string> GetFactoryNamespaceUris(
            ArrayOf<IRoboticsModelProvider> providers,
            RoboticsServerOptions options)
        {
            options = ValidateOptions(options);
            ArrayOf<IRoboticsModelProvider> normalized = Normalize(providers);
            ValidateInstanceNamespace(options, normalized);
            var namespaceUris = new List<string>();
            AddProviderNamespaces(namespaceUris, normalized);
            AddNamespace(namespaceUris, options.InstanceNamespaceUri);
            AddNamespace(namespaceUris, Opc.Ua.Di.Server.DiNodeManager.DiNamespaceUri);
            return namespaceUris;
        }

        public static RoboticsServerOptions ValidateOptions(RoboticsServerOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            options.Validate();
            return options;
        }

        public static RoboticsServerOptions ValidateOptions(
            RoboticsServerOptions options,
            ArrayOf<IRoboticsModelProvider> providers)
        {
            options = ValidateOptions(options);
            ValidateInstanceNamespace(options, providers);
            return options;
        }

        private static void AddProviderNamespaces(
            List<string> namespaceUris,
            ArrayOf<IRoboticsModelProvider> providers)
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
            ArrayOf<IRoboticsModelProvider> providers)
        {
            bool hasIa = false;
            bool hasRobotics = false;
            for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
            {
                ArrayOf<string> providerNamespaces = providers[providerIndex].NamespaceUris;
                if (providerNamespaces.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Robotics model provider '{0}' returned a null namespace URI list.",
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
                            "Robotics model provider '{0}' advertised an empty namespace URI.",
                            GetStableTypeName(providers[providerIndex]));
                    }

                    hasIa |= namespaceUri == Opc.Ua.IA.Namespaces.IA;
                    hasRobotics |= namespaceUri == Opc.Ua.Robotics.Namespaces.Robotics;
                }
            }

            if (!hasIa || !hasRobotics)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Robotics model providers must collectively advertise the IA namespace '{0}' " +
                    "and Robotics namespace '{1}'.",
                    Opc.Ua.IA.Namespaces.IA,
                    Opc.Ua.Robotics.Namespaces.Robotics);
            }
        }

        private static void ValidateInstanceNamespace(
            RoboticsServerOptions options,
            ArrayOf<IRoboticsModelProvider> providers)
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
                            "RoboticsServerOptions.InstanceNamespaceUri '{0}' is provided by " +
                            "Robotics model provider '{1}'. Configure a distinct " +
                            "application-owned namespace for Robotics instances.",
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
                    "Robotics model providers must advertise non-empty namespace URIs.",
                    nameof(namespaceUri));
            }

            if (!namespaceUris.Contains(namespaceUri))
            {
                namespaceUris.Add(namespaceUri);
            }
        }

        private static string GetStableTypeName(IRoboticsModelProvider provider)
        {
            Type type = provider.GetType();
            return type.FullName ?? type.Name;
        }

        private readonly record struct ProviderEntry(
            IRoboticsModelProvider Provider,
            int OriginalIndex);
    }
}

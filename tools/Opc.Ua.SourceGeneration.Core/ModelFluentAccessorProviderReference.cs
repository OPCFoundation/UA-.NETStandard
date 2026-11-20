// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Information read from a referenced assembly's
    /// <see cref="ModelFluentAccessorProviderAttribute"/>.
    /// </summary>
    public readonly record struct ModelFluentAccessorProviderReference
    {
        /// <summary>
        /// Creates a provider reference.
        /// </summary>
        public ModelFluentAccessorProviderReference(
            string assemblyName,
            string modelUri,
            string prefix)
        {
            AssemblyName = assemblyName ?? string.Empty;
            ModelUri = modelUri ?? string.Empty;
            Prefix = prefix ?? string.Empty;
        }

        /// <summary>
        /// The assembly containing the generated accessors.
        /// </summary>
        public string AssemblyName { get; }

        /// <summary>
        /// The OPC UA model URI.
        /// </summary>
        public string ModelUri { get; }

        /// <summary>
        /// The C# namespace prefix of the model the accessors are generated for.
        /// </summary>
        public string Prefix { get; }

        /// <summary>
        /// Returns true when the provider has a model URI and C# prefix.
        /// </summary>
        public bool IsValid =>
            !string.IsNullOrEmpty(ModelUri) &&
            !string.IsNullOrEmpty(Prefix);
    }
}

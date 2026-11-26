// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using Opc.Ua.SourceGeneration.Dependency;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Information read from a referenced assembly's
    /// <see cref="ModelDependencyAttribute"/>. Value-type equatable so the
    /// incremental source-generator cache keys behave correctly.
    /// </summary>
    public readonly struct ModelDependencyReference : IEquatable<ModelDependencyReference>
    {
        /// <summary>
        /// Create a reference.
        /// </summary>
        public ModelDependencyReference(
            string assemblyName,
            string modelUri,
            string prefix,
            string version,
            string publicationDate,
            string? name = null,
            string? payload = null)
        {
            AssemblyName = assemblyName ?? string.Empty;
            ModelUri = modelUri ?? string.Empty;
            Prefix = prefix ?? string.Empty;
            Version = version ?? string.Empty;
            PublicationDate = publicationDate ?? string.Empty;
            Name = name ?? string.Empty;
            Payload = payload ?? string.Empty;
        }

        /// <summary>
        /// Returns true when the entry has both a model URI and a C# prefix.
        /// </summary>
        public bool IsValid => ModelUri.Length != 0 && Prefix.Length != 0;

        /// <summary>
        /// The assembly the attribute was read from.
        /// </summary>
        public string AssemblyName { get; }

        /// <summary>
        /// The OPC UA model URI recorded in the attribute.
        /// </summary>
        public string ModelUri { get; }

        /// <summary>
        /// The C# namespace the assembly used for this model's generated types.
        /// </summary>
        public string Prefix { get; }

        /// <summary>
        /// The model version string (may be empty).
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// The model publication date (ISO-8601; may be empty).
        /// </summary>
        public string PublicationDate { get; }

        /// <summary>
        /// The C# identifier used for the model's name constant inside
        /// the referenced assembly's <c>Namespaces</c> class (may be empty).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Base64-encoded Deflate-compressed <c>ModelDependencyV1</c>
        /// payload. Empty on transitive-dependency entries; non-empty
        /// only on the producing assembly's self-declaration entry.
        /// </summary>
        public string Payload { get; }

        /// <summary>
        /// Decodes and memoises the <see cref="Payload"/> as a
        /// <see cref="ModelDependencyV1"/>. Returns <c>null</c> when the
        /// legacy payload is empty, malformed, or carries an unknown version.
        /// Unreadable security extensions instead fail generation.
        /// </summary>
        /// <remarks>
        /// Memoisation is keyed on the payload string to avoid re-decoding
        /// the same multi-kilobyte byte block when the same dependency
        /// flows through Roslyn's incremental cache multiple times.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The dependency contains security metadata this generator cannot preserve.
        /// </exception>
        public ModelDependencyV1? GetDependency()
        {
            if (string.IsNullOrEmpty(Payload))
            {
                return null;
            }
            return s_decoded.GetDependency(Payload);
        }

        /// <summary>
        /// Borrows a shared dependency for read-only generator use. Mutable model
        /// designs must be materialized separately; this graph must never escape to callers.
        /// </summary>
        internal ModelDependencyV1? GetDependencyForGeneration(CancellationToken cancellationToken = default)
        {
            return s_generatorDecoded.GetDependency(Payload, cancellationToken);
        }

        private static ModelDependencyV1? DecodePayload(string payload, CancellationToken cancellationToken)
        {
            try
            {
                return ModelDependencyV1.FromBase64Payload(payload, cancellationToken);
            }
            catch (Exception ex) when (
                ex is InvalidDataException or
                IOException or
                ArgumentException or
                FormatException or
                OverflowException)
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public bool Equals(ModelDependencyReference other)
        {
            return
                string.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal) &&
                string.Equals(ModelUri, other.ModelUri, StringComparison.Ordinal) &&
                string.Equals(Prefix, other.Prefix, StringComparison.Ordinal) &&
                string.Equals(Version, other.Version, StringComparison.Ordinal) &&
                string.Equals(PublicationDate, other.PublicationDate, StringComparison.Ordinal) &&
                string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                string.Equals(Payload, other.Payload, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is ModelDependencyReference other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(AssemblyName);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ModelUri);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Prefix);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Version);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(PublicationDate);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Name);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Payload);
                return hash;
            }
        }

        /// <inheritdoc/>
        public static bool operator ==(ModelDependencyReference left, ModelDependencyReference right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ModelDependencyReference left, ModelDependencyReference right)
        {
            return !left.Equals(right);
        }

        private static readonly DependencyDecodeCache s_decoded = new(false, DecodePayload);
        private static readonly DependencyDecodeCache s_generatorDecoded = new(true, DecodePayload);
    }
}

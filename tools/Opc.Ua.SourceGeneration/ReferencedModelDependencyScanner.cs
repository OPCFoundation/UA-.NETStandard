// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Walks the referenced assemblies of a <see cref="Compilation"/> and
    /// returns the <c>[assembly: Opc.Ua.ModelDependencyAttribute(...)]</c>
    /// entries that downstream consumers must honour.
    /// </summary>
    /// <remarks>
    /// Implemented via <see cref="IAssemblySymbol.GetAttributes"/> so it works
    /// for both <see cref="PortableExecutableReference"/> and
    /// <see cref="CompilationReference"/>, hooks Roslyn's per-symbol
    /// incremental cache, avoids file IO and is AOT-safe.
    /// </remarks>
    internal static class ReferencedModelDependencyScanner
    {
        public const string AttributeMetadataName = "Opc.Ua.ModelDependencyAttribute";

        /// <summary>
        /// Scan all referenced assemblies for ModelDependencyAttribute entries.
        /// </summary>
        public static ImmutableArray<ModelDependencyReference> Scan(Compilation compilation)
        {
            if (compilation == null)
            {
                return [];
            }
            INamedTypeSymbol attrType = compilation.GetTypeByMetadataName(
                AttributeMetadataName);
            if (attrType is null)
            {
                return [];
            }
            ImmutableArray<ModelDependencyReference>.Builder results =
                ImmutableArray.CreateBuilder<ModelDependencyReference>();
            foreach (IAssemblySymbol assembly in
                compilation.SourceModule.ReferencedAssemblySymbols)
            {
                foreach (AttributeData attr in assembly.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrType))
                    {
                        continue;
                    }
                    ImmutableArray<TypedConstant> args = attr.ConstructorArguments;
                    if (args.IsDefaultOrEmpty || args.Length < 2)
                    {
                        continue;
                    }
                    string modelUri = args[0].Value as string;
                    string prefix = args[1].Value as string;
                    if (string.IsNullOrEmpty(modelUri) || string.IsNullOrEmpty(prefix))
                    {
                        continue;
                    }
                    string version = args.Length > 2 ? args[2].Value as string : null;
                    string publicationDate =
                        args.Length > 3 ? args[3].Value as string : null;
                    string name = args.Length > 4 ? args[4].Value as string : null;
                    string payload = args.Length > 5 ? args[5].Value as string : null;
                    results.Add(new ModelDependencyReference(
                        assembly.Identity.Name,
                        modelUri,
                        prefix,
                        version,
                        publicationDate,
                        name,
                        payload));
                }
            }
            return results.ToImmutable();
        }
    }
}

// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Scans referenced assemblies for generated fluent-accessor providers.
    /// </summary>
    internal static class ReferencedFluentAccessorProviderScanner
    {
        public const string AttributeMetadataName =
            "Opc.Ua.ModelFluentAccessorProviderAttribute";

        /// <summary>
        /// Returns every valid accessor-provider declaration.
        /// </summary>
        public static ImmutableArray<ModelFluentAccessorProviderReference> Scan(
            Compilation compilation)
        {
            if (compilation == null)
            {
                return [];
            }
            INamedTypeSymbol attrType = compilation.GetTypeByMetadataName(
                AttributeMetadataName);
            if (attrType == null)
            {
                return [];
            }

            ImmutableArray<ModelFluentAccessorProviderReference>.Builder results =
                ImmutableArray.CreateBuilder<ModelFluentAccessorProviderReference>();
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
                    if (args.Length < 2)
                    {
                        continue;
                    }
                    var reference = new ModelFluentAccessorProviderReference(
                        assembly.Identity.Name,
                        args[0].Value as string ?? string.Empty,
                        args[1].Value as string ?? string.Empty);
                    if (reference.IsValid)
                    {
                        results.Add(reference);
                    }
                }
            }
            return results.ToImmutable();
        }
    }
}

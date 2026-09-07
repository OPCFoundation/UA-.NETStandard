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

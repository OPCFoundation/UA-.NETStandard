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
                    results.Add(new ModelDependencyReference(
                        assembly.Identity.Name,
                        modelUri,
                        prefix,
                        version,
                        publicationDate));
                }
            }
            return results.ToImmutable();
        }
    }
}

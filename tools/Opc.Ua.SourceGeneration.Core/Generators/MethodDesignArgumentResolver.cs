/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Resolves the effective method argument metadata used by generators.
    /// </summary>
    internal static class MethodDesignArgumentResolver
    {
        /// <summary>
        /// Resolves the effective input arguments for a method design.
        /// </summary>
        public static Parameter[] ResolveMethodInputs(MethodDesign method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            return ResolveMethodDefinition(method).InputArguments ?? [];
        }

        /// <summary>
        /// Resolves the effective output arguments for a method design.
        /// </summary>
        public static Parameter[] ResolveMethodOutputs(MethodDesign method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            return ResolveMethodDefinition(method).OutputArguments ?? [];
        }

        /// <summary>
        /// Returns whether a method or one of its declaration/type links defines arguments.
        /// </summary>
        public static bool HasMethodArguments(MethodDesign method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            MethodDesign definition = ResolveMethodDefinition(method);
            return method.HasArguments ||
                definition.HasArguments ||
                HasDeclaredArguments(definition);
        }

        /// <summary>
        /// Returns whether the method directly declares any arguments.
        /// </summary>
        public static bool HasDeclaredArguments(MethodDesign method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            return method.InputArguments is { Length: > 0 } ||
                method.OutputArguments is { Length: > 0 };
        }

        /// <summary>
        /// Resolves the method design that owns the effective method state and
        /// argument metadata.
        /// </summary>
        public static MethodDesign ResolveMethodDefinition(MethodDesign method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            return ResolveMethodDefinition(method, []);
        }

        /// <summary>
        /// Resolves the qualified identity used to name the generated method
        /// state class.
        /// </summary>
        public static XmlQualifiedName ResolveMethodStateIdentity(MethodDesign method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            if (method.TypeDefinition != null && method.MethodType == null)
            {
                return method.TypeDefinition;
            }

            MethodDesign definition = ResolveMethodDefinition(method);
            return definition.TypeDefinition ??
                definition.SymbolicName ??
                definition.SymbolicId ??
                method.SymbolicName ??
                method.SymbolicId;
        }

        /// <summary>
        /// Returns whether two methods have identical effective signatures.
        /// </summary>
        public static bool HaveSameMethodSignature(
            MethodDesign first,
            MethodDesign second)
        {
            if (first == null)
            {
                throw new ArgumentNullException(nameof(first));
            }
            if (second == null)
            {
                throw new ArgumentNullException(nameof(second));
            }

            return HaveSameSignature(
                ResolveMethodInputs(first),
                ResolveMethodOutputs(first),
                ResolveMethodInputs(second),
                ResolveMethodOutputs(second));
        }

        /// <summary>
        /// Returns whether two methods directly declare identical signatures.
        /// </summary>
        public static bool HaveSameDeclaredSignature(
            MethodDesign first,
            MethodDesign second)
        {
            if (first == null)
            {
                throw new ArgumentNullException(nameof(first));
            }
            if (second == null)
            {
                throw new ArgumentNullException(nameof(second));
            }

            return HaveSameSignature(
                first.InputArguments ?? [],
                first.OutputArguments ?? [],
                second.InputArguments ?? [],
                second.OutputArguments ?? []);
        }

        private static MethodDesign ResolveMethodDefinition(
            MethodDesign method,
            List<MethodDesign> visited)
        {
            if (ContainsReference(visited, method))
            {
                return method;
            }
            visited.Add(method);

            bool hasDeclaredArguments = HasDeclaredArguments(method);
            if (method.MethodType != null &&
                DelegatesTo(method, method.MethodType, hasDeclaredArguments))
            {
                return ResolveMethodDefinition(method.MethodType, visited);
            }

            if (method.MethodDeclarationNode != null &&
                !ReferenceEquals(method.MethodDeclarationNode, method) &&
                DelegatesTo(method, method.MethodDeclarationNode, hasDeclaredArguments))
            {
                return ResolveMethodDefinition(method.MethodDeclarationNode, visited);
            }

            return method;
        }

        /// <summary>
        /// Returns whether <paramref name="method"/> takes its signature from
        /// <paramref name="target"/>, which is the case when it declares no
        /// arguments of its own or declares exactly the target's effective
        /// signature.
        /// </summary>
        /// <remarks>
        /// The target is resolved once here rather than through
        /// <see cref="ResolveMethodInputs"/> and
        /// <see cref="ResolveMethodOutputs"/>, which would each walk the target
        /// chain again from scratch with their own visited list. The resolution
        /// is deliberately not cached across calls: the model is still being
        /// mutated while it is validated, and a generator that carried state
        /// between passes would stop being deterministic.
        /// </remarks>
        private static bool DelegatesTo(
            MethodDesign method,
            MethodDesign target,
            bool hasDeclaredArguments)
        {
            if (!hasDeclaredArguments)
            {
                return true;
            }

            MethodDesign targetDefinition = ResolveMethodDefinition(target, []);
            return HaveSameSignature(
                method.InputArguments ?? [],
                method.OutputArguments ?? [],
                targetDefinition.InputArguments ?? [],
                targetDefinition.OutputArguments ?? []);
        }

        private static bool HaveSameSignature(
            Parameter[] firstInputs,
            Parameter[] firstOutputs,
            Parameter[] secondInputs,
            Parameter[] secondOutputs)
        {
            return HaveSameParameters(firstInputs, secondInputs) &&
                HaveSameParameters(firstOutputs, secondOutputs);
        }

        private static bool HaveSameParameters(
            Parameter[] first,
            Parameter[] second)
        {
            if (first.Length != second.Length)
            {
                return false;
            }

            for (int ii = 0; ii < first.Length; ii++)
            {
                Parameter left = first[ii];
                Parameter right = second[ii];
                if (left == null || right == null)
                {
                    if (!ReferenceEquals(left, right))
                    {
                        return false;
                    }
                    continue;
                }

                if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal) ||
                    !XmlQualifiedNameEquals(left.DataType, right.DataType) ||
                    left.ValueRank != right.ValueRank ||
                    !string.Equals(
                        left.ArrayDimensions,
                        right.ArrayDimensions,
                        StringComparison.Ordinal) ||
                    left.AllowSubTypes != right.AllowSubTypes ||
                    left.IsOptional != right.IsOptional)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool XmlQualifiedNameEquals(
            XmlQualifiedName first,
            XmlQualifiedName second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }
            if (first == null || second == null)
            {
                return false;
            }
            return string.Equals(first.Name, second.Name, StringComparison.Ordinal) &&
                string.Equals(first.Namespace, second.Namespace, StringComparison.Ordinal);
        }

        private static bool ContainsReference(
            List<MethodDesign> methods,
            MethodDesign candidate)
        {
            foreach (MethodDesign method in methods)
            {
                if (ReferenceEquals(method, candidate))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

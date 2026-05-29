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

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Opc.Ua.CodeFixers.Helpers
{
    /// <summary>
    /// Shared helpers for symbol-shape queries used by multiple analyzers.
    /// Lifted out of individual analyzers to keep detection patterns consistent.
    /// </summary>
    internal static class SymbolExtensions
    {
        /// <summary>
        /// True iff <paramref name="symbol"/> carries an <c>[Obsolete]</c> attribute.
        /// </summary>
        public static bool IsObsolete(this ISymbol symbol)
        {
            if (symbol is null)
            {
                return false;
            }
            foreach (AttributeData attr in symbol.GetAttributes())
            {
                INamedTypeSymbol cls = attr.AttributeClass;
                if (cls != null && cls.ToDisplayString() == "System.ObsoleteAttribute")
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True iff <paramref name="symbol"/> carries
        /// <c>[Opc.Ua.OpcUaShimAttribute(ruleId)]</c> for the given
        /// <paramref name="ruleId"/>. For extension methods reduced from a
        /// static declaration, also inspects <see cref="IMethodSymbol.ReducedFrom"/>
        /// since the attribute lives on the original declaration.
        /// </summary>
        public static bool IsOpcUaShim(this ISymbol symbol, string ruleId)
        {
            if (symbol is null)
            {
                return false;
            }
            if (HasShimAttribute(symbol, ruleId))
            {
                return true;
            }
            if (symbol is IMethodSymbol method && method.ReducedFrom != null)
            {
                return HasShimAttribute(method.ReducedFrom, ruleId);
            }
            return false;
        }

        private static bool HasShimAttribute(ISymbol symbol, string ruleId)
        {
            foreach (AttributeData attr in symbol.GetAttributes())
            {
                INamedTypeSymbol cls = attr.AttributeClass;
                if (cls is null || cls.ToDisplayString() != "Opc.Ua.OpcUaShimAttribute")
                {
                    continue;
                }
                if (attr.ConstructorArguments.Length == 1 &&
                    attr.ConstructorArguments[0].Value is string id &&
                    id == ruleId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True iff <paramref name="member"/> is declared (directly or via override)
        /// on the named type referenced by <paramref name="declaringTypeFullName"/>.
        /// </summary>
        public static bool IsDeclaredOn(this ISymbol member, string declaringTypeFullName)
        {
            INamedTypeSymbol declaring = member?.ContainingType;
            while (declaring != null)
            {
                if (declaring.ToDisplayString() == declaringTypeFullName)
                {
                    return true;
                }
                declaring = declaring.BaseType;
            }
            return false;
        }

        /// <summary>
        /// True iff <paramref name="type"/> is assignable to the named target type
        /// (walks <see cref="ITypeSymbol.BaseType"/> and <see cref="ITypeSymbol.AllInterfaces"/>).
        /// </summary>
        public static bool IsAssignableTo(this ITypeSymbol type, ITypeSymbol target)
        {
            if (type is null || target is null)
            {
                return false;
            }

            SymbolEqualityComparer eq = SymbolEqualityComparer.Default;
            ITypeSymbol current = type;
            while (current != null)
            {
                if (eq.Equals(current, target))
                {
                    return true;
                }
                current = current.BaseType;
            }
            foreach (INamedTypeSymbol iface in type.AllInterfaces)
            {
                if (eq.Equals(iface, target))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Closed list of removed <c>{Type}Collection</c> wrappers that 2.0 dropped
        /// in favour of <c>List&lt;T&gt;</c> / <c>ArrayOf&lt;T&gt;</c>. The element
        /// type name is the second tuple item.
        /// </summary>
        public static IReadOnlyList<(string CollectionName, string ElementName)> RemovedCollectionTypes { get; } =
            new[]
            {
                ("Opc.Ua.BooleanCollection", "bool"),
                ("Opc.Ua.SByteCollection", "sbyte"),
                ("Opc.Ua.ByteCollection", "byte"),
                ("Opc.Ua.Int16Collection", "short"),
                ("Opc.Ua.UInt16Collection", "ushort"),
                ("Opc.Ua.Int32Collection", "int"),
                ("Opc.Ua.UInt32Collection", "uint"),
                ("Opc.Ua.Int64Collection", "long"),
                ("Opc.Ua.UInt64Collection", "ulong"),
                ("Opc.Ua.FloatCollection", "float"),
                ("Opc.Ua.DoubleCollection", "double"),
                ("Opc.Ua.StringCollection", "string"),
                ("Opc.Ua.DateTimeCollection", "Opc.Ua.DateTimeUtc"),
                ("Opc.Ua.GuidCollection", "Opc.Ua.Uuid"),
                ("Opc.Ua.ByteStringCollection", "Opc.Ua.ByteString"),
                ("Opc.Ua.XmlElementCollection", "System.Xml.XmlElement"),
                ("Opc.Ua.NodeIdCollection", "Opc.Ua.NodeId"),
                ("Opc.Ua.ExpandedNodeIdCollection", "Opc.Ua.ExpandedNodeId"),
                ("Opc.Ua.QualifiedNameCollection", "Opc.Ua.QualifiedName"),
                ("Opc.Ua.LocalizedTextCollection", "Opc.Ua.LocalizedText"),
                ("Opc.Ua.StatusCodeCollection", "Opc.Ua.StatusCode"),
                ("Opc.Ua.VariantCollection", "Opc.Ua.Variant"),
                ("Opc.Ua.DiagnosticInfoCollection", "Opc.Ua.DiagnosticInfo"),
                ("Opc.Ua.DataValueCollection", "Opc.Ua.DataValue"),
                ("Opc.Ua.ExtensionObjectCollection", "Opc.Ua.ExtensionObject"),
                ("Opc.Ua.ArgumentCollection", "Opc.Ua.Argument"),
                ("Opc.Ua.ServerSecurityPolicyCollection", "Opc.Ua.ServerSecurityPolicy"),
                ("Opc.Ua.TransportConfigurationCollection", "Opc.Ua.TransportConfiguration"),
                ("Opc.Ua.ReverseConnectClientCollection", "Opc.Ua.ReverseConnectClient"),
            };

        /// <summary>
        /// Convenience: returns the element type metadata name if
        /// <paramref name="type"/> is one of the removed collection wrappers.
        /// </summary>
        public static bool TryGetRemovedCollectionElement(this ITypeSymbol type, out string elementName)
        {
            elementName = null;
            if (type is null)
            {
                return false;
            }
            string typeFullName = type.ToDisplayString();
            foreach ((string collection, string element) in RemovedCollectionTypes)
            {
                if (collection == typeFullName)
                {
                    elementName = element;
                    return true;
                }
            }
            return false;
        }
    }
}

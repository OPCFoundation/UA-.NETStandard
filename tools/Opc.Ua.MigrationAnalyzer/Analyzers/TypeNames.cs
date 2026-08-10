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

using Microsoft.CodeAnalysis;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// Name-based type tests shared by the analyzers that run on the migration path.
    /// </summary>
    /// <remarks>
    /// Deliberately matches on simple names rather than fully qualified symbols: the
    /// sources these rules run against still reference the 1.5.378 assemblies, which the
    /// analyzer package cannot assume are resolvable, and a consumer may compile against a
    /// hand-written shim of the same shape.
    /// </remarks>
    internal static class TypeNames
    {
        /// <summary>
        /// True when the type is <c>ILocalNode</c>, implements it, or derives from the
        /// <c>Node</c> class that implemented it.
        /// </summary>
        public static bool IsLocalNode(ITypeSymbol type)
        {
            return IsOrImplements(type, "ILocalNode") || DerivesFrom(type, "Node");
        }

        /// <summary>
        /// True when the type is <c>BaseVariableValue</c> or derives from it, which every
        /// generated <c>&lt;Type&gt;Value</c> class does.
        /// </summary>
        public static bool IsBaseVariableValue(ITypeSymbol type)
        {
            return DerivesFrom(type, "BaseVariableValue");
        }

        /// <summary>
        /// True when the type is the named interface or implements it.
        /// </summary>
        public static bool IsOrImplements(ITypeSymbol type, string interfaceName)
        {
            if (type.Name == interfaceName)
            {
                return true;
            }

            foreach (INamedTypeSymbol implemented in type.AllInterfaces)
            {
                if (implemented.Name == interfaceName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the type is the named class or has it somewhere in its base chain.
        /// </summary>
        public static bool DerivesFrom(ITypeSymbol type, string className)
        {
            for (ITypeSymbol? current = type; current != null; current = current.BaseType)
            {
                if (current.Name == className)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

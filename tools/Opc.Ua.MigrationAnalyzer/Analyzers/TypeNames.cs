// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

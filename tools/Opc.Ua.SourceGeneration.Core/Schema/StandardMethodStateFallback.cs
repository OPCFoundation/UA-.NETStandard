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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Ambient, thread-scoped policy that lets the companion-model generator
    /// degrade a reference to a standard (<c>global::Opc.Ua.*</c>) typed
    /// <c>MethodState</c> class to the base <c>global::Opc.Ua.MethodState</c>
    /// when that typed class is neither present in the compilation (a curated
    /// <c>Opc.Ua.Core</c> may omit standard classes such as the
    /// <c>FileDirectoryType</c> method states) nor declared by the current
    /// generation pass.
    /// </summary>
    /// <remarks>
    /// The scope is only entered around <b>model</b> generation
    /// (<c>ModelCompilation</c>); the Stack generator that builds
    /// <c>Opc.Ua.Core</c> itself never enters it, so it keeps the
    /// assume-present behaviour. A method-state class is recorded as it is
    /// <i>declared</i> (the <c>NodeStates.g.cs</c> pass, which runs before the
    /// <c>NodeStates.ex.g.cs</c> reference pass on the same thread), so a
    /// model that generates its own standard-namespace method-state classes
    /// (e.g. GDS) is never wrongly degraded.
    /// </remarks>
    internal static class StandardMethodStateFallback
    {
        [ThreadStatic]
        private static bool s_active;

        [ThreadStatic]
        private static HashSet<string> s_available;

        [ThreadStatic]
        private static HashSet<string> s_declared;

        /// <summary>
        /// Enter a fallback scope for the duration of one model compilation.
        /// The previous scope (if any) is restored on dispose so nested or
        /// sequential passes on the same thread do not leak state.
        /// </summary>
        /// <param name="availableStateTypeNames">
        /// Simple names of the standard <c>Opc.Ua.*State</c> classes present
        /// in the compilation (including referenced assemblies).
        /// </param>
        public static IDisposable Enter(IEnumerable<string> availableStateTypeNames)
        {
            var restore = new Scope(s_active, s_available, s_declared);
            s_active = true;
            s_available = availableStateTypeNames == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(availableStateTypeNames, StringComparer.Ordinal);
            s_declared = new HashSet<string>(StringComparer.Ordinal);
            return restore;
        }

        /// <summary>
        /// Record that the current pass declares the given method-state class.
        /// Only classes that resolve to the standard <c>global::Opc.Ua.*</c>
        /// namespace (with no further sub-namespace, e.g. a model such as GDS
        /// that emits its own <c>Opc.Ua.*MethodState</c>) are tracked, so a
        /// later reference to the same standard class is not degraded.
        /// </summary>
        /// <param name="typedClassName">
        /// The fully-qualified typed class name, e.g.
        /// <c>global::Opc.Ua.RequestAccessTokenMethodState</c>.
        /// </param>
        public static void RecordDeclaredMethodState(string typedClassName)
        {
            if (s_active && TryGetStandardSimpleName(typedClassName, out string simpleName))
            {
                s_declared?.Add(simpleName);
            }
        }

        /// <summary>
        /// Whether the given fully-qualified typed method-state reference
        /// should be degraded to the base <c>global::Opc.Ua.MethodState</c>.
        /// True only when a scope is active, the reference names a standard
        /// <c>global::Opc.Ua.*MethodState</c> class (no sub-namespace), and
        /// that class is neither declared by this pass nor present in the
        /// compilation.
        /// </summary>
        public static bool ShouldFallBackToBase(string typedClassName)
        {
            if (!s_active)
            {
                return false;
            }
            if (!TryGetStandardSimpleName(typedClassName, out string simpleName))
            {
                return false;
            }
            if (s_declared != null && s_declared.Contains(simpleName))
            {
                return false;
            }
            if (s_available != null && s_available.Contains(simpleName))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Extract the simple class name when <paramref name="typedClassName"/>
        /// is a reference to a type directly in the standard <c>Opc.Ua</c>
        /// namespace (i.e. <c>global::Opc.Ua.Name</c> with no further dotted
        /// sub-namespace such as <c>global::Opc.Ua.Di.Name</c>). An optional
        /// leading <c>new </c> (factory form) is tolerated.
        /// </summary>
        private static bool TryGetStandardSimpleName(string typedClassName, out string simpleName)
        {
            simpleName = null;
            if (string.IsNullOrEmpty(typedClassName))
            {
                return false;
            }
            const string factoryPrefix = "new ";
            const string standardPrefix = "global::Opc.Ua.";
            string candidate = typedClassName.StartsWith(factoryPrefix, StringComparison.Ordinal)
                ? typedClassName.Substring(factoryPrefix.Length)
                : typedClassName;
            if (!candidate.StartsWith(standardPrefix, StringComparison.Ordinal))
            {
                return false;
            }
            string remainder = candidate.Substring(standardPrefix.Length);
            if (remainder.Length == 0 || remainder.Contains('.', StringComparison.Ordinal))
            {
                return false;
            }
            simpleName = remainder;
            return true;
        }

        private sealed class Scope : IDisposable
        {
            public Scope(bool active, HashSet<string> available, HashSet<string> declared)
            {
                m_active = active;
                m_available = available;
                m_declared = declared;
            }

            public void Dispose()
            {
                s_active = m_active;
                s_available = m_available;
                s_declared = m_declared;
            }

            private readonly bool m_active;
            private readonly HashSet<string> m_available;
            private readonly HashSet<string> m_declared;
        }
    }
}

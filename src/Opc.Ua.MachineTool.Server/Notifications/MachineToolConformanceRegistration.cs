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

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Opc.Ua.Di.Server;
using Opc.Ua.Server;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Adds one weak conformance contribution to the existing owner; it never owns machine resources.
    /// Removed runtimes and their equipment providers are not kept alive by the manager's contributor list.
    /// </summary>
    internal sealed class MachineToolConformanceRegistration : IConformanceContributor
    {
        /// <summary>
        /// Creates one contribution for a namespace owner.
        /// </summary>
        private MachineToolConformanceRegistration()
        {
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var result = new HashSet<QualifiedName>();
                foreach (IConformanceContributor contribution in Contributions())
                {
                    foreach (QualifiedName unit in contribution.ConformanceUnits)
                    {
                        result.Add(unit);
                    }
                }
                return new List<QualifiedName>(result).ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles
        {
            get
            {
                var result = new HashSet<string>(StringComparer.Ordinal);
                foreach (IConformanceContributor contribution in Contributions())
                {
                    foreach (string profile in contribution.ServerProfiles)
                    {
                        result.Add(profile);
                    }
                }
                return new List<string>(result).ToArrayOf();
            }
        }

        /// <summary>
        /// Registers only after the machine's existing registration transaction has published its actual graph.
        /// </summary>
        internal static void Register(DiNodeManager manager, IConformanceContributor runtime)
        {
            MachineToolConformanceRegistration registration = s_registrations.GetValue(
                manager, static _ => new MachineToolConformanceRegistration());
            lock (registration.m_gate)
            {
                registration.m_runtimes.RemoveAll(static weak =>
                    !weak.TryGetTarget(out IConformanceContributor? value) || value.ConformanceUnits.Count == 0);
                registration.m_runtimes.Add(new WeakReference<IConformanceContributor>(runtime));
            }
            manager.RegisterConformanceContributor(registration);
        }

        /// <summary>
        /// Copies live contributors without calling the namespace owner recursively.
        /// </summary>
        private List<IConformanceContributor> Contributions()
        {
            var values = new List<IConformanceContributor>();
            lock (m_gate)
            {
                for (int index = m_runtimes.Count - 1; index >= 0; index--)
                {
                    if (m_runtimes[index].TryGetTarget(out IConformanceContributor? value) &&
                        value.ConformanceUnits.Count != 0)
                    {
                        values.Add(value);
                    }
                    else
                    {
                        m_runtimes.RemoveAt(index);
                    }
                }
            }
            return values;
        }

        /// <summary>One contributor per existing shared owner, not another node manager.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, MachineToolConformanceRegistration>
            s_registrations =
#if NET8_0_OR_GREATER
            [];
#else
            new();
#endif

        /// <summary>Protects the short weak-reference registry updates.</summary>
        private readonly Lock m_gate = new();

        /// <summary>Conformance references only; Machinery registrations own runtime resources.</summary>
        private readonly List<WeakReference<IConformanceContributor>> m_runtimes = [];
    }
}

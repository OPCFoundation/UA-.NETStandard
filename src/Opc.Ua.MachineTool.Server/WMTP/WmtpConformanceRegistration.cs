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
    /// Aggregates already computed per-peripheral capabilities on the existing owner without retaining providers.
    /// </summary>
    internal sealed class WmtpConformanceRegistration : IConformanceContributor
    {
        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var units = new HashSet<QualifiedName>();
                foreach (WmtpPeripheral peripheral in ActivePeripherals())
                {
                    foreach (QualifiedName unit in peripheral.ConformanceUnits)
                    {
                        units.Add(unit);
                    }
                }
                return new List<QualifiedName>(units).ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles
        {
            get
            {
                var profiles = new HashSet<string>(StringComparer.Ordinal);
                foreach (WmtpPeripheral peripheral in ActivePeripherals())
                {
                    foreach (string profile in peripheral.ServerProfiles)
                    {
                        profiles.Add(profile);
                    }
                }
                return new List<string>(profiles).ToArrayOf();
            }
        }

        /// <summary>
        /// Adds a weak contribution after publication, with one registration per owner across every WMTP instance.
        /// </summary>
        internal static void Register(DiNodeManager manager, WmtpPeripheral peripheral)
        {
            WmtpConformanceRegistration registration = s_registrations.GetValue(
                manager, static _ => new WmtpConformanceRegistration());
            lock (registration.m_gate)
            {
                registration.m_peripherals.RemoveAll(static weak => !weak.TryGetTarget(out _));
                registration.m_peripherals.Add(new WeakReference<WmtpPeripheral>(peripheral));
            }
            manager.RegisterConformanceContributor(registration);
        }

        /// <summary>
        /// Copies live weak targets before asking them for capabilities, avoiding callbacks under the registry gate.
        /// </summary>
        private List<WmtpPeripheral> ActivePeripherals()
        {
            WeakReference<WmtpPeripheral>[] snapshot;
            lock (m_gate)
            {
                snapshot = [.. m_peripherals];
            }
            var result = new List<WmtpPeripheral>();
            var inactive = new List<WeakReference<WmtpPeripheral>>();
            foreach (WeakReference<WmtpPeripheral> weak in snapshot)
            {
                if (weak.TryGetTarget(out WmtpPeripheral? peripheral) && peripheral.IsConformanceActive)
                {
                    result.Add(peripheral);
                }
                else
                {
                    inactive.Add(weak);
                }
            }
            lock (m_gate)
            {
                foreach (WeakReference<WmtpPeripheral> weak in inactive)
                {
                    m_peripherals.Remove(weak);
                }
            }
            return result;
        }

        /// <summary>One weak runtime registry for each existing DI/Machinery owner.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, WmtpConformanceRegistration> s_registrations =
            new();

        /// <summary>The short weak-reference registry gate, never held while inspecting runtime nodes.</summary>
        private readonly Lock m_gate = new();

        /// <summary>Weak references; resource ownership stays in the Machinery registration lifetime.</summary>
        private readonly List<WeakReference<WmtpPeripheral>> m_peripherals = [];
    }
}

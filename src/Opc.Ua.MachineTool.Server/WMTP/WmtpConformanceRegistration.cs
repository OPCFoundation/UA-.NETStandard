// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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

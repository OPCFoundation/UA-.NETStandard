// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server;

[assembly: CLSCompliant(false)]

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Builds crane systems by composing the existing Machinery and Robotics owners.
    /// </summary>
    public sealed class CranesHoistsBuildContext : IConformanceContributor
    {
        /// <summary>
        /// Creates a direct construction path over the same manager and application namespace.
        /// </summary>
        public CranesHoistsBuildContext(
            IRoboticsBuildContext robotics,
            IMachineryBuildContext machinery,
            TimeProvider? timeProvider = null)
        {
            Robotics = robotics ?? throw new ArgumentNullException(nameof(robotics));
            Machinery = machinery ?? throw new ArgumentNullException(nameof(machinery));
            if (!ReferenceEquals(robotics.Manager, machinery.Manager) ||
                robotics.InstanceNamespaceIndex != machinery.InstanceNamespaceIndex)
            {
                throw new ArgumentException("Both contexts must use the same manager and application namespace.");
            }
            if (!machinery.Parts.HasFlag(MachineryParts.BuildingBlocks))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Crane systems require Machinery building blocks.");
            }
            _ = CraneSignals.Index(robotics.Context.NamespaceUris, CranesHoistsModel.NamespaceUri);
            TimeProvider = timeProvider ?? TimeProvider.System;
            robotics.Manager.RegisterConformanceContributor(this);
        }

        /// <summary>
        /// Gets the shared Robotics construction context.
        /// </summary>
        public IRoboticsBuildContext Robotics { get; }

        /// <summary>
        /// Gets the shared Machinery binding context.
        /// </summary>
        public IMachineryBuildContext Machinery { get; }

        /// <summary>
        /// Gets the injectable clock used for accepted request timestamps.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var units = new HashSet<QualifiedName>();
                foreach (CraneSystemHandle handle in ActiveHandles())
                {
                    foreach (QualifiedName unit in handle.ConformanceUnits)
                    {
                        units.Add(unit);
                    }
                }
                return new List<QualifiedName>(units).ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles => ActiveHandles().Count == 0
            ? []
            : ["http://opcfoundation.org/UA-Profile/CranesHoists/Server/Basic"];

        /// <summary>
        /// Starts an independently provided crane system with mandatory-only defaults.
        /// </summary>
        public CraneSystemBuilder AddCraneSystem(string name)
        {
            return new CraneSystemBuilder(this, name);
        }

        /// <summary>
        /// Adds only committed systems to this owner's conformance contribution.
        /// </summary>
        internal void Commit(CraneSystemHandle handle)
        {
            lock (m_gate)
            {
                m_handles.Add(new WeakReference<CraneSystemHandle>(handle));
            }
        }

        /// <summary>
        /// Prunes removed instances without a process-wide registry or retained equipment providers.
        /// </summary>
        private List<CraneSystemHandle> ActiveHandles()
        {
            var result = new List<CraneSystemHandle>();
            lock (m_gate)
            {
                for (int i = m_handles.Count - 1; i >= 0; i--)
                {
                    if (m_handles[i].TryGetTarget(out CraneSystemHandle? handle) && !handle.IsDisposed)
                    {
                        result.Add(handle);
                    }
                    else
                    {
                        m_handles.RemoveAt(i);
                    }
                }
            }
            return result;
        }

        private readonly Lock m_gate = new();
        private readonly List<WeakReference<CraneSystemHandle>> m_handles = [];
    }
}

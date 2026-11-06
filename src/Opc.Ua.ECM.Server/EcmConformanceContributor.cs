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
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Di.Server;
using Opc.Ua.Server;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Registers an ECM binding with its DI address-space owner's conformance report.
    /// </summary>
    public static class EcmConformanceExtensions
    {
        /// <summary>
        /// Registers a standby binding and its configured standby or WOL capabilities.
        /// </summary>
        /// <param name="manager">The DI address-space owner.</param>
        /// <param name="binding">The published provider binding.</param>
        public static void RegisterEcmBinding(this DiNodeManager manager, EcmPowerBinding binding)
        {
            manager.ThrowIfNull(nameof(manager));
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.Register(binding);
            contributor.SetServer(binding.Server);
        }

        /// <summary>
        /// Registers a standby binding and publishes updated server capabilities.
        /// </summary>
        /// <param name="manager">The DI address-space owner.</param>
        /// <param name="binding">The published provider binding.</param>
        /// <param name="cancellationToken">Cancels capability publication.</param>
        public static ValueTask RegisterEcmBindingAsync(
            this DiNodeManager manager,
            EcmPowerBinding binding,
            CancellationToken cancellationToken = default)
        {
            manager.ThrowIfNull(nameof(manager));
            binding.ThrowIfNull(nameof(binding));
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.SetServer(binding.Server);
            contributor.EnsureServerAvailable();
            contributor.Register(binding);
            return contributor.PublishAsync(binding, cancellationToken);
        }

        /// <summary>
        /// Registers a standalone WOL binding with its DI address-space owner.
        /// </summary>
        /// <param name="manager">The DI address-space owner.</param>
        /// <param name="binding">The published provider binding.</param>
        public static void RegisterEcmBinding(this DiNodeManager manager, EcmWolBinding binding)
        {
            manager.ThrowIfNull(nameof(manager));
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.Register(binding);
            contributor.SetServer(binding.Server);
        }

        /// <summary>
        /// Registers a standalone WOL binding and publishes updated server capabilities.
        /// </summary>
        /// <param name="manager">The DI address-space owner.</param>
        /// <param name="binding">The published provider binding.</param>
        /// <param name="cancellationToken">Cancels capability publication.</param>
        public static ValueTask RegisterEcmBindingAsync(
            this DiNodeManager manager,
            EcmWolBinding binding,
            CancellationToken cancellationToken = default)
        {
            manager.ThrowIfNull(nameof(manager));
            binding.ThrowIfNull(nameof(binding));
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.SetServer(binding.Server);
            contributor.EnsureServerAvailable();
            contributor.Register(binding);
            return contributor.PublishAsync(binding, cancellationToken);
        }

        /// <summary>
        /// Registers a standalone measurement binding and its complete EnergyProfiles.
        /// </summary>
        /// <param name="manager">The DI address-space owner.</param>
        /// <param name="binding">The published provider binding.</param>
        public static void RegisterEcmBinding(this DiNodeManager manager, EcmMeasurementBinding binding)
        {
            manager.ThrowIfNull(nameof(manager));
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.Register(binding);
            contributor.SetServer(binding.Server);
        }

        /// <summary>
        /// Registers a measurement binding and publishes updated server capabilities.
        /// </summary>
        /// <param name="manager">The DI address-space owner.</param>
        /// <param name="binding">The published provider binding.</param>
        /// <param name="cancellationToken">Cancels capability publication.</param>
        public static ValueTask RegisterEcmBindingAsync(
            this DiNodeManager manager,
            EcmMeasurementBinding binding,
            CancellationToken cancellationToken = default)
        {
            manager.ThrowIfNull(nameof(manager));
            binding.ThrowIfNull(nameof(binding));
            EcmConformanceContributor contributor = EcmConformanceContributor.For(manager);
            contributor.SetServer(binding.Server);
            contributor.EnsureServerAvailable();
            contributor.Register(binding);
            return contributor.PublishAsync(binding, cancellationToken);
        }

        /// <summary>
        /// Republishes the current ECM capability report after a registered binding changes or is disposed.
        /// </summary>
        /// <param name="manager">The DI address-space owner.</param>
        /// <param name="cancellationToken">Cancels capability publication.</param>
        public static ValueTask PublishEcmCapabilitiesAsync(
            this DiNodeManager manager,
            CancellationToken cancellationToken = default)
        {
            manager.ThrowIfNull(nameof(manager));
            return EcmConformanceContributor.For(manager).PublishAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Reports ECM units and profiles supplied by the active provider bindings.
    /// </summary>
    internal sealed class EcmConformanceContributor : IConformanceContributor
    {
        /// <summary>
        /// Creates a contributor for one server.
        /// </summary>
        /// <param name="server">The owning OPC UA server, when available.</param>
        private EcmConformanceContributor(IServerInternal? server)
        {
            m_server = server;
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                CapabilitySnapshot snapshot = GetSnapshot();
                var units = new List<QualifiedName>
                {
                    Unit(ConformanceUnitNames.EnergyMeasurementCommonDataTypes),
                    Unit(ConformanceUnitNames.AccuracyDomainPercentFullScale),
                    Unit(ConformanceUnitNames.AccuracyDomainPercentActualReading),
                    Unit(ConformanceUnitNames.AccuracyDomainIec),
                    Unit(ConformanceUnitNames.AccuracyDomainEn)
                };
                if (snapshot.HasStandby)
                {
                    AddUnit(units, ConformanceUnitNames.StandbyManagement);
                    AddUnit(units, ConformanceUnitNames.StandbyManagementControl);
                }
                if (snapshot.HasWol)
                {
                    AddUnit(units, ConformanceUnitNames.SleepModeWol);
                }
                if (snapshot.HasMeasurement)
                {
                    AddUnit(units, ConformanceUnitNames.EnergyMeasurement);
                    foreach (string profile in snapshot.MeasurementProfiles)
                    {
                        AddUnit(units, $"ECM EnergyProfile {profile}");
                    }
                }
                if (snapshot.HasStandby || snapshot.HasWol || snapshot.HasMeasurement)
                {
                    AddUnit(units, ConformanceUnitNames.EnergyManagementSupport);
                }
                return units.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles
        {
            get
            {
                CapabilitySnapshot snapshot = GetSnapshot();
                if (!snapshot.HasStandby && !snapshot.HasWol && !snapshot.HasMeasurement)
                {
                    return Array.Empty<string>().ToArrayOf();
                }
                var profiles = new List<string>
                {
                    ProfileUris.NanoEmbeddedDevice2017,
                    ProfileUris.DataAccess,
                    ProfileUris.ComplexTypes2017,
                    ProfileUris.EnergyManagement
                };
                if (snapshot.HasStandby && snapshot.HasLocking)
                {
                    profiles.Add(ProfileUris.Locking);
                    profiles.Add(ProfileUris.EnergyController);
                    profiles.Add(ProfileUris.Embedded2017);
                    profiles.Add(ProfileUris.EnergyManagementControl);
                }
                return profiles.ToArrayOf();
            }
        }

        /// <summary>
        /// Gets the ECM conformance contributor registered for <paramref name="manager"/>.
        /// </summary>
        /// <param name="manager">The DI address-space owner.</param>
        /// <param name="server">The optional server receiving capability updates.</param>
        internal static EcmConformanceContributor For(DiNodeManager manager, IServerInternal? server = null)
        {
            manager.ThrowIfNull(nameof(manager));
            lock (s_gate)
            {
                if (s_contributors.TryGetValue(manager, out EcmConformanceContributor? existing))
                {
                    existing.SetServer(server);
                    return existing;
                }
                var contributor = new EcmConformanceContributor(server);
                s_contributors.Add(manager, contributor);
                try
                {
                    manager.RegisterConformanceContributor(contributor);
                    return contributor;
                }
                catch
                {
                    s_contributors.Remove(manager);
                    throw;
                }
            }
        }

        /// <summary>
        /// Adds one standby binding to the live capability set.
        /// </summary>
        /// <param name="binding">The published provider binding.</param>
        internal void Register(EcmPowerBinding binding)
        {
            binding.ThrowIfNull(nameof(binding));
            lock (m_gate)
            {
                if (!m_powerBindings.Contains(binding))
                {
                    m_powerBindings.Add(binding);
                }
            }
        }

        /// <summary>
        /// Adds one standalone WOL binding to the live capability set.
        /// </summary>
        /// <param name="binding">The published provider binding.</param>
        internal void Register(EcmWolBinding binding)
        {
            binding.ThrowIfNull(nameof(binding));
            lock (m_gate)
            {
                if (!m_wolBindings.Contains(binding))
                {
                    m_wolBindings.Add(binding);
                }
            }
        }

        /// <summary>
        /// Adds one measurement binding to the live capability set.
        /// </summary>
        /// <param name="binding">The published provider binding.</param>
        internal void Register(EcmMeasurementBinding binding)
        {
            binding.ThrowIfNull(nameof(binding));
            lock (m_gate)
            {
                if (!m_measurementBindings.Contains(binding))
                {
                    m_measurementBindings.Add(binding);
                }
            }
        }

        /// <summary>
        /// Publishes new server capabilities when the server has completed startup.
        /// </summary>
        /// <param name="binding">The binding that supplies the server context.</param>
        /// <param name="cancellationToken">Cancels capability publication.</param>
        internal ValueTask PublishAsync(EcmPowerBinding binding, CancellationToken cancellationToken)
        {
            binding.ThrowIfNull(nameof(binding));
            SetServer(binding.Server);
            return PublishAsync(cancellationToken);
        }

        /// <summary>
        /// Publishes new server capabilities when the server has completed startup.
        /// </summary>
        /// <param name="binding">The binding that supplies the server context.</param>
        /// <param name="cancellationToken">Cancels capability publication.</param>
        internal ValueTask PublishAsync(EcmWolBinding binding, CancellationToken cancellationToken)
        {
            binding.ThrowIfNull(nameof(binding));
            SetServer(binding.Server);
            return PublishAsync(cancellationToken);
        }

        /// <summary>
        /// Publishes new server capabilities when the server has completed startup.
        /// </summary>
        /// <param name="binding">The binding that supplies the server context.</param>
        /// <param name="cancellationToken">Cancels capability publication.</param>
        internal ValueTask PublishAsync(EcmMeasurementBinding binding, CancellationToken cancellationToken)
        {
            binding.ThrowIfNull(nameof(binding));
            SetServer(binding.Server);
            return PublishAsync(cancellationToken);
        }

        /// <summary>
        /// Retains a server reference supplied by a binding or node manager.
        /// </summary>
        /// <param name="server">The owning OPC UA server.</param>
        internal void SetServer(IServerInternal? server)
        {
            if (server != null)
            {
                lock (m_gate)
                {
                    m_server = server;
                }
            }
        }

        /// <summary>
        /// Ensures a server context is available before accepting a request to publish capability changes.
        /// </summary>
        internal void EnsureServerAvailable()
        {
            lock (m_gate)
            {
                if (m_server is not IConformanceUnitsManagerProvider)
                {
                    throw new InvalidOperationException(
                        "Runtime ECM capability publication requires an IConformanceUnitsManagerProvider.");
                }
            }
        }

        /// <summary>
        /// Publishes the current report after server startup has created its conformance manager.
        /// </summary>
        /// <param name="cancellationToken">Cancels capability publication.</param>
        internal ValueTask PublishAsync(CancellationToken cancellationToken)
        {
            IServerInternal? server;
            lock (m_gate)
            {
                server = m_server;
            }
            if (server is not IConformanceUnitsManagerProvider
                { ConformanceUnitsManager: { } manager })
            {
                return default;
            }
            return manager.PublishAsync(cancellationToken);
        }

        /// <summary>
        /// Copies active binding state before evaluating advertised units.
        /// </summary>
        private CapabilitySnapshot GetSnapshot()
        {
            lock (m_gate)
            {
                var profiles = new List<string>();
                bool hasStandby = false;
                bool hasWol = false;
                bool hasLocking = false;
                foreach (EcmPowerBinding binding in m_powerBindings)
                {
                    if (binding.IsDisposed)
                    {
                        continue;
                    }
                    hasStandby = true;
                    hasWol |= binding.SupportsWol;
                    hasLocking |= binding.SupportsLocking;
                }
                foreach (EcmWolBinding binding in m_wolBindings)
                {
                    hasWol |= !binding.IsDisposed;
                }
                foreach (EcmMeasurementBinding binding in m_measurementBindings)
                {
                    if (binding.IsDisposed)
                    {
                        continue;
                    }
                    foreach (string profile in binding.ProfileNames)
                    {
                        if (!profiles.Contains(profile))
                        {
                            profiles.Add(profile);
                        }
                    }
                }
                return new CapabilitySnapshot(
                    hasStandby,
                    hasWol,
                    profiles.Count > 0,
                    hasLocking,
                    profiles);
            }
        }

        /// <summary>
        /// Adds a conformance unit once.
        /// </summary>
        /// <param name="units">The accumulating unit list.</param>
        /// <param name="name">The published unit BrowseName.</param>
        private static void AddUnit(List<QualifiedName> units, string name)
        {
            QualifiedName unit = Unit(name);
            if (!units.Contains(unit))
            {
                units.Add(unit);
            }
        }

        /// <summary>
        /// Creates a standard-namespace conformance unit BrowseName.
        /// </summary>
        /// <param name="name">The published unit BrowseName.</param>
        private static QualifiedName Unit(string name)
        {
            return new QualifiedName(name);
        }

        /// <summary>
        /// Holds the live capability state used for one report.
        /// </summary>
        /// <param name="HasStandby">Whether an active standby binding exists.</param>
        /// <param name="HasWol">Whether an active WOL binding exists.</param>
        /// <param name="HasMeasurement">Whether an active measurement binding exists.</param>
        /// <param name="HasLocking">Whether standby control includes a DI lock.</param>
        /// <param name="MeasurementProfiles">The complete measurement profiles.</param>
        private readonly record struct CapabilitySnapshot(
            bool HasStandby,
            bool HasWol,
            bool HasMeasurement,
            bool HasLocking,
            IReadOnlyList<string> MeasurementProfiles);

        /// <summary>Serializes capability registration and snapshots.</summary>
        private readonly Lock m_gate = new();

        /// <summary>Active standby and optional WOL bindings.</summary>
        private readonly List<EcmPowerBinding> m_powerBindings = [];

        /// <summary>Active standalone WOL bindings.</summary>
        private readonly List<EcmWolBinding> m_wolBindings = [];

        /// <summary>Active measurement bindings.</summary>
        private readonly List<EcmMeasurementBinding> m_measurementBindings = [];

        /// <summary>The server used to publish runtime capability changes.</summary>
        private IServerInternal? m_server;

        /// <summary>One contributor per address-space owner.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, EcmConformanceContributor> s_contributors =
            new();

        /// <summary>Serializes contributor lookup and registration.</summary>
        private static readonly Lock s_gate = new();

        /// <summary>The OPC 34100 conformance-unit names.</summary>
        private static class ConformanceUnitNames
        {
            /// <summary>The measurement metadata data types are loaded.</summary>
            public const string EnergyMeasurementCommonDataTypes = "ECM Energy Measurement Common DataTypes";

            /// <summary>The percent-full-scale accuracy domain is loaded.</summary>
            public const string AccuracyDomainPercentFullScale = "ECM Accuracy Domain Percent full scale";

            /// <summary>The percent-actual-reading accuracy domain is loaded.</summary>
            public const string AccuracyDomainPercentActualReading =
                "ECM Accuracy Domain Percent actual reading";

            /// <summary>The IEC accuracy domain is loaded.</summary>
            public const string AccuracyDomainIec = "ECM Accuracy Domain IEC";

            /// <summary>The EN accuracy domain is loaded.</summary>
            public const string AccuracyDomainEn = "ECM Accuracy Domain EN";

            /// <summary>At least one energy-management capability is configured.</summary>
            public const string EnergyManagementSupport = "ECM Energy Management Support";

            /// <summary>At least one standby object is configured.</summary>
            public const string StandbyManagement = "ECM Standby Management";

            /// <summary>Standby control methods are bound.</summary>
            public const string StandbyManagementControl = "ECM Standby Management Control";

            /// <summary>At least one WOL object is configured.</summary>
            public const string SleepModeWol = "ECM Sleep Mode WOL";

            /// <summary>At least one measurement point is configured.</summary>
            public const string EnergyMeasurement = "ECM Energy Measurement";
        }

        /// <summary>The OPC UA server profile URIs required by ECM profiles.</summary>
        private static class ProfileUris
        {
            /// <summary>The ECM Energy Management Server Profile.</summary>
            public const string EnergyManagement =
                "http://opcfoundation.org/UA-Profile/ECM/Server/EnergyManagement";

            /// <summary>The ECM Energy Controller Server Facet.</summary>
            public const string EnergyController =
                "http://opcfoundation.org/UA-Profile/ECM/Server/EnergyController";

            /// <summary>The ECM Energy Management Control Server Profile.</summary>
            public const string EnergyManagementControl =
                "http://opcfoundation.org/UA-Profile/ECM/Server/EnergyManagementControl";

            /// <summary>The DI Locking Server Facet.</summary>
            public const string Locking =
                "http://opcfoundation.org/UA-Profile/DI/Server/Locking";

            /// <summary>The OPC UA Nano Embedded Device 2017 Server Profile.</summary>
            public const string NanoEmbeddedDevice2017 =
                "http://opcfoundation.org/UA-Profile/Server/NanoEmbeddedDevice2017";

            /// <summary>The OPC UA Data Access Server Facet.</summary>
            public const string DataAccess =
                "http://opcfoundation.org/UA-Profile/Server/DataAccess";

            /// <summary>The OPC UA ComplexType 2017 Server Facet.</summary>
            public const string ComplexTypes2017 =
                "http://opcfoundation.org/UA-Profile/Server/ComplexTypes2017";

            /// <summary>The OPC UA Embedded 2017 Server Profile.</summary>
            public const string Embedded2017 =
                "http://opcfoundation.org/UA-Profile/Server/EmbeddedUA2017";
        }
    }
}

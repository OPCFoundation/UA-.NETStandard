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
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server;

namespace Opc.Ua.GMS.Server
{
    internal sealed partial class GmsSystemBinding
    {
        /// <summary>
        /// Registers one weak native contribution through the existing namespace owner's conformance mechanism.
        /// </summary>
        internal void RegisterCapabilities(GmsHandle handle)
        {
            GmsConformanceRegistration.Register(m_context.Manager, handle);
        }

        /// <summary>
        /// Maps native support to committed actual instances and their selected shared providers.
        /// </summary>
        internal GmsSystemCapabilities Capabilities(
            MachineToolRuntime<GMSState> runtime, GmsResultBindingOptions results)
        {
            ArrayOf<string> limitations = Has(GmsFeatures.Production)
                ? ["OPC 40210 section 12.3.2.7 requires GMS GMS_ProductionStatisticsType, but section 12.2 and the " +
                    "released normative NodeSet define no such type or NodeId. Only the complete GMS Production " +
                    "facet is withheld; inherited production statistics and defined GMS jobs/parts remain supported."]
                : [];
            GmsSystemSnapshot? snapshot = Observation;
            if (m_disposed || snapshot == null ||
                !ReferenceEquals(m_context.Manager.FindPredefinedNode(runtime.NodeId), runtime.State))
            {
                return new GmsSystemCapabilities(runtime.NodeId, [], [], limitations);
            }
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                "GMS GMSType", "GMS GMSEquipmentType", "GMS GMSIdentificationType", "GMS GMSMonitoringType",
                "GMS ResultManagementType"
            };
            bool sensor = false;
            bool multi = false;
            foreach (BaseToolState tool in m_committedSensors.Values)
            {
                sensor |= tool is SensorState;
                multi |= tool is MultiSensorState;
            }
            Add("GMS SensorType", sensor);
            Add("GMS MultiSensorType", multi);
            Add("GMS QualifiedToolLife", sensor);
            Add("GMS LoadingMonitoringType",
                snapshot.Loading != null && runtime.State.Monitoring?.LoadingMonitoring != null);
            Add("GMS ToolMonitoringType",
                snapshot.ToolMonitoring != null && runtime.State.Monitoring?.ToolMonitoring != null);
            Add("GMS CalibrationPrognosisType", snapshot.Calibration != null && m_calibration != null);
            Add("GMS GMSJobType", snapshot.MachineTool.Production.Jobs.Count != 0);
            bool parts = false;
            foreach (MachineToolProductionJob job in snapshot.MachineTool.Production.Jobs)
            {
                foreach (MachineToolProductionPartSet set in job.PartSets)
                {
                    parts |= set.Parts.Count != 0;
                }
            }
            Add("GMS GMSPartType", parts);
            foreach (GmsAccessory accessory in snapshot.Accessories)
            {
                names.Add(accessory.Kind switch
                {
                    GmsAccessoryKind.RotaryTable => "GMS RotaryTableType",
                    GmsAccessoryKind.SensorExchangeRack => "GMS SensorExchangeRackType",
                    _ => "GMS TipExchangeRackType"
                });
            }
            Add("GMS SensorWarningEventType", snapshot.SensorWarnings.Count != 0 && m_warnings.Count != 0);
            Add("GMS ResultType", results.PublishedResults != 0);
            Add("GMS CorrectionType", results.Corrections.Count != 0);
            Add("GMS IntermediateResultEventType", results.EnableIntermediateEvents);
            Add("GMS CharacteristicType", results.EnableIntermediateEvents);
            MachineToolCapabilities inherited = runtime.Capabilities;
            var inheritedUnits = new HashSet<string>(StringComparer.Ordinal);
            foreach (QualifiedName unit in inherited.ConformanceUnits)
            {
                inheritedUnits.Add(unit.Name!);
            }
            var inheritedProfiles = new HashSet<string>(inherited.ServerProfiles.ToArray()!, StringComparer.Ordinal);
            var profiles = new List<string>();
            bool basic = inheritedUnits.Contains("MachineTool Monitor Items Min") &&
                inheritedUnits.Contains("MachineTool MachineToolType Mandatory Nodes") &&
                inheritedUnits.Contains("MachineTool Production Basic") && results.PublishedResults > 0 &&
                HasSecureEndpoint(SecurityPolicies.Basic256Sha256, false);
            Profile("Basic", basic);
            Profile("BasicSecure", basic && HasSecureEndpoint(SecurityPolicies.Aes128_Sha256_RsaOaep, true));
            Profile("Monitoring", Inherited("Monitoring"));
            Profile("Tools", Inherited("Tools") && sensor && multi);
            Profile("ToolLife", Inherited("ToolLife") && sensor);
            Profile("Prognoses", Inherited("Prognoses") && names.Contains("GMS CalibrationPrognosisType"));
            Profile("ErrorsAndAlerts", Inherited("ErrorsAndAlerts") && names.Contains("GMS SensorWarningEventType"));
            Profile("Result", names.Contains("GMS ResultManagementType") && names.Contains("GMS ResultType"));
            ushort ns = (ushort)m_context.Context.NamespaceUris.GetIndex(GmsModel.NamespaceUri);
            var units = new List<QualifiedName>();
            foreach (string name in names)
            {
                units.Add(new QualifiedName(name, ns));
            }
            return new GmsSystemCapabilities(runtime.NodeId, units.ToArrayOf(), profiles.ToArrayOf(), limitations);

            void Add(string name, bool supported)
            {
                if (supported)
                {
                    names.Add(name);
                }
            }

            bool Inherited(string suffix) =>
                inheritedProfiles.Contains("http://opcfoundation.org/UA-Profile/MachineTool/Server/" + suffix);

            void Profile(string suffix, bool supported)
            {
                if (supported)
                {
                    profiles.Add("http://opcfoundation.org/UA-Profile/GMS/Server/" + suffix);
                }
            }
        }

        /// <summary>
        /// Checks real configured endpoint certificates, policies and certificate-token capability.
        /// </summary>
        private bool HasSecureEndpoint(string policy, bool certificateToken)
        {
            if (m_context.Manager.Server is not IServerEndpointRegistryProvider registry)
            {
                return false;
            }
            foreach (EndpointDescription endpoint in registry.ServerEndpoints)
            {
                if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                    endpoint.SecurityPolicyUri != policy || endpoint.ServerCertificate.IsEmpty)
                {
                    continue;
                }
                if (!certificateToken)
                {
                    return true;
                }
                foreach (UserTokenPolicy token in endpoint.UserIdentityTokens)
                {
                    if (token.TokenType == UserTokenType.Certificate &&
                        (string.IsNullOrEmpty(token.SecurityPolicyUri) ||
                            token.SecurityPolicyUri != SecurityPolicies.None))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Keeps one contribution per namespace owner without retaining deleted GMS providers or runtime resources.
    /// </summary>
    internal sealed class GmsConformanceRegistration : IConformanceContributor
    {
        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var result = new HashSet<QualifiedName>();
                foreach (GmsHandle handle in Live())
                {
                    foreach (QualifiedName unit in handle.ConformanceUnits)
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
                foreach (GmsHandle handle in Live())
                {
                    foreach (string profile in handle.ServerProfiles)
                    {
                        result.Add(profile);
                    }
                }
                return new List<string>(result).ToArrayOf();
            }
        }

        /// <summary>
        /// Registers only a successfully published real instance.
        /// </summary>
        internal static void Register(DiNodeManager manager, GmsHandle handle)
        {
            GmsConformanceRegistration registry = s_registrations.GetValue(manager, static _ => new());
            lock (registry.m_gate)
            {
                registry.m_handles.RemoveAll(static weak =>
                    !weak.TryGetTarget(out GmsHandle? value) || value.ConformanceUnits.Count == 0);
                registry.m_handles.Add(new WeakReference<GmsHandle>(handle));
            }
            manager.RegisterConformanceContributor(registry);
        }

        /// <summary>
        /// Prunes dead references without taking ownership of machine shutdown.
        /// </summary>
        private List<GmsHandle> Live()
        {
            var result = new List<GmsHandle>();
            lock (m_gate)
            {
                for (int i = m_handles.Count - 1; i >= 0; i--)
                {
                    if (m_handles[i].TryGetTarget(out GmsHandle? handle) && handle.ConformanceUnits.Count != 0)
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

        /// <summary>One native registry per already selected namespace owner.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, GmsConformanceRegistration> s_registrations = new();

        /// <summary>Protects short weak-reference registry updates only.</summary>
        private readonly Lock m_gate = new();

        /// <summary>Weak contributions, never strong provider or machine resource ownership.</summary>
        private readonly List<WeakReference<GmsHandle>> m_handles = [];
    }
}

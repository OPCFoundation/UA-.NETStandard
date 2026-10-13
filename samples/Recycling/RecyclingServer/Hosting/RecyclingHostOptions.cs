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
using System.IO;
using System.Linq;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// One executable selects either the sole coordinator or exactly one independently hosted process view.
    /// </summary>
    public enum RecyclingHostRole
    {
        /// <summary>The only material engine and journal owner.</summary>
        Coordinator,
        /// <summary>A committed-state projection with no independent material clock.</summary>
        ProcessView
    }

    /// <summary>
    /// A trusted user-certificate fingerprint mapped to one sample application role.
    /// Certificate validation and proof of possession still run through the stack's X.509 authenticator.
    /// </summary>
    public sealed record RecyclingIdentityBinding(
        string CertificateSha256,
        string Identity,
        RecyclingAccess Access,
        RecyclingProcess Process);

    /// <summary>
    /// Explicit host inputs. No secret, private key or automatic peer-trust shortcut is embedded in this sample.
    /// </summary>
    public sealed record RecyclingHostOptions
    {
        /// <summary>
        /// Host role.
        /// </summary>
        public RecyclingHostRole Role { get; init; }

        /// <summary>
        /// Process selection, meaningful only for a view.
        /// </summary>
        public RecyclingProcess Process { get; init; }

        /// <summary>
        /// Shared generic line identity.
        /// </summary>
        public string LineId { get; init; } = "line-1";

        /// <summary>
        /// Secure UA TCP listening endpoint.
        /// </summary>
        public string EndpointUrl { get; init; } = "opc.tcp://localhost:62570/Recycling";

        /// <summary>
        /// Coordinator discovery endpoint used by a view.
        /// </summary>
        public string CoordinatorUrl { get; init; } = string.Empty;

        /// <summary>
        /// Stable application certificate identity.
        /// </summary>
        public string ApplicationName { get; init; } = "RecyclingCoordinator";

        /// <summary>
        /// Stable application URI included in its certificate.
        /// </summary>
        public string ApplicationUri { get; init; } = "urn:localhost:OPCFoundation:RecyclingCoordinator";

        /// <summary>
        /// Application, peer and user certificate stores; must be independently provisioned for every role.
        /// </summary>
        public string PkiRoot { get; init; } = Path.Combine(".recycling", "coordinator", "pki");

        /// <summary>
        /// Exclusively owned durable coordinator root. Never shared by several replicas.
        /// </summary>
        public string StateRoot { get; init; } = Path.Combine(".recycling", "coordinator", "state");

        /// <summary>
        /// Process user-certificate store used by the stack's lazy X.509 identity provider.
        /// </summary>
        public string IdentityStorePath { get; init; } = string.Empty;

        /// <summary>
        /// Process user-certificate subject selector; key material remains in the configured certificate store.
        /// </summary>
        public string IdentitySubject { get; init; } = string.Empty;

        /// <summary>
        /// Explicit authenticated certificate-to-role mapping.
        /// </summary>
        public ArrayOf<RecyclingIdentityBinding> Identities { get; init; } = [];

        /// <summary>
        /// Sole coordinator's independently authored synthetic configuration.
        /// </summary>
        public RecyclingConfiguration Simulation { get; init; } = new();

        /// <summary>
        /// Real-time pacing; never changes logical tick duration.
        /// </summary>
        public TimeSpan TickPeriod { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// View lease renewal and reconnect pacing.
        /// </summary>
        public TimeSpan ViewPollPeriod { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Disables automatic material pacing for controlled callers/tests, not administrative commands.
        /// </summary>
        public bool AutomaticTicks { get; init; } = true;

        /// <summary>
        /// Machinery building blocks loaded by the one DI/Machinery owner.
        /// </summary>
        public MachineryParts MachineryParts { get; init; } = MachineryParts.BuildingBlocks;

        /// <summary>
        /// Validates mutually exclusive roles, secure endpoint choices and bounded pacing.
        /// </summary>
        public void Validate()
        {
            Simulation.Validate();
            if (!Enum.IsDefined(Role) || !Enum.IsDefined(Process) ||
                string.IsNullOrWhiteSpace(LineId) || LineId.Length > 64 ||
                !LineId.All(static character => char.IsAsciiLetterOrDigit(character) || character == '-') ||
                string.IsNullOrWhiteSpace(ApplicationName) || ApplicationName.Length > 128 ||
                !Uri.TryCreate(ApplicationUri, UriKind.Absolute, out _) ||
                !Uri.TryCreate(EndpointUrl, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != "opc.tcp" ||
                string.IsNullOrWhiteSpace(PkiRoot) || string.IsNullOrWhiteSpace(StateRoot) ||
                TickPeriod < TimeSpan.FromMilliseconds(100) || TickPeriod > TimeSpan.FromMinutes(1) ||
                ViewPollPeriod < TimeSpan.FromMilliseconds(100) ||
                ViewPollPeriod.TotalSeconds >= Simulation.LeaseSeconds / 2.0)
            {
                throw new ArgumentException("Invalid recycling host identity, endpoint or pacing.");
            }
            if (Role == RecyclingHostRole.ProcessView &&
                (!Uri.TryCreate(CoordinatorUrl, UriKind.Absolute, out Uri? coordinator) ||
                    coordinator.Scheme != "opc.tcp" ||
                    string.IsNullOrWhiteSpace(IdentityStorePath) || string.IsNullOrWhiteSpace(IdentitySubject)))
            {
                throw new ArgumentException(
                    "A process view needs a secure coordinator endpoint and provisioned user certificate.");
            }
            if (Role == RecyclingHostRole.Coordinator && CoordinatorUrl.Length != 0)
            {
                throw new ArgumentException("Coordinator and process-view options are mutually exclusive.");
            }
        }
    }
}

/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Opc.Ua;

namespace OnboardingDevice
{
    /// <summary>
    /// Requires operator-selected identity, signed ticket files and an exclusively owned PKI root.
    /// No password is accepted through command-line configuration.
    /// </summary>
    internal sealed class DeviceSampleOptions
    {
        /// <summary>
        /// Reads explicit deployment inputs; ticketFiles is a semicolon-separated list of absolute paths.
        /// factoryCertificate is the original public DER certificate, never a PFX or a generated identity.
        /// </summary>
        public DeviceSampleOptions(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            PkiRoot = AbsolutePath(Required(configuration, "pkiRoot"), "pkiRoot");
            ApplicationUri = AbsoluteUri(configuration, "applicationUri");
            ProductUri = AbsoluteUri(configuration, "productUri");
            ProductInstanceUri = AbsoluteUri(configuration, "productInstanceUri");
            SubjectName = Required(configuration, "subjectName");
            FactoryCertificatePath = AbsolutePath(
                Required(configuration, "factoryCertificate"), "factoryCertificate");
            string[] tickets = Required(configuration, "ticketFiles").Split(';');
            if (tickets.Length > 32)
            {
                throw new ArgumentException("At most 32 signed ticket files may be offered.");
            }
            for (int i = 0; i < tickets.Length; i++)
            {
                tickets[i] = AbsolutePath(tickets[i], "ticketFiles");
            }
            TicketFiles = tickets;
            string? managedApplicationUris = configuration["managedApplicationUris"];
            if (string.IsNullOrWhiteSpace(managedApplicationUris))
            {
                ManagedApplicationUris = [];
            }
            else
            {
                string[] applicationUris = managedApplicationUris.Split(';');
                if (applicationUris.Length > 32 ||
                    applicationUris.Any(static value => string.IsNullOrWhiteSpace(value)))
                {
                    throw new ArgumentException(
                        "--managedApplicationUris requires 1 to 32 semicolon-separated absolute URIs.");
                }
                for (int i = 0; i < applicationUris.Length; i++)
                {
                    applicationUris[i] = applicationUris[i].Trim();
                    if (!Uri.TryCreate(applicationUris[i], UriKind.Absolute, out _))
                    {
                        throw new ArgumentException(
                            "--managedApplicationUris requires 1 to 32 semicolon-separated absolute URIs.");
                    }
                    if (string.Equals(applicationUris[i], ApplicationUri, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "--managedApplicationUris cannot include the device application URI.");
                    }
                    for (int j = 0; j < i; j++)
                    {
                        if (string.Equals(applicationUris[j], applicationUris[i], StringComparison.Ordinal))
                        {
                            throw new ArgumentException(
                                "--managedApplicationUris cannot contain duplicate application URIs.");
                        }
                    }
                }
                ManagedApplicationUris = applicationUris.ToArrayOf();
            }
            EndpointUrl = AbsoluteUri(configuration, "endpointUrl");
            var endpoint = new Uri(EndpointUrl);
            if (endpoint.Scheme != "opc.tcp" || endpoint.Port is < 1 or > 65535 ||
                endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.Query.Length != 0)
            {
                throw new ArgumentException("--endpointUrl requires an opc.tcp URL with an explicit valid port.");
            }
            string? reset = configuration["physicalReset"];
            if (reset != null && !bool.TryParse(reset, out _))
            {
                throw new ArgumentException("--physicalReset must be true or false.");
            }
            PhysicalReset = bool.TryParse(reset, out bool enabled) && enabled;
            string? pullEndpoint = configuration["pullRegistrarEndpoint"];
            string? pullApplication = configuration["pullRegistrarApplicationUri"];
            if ((pullEndpoint == null) != (pullApplication == null))
            {
                throw new ArgumentException(
                    "--pullRegistrarEndpoint and --pullRegistrarApplicationUri must be supplied together.");
            }
            if (pullEndpoint != null)
            {
                if (!Uri.TryCreate(pullEndpoint, UriKind.Absolute, out Uri? selected) ||
                    selected.Scheme != "opc.tcp" || selected.Port is < 1 or > 65535 ||
                    selected.UserInfo.Length != 0 || selected.Fragment.Length != 0 || selected.Query.Length != 0 ||
                    !Uri.TryCreate(pullApplication, UriKind.Absolute, out _))
                {
                    throw new ArgumentException("Pull onboarding requires an opc.tcp endpoint and application URI.");
                }
                if (PhysicalReset)
                {
                    throw new ArgumentException("Physical reset and outbound pull cannot run in the same invocation.");
                }
                PullRegistrarEndpoint = selected;
                PullRegistrarApplicationUri = pullApplication;
            }
        }

        /// <summary>
        /// Gets the operator-controlled root; filesystem permissions must protect private certificate stores.
        /// </summary>
        public string PkiRoot { get; }

        /// <summary>
        /// Gets the one DCA and operational server application URI.
        /// </summary>
        public string ApplicationUri { get; }

        /// <summary>
        /// Gets the application product URI.
        /// </summary>
        public string ProductUri { get; }

        /// <summary>
        /// Gets the product instance URI that must occur in both factory certificate and signed device ticket.
        /// </summary>
        public string ProductInstanceUri { get; }

        /// <summary>
        /// Gets the certificate subject shared by the factory and rotating application slot.
        /// </summary>
        public string SubjectName { get; }

        /// <summary>
        /// Gets the independent operator-supplied original public identity certificate.
        /// </summary>
        public string FactoryCertificatePath { get; }

        /// <summary>
        /// Gets the original signed envelopes; inputs are required again on restart and revalidated.
        /// </summary>
        public ArrayOf<string> TicketFiles { get; }

        /// <summary>
        /// Gets the optional application URIs published as separate client components beneath a non-singleton device.
        /// </summary>
        public ArrayOf<string> ManagedApplicationUris { get; }

        /// <summary>
        /// Gets the single listening endpoint.
        /// </summary>
        public string EndpointUrl { get; }

        /// <summary>
        /// Gets whether to perform an explicit local security reset before opening listeners, then serve defaults.
        /// Remove this option on subsequent restarts unless another physical reset is intended.
        /// </summary>
        public bool PhysicalReset { get; }

        /// <summary>
        /// Gets the optional operator-selected pull endpoint, which must already be a persisted registrar candidate.
        /// </summary>
        public Uri? PullRegistrarEndpoint { get; }

        /// <summary>
        /// Gets the expected application URI authenticated by the selected registrar's actual channel certificate.
        /// </summary>
        public string? PullRegistrarApplicationUri { get; }

        /// <summary>
        /// Gets the rotating application certificate store; original factory keys are never generated here.
        /// </summary>
        public string ApplicationStorePath => Path.Combine(PkiRoot, "own");

        /// <summary>
        /// Gets the independent immutable factory certificate and private-key store.
        /// </summary>
        public string IdentityStorePath => Path.Combine(PkiRoot, "device-identity");

        /// <summary>
        /// Gets a nonempty explicit option without inventing deployment metadata.
        /// </summary>
        private static string Required(IConfiguration configuration, string name)
        {
            string? value = configuration[name];
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"--{name} is required.");
        }

        /// <summary>
        /// Rejects relative state and input paths.
        /// </summary>
        private static string AbsolutePath(string value, string name)
        {
            if (!Path.IsPathFullyQualified(value))
            {
                throw new ArgumentException($"--{name} requires an absolute operator-controlled path.");
            }
            return Path.GetFullPath(value);
        }

        /// <summary>
        /// Requires an absolute URI without silently substituting machine-local identifiers.
        /// </summary>
        private static string AbsoluteUri(IConfiguration configuration, string name)
        {
            string value = Required(configuration, name);
            return Uri.TryCreate(value, UriKind.Absolute, out _)
                ? value
                : throw new ArgumentException($"--{name} requires an absolute URI.");
        }
    }
}

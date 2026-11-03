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
        /// Gets the single listening endpoint.
        /// </summary>
        public string EndpointUrl { get; }

        /// <summary>
        /// Gets whether to perform an explicit local security reset before opening listeners, then serve defaults.
        /// Remove this option on subsequent restarts unless another physical reset is intended.
        /// </summary>
        public bool PhysicalReset { get; }

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

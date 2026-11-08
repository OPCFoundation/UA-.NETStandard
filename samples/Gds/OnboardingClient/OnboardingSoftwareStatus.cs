/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Opc.Ua.Onboarding;

namespace OnboardingClientSample
{
    /// <summary>
    /// Reports an operator-supplied software status without performing a software update.
    /// </summary>
    internal sealed class OnboardingSoftwareStatus
    {
        /// <summary>
        /// Creates an explicit report after validating the sample options.
        /// </summary>
        private OnboardingSoftwareStatus(string productInstanceUri, bool status, string softwareRevision)
        {
            ProductInstanceUri = productInstanceUri;
            Status = status;
            SoftwareRevision = softwareRevision;
        }

        /// <summary>
        /// Gets the product instance whose status is reported.
        /// </summary>
        public string ProductInstanceUri { get; }

        /// <summary>
        /// Gets whether the installed software is up to date.
        /// </summary>
        public bool Status { get; }

        /// <summary>
        /// Gets the installed or required software revision.
        /// </summary>
        public string SoftwareRevision { get; }

        /// <summary>
        /// Selects the reporting mode only when an explicit status is supplied.
        /// </summary>
        public static OnboardingSoftwareStatus? FromConfiguration(IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            string? statusText = configuration["reportSoftwareStatus"];
            string? productInstanceUri = configuration["productInstanceUri"];
            string? softwareRevision = configuration["softwareRevision"];
            if (statusText == null)
            {
                if (productInstanceUri != null || softwareRevision != null)
                {
                    throw new ArgumentException(
                        "--productInstanceUri and --softwareRevision require --reportSoftwareStatus.");
                }
                return null;
            }
            if (!bool.TryParse(statusText, out bool status))
            {
                throw new ArgumentException("--reportSoftwareStatus must be true or false.");
            }
            if (IsEnabledOrInvalid(configuration["anonymous"]) ||
                IsEnabledOrInvalid(configuration["pull"]) ||
                IsEnabledOrInvalid(configuration["prepareTickets"]))
            {
                throw new ArgumentException(
                    "--reportSoftwareStatus requires a named user and cannot be combined with " +
                    "--pull or --prepareTickets.");
            }
            foreach (string option in s_conflictingOptions)
            {
                if (configuration[option] != null)
                {
                    throw new ArgumentException($"--{option} cannot be combined with --reportSoftwareStatus.");
                }
            }
            if (string.IsNullOrWhiteSpace(productInstanceUri) ||
                !Uri.TryCreate(productInstanceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("--productInstanceUri must be an absolute URI.");
            }
            if (string.IsNullOrWhiteSpace(softwareRevision))
            {
                throw new ArgumentException("--softwareRevision must not be empty.");
            }
            return new OnboardingSoftwareStatus(productInstanceUri, status, softwareRevision);
        }

        /// <summary>
        /// Calls the registrar using the supplied authenticated session's generated client.
        /// </summary>
        public async Task ReportAsync(
            DeviceRegistrarTypeClient registrar,
            CancellationToken cancellationToken = default)
        {
            if (registrar == null)
            {
                throw new ArgumentNullException(nameof(registrar));
            }
            cancellationToken.ThrowIfCancellationRequested();
            await registrar.UpdateSoftwareStatusAsync(
                ProductInstanceUri, Status, SoftwareRevision, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects an enabled or malformed flag while allowing an absent or explicit false value.
        /// </summary>
        private static bool IsEnabledOrInvalid(string? value)
        {
            return value != null && (!bool.TryParse(value, out bool enabled) || enabled);
        }

        /// <summary>Options belonging to the ticket preparation and pull modes.</summary>
        private static readonly string[] s_conflictingOptions =
        [
            "registrarPkiRoot",
            "ticketAuthorityTrustPath",
            "untrustedClientPkiRoot",
            "externalAuthentication",
            "deviceTicket",
            "compositeTicket",
            "exportSigningKeyCertificate",
            "resumeApplicationId",
            "resumeRequestId",
            "resumeSigningKeyCertificate",
            "signingJournal",
            "signingKeyStore",
            "discardSigningRequest"
        ];
    }
}

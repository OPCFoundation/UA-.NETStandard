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
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using OnboardingClientSample;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the sample's explicit software-status command selection.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class OnboardingSoftwareStatusTests
    {
        /// <summary>
        /// Existing default and pull selections do not implicitly report a status.
        /// </summary>
        [TestCase(null)]
        [TestCase("true")]
        public void MissingReportSelectionPreservesOtherModes(string? pull)
        {
            IConfiguration configuration = Configuration(new Dictionary<string, string?> { ["pull"] = pull });

            Assert.That(OnboardingSoftwareStatus.FromConfiguration(configuration), Is.Null);
        }

        /// <summary>
        /// Both up-to-date and update-required values select the reporting operation.
        /// </summary>
        [TestCase("true", true)]
        [TestCase("false", false)]
        public void ExplicitReportPreservesStatusAndIdentity(string statusText, bool expected)
        {
            Dictionary<string, string?> values = Inputs();
            values["reportSoftwareStatus"] = statusText;

            OnboardingSoftwareStatus? report = OnboardingSoftwareStatus.FromConfiguration(Configuration(values));

            Assert.That(report, Is.Not.Null);
            Assert.That(report!.ProductInstanceUri, Is.EqualTo("urn:test:device"));
            Assert.That(report.Status, Is.EqualTo(expected));
            Assert.That(report.SoftwareRevision, Is.EqualTo("revision-1"));
        }

        /// <summary>
        /// A missing selection cannot turn report arguments into another sample operation.
        /// </summary>
        [TestCase("productInstanceUri")]
        [TestCase("softwareRevision")]
        public void ReportArgumentsRequireExplicitSelection(string option)
        {
            var values = new Dictionary<string, string?> { [option] = "value" };

            Assert.That(() => OnboardingSoftwareStatus.FromConfiguration(Configuration(values)),
                Throws.ArgumentException.With.Message.Contains("--reportSoftwareStatus"));
        }

        /// <summary>
        /// Invalid status text is not interpreted as approval or rejection.
        /// </summary>
        [TestCase("")]
        [TestCase("yes")]
        public void InvalidStatusIsRejected(string statusText)
        {
            Dictionary<string, string?> values = Inputs();
            values["reportSoftwareStatus"] = statusText;

            Assert.That(() => OnboardingSoftwareStatus.FromConfiguration(Configuration(values)),
                Throws.ArgumentException.With.Message.Contains("--reportSoftwareStatus"));
        }

        /// <summary>
        /// Product identity and revision must describe the target status explicitly.
        /// </summary>
        [TestCase("productInstanceUri", null)]
        [TestCase("productInstanceUri", " ")]
        [TestCase("productInstanceUri", "relative")]
        [TestCase("softwareRevision", null)]
        [TestCase("softwareRevision", " ")]
        public void MissingOrInvalidReportArgumentsAreRejected(string option, string? value)
        {
            Dictionary<string, string?> values = Inputs();
            values[option] = value;

            Assert.That(() => OnboardingSoftwareStatus.FromConfiguration(Configuration(values)),
                Throws.ArgumentException.With.Message.Contains("--" + option));
        }

        /// <summary>
        /// Reporting cannot be combined with an anonymous, pull, ticket-preparation, or recovery operation.
        /// </summary>
        [TestCase("anonymous", "true")]
        [TestCase("anonymous", "invalid")]
        [TestCase("pull", "true")]
        [TestCase("pull", "invalid")]
        [TestCase("prepareTickets", "true")]
        [TestCase("prepareTickets", "invalid")]
        [TestCase("registrarPkiRoot", "registrar-pki")]
        [TestCase("ticketAuthorityTrustPath", "ticket-trust")]
        [TestCase("untrustedClientPkiRoot", "untrusted-pki")]
        [TestCase("deviceTicket", "ticket.json")]
        [TestCase("compositeTicket", "composite.json")]
        [TestCase("externalAuthentication", "true")]
        [TestCase("exportSigningKeyCertificate", "certificate.der")]
        [TestCase("signingJournal", "journal")]
        [TestCase("signingKeyStore", "signing-keys")]
        [TestCase("discardSigningRequest", "true")]
        [TestCase("resumeApplicationId", "application")]
        [TestCase("resumeRequestId", "request")]
        [TestCase("resumeSigningKeyCertificate", "certificate.der")]
        public void ConflictingOperationsAreRejected(string option, string value)
        {
            Dictionary<string, string?> values = Inputs();
            values[option] = value;

            Assert.That(() => OnboardingSoftwareStatus.FromConfiguration(Configuration(values)),
                Throws.ArgumentException.With.Message.Contains("--reportSoftwareStatus"));
        }

        /// <summary>
        /// Explicitly false mode flags do not prevent an authenticated report.
        /// </summary>
        [Test]
        public void ExplicitFalseModeFlagsAreAllowed()
        {
            Dictionary<string, string?> values = Inputs();
            values["anonymous"] = "false";
            values["pull"] = "false";
            values["prepareTickets"] = "false";

            Assert.That(OnboardingSoftwareStatus.FromConfiguration(Configuration(values)), Is.Not.Null);
        }

        /// <summary>
        /// Missing configuration and registrar dependencies are reported explicitly.
        /// </summary>
        [Test]
        public async Task MissingDependenciesAreRejected()
        {
            Assert.That(() => OnboardingSoftwareStatus.FromConfiguration(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
            OnboardingSoftwareStatus report =
                OnboardingSoftwareStatus.FromConfiguration(Configuration(Inputs()))!;

            ArgumentNullException? error = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await report.ReportAsync(null!).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error!.ParamName, Is.EqualTo("registrar"));
        }

        /// <summary>
        /// Supplies a valid explicit report without opening files or a connection.
        /// </summary>
        private static Dictionary<string, string?> Inputs()
        {
            return new Dictionary<string, string?>
            {
                ["reportSoftwareStatus"] = "true",
                ["productInstanceUri"] = "urn:test:device",
                ["softwareRevision"] = "revision-1"
            };
        }

        /// <summary>
        /// Builds the same configuration view used by the sample.
        /// </summary>
        private static IConfiguration Configuration(Dictionary<string, string?> values)
        {
            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }
    }
}

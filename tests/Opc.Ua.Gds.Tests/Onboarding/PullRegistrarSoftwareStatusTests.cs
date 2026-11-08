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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using OnboardingClientSample;
using Opc.Ua.Gds.Server.Onboarding;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the sample's status report using the hosted registrar fixture.
    /// </summary>
    public sealed partial class PullRegistrarIntegrationTests
    {
        /// <summary>
        /// The sample report uses the server's software-admin authorization and records both status values.
        /// </summary>
        [Test]
        public async Task SoftwareStatusSampleReportsOnlyForAnAuthorizedKnownDevice()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            string ticket = await WriteTicketAsync(m_signer, m_product, m_identity).ConfigureAwait(false);
            await Registrar(m_session).ProvideIdentitiesAsync([m_identity], [], [ticket], ct).ConfigureAwait(false);
            OnboardingSoftwareStatus approved = CreateSoftwareStatusReport(m_product, true, "installed-1");

            await AssertStatusAsync(StatusCodes.BadUserAccessDenied,
                () => approved.ReportAsync(Registrar(m_session), ct)).ConfigureAwait(false);
            DeviceRegistrarDevice? device = await m_lifecycle.FindDeviceAsync(m_product, ct).ConfigureAwait(false);
            Assert.That(device, Is.Not.Null);
            Assert.That(device!.SoftwareStatus, Is.False);
            Assert.That(device.SoftwareRevision, Is.Empty);

            await CloseSessionAsync(m_otherSession).ConfigureAwait(false);
            m_otherSession = await ConnectAsync(
                m_other, new UserIdentity("software-admin", Encoding.UTF8.GetBytes(m_password)))
                .ConfigureAwait(false);
            OnboardingSoftwareStatus unknown = CreateSoftwareStatusReport("urn:test:unknown", true, "installed-1");
            await AssertStatusAsync(StatusCodes.BadNotFound,
                () => unknown.ReportAsync(Registrar(m_otherSession), ct)).ConfigureAwait(false);
            device = await m_lifecycle.FindDeviceAsync(m_product, ct).ConfigureAwait(false);
            Assert.That(device!.SoftwareStatus, Is.False);
            Assert.That(device.SoftwareRevision, Is.Empty);

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await approved.ReportAsync(Registrar(m_otherSession), cancelled.Token)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            device = await m_lifecycle.FindDeviceAsync(m_product, ct).ConfigureAwait(false);
            Assert.That(device!.SoftwareStatus, Is.False);
            Assert.That(device.SoftwareRevision, Is.Empty);

            await approved.ReportAsync(Registrar(m_otherSession), ct).ConfigureAwait(false);
            device = await m_lifecycle.FindDeviceAsync(m_product, ct).ConfigureAwait(false);
            Assert.That(device!.SoftwareStatus, Is.True);
            Assert.That(device.SoftwareRevision, Is.EqualTo("installed-1"));
            DeviceRegistrarIdentityCandidate? candidate = await m_lifecycle.FindCandidateByCertificateAsync(
                m_identity, ct).ConfigureAwait(false);
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate!.CertificateIssuanceAllowed, Is.True);

            OnboardingSoftwareStatus required = CreateSoftwareStatusReport(m_product, false, "required-2");
            await required.ReportAsync(Registrar(m_otherSession), ct).ConfigureAwait(false);
            device = await m_lifecycle.FindDeviceAsync(m_product, ct).ConfigureAwait(false);
            Assert.That(device!.SoftwareStatus, Is.False);
            Assert.That(device.SoftwareRevision, Is.EqualTo("required-2"));
            candidate = await m_lifecycle.FindCandidateByCertificateAsync(
                m_identity, ct).ConfigureAwait(false);
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate!.CertificateIssuanceAllowed, Is.False);
        }

        /// <summary>
        /// Creates the same explicit report selected by the sample's command-line configuration.
        /// </summary>
        private static OnboardingSoftwareStatus CreateSoftwareStatusReport(
            string productInstanceUri, bool status, string revision)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["reportSoftwareStatus"] = status.ToString(),
                    ["productInstanceUri"] = productInstanceUri,
                    ["softwareRevision"] = revision
                }).Build();
            return OnboardingSoftwareStatus.FromConfiguration(configuration) ??
                throw new AssertionException("The explicit report was not selected.");
        }
    }
}

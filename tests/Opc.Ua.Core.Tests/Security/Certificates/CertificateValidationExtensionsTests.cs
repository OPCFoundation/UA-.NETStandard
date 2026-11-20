// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Part 4 §6.1.3: the client checks the host name of the endpoint URL against the
    /// server certificate. A disabled check may suppress the error but reports it.
    /// </summary>
    [TestFixture]
    [Category("CertificateValidation")]
    [Parallelizable]
    public class CertificateValidationExtensionsTests
    {
        [Test]
        public async Task DisabledHostNameCheckReportsMismatchAsWarningAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var manager = new CertificateManager(telemetry);
            using Certificate certificate = CreateCertificate("actual.invalid");
            var logger = new FakeLogger();

            Assert.That(
                () => manager.CheckServerDomain(certificate, CreateEndpoint("different.invalid"), false, logger),
                Throws.Nothing);

            FakeLogRecord record = logger.LatestRecord;
            Assert.That(record.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(record.Message, Does.Contain("CheckDomain = false"));
        }

        [Test]
        public async Task DisabledHostNameCheckIsSilentWhenHostMatchesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var manager = new CertificateManager(telemetry);
            using Certificate certificate = CreateCertificate("actual.invalid");
            var logger = new FakeLogger();

            manager.CheckServerDomain(certificate, CreateEndpoint("actual.invalid"), false, logger);

            Assert.That(logger.Collector.Count, Is.Zero);
        }

        [Test]
        public async Task EnabledHostNameCheckRejectsMismatchAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var manager = new CertificateManager(telemetry);
            using Certificate certificate = CreateCertificate("actual.invalid");

            Assert.That(
                () => manager.CheckServerDomain(
                    certificate, CreateEndpoint("different.invalid"), true, new FakeLogger()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCertificateHostNameInvalid));
        }

        private static Certificate CreateCertificate(string host)
        {
            return CertificateBuilder.Create("CN=Host Name Check")
                .AddExtension(new X509SubjectAltNameExtension("urn:host:name:check", [host]))
                .CreateForRSA();
        }

        private static ConfiguredEndpoint CreateEndpoint(string host)
        {
            return new ConfiguredEndpoint(null!, new EndpointDescription
            {
                EndpointUrl = $"opc.tcp://{host}:4840"
            });
        }
    }
}

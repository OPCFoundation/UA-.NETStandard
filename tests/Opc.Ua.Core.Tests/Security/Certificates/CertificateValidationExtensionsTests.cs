/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

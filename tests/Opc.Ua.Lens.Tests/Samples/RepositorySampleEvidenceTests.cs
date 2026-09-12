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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    [TestFixture]
    public sealed class RepositorySampleEvidenceTests
    {
        [TestCase("port")]
        [TestCase("path")]
        [TestCase("host")]
        [TestCase("query")]
        [TestCase("application-uri")]
        [TestCase("name")]
        [TestCase("product")]
        [TestCase("type")]
        public async Task DiscoveryRejectsUnexpectedEndpointOrApplication(string mismatch)
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleLaunch launch = await context.PrepareAsync(
                    RepositorySampleId.PumpSoftwareUpdateSimulator).ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints = RepositorySampleTestContext.Endpoints(launch);
                EndpointDescription endpoint = endpoints[0];
                switch (mismatch)
                {
                    case "port":
                        endpoint.EndpointUrl = "opc.tcp://127.0.0.1:58124/PumpDeviceIntegrationServer";
                        break;
                    case "path":
                        endpoint.EndpointUrl = "opc.tcp://127.0.0.1:58123/OtherServer";
                        break;
                    case "host":
                        endpoint.EndpointUrl = "opc.tcp://example.invalid:58123/PumpDeviceIntegrationServer";
                        break;
                    case "query":
                        endpoint.EndpointUrl += "?redirect=other";
                        break;
                    case "application-uri":
                        endpoint.Server.ApplicationUri = "urn:unowned:server";
                        break;
                    case "name":
                        endpoint.Server.ApplicationName = new LocalizedText("Different application");
                        break;
                    case "product":
                        endpoint.Server.ProductUri = "urn:different:product";
                        break;
                    case "type":
                        endpoint.Server.ApplicationType = ApplicationType.Client;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mismatch));
                }
                await Assert.ThatAsync(
                    () => RepositorySampleEvidenceVerifier.VerifyAsync(launch, endpoints, CancellationToken.None),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
            }
        }

        [TestCase("missing")]
        [TestCase("different")]
        [TestCase("none")]
        [TestCase("sign-only")]
        [TestCase("unknown-policy")]
        public async Task DiscoveryRequiresOwnedCertificateAndSecureEndpoint(string mismatch)
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleLaunch launch = await context.PrepareAsync(
                    RepositorySampleId.ConsoleReferenceServer).ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints = RepositorySampleTestContext.Endpoints(launch);
                if (mismatch != "missing")
                {
                    string directory = Path.Combine(launch.Files.PkiRoot, "own", "certs");
                    Directory.CreateDirectory(directory);
                    ByteString certificate = mismatch == "different"
                        ? ByteString.From("another-public-certificate"u8)
                        : RepositorySampleTestContext.PublicCertificate;
                    await File.WriteAllBytesAsync(Path.Combine(directory, "test.der"), certificate.ToArray())
                        .ConfigureAwait(false);
                }
                if (mismatch == "none")
                {
                    endpoints[0].SecurityMode = MessageSecurityMode.None;
                    endpoints[0].SecurityPolicyUri = SecurityPolicies.None;
                }
                else if (mismatch == "sign-only")
                {
                    endpoints[0].SecurityMode = MessageSecurityMode.Sign;
                }
                else if (mismatch == "unknown-policy")
                {
                    endpoints[0].SecurityPolicyUri = "urn:unknown-policy";
                }

                RepositorySampleEvidence? evidence = await RepositorySampleEvidenceVerifier.VerifyAsync(
                    launch, endpoints, CancellationToken.None).ConfigureAwait(false);
                Assert.That(evidence, Is.Null);
            }
        }

        [Test]
        public async Task ForeignPkiCertificateDoesNotCountAsOwned()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleLaunch launch = await context.PrepareAsync(
                    RepositorySampleId.ConsoleReferenceServer).ConfigureAwait(false);
                string outside = Path.Combine(context.Root, "host-pki");
                Directory.CreateDirectory(outside);
                string certificate = Path.Combine(outside, "matching.der");
                await File.WriteAllBytesAsync(certificate, RepositorySampleTestContext.PublicCertificate.ToArray())
                    .ConfigureAwait(false);

                RepositorySampleEvidence? evidence = await RepositorySampleEvidenceVerifier.VerifyAsync(
                    launch, RepositorySampleTestContext.Endpoints(launch), CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(evidence, Is.Null);
                Assert.That(
                    ByteString.From(await File.ReadAllBytesAsync(certificate).ConfigureAwait(false)),
                    Is.EqualTo(RepositorySampleTestContext.PublicCertificate));
                Assert.That(Directory.Exists(Path.Combine(launch.Files.PkiRoot, "trusted")), Is.False);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public async Task ReadinessUsesOnlyTheSelectedSamplesApplicationCertificateStore(int sampleId)
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleLaunch launch = await context.PrepareAsync((RepositorySampleId)sampleId)
                    .ConfigureAwait(false);
                string expected = sampleId == 0
                    ? Path.Combine(launch.Files.PkiRoot, "own", "certs")
                    : Path.Combine(launch.Files.PkiRoot, "certs");
                string other = sampleId == 0
                    ? Path.Combine(launch.Files.PkiRoot, "certs")
                    : Path.Combine(launch.Files.PkiRoot, "own", "certs");
                Directory.CreateDirectory(other);
                await File.WriteAllBytesAsync(
                    Path.Combine(other, "foreign.der"), RepositorySampleTestContext.PublicCertificate.ToArray())
                    .ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints = RepositorySampleTestContext.Endpoints(launch);
                Assert.That(await RepositorySampleEvidenceVerifier.VerifyAsync(
                    launch, endpoints, CancellationToken.None).ConfigureAwait(false), Is.Null);

                Directory.CreateDirectory(expected);
                await File.WriteAllBytesAsync(
                    Path.Combine(expected, "owned.der"), RepositorySampleTestContext.PublicCertificate.ToArray())
                    .ConfigureAwait(false);
                RepositorySampleEvidence? evidence = await RepositorySampleEvidenceVerifier.VerifyAsync(
                    launch, endpoints, CancellationToken.None).ConfigureAwait(false);

                Assert.That(evidence, Is.Not.Null);
                Assert.That(evidence!.ApplicationName, Is.EqualTo(launch.Descriptor.ApplicationName));
                Assert.That(evidence.Endpoint, Is.EqualTo(launch.Endpoint));
            }
        }

        [Test]
        public async Task EmptyDiscoveryIsPendingAndOversizedDiscoveryFails()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleLaunch launch = await context.PrepareAsync(
                    RepositorySampleId.ConsoleReferenceServer).ConfigureAwait(false);
                Assert.That(await RepositorySampleEvidenceVerifier.VerifyAsync(
                    launch, [], CancellationToken.None).ConfigureAwait(false), Is.Null);
                EndpointDescription endpoint = RepositorySampleTestContext.Endpoints(launch)[0];
                string certificates = Path.Combine(launch.Files.PkiRoot, "own", "certs");
                Directory.CreateDirectory(certificates);
                await File.WriteAllBytesAsync(
                    Path.Combine(certificates, "quota.der"), RepositorySampleTestContext.PublicCertificate.ToArray())
                    .ConfigureAwait(false);
                var atLimit = Enumerable.Repeat(endpoint, 64).ToArrayOf();
                RepositorySampleEvidence? evidence = await RepositorySampleEvidenceVerifier.VerifyAsync(
                    launch, atLimit, CancellationToken.None).ConfigureAwait(false);
                Assert.That(evidence, Is.Not.Null);
                Assert.That(evidence!.ApplicationName, Is.EqualTo("ConsoleReferenceServer"));
                var tooMany = Enumerable.Repeat(endpoint, 65).ToArrayOf();
                await Assert.ThatAsync(
                    () => RepositorySampleEvidenceVerifier.VerifyAsync(launch, tooMany, CancellationToken.None),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
            }
        }

        [TestCase("opc.tcp://example.invalid:4840/Server")]
        [TestCase("https://localhost:4840/Server")]
        [TestCase("opc.tcp://operator@localhost:4840/Server")]
        [TestCase("opc.tcp://localhost:4840/Server?redirect=other")]
        public async Task DefaultProbeRejectsNonSampleTargetsBeforeUsingTelemetry(string address)
        {
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            var probe = new RepositorySampleDiscoveryProbe(telemetry.Object);

            await Assert.ThatAsync(
                () => probe.DiscoverAsync(new Uri(address), CancellationToken.None),
                Throws.ArgumentException).ConfigureAwait(false);
            telemetry.VerifyNoOtherCalls();
        }
    }
}

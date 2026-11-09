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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Gds;
using Opc.Ua.Mcp;
using GdsMethods = Opc.Ua.Gds.MethodIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Checks the distinct certificate mutations rather than treating method reachability as behavioral coverage.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsCertificateCommandTests
    {
        [Test]
        public async Task SigningAndKeyPairStartsSendNativeArgumentsWithoutFinishingOrExposingPasswordAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            (ByteString csr, _) = Material(fixture);
            await File.WriteAllBytesAsync(Path.Combine(fixture.Root!, "request.der"), csr.ToArray()!)
                .ConfigureAwait(false);
            fixture.Store.Seed("password", "test-password-do-not-output");
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(fixture.Remote.Id("request-id")));
            GdsCertificateTools tools = fixture.Services.GetRequiredService<GdsCertificateTools>();
            string directory = fixture.Remote.Id("directory").ToString();
            string application = fixture.Remote.Id("application").ToString();
            string type = ObjectTypeIds.RsaSha256ApplicationCertificateType.ToString();

            CallToolResult signed = await tools.StartSigningRequestAsync(
                directory, application, type, "request.der", certificateGroupId: "i=0", sessionName: "selected")
                .ConfigureAwait(false);
            Assert.That(signed.IsError, Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Remote.Calls[0].InputArguments[3], Is.EqualTo(Variant.From(csr)));
            CallToolResult key = await tools.StartKeyPairRequestAsync(directory, new GdsKeyPairInput
            {
                ApplicationId = application,
                CertificateTypeId = type,
                CertificateGroupId = "i=0",
                SubjectName = "CN=Requested",
                DomainNames = ["plant.example.test"],
                PrivateKeyFormat = "PFX",
                PasswordReference = "alias:password"
            }, "selected").ConfigureAwait(false);

            Assert.That(key.IsError, Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
            Assert.That(fixture.Remote.Calls.Select(call => call.MethodId), Is.EqualTo(
            [
                fixture.Remote.Model(GdsMethods.CertificateDirectoryType_StartSigningRequest),
                fixture.Remote.Model(GdsMethods.CertificateDirectoryType_StartNewKeyPairRequest)
            ]));
            Assert.That(fixture.Remote.Calls[1].InputArguments[1], Is.EqualTo(Variant.From(NodeId.Null)));
            Assert.That(fixture.Remote.Calls[1].InputArguments[4],
                Is.EqualTo(Variant.From(["plant.example.test"])));
            Assert.That(fixture.Remote.Calls[1].InputArguments[6],
                Is.EqualTo(Variant.From("test-password-do-not-output")));
            Assert.That(RemoteModelTestSession.Payload(key).GetProperty("requestId").GetString(),
                Is.EqualTo(fixture.Remote.Id("request-id").ToString()));
            Assert.That(RemoteModelTestSession.Payload(key).GetRawText(),
                Does.Not.Contain("test-password-do-not-output"));
            Assert.That(fixture.Store.SetTokens, Is.Empty);
        }

        [Test]
        public async Task ServerCsrSelfSignedAndUpdateRemainThreeExplicitOperationsWithPublicArtifactsAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            (ByteString csr, ByteString certificate) = Material(fixture);
            fixture.Remote.Child(fixture.Remote.Id("configuration"), new QualifiedName("CreateSelfSignedCertificate"),
                fixture.Remote.Id("self-signed-method"));
            fixture.Remote.OnCall = call =>
            {
                if (call.MethodId == fixture.Remote.Model(MethodIds.ServerConfigurationType_CreateSigningRequest))
                {
                    return Reply(Variant.From(csr));
                }
                if (call.MethodId == fixture.Remote.Model(
                    MethodIds.ServerConfigurationType_CreateSelfSignedCertificate))
                {
                    return Reply(Variant.From(certificate));
                }
                Assert.That(call.MethodId,
                    Is.EqualTo(fixture.Remote.Model(MethodIds.ServerConfigurationType_UpdateCertificate)));
                return Reply(Variant.From(true));
            };
            GdsCertificateTools tools = fixture.Services.GetRequiredService<GdsCertificateTools>();
            string root = fixture.Remote.Id("configuration").ToString();
            string type = ObjectTypeIds.RsaSha256ApplicationCertificateType.ToString();

            CallToolResult created = await tools.CreateCsrAsync(root, new GdsCsrInput
            {
                CertificateTypeId = type,
                SubjectName = "CN=Requested",
                RegeneratePrivateKey = false
            }, "created.csr", "selected").ConfigureAwait(false);
            Assert.That(created.IsError, Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(fixture.Root!, "created.csr")).ConfigureAwait(false),
                Is.EqualTo(csr.ToArray()));
            CallToolResult selfSigned = await tools.CreateSelfSignedAsync(root, new GdsSelfSignedInput
            {
                CertificateTypeId = type,
                SubjectName = "CN=Requested",
                DnsNames = ["plant.example.test"],
                IpAddresses = ["127.0.0.1"],
                LifetimeInDays = 14,
                KeySizeInBits = 2048
            }, "self.der", "selected").ConfigureAwait(false);
            Assert.That(selfSigned.IsError, Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
            McpSecretReference key = await fixture.Secrets.StoreIssuedAsync(
                ByteString.From(Encoding.UTF8.GetBytes("opaque-protected-private-key")),
                McpCapability.CertificateAdministration, "certificate-private-key", "opcua-app:urn:test:gds-server",
                fixture.Clock.GetUtcNow().AddMinutes(1)).ConfigureAwait(false);

            CallToolResult staged = await tools.StageUpdateAsync(root, new GdsPushCertificateInput
            {
                CertificateTypeId = type,
                CertificatePath = "self.der",
                PrivateKeyFormat = "PEM",
                PrivateKeyReference = key.Reference
            }, "selected").ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(staged);
            Assert.That(staged.IsError, Is.False);
            Assert.That(json.GetProperty("applyChangesRequired").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("applyInvoked").GetBoolean(), Is.False);
            Assert.That(json.GetRawText(), Does.Not.Contain("opaque-protected-private-key"));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(3));
            Assert.That(fixture.Remote.Calls[1].InputArguments[5], Is.EqualTo(Variant.From((ushort)14)));
            Assert.That(fixture.Remote.Calls[1].InputArguments[6], Is.EqualTo(Variant.From((ushort)2048)));
            Assert.That(fixture.Remote.Calls[2].InputArguments[2], Is.EqualTo(Variant.From(certificate)));
            Assert.That(fixture.Remote.Calls[2].InputArguments[3],
                Is.EqualTo(Variant.From(ArrayOf<ByteString>.Empty)));
            Assert.That(fixture.Remote.Calls[2].InputArguments[4], Is.EqualTo(Variant.From("PEM")));
            Assert.That(fixture.Remote.Calls[2].InputArguments[5],
                Is.EqualTo(Variant.From(ByteString.From(Encoding.UTF8.GetBytes("opaque-protected-private-key")))));
        }

        [Test]
        public async Task RevocationAndTrustChangesPreserveActualStatusAndInvalidateOnlyTheMatchingKeyAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            (_, ByteString certificate) = Material(fixture);
            await File.WriteAllBytesAsync(Path.Combine(fixture.Root!, "public.der"), certificate.ToArray()!)
                .ConfigureAwait(false);
            fixture.Remote.Browse(fixture.Remote.Id("configuration"), ReferenceTypeIds.HierarchicalReferences,
                [fixture.Remote.Reference(fixture.Remote.Id("trust"), ObjectTypeIds.TrustListType)]);
            McpSecretReference key = await fixture.Secrets.StoreIssuedAsync(ByteString.From([13]),
                McpCapability.CertificateAdministration, "certificate-private-key", "opcua-app:urn:test:gds-server",
                fixture.Clock.GetUtcNow().AddMinutes(1), Convert.ToHexString(SHA256.HashData(certificate.Span)))
                .ConfigureAwait(false);
            DateTimeUtc validity = fixture.Clock.GetUtcNow().UtcDateTime;
            fixture.Remote.OnCall = call =>
            {
                if (call.MethodId == fixture.Remote.Model(GdsMethods.CertificateDirectoryType_CheckRevocationStatus))
                {
                    return Reply(Variant.From(StatusCodes.BadCertificateRevoked), Variant.From(validity));
                }
                if (call.MethodId == fixture.Remote.Model(GdsMethods.DirectoryType_GetApplication))
                {
                    return Reply(Variant.FromStructure(new ApplicationRecordDataType
                    {
                        ApplicationUri = "urn:test:gds-server"
                    }));
                }
                return Reply();
            };
            GdsCertificateTools tools = fixture.Services.GetRequiredService<GdsCertificateTools>();
            string directory = fixture.Remote.Id("directory").ToString();

            CallToolResult status = await tools.CheckRevocationAsync(
                directory, "public.der", "selected").ConfigureAwait(false);
            JsonElement inspected = RemoteModelTestSession.Payload(status);
            Assert.That(status.IsError, Is.True);
            Assert.That(inspected.GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(inspected.GetProperty("certificateStatusValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadCertificateRevoked.Code));
            Assert.That(inspected.GetProperty("validityTime").GetDateTime(), Is.EqualTo((DateTime)validity));
            CallToolResult revoked = await tools.RevokeAsync(
                directory, fixture.Remote.Id("app").ToString(), "public.der", "selected").ConfigureAwait(false);
            Assert.That(revoked.IsError, Is.False);
            Assert.That(() => fixture.Secrets.RequireReference(
                key.Reference, "opcua-app:urn:test:gds-server", McpCapability.CertificateAdministration),
                Throws.TypeOf<ServiceResultException>());
            CallToolResult added = await tools.AddToTrustListAsync(
                fixture.Remote.Id("configuration").ToString(), fixture.Remote.Id("trust").ToString(),
                "public.der", true, "selected").ConfigureAwait(false);
            CallToolResult removed = await tools.RemoveFromTrustListAsync(
                fixture.Remote.Id("configuration").ToString(), fixture.Remote.Id("trust").ToString(),
                "public-thumbprint", false, "selected").ConfigureAwait(false);

            Assert.That(added.IsError, Is.False);
            Assert.That(removed.IsError, Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(5));
            Assert.That(fixture.Remote.Calls[3].InputArguments[0], Is.EqualTo(Variant.From(certificate)));
            Assert.That(fixture.Remote.Calls[3].InputArguments[1], Is.EqualTo(Variant.From(true)));
            Assert.That(fixture.Remote.Calls[4].InputArguments[0], Is.EqualTo(Variant.From("public-thumbprint")));
            Assert.That(fixture.Remote.Calls[4].InputArguments[1], Is.EqualTo(Variant.From(false)));
            Assert.That(fixture.Store.Values, Is.Empty);
        }

        /// <summary>
        /// Creates only runtime-owned cryptographic material and exports public encodings.
        /// </summary>
        private static (ByteString Csr, ByteString Certificate) Material(GdsTestContext fixture)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=Public command test", key, HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                fixture.Clock.GetUtcNow().AddMinutes(-1), fixture.Clock.GetUtcNow().AddDays(1));
            return (ByteString.From(request.CreateSigningRequest()),
                ByteString.From(certificate.Export(X509ContentType.Cert)));
        }

        /// <summary>
        /// Preserves the real output tuple shape in the deterministic service seam.
        /// </summary>
        private static CallMethodResult Reply(params Variant[] outputs)
        {
            return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = outputs.ToArrayOf() };
        }
    }
}
#endif

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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Pins public metadata, discovery and optional-capability behavior independently from issuance.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsReadToolsTests
    {
        [Test]
        public async Task TypedDirectoryCredentialAndRegistrarDiscoveryPreservesRealInstanceIdentitiesAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Browse(ObjectIds.ObjectsFolder, ReferenceTypeIds.HierarchicalReferences,
                [
                    fixture.Remote.Reference(
                        fixture.Remote.Id("directory"), Gds.ObjectTypeIds.CertificateDirectoryType),
                    fixture.Remote.Reference(
                        fixture.Remote.Id("key-service"), Gds.ObjectTypeIds.KeyCredentialServiceType),
                    fixture.Remote.Reference(
                        fixture.Remote.Id("registrar"), Onboarding.ObjectTypeIds.DeviceRegistrarAdminType)
                ]);

            CallToolResult directories = await fixture.Services.GetRequiredService<GdsDirectoryTools>()
                .ListDirectoriesAsync(sessionName: "selected").ConfigureAwait(false);
            CallToolResult credentials = await fixture.Services.GetRequiredService<GdsCredentialTools>()
                .ListServicesAsync(GdsCredentialServiceKind.KeyCredential, sessionName: "selected")
                .ConfigureAwait(false);
            CallToolResult registrars = await fixture.Services.GetRequiredService<GdsOnboardingTools>()
                .ListRegistrarsAsync(sessionName: "selected").ConfigureAwait(false);

            Assert.That(directories.IsError, Is.False);
            Assert.That(credentials.IsError, Is.False);
            Assert.That(registrars.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(directories).GetProperty("items")[0]
                .GetProperty("nodeId").GetString(), Is.EqualTo(fixture.Remote.Id("directory").ToString()));
            Assert.That(RemoteModelTestSession.Payload(credentials).GetProperty("items")[0]
                .GetProperty("nodeId").GetString(), Is.EqualTo(fixture.Remote.Id("key-service").ToString()));
            Assert.That(RemoteModelTestSession.Payload(registrars).GetProperty("items")[0]
                .GetProperty("nodeId").GetString(), Is.EqualTo(fixture.Remote.Id("registrar").ToString()));
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Store.SetTokens, Is.Empty);
        }

        [Test]
        public async Task GroupAndTrustMetadataKeepFalseValuesBadQualityAndOptionalAbsenceDistinctAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Type(fixture.Remote.Id("group"), ObjectTypeIds.CertificateGroupType);
            fixture.Remote.Type(fixture.Remote.Id("trust"), ObjectTypeIds.TrustListType);
            fixture.Remote.Child(
                fixture.Remote.Id("group"), new QualifiedName("TrustList"), fixture.Remote.Id("trust"));
            fixture.Remote.Property(fixture.Remote.Id("group"), new QualifiedName("CertificateTypes"),
                Variant.From([ObjectTypeIds.RsaSha256ApplicationCertificateType]));
            fixture.Remote.Property(fixture.Remote.Id("trust"), new QualifiedName("Writable"), Variant.From(false));
            fixture.Remote.Property(fixture.Remote.Id("trust"), new QualifiedName("UserWritable"),
                DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied));
            GdsCertificateTools tools = fixture.Services.GetRequiredService<GdsCertificateTools>();

            CallToolResult group = await tools.InspectGroupAsync(
                fixture.Remote.Id("group").ToString(), "selected").ConfigureAwait(false);
            CallToolResult trust = await tools.InspectTrustListAsync(
                fixture.Remote.Id("trust").ToString(), "selected").ConfigureAwait(false);

            Assert.That(group.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(group).GetProperty("trustListId").GetString(),
                Is.EqualTo(fixture.Remote.Id("trust").ToString()));
            JsonElement properties = RemoteModelTestSession.Payload(trust).GetProperty("properties");
            Assert.That(trust.IsError, Is.True);
            Assert.That(properties.GetProperty("Writable").GetProperty("supported").GetBoolean(), Is.True);
            Assert.That(properties.GetProperty("Writable").GetProperty("dataValue").GetProperty("Value").GetBoolean(),
                Is.False);
            Assert.That(properties.GetProperty("UserWritable").GetProperty("supported").GetBoolean(), Is.True);
            Assert.That(properties.GetProperty("ActivityTimeout").GetProperty("supported").GetBoolean(), Is.False);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        [Test]
        public async Task CredentialMetadataAdvertisesIssuerBindingAndOptionalPoliciesWithoutIssuanceAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Property(fixture.Remote.Id("key-service"),
                fixture.Remote.Name(Gds.Namespaces.OpcUaGds, "ProfileUris"),
                Variant.From(["urn:profile:resource-credential"]));

            CallToolResult result = await fixture.Services.GetRequiredService<GdsCredentialTools>().InspectServiceAsync(
                fixture.Remote.Id("key-service").ToString(), GdsCredentialServiceKind.KeyCredential, "selected")
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("issuerResource").GetString(), Does.Contain("s=key-service"));
            Assert.That(json.GetProperty("properties").GetProperty("ResourceUri").GetProperty("dataValue")
                .GetProperty("Value").GetString(), Is.EqualTo("urn:resource"));
            Assert.That(json.GetProperty("properties").GetProperty("SecurityPolicyUris")
                .GetProperty("supported").GetBoolean(), Is.False);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Store.Views, Is.Empty);
        }

        [Test]
        public async Task AuthorizationDescriptionAndRejectedListExportOnlyValidatedPublicCertificatesAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=Public issuer", key, HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                fixture.Clock.GetUtcNow().AddMinutes(-1), fixture.Clock.GetUtcNow().AddDays(1));
            var der = ByteString.From(certificate.Export(X509ContentType.Cert));
            fixture.Remote.Reply(StatusCodes.Good, Variant.From("urn:issuer"), Variant.From(der),
                Variant.FromStructure(
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anonymous", TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ]));

            CallToolResult description = await fixture.Services.GetRequiredService<GdsCredentialTools>()
                .GetAuthorizationDescriptionAsync(
                    fixture.Remote.Id("auth-service").ToString(), "issuer.der", "selected").ConfigureAwait(false);
            fixture.Remote.Reply(StatusCodes.Good, Variant.From([der]));
            CallToolResult rejected = await fixture.Services.GetRequiredService<GdsCertificateTools>()
                .GetRejectedAsync(
                    fixture.Remote.Id("configuration").ToString(), "rejected.json", sessionName: "selected")
                .ConfigureAwait(false);

            Assert.That(description.IsError, Is.False);
            Assert.That(rejected.IsError, Is.False);
            JsonElement json = RemoteModelTestSession.Payload(description);
            Assert.That(json.GetProperty("serviceUri").GetString(), Is.EqualTo("urn:issuer"));
            Assert.That(
                json.GetProperty("userTokenPolicies").GetProperty("items")[0].GetProperty("PolicyId").GetString(),
                Is.EqualTo("anonymous"));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(fixture.Root!, "issuer.der")).ConfigureAwait(false),
                Is.EqualTo(der.ToArray()));
            using var bundle = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(fixture.Root!, "rejected.json")).ConfigureAwait(false));
            Assert.That(bundle.RootElement.GetProperty("certificates")[0].GetBytesFromBase64(),
                Is.EqualTo(der.ToArray()));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
            Assert.That(fixture.Store.Values, Is.Empty);
        }
    }
}
#endif

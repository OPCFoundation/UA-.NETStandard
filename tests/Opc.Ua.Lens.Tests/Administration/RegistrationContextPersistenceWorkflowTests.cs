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

using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Gds;

namespace UaLens.Tests.Administration;

[TestFixture]
public sealed class RegistrationContextPersistenceWorkflowTests
{
    [Test]
    public void DtoConversionCopiesListsAndPreservesBothCertificatePathFamilies()
    {
        RegisteredApplicationContext context = RegistrationTestData.Create();

        RegisteredApplicationContextDto dto = RegisteredApplicationContextXml.ToDto(context);

        Assert.That(dto.ApplicationId, Is.EqualTo("ns=2;s=assembly"));
        Assert.That(dto.ApplicationName, Is.EqualTo("Assembly line"));
        Assert.That(dto.ApplicationUri, Is.EqualTo("urn:fixture:assembly"));
        Assert.That(dto.ProductUri, Is.EqualTo("urn:fixture:product"));
        Assert.That(dto.RegistrationType, Is.EqualTo("ServerPush"));
        Assert.That(dto.DiscoveryUrls,
            Is.EqualTo(s_dtoConversionCopiesListsAndPreservesBothCertificatePathFamiliExpected));
        Assert.That(dto.ServerCapabilities, Is.EqualTo(s_dtoConversionCopiesListsAndPreservesBothCertificatePathFamiliExpected2));
        Assert.That(dto.PushEndpointSecurityMode, Is.EqualTo("Sign"));
        Assert.That(dto.PushEndpointSecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
        RegisteredApplicationContext rebuilt = RegisteredApplicationContextXml.ToRecord(dto);
        RegistrationTestData.AssertPaths(rebuilt);
        Assert.That(rebuilt.ApplicationId, Is.EqualTo(new NodeId("assembly", 2)));
        Assert.That(rebuilt.PushEndpoint!.EndpointUrl, Is.EqualTo("opc.tcp://assembly.example.test:4840"));
        Assert.That(rebuilt.PushEndpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.Sign));
        Assert.That(rebuilt.PushEndpoint.UserIdentityTokens.Count, Is.Zero,
            "Persisted endpoint intent deliberately excludes credentials; use-time discovery is separate.");
        Assert.That(rebuilt.PushEndpoint.ServerCertificate.IsEmpty, Is.True);
        dto.DiscoveryUrls.Add("opc.tcp://new.example.test");
        dto.ServerCapabilities.Clear();
        Assert.That(context.DiscoveryUrls, Has.Count.EqualTo(2));
        Assert.That(context.ServerCapabilities, Is.EqualTo(s_dtoConversionCopiesListsAndPreservesBothCertificatePathFamiliExpected2));
    }

    [TestCase("", "", false)]
    [TestCase("invalid node", "unrecognized", true)]
    [TestCase("i=45", "serverpull", false)]
    public void DtoFallbacksDistinguishInvalidIdentityAndMissingPushEndpoint(
        string applicationId, string registration, bool push)
    {
        var dto = new RegisteredApplicationContextDto
        {
            ApplicationId = applicationId,
            RegistrationType = registration,
            ApplicationUri = null!,
            ApplicationName = null!,
            ProductUri = null!,
            DiscoveryUrls = null!,
            ServerCapabilities = null!,
            PushEndpointUrl = push ? "opc.tcp://fallback.example.test:4840" : " ",
            PushEndpointSecurityMode = "unrecognized",
            PushEndpointSecurityPolicyUri = null
        };

        RegisteredApplicationContext context = RegisteredApplicationContextXml.ToRecord(dto);

        Assert.That(context.ApplicationId, Is.EqualTo(applicationId == "i=45" ? new NodeId(45) : NodeId.Null));
        Assert.That(context.RegistrationType, Is.EqualTo(
            registration == "serverpull" ? GdsRegistrationType.ServerPull : GdsRegistrationType.ClientPull));
        Assert.That(context.ApplicationName, Is.Empty);
        Assert.That(context.ApplicationUri, Is.Empty);
        Assert.That(context.ProductUri, Is.Empty);
        Assert.That(context.DiscoveryUrls, Is.Empty);
        Assert.That(context.ServerCapabilities, Is.Empty);
        Assert.That(context.HasPushEndpoint, Is.EqualTo(push));
        if (push)
        {
            Assert.That(context.PushEndpoint!.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(context.PushEndpoint.SecurityPolicyUri, Is.Empty);
        }
        else
        {
            Assert.That(context.PushEndpoint, Is.Null);
        }
    }

    [Test]
    public void SaveWritesPortableJsonAtTheOwnedPathAndLoadReconstructsItsValues()
    {
        using var temporary = new TemporaryCertificateStores();
        string path = Path.Combine(temporary.Root, "nested", "registration.json");
        RegisteredApplicationContext original = RegistrationTestData.Create();

        RegisteredApplicationContextXml.Save(original, path);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement payload = document.RootElement;
        Assert.That(payload.GetProperty("applicationId").GetString(), Is.EqualTo("ns=2;s=assembly"));
        Assert.That(payload.GetProperty("registrationType").GetString(), Is.EqualTo("ServerPush"));
        Assert.That(payload.GetProperty("certificatePublicKeyPath").GetString(), Is.EqualTo("ua/application.cer"));
        Assert.That(payload.GetProperty("httpsCertificatePublicKeyPath").GetString(), Is.EqualTo("https/server.cer"));
        Assert.That(payload.GetProperty("pushEndpointSecurityMode").GetString(), Is.EqualTo("Sign"));
        Assert.That(payload.TryGetProperty("userIdentityTokens", out _), Is.False);
        Assert.That(payload.TryGetProperty("serverCertificate", out _), Is.False);
        RegisteredApplicationContext loaded = RegisteredApplicationContextXml.Load(path);
        RegistrationTestData.AssertPaths(loaded);
        Assert.That(loaded.ApplicationName, Is.EqualTo("Assembly line"));
        Assert.That(loaded.ServerCapabilities, Is.EqualTo(s_dtoConversionCopiesListsAndPreservesBothCertificatePathFamiliExpected2));
        using FileStream exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.That(exclusive.Length, Is.GreaterThan(100));
        Assert.That(Directory.GetFiles(temporary.Root, "*", SearchOption.AllDirectories), Is.EqualTo(new[] { path }));
    }

    [Test]
    public void LoadReadsLegacyXmlListsAndBothApplicationAndHttpsDeliverySettings()
    {
        using var temporary = new TemporaryCertificateStores();
        string path = Path.Combine(temporary.Root, "legacy.xml");
        File.WriteAllText(path, """
            <RegisteredApplicationContext>
              <ApplicationId>ns=2;s=legacy</ApplicationId>
              <ApplicationUri>urn:fixture:legacy</ApplicationUri>
              <ApplicationName>Legacy line</ApplicationName>
              <ProductUri>urn:fixture:legacy-product</ProductUri>
              <RegistrationType>serverpush</RegistrationType>
              <DiscoveryUrls><Url>opc.tcp://legacy.test:4840</Url><Url/><Url>https://legacy.test</Url></DiscoveryUrls>
              <ServerCapabilities>
                <Capability>DA</Capability><Capability/><Capability>HA</Capability>
              </ServerCapabilities>
              <Domains>legacy.test,backup.test</Domains>
              <CertificateStorePath>ua/store</CertificateStorePath>
              <CertificateSubjectName>CN=Legacy</CertificateSubjectName>
              <CertificatePublicKeyPath>ua/application.cer</CertificatePublicKeyPath>
              <CertificatePrivateKeyPath>ua/application.key</CertificatePrivateKeyPath>
              <TrustListStorePath>ua/trusted</TrustListStorePath>
              <IssuerListStorePath>ua/issuers</IssuerListStorePath>
              <HttpsCertificatePublicKeyPath>https/server.cer</HttpsCertificatePublicKeyPath>
              <HttpsCertificatePrivateKeyPath>https/server.key</HttpsCertificatePrivateKeyPath>
              <HttpsTrustListStorePath>https/trusted</HttpsTrustListStorePath>
              <HttpsIssuerListStorePath>https/issuers</HttpsIssuerListStorePath>
              <PushEndpointUrl>opc.tcp://legacy.test:4840</PushEndpointUrl>
              <PushEndpointSecurityMode>signandencrypt</PushEndpointSecurityMode>
              <PushEndpointSecurityPolicyUri>urn:fixture:policy</PushEndpointSecurityPolicyUri>
            </RegisteredApplicationContext>
            """);

        RegisteredApplicationContext loaded = RegisteredApplicationContextXml.Load(path);

        Assert.That(loaded.ApplicationId, Is.EqualTo(new NodeId("legacy", 2)));
        Assert.That(loaded.ApplicationName, Is.EqualTo("Legacy line"));
        Assert.That(loaded.ApplicationUri, Is.EqualTo("urn:fixture:legacy"));
        Assert.That(loaded.ProductUri, Is.EqualTo("urn:fixture:legacy-product"));
        Assert.That(loaded.DiscoveryUrls, Is.EqualTo(s_loadReadsLegacyXmlListsAndBothApplicationAndHttpsDeliverySettExpected));
        Assert.That(loaded.ServerCapabilities, Is.EqualTo(s_dtoConversionCopiesListsAndPreservesBothCertificatePathFamiliExpected2));
        Assert.That(loaded.RegistrationType, Is.EqualTo(GdsRegistrationType.ServerPush));
        Assert.That(loaded.Domains, Is.EqualTo("legacy.test,backup.test"));
        Assert.That(loaded.CertificateSubjectName, Is.EqualTo("CN=Legacy"));
        Assert.That(loaded.CertificateStorePath, Is.EqualTo("ua/store"));
        Assert.That(loaded.CertificatePublicKeyPath, Is.EqualTo("ua/application.cer"));
        Assert.That(loaded.CertificatePrivateKeyPath, Is.EqualTo("ua/application.key"));
        Assert.That(loaded.TrustListStorePath, Is.EqualTo("ua/trusted"));
        Assert.That(loaded.IssuerListStorePath, Is.EqualTo("ua/issuers"));
        Assert.That(loaded.HttpsCertificatePublicKeyPath, Is.EqualTo("https/server.cer"));
        Assert.That(loaded.HttpsCertificatePrivateKeyPath, Is.EqualTo("https/server.key"));
        Assert.That(loaded.HttpsTrustListStorePath, Is.EqualTo("https/trusted"));
        Assert.That(loaded.HttpsIssuerListStorePath, Is.EqualTo("https/issuers"));
        Assert.That(loaded.PushEndpoint!.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        Assert.That(loaded.PushEndpoint.SecurityPolicyUri, Is.EqualTo("urn:fixture:policy"));
    }

    [TestCase("<RegisteredApplicationContext/>")]
    [TestCase("{}")]
    public void EmptyDocumentPreservesUnregisteredAndAbsentEndpointSemantics(string input)
    {
        using var temporary = new TemporaryCertificateStores();
        string path = Path.Combine(temporary.Root, "empty.context");
        File.WriteAllText(path, input);

        RegisteredApplicationContext loaded = RegisteredApplicationContextXml.Load(path);

        Assert.That(loaded.ApplicationId.IsNull, Is.True);
        Assert.That(loaded.IsRegistered, Is.False);
        Assert.That(loaded.RegistrationType, Is.EqualTo(GdsRegistrationType.ClientPull));
        Assert.That(loaded.PushEndpoint, Is.Null);
        Assert.That(loaded.ServerCapabilities, Is.Empty);
        Assert.That(loaded.DiscoveryUrls, Is.Empty);
        Assert.That(loaded.CertificatePublicKeyPath, Is.Null);
        Assert.That(loaded.HttpsCertificatePublicKeyPath, Is.Null);
    }

    [TestCase("")]
    [TestCase("{ malformed")]
    [TestCase("<OtherDocument/>")]
    [TestCase("null")]
    public void InvalidDocumentReportsTheActualOwnedPathAndDoesNotRewriteIt(string input)
    {
        using var temporary = new TemporaryCertificateStores();
        string path = Path.Combine(temporary.Root, "invalid.context");
        File.WriteAllText(path, input);
        Assert.That(() => RegisteredApplicationContextXml.Load(path),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(path));
        Assert.That(File.ReadAllText(path), Is.EqualTo(input));
        Assert.That(() => RegisteredApplicationContextXml.Load(Path.Combine(temporary.Root, "absent.json")),
            Throws.TypeOf<FileNotFoundException>());
    }

    private static readonly string[] s_dtoConversionCopiesListsAndPreservesBothCertificatePathFamiliExpected =
    [
        "opc.tcp://assembly.example.test:4840",
        "https://assembly.test",
    ];
    private static readonly string[] s_dtoConversionCopiesListsAndPreservesBothCertificatePathFamiliExpected2 =
    [
        "DA",
        "HA",
    ];
    private static readonly string[] s_loadReadsLegacyXmlListsAndBothApplicationAndHttpsDeliverySettExpected =
    [
        "opc.tcp://legacy.test:4840",
        "https://legacy.test",
    ];
}

internal static class RegistrationTestData
{
    public static RegisteredApplicationContext Create()
    {
        return new RegisteredApplicationContext(
            new NodeId("assembly", 2), "urn:fixture:assembly", "Assembly line", "urn:fixture:product",
            GdsRegistrationType.ServerPush,
            ["opc.tcp://assembly.example.test:4840", "https://assembly.test"], ["DA", "HA"],
            Domains: "assembly.test,backup.test",
            CertificateStorePath: "ua/store",
            CertificateSubjectName: "CN=Assembly",
            CertificatePublicKeyPath: "ua/application.cer",
            CertificatePrivateKeyPath: "ua/application.key",
            TrustListStorePath: "ua/trusted",
            IssuerListStorePath: "ua/issuers",
            HttpsCertificatePublicKeyPath: "https/server.cer",
            HttpsCertificatePrivateKeyPath: "https/server.key",
            HttpsTrustListStorePath: "https/trusted",
            HttpsIssuerListStorePath: "https/issuers",
            PushEndpoint: new EndpointDescription
            {
                EndpointUrl = "opc.tcp://assembly.example.test:4840",
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                Server = new ApplicationDescription
                {
                    ApplicationName = new LocalizedText("Assembly server"),
                    ApplicationUri = "urn:fixture:assembly-server"
                },
                UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Certificate) { PolicyId = "x509-operator" }]
            });
    }

    public static void AssertPaths(RegisteredApplicationContext context)
    {
        Assert.That(context.Domains, Is.EqualTo("assembly.test,backup.test"));
        Assert.That(context.CertificateStorePath, Is.EqualTo("ua/store"));
        Assert.That(context.CertificateSubjectName, Is.EqualTo("CN=Assembly"));
        Assert.That(context.CertificatePublicKeyPath, Is.EqualTo("ua/application.cer"));
        Assert.That(context.CertificatePrivateKeyPath, Is.EqualTo("ua/application.key"));
        Assert.That(context.TrustListStorePath, Is.EqualTo("ua/trusted"));
        Assert.That(context.IssuerListStorePath, Is.EqualTo("ua/issuers"));
        Assert.That(context.HttpsCertificatePublicKeyPath, Is.EqualTo("https/server.cer"));
        Assert.That(context.HttpsCertificatePrivateKeyPath, Is.EqualTo("https/server.key"));
        Assert.That(context.HttpsTrustListStorePath, Is.EqualTo("https/trusted"));
        Assert.That(context.HttpsIssuerListStorePath, Is.EqualTo("https/issuers"));
    }
}

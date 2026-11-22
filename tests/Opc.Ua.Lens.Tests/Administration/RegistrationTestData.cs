// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Gds;

namespace UaLens.Tests.Administration;

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

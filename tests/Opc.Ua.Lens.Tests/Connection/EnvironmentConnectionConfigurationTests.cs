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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Identity;
using UaLens.Connection;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class EnvironmentConnectionConfigurationTests
{
    [Test]
    public void NoEnvironmentSettingsDisableAutomaticConnection()
    {
        bool result = EnvironmentConnectionConfiguration.TryCreate(
            _ => null,
            out EnvironmentConnectionConfiguration? configuration,
            out string? error);

        Assert.That(result, Is.True);
        Assert.That(configuration, Is.Null);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void InconsistentSecuritySettingsAreRejected()
    {
        var values = new Dictionary<string, string>
        {
            ["UALENS_ENDPOINT_URL"] = "opc.tcp://localhost:62542/PumpDeviceIntegrationServer",
            ["UALENS_SECURITY_MODE"] = "SignAndEncrypt",
            ["UALENS_SECURITY_POLICY"] = "None"
        };

        bool result = EnvironmentConnectionConfiguration.TryCreate(
            name => values.GetValueOrDefault(name),
            out EnvironmentConnectionConfiguration? configuration,
            out string? error);

        Assert.That(result, Is.False);
        Assert.That(configuration, Is.Null);
        Assert.That(error, Does.Contain("inconsistent"));
    }

    [TestCase("Basic128Rsa15")]
    [TestCase("Basic256")]
    [TestCase("http://opcfoundation.org/UA/SecurityPolicy#Basic128Rsa15")]
    public void DeprecatedPreSha2SecurityPoliciesAreRejected(string securityPolicy)
    {
        var values = new Dictionary<string, string>
        {
            ["UALENS_ENDPOINT_URL"] = "opc.tcp://localhost:62542/PumpDeviceIntegrationServer",
            ["UALENS_SECURITY_MODE"] = "SignAndEncrypt",
            ["UALENS_SECURITY_POLICY"] = securityPolicy
        };

        bool result = EnvironmentConnectionConfiguration.TryCreate(
            name => values.GetValueOrDefault(name),
            out EnvironmentConnectionConfiguration? configuration,
            out string? error);

        Assert.That(result, Is.False);
        Assert.That(configuration, Is.Null);
        Assert.That(error, Does.Contain("supported policy"));
    }

    [Test]
    public async Task AnonymousSettingsSelectTheExactEndpoint()
    {
        var values = new Dictionary<string, string>
        {
            ["UALENS_ENDPOINT_URL"] = "opc.tcp://localhost:62542/PumpDeviceIntegrationServer",
            ["UALENS_APPLICATION_URI"] = "urn:localhost:OPCFoundation:PumpDeviceIntegrationServer",
            ["UALENS_SECURITY_MODE"] = "None",
            ["UALENS_SECURITY_POLICY"] = "None"
        };
        Assert.That(EnvironmentConnectionConfiguration.TryCreate(
            name => values.GetValueOrDefault(name),
            out EnvironmentConnectionConfiguration? configuration,
            out string? error), Is.True, error);
        using (configuration)
        {
            EndpointDescription endpoint = CreateEndpoint(UserTokenType.Anonymous);
            ConnectionSelection selection = await configuration!.CreateSelectionAsync(
                [endpoint],
                SubscriptionEngineKind.ChannelV2,
                CancellationToken.None).ConfigureAwait(false);
            await using (selection.ConfigureAwait(false))
            {
                Assert.That(selection.Profile.EndpointUrl, Is.EqualTo(endpoint.EndpointUrl));
                Assert.That(selection.Profile.ServerApplicationUri, Is.EqualTo(values["UALENS_APPLICATION_URI"]));
                Assert.That(selection.Profile.IdentityType, Is.EqualTo(UserTokenType.Anonymous));
            }
        }
    }

    [Test]
    public async Task UsernameSettingsCreateAReusableCredentialProvider()
    {
        var values = new Dictionary<string, string>
        {
            ["UALENS_ENDPOINT_URL"] = "opc.tcp://localhost:62542/PumpDeviceIntegrationServer",
            ["UALENS_USERNAME"] = "operator",
            ["UALENS_PASSWORD"] = "secret",
            ["UALENS_SECURITY_MODE"] = "SignAndEncrypt",
            ["UALENS_SECURITY_POLICY"] = "Basic256Sha256"
        };
        Assert.That(EnvironmentConnectionConfiguration.TryCreate(
            name => values.GetValueOrDefault(name),
            out EnvironmentConnectionConfiguration? configuration,
            out string? error), Is.True, error);
        using (configuration)
        {
            EndpointDescription endpoint = CreateEndpoint(
                UserTokenType.UserName,
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Basic256Sha256);
            ConnectionSelection selection = await configuration!.CreateSelectionAsync(
                [endpoint],
                SubscriptionEngineKind.ChannelV2,
                CancellationToken.None).ConfigureAwait(false);
            await using (selection.ConfigureAwait(false))
            {
                IUserIdentity identity = await selection.Provider
                    .AcquireIdentityAsync(
                        endpoint,
                        ServiceMessageContext.Create(null),
                        CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(identity.DisplayName, Is.EqualTo("operator"));
                    Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.UserName));
                }
                finally
                {
                    await ConnectionCredentials.ReleaseIdentityAsync(identity).ConfigureAwait(false);
                }
            }
        }
    }

    private static EndpointDescription CreateEndpoint(
        UserTokenType tokenType,
        MessageSecurityMode securityMode = MessageSecurityMode.None,
        string securityPolicyUri = SecurityPolicies.None)
    {
        return new EndpointDescription
        {
            EndpointUrl = "opc.tcp://localhost:62542/PumpDeviceIntegrationServer",
            SecurityMode = securityMode,
            SecurityPolicyUri = securityPolicyUri,
            Server = new ApplicationDescription
            {
                ApplicationUri = "urn:localhost:OPCFoundation:PumpDeviceIntegrationServer"
            },
            UserIdentityTokens =
            [
                new UserTokenPolicy(tokenType)
                {
                    PolicyId = tokenType.ToString(),
                    SecurityPolicyUri = securityPolicyUri
                }
            ]
        };
    }
}

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
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.PubSub;

namespace UaLens.Tests.PubSub;

[TestFixture]
public sealed class PubSubConfigurationTests
{
    [Test]
    public void DefaultConfigurationRestoresOfflineWithoutSelectingUnsecuredMode()
    {
        JsonElement state = PubSubStateCodec.Capture(new PubSubConfiguration());
        PubSubConfiguration restored = PubSubStateCodec.Restore(state);

        Assert.That(restored.Endpoint, Is.Empty);
        Assert.That(restored.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        Assert.That(restored.Publication, Is.EqualTo(PubSubPublication.Disabled));
        Assert.That(restored.WriteBackEnabled, Is.False);
        Assert.That(restored.ActionResponderEnabled, Is.False);
        Assert.That(PubSubConfigurationValidation.Inspect(restored).Count, Is.GreaterThan(0));
        Assert.That(restored.Fields.ToList().Select(field => field.Name),
            Is.EqualTo(s_defaultFieldNames));
    }

    [TestCase(99, 30, 100)]
    [TestCase(60001, 30, 100)]
    [TestCase(1000, 0, 100)]
    [TestCase(1000, 601, 100)]
    [TestCase(1000, 30, 0)]
    [TestCase(1000, 30, 10001)]
    public void PublicationBoundsRejectUnboundedOrExcessiveWork(int interval, int duration, int samples)
    {
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            Publication = PubSubPublication.Synthetic,
            PublishingIntervalMs = interval,
            DurationSeconds = duration,
            MaxPublishedMessages = samples
        };
        Assert.That(() => PubSubConfigurationValidation.RequireValid(configuration), Throws.ArgumentException);
    }

    [TestCase("opc.udp://user:credential-marker@239.0.0.1:49331")]
    [TestCase("opc.udp://239.0.0.1:49331?password=credential-marker")]
    [TestCase("opc.udp://239.0.0.1:49331#key-marker")]
    [TestCase("opc.udp://239.0.0.1:49331/arbitrary-file")]
    public void EndpointsCannotHideCredentialsOrProviderPaths(string endpoint)
    {
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with { Endpoint = endpoint };
        Assert.That(() => PubSubStateCodec.Capture(configuration), Throws.ArgumentException);
        Assert.That(() => PubSubConfigurationValidation.RequireValid(configuration), Throws.ArgumentException);
    }

    [TestCase("password")]
    [TestCase("securityKey")]
    [TestCase("rawConfiguration")]
    [TestCase("allowPublication")]
    [TestCase("autoStart")]
    public void UnknownSensitiveOrActiveStatePropertiesAreRejected(string property)
    {
        string json = "{\"" + property + "\":\"not-a-real-secret\"}";
        Assert.That(() => PubSubStateCodec.Parse(json), Throws.InstanceOf<JsonException>());
    }

    [TestCase("C:\\providers\\keys")]
    [TestCase("https://authority/token")]
    [TestCase("../provider")]
    [TestCase("password=value")]
    public void ProviderReferencesAreOpaqueRegisteredIdentifiers(string reference)
    {
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with { TransportProviderId = reference };
        Assert.That(() => PubSubStateCodec.Capture(configuration), Throws.ArgumentException);
    }

    [Test]
    public void SavedSecurityIntentContainsReferencesButNoMaterialOrAuthorization()
    {
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityGroupId = "sample-group",
            SecurityProviderId = "configured-sks",
            Publication = PubSubPublication.Synthetic
        };
        JsonElement state = PubSubStateCodec.Capture(configuration);
        string json = state.GetRawText();
        PubSubConfiguration restored = PubSubStateCodec.Restore(state);

        Assert.That(json, Does.Contain("configured-sks"));
        Assert.That(json, Does.Not.Contain("password"));
        Assert.That(json, Does.Not.Contain("signingKey"));
        Assert.That(json, Does.Not.Contain("encryptingKey"));
        Assert.That(json, Does.Not.Contain("allowPublication"));
        Assert.That(json, Does.Not.Contain("observations"));
        Assert.That(restored.SecurityProviderId, Is.EqualTo("configured-sks"));
        Assert.That(restored.Fields[1].Type, Is.EqualTo(BuiltInType.Int32));
    }

    [Test]
    public void MissingOrMismatchedSecurityCannotFallBackToNone()
    {
        PubSubConfiguration secured = PubSubTestRuntime.Configuration with
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt
        };
        Assert.That(() => PubSubConfigurationValidation.RequireValid(secured), Throws.ArgumentException);
        PubSubConfiguration json = PubSubTestRuntime.Configuration with
        {
            Profile = PubSubProfile.MqttJson,
            Endpoint = "mqtts://broker.example:8883",
            Topic = "sample",
            BrokerAuthentication = PubSubBrokerAuthentication.Anonymous,
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityProviderId = "keys",
            SecurityGroupId = "group"
        };
        Assert.That(() => PubSubConfigurationValidation.RequireValid(json), Throws.ArgumentException);
    }

    [Test]
    public void WorkloadAuthorizationsAreSeparateAndRequiredAgainAfterRestore()
    {
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            Publication = PubSubPublication.Synthetic
        };
        PubSubConfiguration restored = PubSubStateCodec.Restore(PubSubStateCodec.Capture(configuration));
        Assert.That(() => PubSubConfigurationValidation.RequireAuthorization(restored, new()),
            Throws.InvalidOperationException);
        Assert.That(() => PubSubConfigurationValidation.RequireAuthorization(
            restored, new PubSubStartAuthorization(AllowUnsecured: true)), Throws.InvalidOperationException);
        Assert.That(() => PubSubConfigurationValidation.RequireAuthorization(
            restored, new PubSubStartAuthorization(AllowUnsecured: true, AllowPublication: true)), Throws.Nothing);
    }

    [Test]
    public void WriteBackAndResponderNeedIndependentConsent()
    {
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            WriteBackEnabled = true,
            ActionResponderEnabled = true
        };
        Assert.That(() => PubSubConfigurationValidation.RequireAuthorization(
            configuration, new PubSubStartAuthorization(AllowUnsecured: true)), Throws.InvalidOperationException);
        Assert.That(() => PubSubConfigurationValidation.RequireAuthorization(
            configuration, new PubSubStartAuthorization(AllowUnsecured: true, AllowWriteBack: true)),
            Throws.InvalidOperationException);
        Assert.That(() => PubSubConfigurationValidation.RequireAuthorization(
            configuration, new PubSubStartAuthorization(
                AllowUnsecured: true, AllowWriteBack: true, AllowResponder: true)), Throws.Nothing);
    }

    [TestCase("ns=2;s=Value", false)]
    [TestCase("nsu=urn:sample;s=Value", true)]
    [TestCase("i=2258", true)]
    [TestCase("svr=2;i=2258", false)]
    public void UaMappingsMustBePortableAndLocalToTheSelectedServer(string nodeId, bool valid)
    {
        Assert.That(PubSubConfigurationValidation.IsPortableNodeId(nodeId), Is.EqualTo(valid));
    }

    [Test]
    public void NullAndExcessiveSchemaAreRejectedBeforeAnyProviderCanRun()
    {
        Assert.That(() => PubSubStateCodec.Parse("{\"endpoint\":null}"), Throws.InstanceOf<JsonException>());
        Assert.That(() => PubSubStateCodec.Parse("{\"fields\":[null]}"), Throws.InstanceOf<JsonException>());
        string fields = string.Join(",", Enumerable.Repeat("{\"name\":\"Value\",\"type\":\"Int32\"}", 33));
        Assert.That(() => PubSubStateCodec.Parse("{\"fields\":[" + fields + "]}"), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void AdvancedProfilesHaveActionableUnconfiguredPrerequisites()
    {
        var factory = new PubSubRuntimeFactory(DefaultTelemetry.Create(static _ => { }));
        PubSubConfiguration dtls = PubSubTestRuntime.Configuration with
        {
            Profile = PubSubProfile.DtlsUadp,
            Endpoint = "opc.dtls://127.0.0.1:49331"
        };
        Assert.That(factory.Inspect(dtls, false).Contains(issue =>
            issue.Readiness == PubSubReadiness.RequiresConfiguration &&
            issue.Detail.Contains("DTLS", StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public void ActionInputParsingIsTypedBoundedAndNeverBoxesVariants()
    {
        var fields = PubSubActionInputs.Parse("[{\"name\":\"input\",\"type\":\"Int32\",\"text\":\"21\"}]");
        Assert.That(fields, Has.Count.EqualTo(1));
        Assert.That(fields[0].Value.TryGetValue(out int value), Is.True);
        Assert.That(value, Is.EqualTo(21));
        Assert.That(() => PubSubActionInputs.Parse("[{\"name\":\"input\",\"type\":\"Double\",\"text\":\"NaN\"}]"),
            Throws.InstanceOf<JsonException>());
        Assert.That(() => PubSubActionInputs.Parse("[{\"name\":\"input\",\"type\":\"String\",\"text\":\"" +
            new string('x', 513) + "\"}]"), Throws.InstanceOf<JsonException>());
    }

    private static readonly string[] s_defaultFieldNames = ["BoolToggle", "Int32", "DateTime"];
}

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
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AMB.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    public sealed partial class AmbCompanionProviderTests
    {
        [Test]
        public async Task AssetIdPreparationIsReadOnlyAndExecutionUsesTheReviewedValueOnceAsync()
        {
            var test = new AssetTaskContext();
            CompanionValue[] fields = [new("assetId", Variant.From("reviewed-id"))];
            CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "set-asset-id", fields, CancellationToken.None).ConfigureAwait(false);
            Assert.That(test.Writes, Is.Empty);
            fields[0] = new CompanionValue("assetId", Variant.From("changed-after-review"));

            CompanionOperationResult result = await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "set-asset-id", prepared, null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(test.Writes, Has.Count.EqualTo(1));
            Assert.That(test.Writes[0].NodeId, Is.EqualTo(test.AssetIdNode));
            Assert.That(test.Writes[0].Value.WrappedValue, Is.EqualTo(Variant.From("reviewed-id")));
            Assert.That(result.Summary, Does.Contain("accepted").And.Contain("not atomically"));
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "set-asset-id", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(test.Writes, Has.Count.EqualTo(1));
        }

        [TestCase("asset-value")]
        [TestCase("product-identity")]
        [TestCase("product-version")]
        [TestCase("asset-version")]
        [TestCase("writable")]
        [TestCase("identity")]
        [TestCase("certificate")]
        [TestCase("namespace")]
        [TestCase("expired")]
        [TestCase("grant")]
        public async Task ChangedAssetEvidenceRejectsAnOldPreparationWithoutWritingAsync(string change)
        {
            var test = new AssetTaskContext();
            CompanionTaskInput prepared = await test.PrepareAsync().ConfigureAwait(false);
            switch (change)
            {
                case "asset-value":
                    test.Session.SetValue(test.AssetIdNode, Variant.From("another-editor"));
                    break;
                case "product-identity":
                    test.Session.ReadHandler = (read, _) =>
                    {
                        DataValue value = test.Session.Read(read);
                        return value.WrappedValue == Variant.From("urn:asset:one")
                            ? new DataValue(Variant.From("urn:asset:replacement"))
                            : value;
                    };
                    break;
                case "product-version":
                    test.Session.SetValue(test.ProductVersion, Variant.From("2"));
                    break;
                case "asset-version":
                    test.Session.SetValue(test.AssetVersion, Variant.From("2"));
                    break;
                case "writable":
                    test.Session.SetValue(test.AssetIdNode, Variant.From((byte)AccessLevels.CurrentRead),
                        Attributes.UserAccessLevel);
                    break;
                case "identity":
                    test.Session.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
                    break;
                case "certificate":
                    test.Endpoint.ServerCertificate = ByteString.From("changed-public-peer"u8);
                    break;
                case "namespace":
                    test.Session.NamespaceUris.GetIndexOrAppend("urn:changed");
                    break;
                case "expired":
                    test.Clock.Now = test.Expires;
                    break;
                case "grant":
                    test.GrantRevision = "changed";
                    break;
                default:
                    throw new AssertionException("Unknown change partition.");
            }
            Type failureType = change is "expired" or "grant"
                ? typeof(UnauthorizedAccessException)
                : typeof(ServiceResultException);
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "set-asset-id", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf(failureType)).ConfigureAwait(false);
            Assert.That(test.Writes, Is.Empty);
        }

        [TestCase("no-policy")]
        [TestCase("missing-version")]
        [TestCase("none")]
        [TestCase("disconnected")]
        [TestCase("read-only")]
        public async Task AssetIdPreparationRequiresCompleteAuthorizationEvidenceAsync(string missing)
        {
            var test = new AssetTaskContext();
            AmbCompanionProvider provider = test.Provider;
            switch (missing)
            {
                case "no-policy":
                    provider = new AmbCompanionProvider(timeProvider: test.Clock);
                    break;
                case "missing-version":
                    test.Session.SetValue(test.AssetVersion, Variant.Null);
                    break;
                case "none":
                    test.Endpoint.SecurityMode = MessageSecurityMode.None;
                    break;
                case "disconnected":
                    test.Session.Session.SetupGet(session => session.Connected).Returns(false);
                    break;
                case "read-only":
                    test.Session.SetValue(test.AssetIdNode, Variant.From((byte)AccessLevels.CurrentRead),
                        Attributes.UserAccessLevel);
                    break;
                default:
                    throw new AssertionException("Unknown missing-evidence partition.");
            }
            Type failureType = missing is "no-policy" or "none" or "disconnected"
                ? typeof(UnauthorizedAccessException)
                : typeof(ServiceResultException);
            await Assert.ThatAsync(async () => await provider.PrepareInputAsync(
                test.Context, test.Target, "set-asset-id", [new("assetId", Variant.From("reviewed"))],
                CancellationToken.None).ConfigureAwait(false), Throws.TypeOf(failureType)).ConfigureAwait(false);
            Assert.That(test.Writes, Is.Empty);
        }

        [TestCase("timeout")]
        [TestCase("uncertain")]
        public async Task UnknownWriteOutcomeNeverReplaysTheRequestAsync(string outcome)
        {
            var test = new AssetTaskContext();
            CompanionTaskInput prepared = await test.PrepareAsync().ConfigureAwait(false);
            test.WriteFailure = outcome == "timeout" ? new TimeoutException("Unknown applied state") : null;
            test.WriteStatus = StatusCodes.Uncertain;
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "set-asset-id", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf(outcome == "timeout" ? typeof(TimeoutException) : typeof(ServiceResultException)))
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "set-asset-id", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(test.Writes, Has.Count.EqualTo(1));
        }

        [TestCase("file:///C:/secret")]
        [TestCase("https://user:password@example.invalid/manual")]
        [TestCase("https://example.invalid/manual?token=secret")]
        [TestCase("https://example.invalid/%0aheader")]
        public async Task UnsafeDocumentationUrisAreRejectedBeforeNetworkReadsAsync(string uri)
        {
            var test = new AssetTaskContext();
            await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "write-link",
                [new("linkId", Variant.From(test.AssetIdNode)), new("uri", Variant.From(uri))],
                CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>()).ConfigureAwait(false);
            Assert.That(test.Session.BrowseCalls.IsEmpty, Is.True);
            Assert.That(test.Writes, Is.Empty);
        }

        private sealed class AssetTaskContext
        {
            public AssetTaskContext()
            {
                (Session, Target) = CreateAsset();
                Endpoint = new EndpointDescription("opc.tcp://127.0.0.1:58124/AmbServer")
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    ServerCertificate = ByteString.From("owned-public-certificate"u8),
                    Server = new ApplicationDescription { ApplicationUri = "urn:owned:AmbServer" }
                };
                Session.Session.SetupGet(session => session.Connected).Returns(true);
                Session.Session.SetupGet(session => session.SessionId).Returns(new NodeId(1234u));
                Session.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
                Session.Session.SetupGet(session => session.Endpoint).Returns(Endpoint);
                var client = new AmbClient(Session.Session.Object, Session.Telemetry, new AmbClientOptions());
                ProductVersion = Session.AddProperty(client.GetCategoryId(AssetAliasCategory.ByProductInstanceUri),
                    Namespaces.OpcUa, BrowseNames.NodeVersion, Variant.From("1"));
                AssetVersion = Session.AddProperty(client.GetCategoryId(AssetAliasCategory.ByAssetId),
                    Namespaces.OpcUa, BrowseNames.NodeVersion, Variant.From("1"));
                AssetIdNode = Session.AddProperty(Target.NodeId, Opc.Ua.Di.Namespaces.OpcUaDi,
                    Opc.Ua.Di.BrowseNames.AssetId, Variant.From("old-id"));
                Session.SetValue(AssetIdNode, Variant.From((byte)AccessLevels.CurrentReadOrWrite), Attributes.AccessLevel);
                Session.SetValue(AssetIdNode, Variant.From((byte)AccessLevels.CurrentReadOrWrite), Attributes.UserAccessLevel);
                Session.SetValue(AssetIdNode, Variant.From(DataTypeIds.String), Attributes.DataType);
                Session.Session.Setup(session => session.WriteAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader? _, ArrayOf<WriteValue> writes, CancellationToken token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (WriteValue write in writes)
                        {
                            Writes.Add(write);
                        }
                        if (WriteFailure is not null)
                        {
                            throw WriteFailure;
                        }
                        return new ValueTask<WriteResponse>(new WriteResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [WriteStatus]
                        });
                    });
                Context = Session.Context();
                Expires = Clock.Now.AddMinutes(1);
                var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<CompanionDeploymentGrant>(
                        new CompanionDeploymentGrant("owned-amb", GrantRevision, Expires)));
                Provider = new AmbCompanionProvider(policy.Object, Clock);
            }

            public IndustrialCompanionTestSession Session { get; }
            public CompanionTarget Target { get; }
            public CompanionContext Context { get; }
            public AmbCompanionProvider Provider { get; }
            public EndpointDescription Endpoint { get; }
            public AssetClock Clock { get; } = new();
            public DateTimeOffset Expires { get; }
            public string GrantRevision { get; set; } = "1";
            public NodeId ProductVersion { get; }
            public NodeId AssetVersion { get; }
            public NodeId AssetIdNode { get; }
            public List<WriteValue> Writes { get; } = [];
            public StatusCode WriteStatus { get; set; }
            public Exception? WriteFailure { get; set; }

            public ValueTask<CompanionTaskInput> PrepareAsync()
            {
                return Provider.PrepareInputAsync(Context, Target, "set-asset-id",
                    [new("assetId", Variant.From("reviewed"))], CancellationToken.None);
            }
        }

        private sealed class AssetClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}

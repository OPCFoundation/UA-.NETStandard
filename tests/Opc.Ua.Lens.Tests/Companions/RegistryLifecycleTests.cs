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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;
using Registry = Opc.Ua.XRegistry;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class RegistryLifecycleTests
    {
        [TestCase("create-group")]
        [TestCase("create-version")]
        [TestCase("delete-group")]
        public async Task LifecycleCallsTheExactTypedRequestWithoutOverwritingOrRetrying(string operation)
        {
            var fixture = new RegistryFixture(operation == "create-group" ? "registry" : "group");
            using (fixture)
            {
                CompanionTaskInput prepared = await fixture.Provider.PrepareInputAsync(
                    fixture.Context, fixture.Target, operation, RegistryFixture.Inputs(operation),
                    CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(fixture.Server.Calls.IsEmpty, Is.True);

                CompanionOperationResult result = await fixture.Provider.ExecutePreparedAsync(
                    fixture.Context, fixture.Target, operation, prepared, null, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(fixture.Methods[0], Is.EqualTo(operation switch
                {
                    "create-group" => "CreateGroup",
                    "create-version" => "CreateResource",
                    _ => "Delete"
                }));
                Assert.That(fixture.Server.Calls[0].ObjectId, Is.EqualTo(fixture.Target.NodeId));
                if (operation == "create-version")
                {
                    Assert.That(fixture.Methods, Is.EqualTo(sUpload));
                    Assert.That(fixture.Document.ToArray(), Is.EqualTo("""{"name":"reviewed"}"""u8.ToArray()));
                    Assert.That(Field(result.Values, "Version ID").TryGetValue(out string? version), Is.True);
                    Assert.That(version, Is.EqualTo("v2"));
                }
                else
                {
                    Assert.That(fixture.Methods, Has.Count.EqualTo(1));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ResourceAndVersionDeletesUseTheirOwnEpochAndExplicitScope(bool logical)
        {
            using var fixture = new RegistryFixture(logical ? "resource" : "version");
            CompanionTaskInput prepared = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "delete-resource",
                RegistryFixture.Inputs("delete-resource", logical ? 7u : 23u), CancellationToken.None)
                .ConfigureAwait(false);

            CompanionOperationResult result = await fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "delete-resource", prepared, null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(fixture.Methods, Is.EqualTo(sDelete));
            Assert.That(fixture.Server.Calls[0].InputArguments[0].TryGetValue(out uint epoch), Is.True);
            Assert.That(epoch, Is.EqualTo(logical ? 7u : 23u));
            Assert.That(prepared.Review, Does.Contain(logical ? "MetaEpoch" : "Epoch"));
            Assert.That(prepared.Review, Does.Contain(logical ? "all versions" : "selected Version"));
            Assert.That(result.Summary, Does.Contain("accepted"));
        }

        [TestCase("/groups/group/resources/resource", true)]
        [TestCase("/groups/group/resources/resource/versions/v1", false)]
        public async Task ExplicitCollectionXidsRetainLogicalAndVersionScopes(string xid, bool logical)
        {
            using var fixture = new RegistryFixture(logical ? "resource" : "version");
            fixture.Server.SetValue(fixture.Xid, Variant.From(xid));
            CompanionTaskInput prepared = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "delete-resource",
                RegistryFixture.Inputs("delete-resource", logical ? 7u : 23u), CancellationToken.None)
                .ConfigureAwait(false);

            await fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "delete-resource", prepared, null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(prepared.Review, Does.Contain(logical ? "MetaEpoch" : "Epoch"));
            Assert.That(fixture.Server.Calls[0].InputArguments[0].TryGetValue(out uint epoch), Is.True);
            Assert.That(epoch, Is.EqualTo(logical ? 7u : 23u));
            Assert.That(fixture.Methods, Is.EqualTo(sDelete));
        }

        [Test]
        public async Task ALogicalResourcesVersionEpochCannotAuthorizeItsDeletion()
        {
            using var fixture = new RegistryFixture("resource");

            await Assert.ThatAsync(() => fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "delete-resource",
                RegistryFixture.Inputs("delete-resource", 23), CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [TestCase("epoch")]
        [TestCase("scope")]
        [TestCase("permission")]
        public async Task ChangedEpochScopeOrPermissionRejectsBeforeMutation(string change)
        {
            using var fixture = new RegistryFixture("version");
            CompanionTaskInput prepared = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "delete-resource",
                RegistryFixture.Inputs("delete-resource", 23), CancellationToken.None).ConfigureAwait(false);
            switch (change)
            {
                case "epoch":
                    fixture.Server.SetValue(fixture.Epoch, Variant.From(24u));
                    break;
                case "scope":
                    fixture.Server.SetValue(fixture.Xid, Variant.From("/group/resource"));
                    fixture.Server.SetValue(fixture.MetaEpoch, Variant.From(23u));
                    break;
                case "permission":
                    fixture.Server.SetValue(fixture.Delete, Variant.From(false), Attributes.UserExecutable);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }

            await Assert.ThatAsync(() => fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, "delete-resource", prepared, null, CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task DeletionRequiresItsOwnExplicitScopeConfirmation()
        {
            using var fixture = new RegistryFixture("group");

            await Assert.ThatAsync(() => fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, "delete-group",
                [new("epoch", Variant.From(7u)), new("includeChildren", Variant.From(false))], CancellationToken.None)
                .AsTask(), Throws.ArgumentException).ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [TestCase("null-id")]
        [TestCase("different-version")]
        public async Task InvalidCreationResultsAreNotPresentedAsTheReviewedCreation(string failure)
        {
            using var fixture = new RegistryFixture(failure == "null-id" ? "registry" : "group");
            string operation = failure == "null-id" ? "create-group" : "create-version";
            CompanionTaskInput prepared = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Target, operation, RegistryFixture.Inputs(operation), CancellationToken.None)
                .ConfigureAwait(false);
            fixture.CreatedNodeId = failure == "null-id" ? NodeId.Null : fixture.CreatedNodeId;
            fixture.AssignedVersion = "different";

            await Assert.ThatAsync(() => fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Target, operation, prepared, null, CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
        }

        private static readonly string[] sUpload = ["CreateResource", "Write", "Close"];
        private static readonly string[] sDelete = ["Delete"];

        private sealed class RegistryFixture : IDisposable
        {
            public RegistryFixture(string role)
            {
                NodeId node = Server.Id(role);
                Server.AddObject(node, role switch
                {
                    "registry" => Registry.ObjectTypeIds.RegistryType,
                    "group" => Registry.ObjectTypeIds.GroupType,
                    _ => Registry.ObjectTypeIds.ResourceType
                });
                Target = new CompanionTarget("xregistry", node, role, role switch
                {
                    "registry" => "Registry",
                    "group" => "Registry group",
                    _ => "Registry resource/version"
                });
                Epoch = Server.AddProperty(node, NamespaceUri, "Epoch",
                    Variant.From(role is "resource" or "version" ? 23u : 7u));
                MetaEpoch = Server.AddProperty(node, NamespaceUri, "MetaEpoch", Variant.From(7u));
                Xid = Server.AddProperty(node, NamespaceUri, "Xid",
                    Variant.From(role == "version" ? "/group/resource/versions/v1" : "/group/resource"));
                Delete = AddMethod("Delete");
                AddMethod("CreateGroup");
                AddMethod("CreateResource");
                CreatedNodeId = Server.Id("created");
                Server.CallHandler = (request, token) =>
                {
                    if (request.MethodId == Server.Resolve(Registry.MethodIds.RegistryType_CreateGroup))
                    {
                        Methods.Add("CreateGroup");
                        Assert.That(request.InputArguments[0].TryGetValue(out string? id), Is.True);
                        Assert.That(id, Is.EqualTo("new-group"));
                        return Good([Variant.From(CreatedNodeId)]);
                    }
                    if (request.MethodId == Server.Resolve(Registry.MethodIds.GroupType_CreateResource))
                    {
                        Methods.Add("CreateResource");
                        Assert.That(request.InputArguments[0].TryGetValue(out string? id), Is.True);
                        Assert.That(id, Is.EqualTo("resource"));
                        Assert.That(request.InputArguments[1].TryGetValue(out string? version), Is.True);
                        Assert.That(version, Is.EqualTo("v2"));
                        Assert.That(request.InputArguments[2].TryGetValue(out bool open), Is.True);
                        Assert.That(open, Is.True);
                        return Good([Variant.From(CreatedNodeId), Variant.From(AssignedVersion), Variant.From(42u)]);
                    }
                    if (request.MethodId == MethodIds.FileType_Write)
                    {
                        Methods.Add("Write");
                        Assert.That(request.ObjectId, Is.EqualTo(CreatedNodeId));
                        Assert.That(request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                        Document.Write(bytes.Span);
                        return Good();
                    }
                    if (request.MethodId == MethodIds.FileType_Close)
                    {
                        Methods.Add("Close");
                        Assert.That(token.IsCancellationRequested, Is.False);
                        return Good();
                    }
                    Assert.That(request.MethodId, Is.EqualTo(Server.Resolve(role == "group" ?
                        Registry.MethodIds.GroupType_Delete : Registry.MethodIds.ResourceType_Delete)));
                    Methods.Add("Delete");
                    return Good();
                };
            }

            public IndustrialCompanionTestSession Server { get; } = new();
            public RegistryCompanionProvider Provider { get; } = new();
            public CompanionContext Context => Server.Context();
            public CompanionTarget Target { get; }
            public NodeId Epoch { get; }
            public NodeId MetaEpoch { get; }
            public NodeId Xid { get; }
            public NodeId Delete { get; }
            public NodeId CreatedNodeId { get; set; }
            public string AssignedVersion { get; set; } = "v2";
            public List<string> Methods { get; } = [];
            public MemoryStream Document { get; } = new();

            public static ArrayOf<CompanionValue> Inputs(string operation, uint epoch = 7)
            {
                return operation switch
                {
                    "create-group" => [new("epoch", Variant.From(epoch)), new("id", Variant.From("new-group"))],
                    "create-version" =>
                    [
                        new("epoch", Variant.From(epoch)), new("id", Variant.From("resource")),
                        new("version", Variant.From("v2")), new("document", Variant.From("""{"name":"reviewed"}"""))
                    ],
                    _ => [new("epoch", Variant.From(epoch)), new("includeChildren", Variant.From(true))]
                };
            }

            public void Dispose()
            {
                Document.Dispose();
            }

            private NodeId AddMethod(string name)
            {
                NodeId method = Server.AddProperty(Target.NodeId, NamespaceUri, name, default);
                Server.SetValue(method, Variant.From((int)NodeClass.Method), Attributes.NodeClass);
                Server.SetValue(method, Variant.From(true), Attributes.Executable);
                Server.SetValue(method, Variant.From(true), Attributes.UserExecutable);
                return method;
            }

            private const string NamespaceUri = Registry.XRegistryWellKnown.XRegistryNamespaceUri;
        }
    }
}

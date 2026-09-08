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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using XRegistry = Opc.Ua.XRegistry;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;

namespace UaLens.Tests.Companions;

[TestFixture]
[Category("IndustrialCompanions")]
public sealed class IndustrialRegistryCompanionProviderTests
{
    [Test]
    public async Task RegistryInspectionReportsLiveVersionEpochAndModelFileWithoutMutatingAsync()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Registry(session);
        NodeId model = session.Id("model-file");
        session.AddObject(model, ObjectTypeIds.FileType);
        session.AddChild(target.NodeId, model, XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "Model");
        var provider = new RegistryCompanionProvider();
        CompanionInspection inspection = await provider.InspectAsync(session.Context(), target, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(Field(inspection.Values, "SpecVersion").TryGetValue(out string? version), Is.True);
        Assert.That(version, Is.EqualTo("1.0"));
        Assert.That(Field(inspection.Values, "Epoch").TryGetValue(out uint epoch), Is.True);
        Assert.That(epoch, Is.EqualTo(27u));
        Assert.That(Field(inspection.Values, "Model file NodeId").TryGetValue(out NodeId actual), Is.True);
        Assert.That(actual, Is.EqualTo(model));
        Assert.That(inspection.Operations.Contains(operation => operation.Id == "inspect-model"), Is.True);
        Assert.That(inspection.Operations.Contains(operation =>
            operation.Id == "register-sample" && operation.Safety == CompanionOperationSafety.SampleMutation), Is.True);
        Assert.That(provider.Descriptor.Maturity, Does.Contain("0.6.0").And.Contain("provisional"));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SampleRegistrationUsesTheGenericClientAndDoesNotOverwriteAnExistingVersionAsync(bool created)
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Registry(session);
        NodeId group = session.Id("group");
        NodeId version = session.Id("version");
        int writes = 0;
        using var cancellation = new CancellationTokenSource();
        session.CallHandler = (request, token) =>
        {
            if (request.MethodId == session.Resolve(XRegistry.MethodIds.RegistryType_GetOrCreateGroup))
            {
                Assert.That(request.ObjectId, Is.EqualTo(target.NodeId));
                Assert.That(request.InputArguments[0].TryGetValue(out string? groupId), Is.True);
                Assert.That(groupId, Is.EqualTo("ualens-samples"));
                Assert.That(token, Is.EqualTo(cancellation.Token));
                return Good([Variant.From(group), Variant.From(false)]);
            }
            if (request.MethodId == session.Resolve(XRegistry.MethodIds.GroupType_GetOrCreateResource))
            {
                Assert.That(request.ObjectId, Is.EqualTo(group));
                Assert.That(request.InputArguments[0].TryGetValue(out string? id), Is.True);
                Assert.That(id, Is.EqualTo("ualens-sample-demo"));
                Assert.That(request.InputArguments[1].TryGetValue(out string? requestedVersion), Is.True);
                Assert.That(requestedVersion, Is.EqualTo("1"));
                Assert.That(request.InputArguments[2].TryGetValue(out bool open), Is.True);
                Assert.That(open, Is.True);
                return Good([Variant.From(version), Variant.From("1"), Variant.From(19u), Variant.From(created)]);
            }
            Assert.That(request.ObjectId, Is.EqualTo(version));
            Assert.That(request.InputArguments[0].TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.EqualTo(19u));
            if (request.MethodId == MethodIds.FileType_Write)
            {
                Assert.That(request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                Assert.That(bytes.Length, Is.InRange(1, 1024));
                writes++;
                return Good();
            }
            Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
            Assert.That(token.IsCancellationRequested, Is.False);
            return Good();
        };

        CompanionOperationResult result = await new RegistryCompanionProvider()
            .ExecuteAsync(session.Context(), target, "register-sample", "demo", cancellation.Token)
            .ConfigureAwait(false);
        Assert.That(Field(result.Values, "Created").TryGetValue(out bool actualCreated), Is.True);
        Assert.That(actualCreated, Is.EqualTo(created));
        Assert.That(Field(result.Values, "Version NodeId").TryGetValue(out NodeId actualVersion), Is.True);
        Assert.That(actualVersion, Is.EqualTo(version));
        Assert.That(writes, Is.EqualTo(created ? 1 : 0));
        Assert.That(session.Calls, Has.Count.EqualTo(created ? 4 : 3));
    }

    [Test]
    public async Task JsonInspectionReturnsOnlyBoundedStructuralMetadataAndReleasesTheReadHandleAsync()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Resource(session);
        ByteString document = ByteString.From(
            """{"securityDefinitions":{"scheme":"basic"},"private":"redaction-probe"}"""u8);
        int reads = 0;
        bool closed = false;
        session.CallHandler = (request, token) =>
        {
            Assert.That(request.ObjectId, Is.EqualTo(target.NodeId));
            if (request.MethodId == MethodIds.FileType_Open)
            {
                Assert.That(request.InputArguments[0].TryGetValue(out byte mode), Is.True);
                Assert.That(mode, Is.EqualTo(1));
                return Good([Variant.From(5u)]);
            }
            Assert.That(request.InputArguments[0].TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.EqualTo(5u));
            if (request.MethodId == MethodIds.FileType_Read)
            {
                reads++;
                return Good([Variant.From(reads == 1 ? document : ByteString.Empty)]);
            }
            Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
            Assert.That(token.IsCancellationRequested, Is.False);
            closed = true;
            return Good();
        };
        CompanionOperationResult result = await new RegistryCompanionProvider()
            .ExecuteAsync(session.Context(), target, "inspect-json", null, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(Field(result.Values, "Document bytes").TryGetValue(out int length), Is.True);
        Assert.That(length, Is.EqualTo(document.Length));
        Assert.That(Field(result.Values, "JSON member count").TryGetValue(out int count), Is.True);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(Field(result.Values, "Document SHA-256").TryGetValue(out ByteString digest), Is.True);
        Assert.That(digest.Span.SequenceEqual(SHA256.HashData(document.Span)), Is.True);
        Assert.That(result.Values.Contains(value =>
            value.Text.Contains("redaction-probe", StringComparison.Ordinal)), Is.False);
        Assert.That(reads, Is.EqualTo(2));
        Assert.That(closed, Is.True);
    }

    [Test]
    public void DocumentLimitStopsTheReadAndStillClosesTheHandle()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Resource(session);
        int reads = 0;
        bool closed = false;
        session.CallHandler = (request, _) =>
        {
            if (request.MethodId == MethodIds.FileType_Open)
            {
                return Good([Variant.From(3u)]);
            }
            if (request.MethodId == MethodIds.FileType_Read)
            {
                reads++;
                Assert.That(request.InputArguments[1].TryGetValue(out int count), Is.True);
                return Good([Variant.From(ByteString.From(new byte[count]))]);
            }
            Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
            closed = true;
            return Good();
        };
        Assert.That(
            async () => await new RegistryCompanionProvider()
                .ExecuteAsync(session.Context(), target, "inspect-json", null, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        Assert.That(reads, Is.EqualTo(17));
        Assert.That(closed, Is.True);
    }

    [Test]
    public async Task ADocumentExactlyAtTheLimitIsReadCompletelyAsync()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Resource(session);
        ByteString document = ByteString.From(Encoding.UTF8.GetBytes(
            "{}" + new string(' ', IndustrialCompanionAccess.MaxDocumentBytes - 2)));
        int offset = 0;
        bool closed = false;
        session.CallHandler = (request, _) =>
        {
            if (request.MethodId == MethodIds.FileType_Open)
            {
                return Good([Variant.From(3u)]);
            }
            if (request.MethodId == MethodIds.FileType_Read)
            {
                Assert.That(request.InputArguments[1].TryGetValue(out int count), Is.True);
                int length = Math.Min(count, document.Length - offset);
                ByteString chunk = ByteString.From(document.Span.Slice(offset, length));
                offset += length;
                return Good([Variant.From(chunk)]);
            }
            Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
            closed = true;
            return Good();
        };
        CompanionOperationResult result = await new RegistryCompanionProvider()
            .ExecuteAsync(session.Context(), target, "inspect-json", null, CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(Field(result.Values, "Document bytes").TryGetValue(out int bytes), Is.True);
        Assert.That(bytes, Is.EqualTo(IndustrialCompanionAccess.MaxDocumentBytes));
        Assert.That(offset, Is.EqualTo(IndustrialCompanionAccess.MaxDocumentBytes));
        Assert.That(closed, Is.True);
    }

    [Test]
    public void ReadFailureIsPreservedWhenClosingTheFileAlsoFails()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Resource(session);
        var failure = new ServiceResultException(StatusCodes.BadTimeout);
        bool closed = false;
        session.CallHandler = (request, token) =>
        {
            if (request.MethodId == MethodIds.FileType_Open)
            {
                return Good([Variant.From(3u)]);
            }
            if (request.MethodId == MethodIds.FileType_Read)
            {
                throw failure;
            }
            Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
            Assert.That(token.IsCancellationRequested, Is.False);
            closed = true;
            throw new ServiceResultException(StatusCodes.BadConnectionClosed);
        };
        Assert.That(
            async () => await new RegistryCompanionProvider()
                .ExecuteAsync(session.Context(), target, "inspect-json", null, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.Exception.SameAs(failure));
        Assert.That(closed, Is.True);
    }

    [Test]
    public void CancellationDuringReadClosesWithAnIndependentToken()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Resource(session);
        using var cancellation = new CancellationTokenSource();
        bool closed = false;
        session.CallHandler = (request, token) =>
        {
            if (request.MethodId == MethodIds.FileType_Open)
            {
                return Good([Variant.From(9u)]);
            }
            if (request.MethodId == MethodIds.FileType_Read)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
            Assert.That(token.IsCancellationRequested, Is.False);
            Assert.That(token, Is.Not.EqualTo(cancellation.Token));
            closed = true;
            return Good();
        };
        Assert.That(
            async () => await new RegistryCompanionProvider()
                .ExecuteAsync(session.Context(), target, "inspect-json", null, cancellation.Token)
                .ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(closed, Is.True);
    }

    [Test]
    public void FieldLimitIsCheckedBeforeOpeningAFile()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Resource(session);
        Assert.That(
            async () => await new RegistryCompanionProvider()
                .ExecuteAsync(session.Context(maxFields: 2), target, "inspect-json", null, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [Test]
    public void RegistrationPropagatesTransportFailure()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Registry(session);
        var failure = new ServiceResultException(StatusCodes.BadConnectionClosed);
        session.CallHandler = (_, _) => throw failure;
        Assert.That(
            async () => await new RegistryCompanionProvider()
                .ExecuteAsync(session.Context(), target, "register-sample", "one", CancellationToken.None)
                .ConfigureAwait(false),
            Throws.Exception.SameAs(failure));
    }

    private static CompanionTarget Registry(IndustrialCompanionTestSession session)
    {
        NodeId registry = session.Id("registry");
        session.AddObject(registry, XRegistry.ObjectTypeIds.RegistryType);
        session.AddProperty(registry, XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "RegistryId",
            Variant.From("test-registry"));
        session.AddProperty(registry, XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "SpecVersion",
            Variant.From("1.0"));
        session.AddProperty(registry, XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "Epoch", Variant.From(27u));
        session.AddProperty(registry, XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "Name",
            Variant.From("Test registry"));
        return new CompanionTarget("xregistry", registry, "Registry", "Registry");
    }

    private static CompanionTarget Resource(IndustrialCompanionTestSession session)
    {
        NodeId resource = session.Id("document");
        session.AddObject(resource, XRegistry.ObjectTypeIds.ResourceType);
        return new CompanionTarget("xregistry", resource, "Document", "Registry resource/version");
    }
}

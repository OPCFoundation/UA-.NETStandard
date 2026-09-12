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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions;

[TestFixture]
[Category("IndustrialCompanions")]
public sealed class IndustrialCompanionDiscoveryTests
{
    [TestCase("di")]
    [TestCase("isa95")]
    [TestCase("wot")]
    [TestCase("xregistry")]
    public async Task DiscoversOnlyTypedInstancesAndPreservesSourceIdentityAsync(string providerId)
    {
        var session = new IndustrialCompanionTestSession();
        (ICompanionProvider provider, ExpandedNodeId type, string kind) = Provider(providerId);
        NodeId root = Root(session, providerId);
        var instance = new NodeId(ByteString.From("instance"u8), session.InstanceNamespaceIndex);
        NodeId unrelated = session.Id("unrelated");
        session.AddObject(instance, type, "Typed instance");
        session.AddObject(unrelated, ObjectTypeIds.FolderType);
        session.AddChild(root, unrelated);
        session.AddChild(root, instance);

        ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(session.Context(), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0].NodeId, Is.EqualTo(instance));
        Assert.That(targets[0].NodeId.NamespaceIndex, Is.EqualTo(session.InstanceNamespaceIndex));
        Assert.That(targets[0].TypeName, Is.EqualTo(kind));
        Assert.That(targets[0].DisplayName, Is.EqualTo("Typed instance"));
        Assert.That(targets[0].ProviderId, Is.EqualTo(providerId));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [TestCase("di")]
    [TestCase("isa95")]
    [TestCase("wot")]
    [TestCase("xregistry")]
    public void NamespacePresenceWithoutInstancesIsExplicitlyUnsupported(string providerId)
    {
        var session = new IndustrialCompanionTestSession();
        ICompanionProvider provider = Provider(providerId).Provider;
        Assert.That(
            async () => await provider.DiscoverAsync(session.Context(), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [TestCase("di")]
    [TestCase("isa95")]
    [TestCase("wot")]
    [TestCase("xregistry")]
    public void MissingNamespaceDoesNotModifyTheNamespaceTableOrBrowse(string providerId)
    {
        var session = new IndustrialCompanionTestSession(includeModels: false);
        int count = session.NamespaceUris.Count;
        ICompanionProvider provider = Provider(providerId).Provider;
        Assert.That(
            async () => await provider.DiscoverAsync(session.Context(), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported));
        Assert.That(session.NamespaceUris.Count, Is.EqualTo(count));
        Assert.That(session.BrowseCalls.IsEmpty, Is.True);
    }

    [TestCase("di")]
    [TestCase("isa95")]
    [TestCase("wot")]
    [TestCase("xregistry")]
    public void AlreadyCancelledDiscoveryDoesNotCallTheSession(string providerId)
    {
        var session = new IndustrialCompanionTestSession();
        ICompanionProvider provider = Provider(providerId).Provider;
        Assert.That(
            async () => await provider.DiscoverAsync(session.Context(), new CancellationToken(true))
                .ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(session.BrowseCalls.IsEmpty, Is.True);
    }

    [TestCase("di")]
    [TestCase("isa95")]
    [TestCase("wot")]
    [TestCase("xregistry")]
    public async Task BrowseFailureIsPropagatedAndNeverCachedAsAnEmptyDiscoveryAsync(string providerId)
    {
        var session = new IndustrialCompanionTestSession();
        (ICompanionProvider provider, ExpandedNodeId type, _) = Provider(providerId);
        NodeId instance = session.Id("retry");
        session.AddObject(instance, type);
        session.AddChild(Root(session, providerId), instance);
        var failure = new ServiceResultException(StatusCodes.BadConnectionClosed);
        session.BrowseHandler = (_, _) => throw failure;
        Assert.That(
            async () => await provider.DiscoverAsync(session.Context(), CancellationToken.None).ConfigureAwait(false),
            Throws.Exception.SameAs(failure));

        session.BrowseHandler = null;
        ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(session.Context(), CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0].NodeId, Is.EqualTo(instance));
    }

    [TestCaseSource(nameof(s_browseFailures))]
    public void PerNodeBrowseStatusIsNotTreatedAsMissingInstances(StatusCode status)
    {
        var session = new IndustrialCompanionTestSession
        {
            BrowseHandler = (_, _) => new BrowseResult { StatusCode = status }
        };
        Assert.That(
            async () => await new RegistryCompanionProvider().DiscoverAsync(session.Context(), CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status));
    }

    [Test]
    public async Task ReachingTargetLimitReleasesContinuationWithAnIndependentTokenAsync()
    {
        var session = new IndustrialCompanionTestSession();
        NodeId first = session.Id("first");
        session.AddObject(first, Opc.Ua.XRegistry.ObjectTypeIds.RegistryType);
        session.AddChild(ObjectIds.ObjectsFolder, first);
        ByteString point = ByteString.From("continuation"u8);
        session.BrowseHandler = (description, _) =>
        {
            BrowseResult result = session.Browse(description);
            result.ContinuationPoint = point;
            return result;
        };
        session.NextHandler = (release, _, token) =>
        {
            Assert.That(release, Is.True);
            Assert.That(token.IsCancellationRequested, Is.False);
            return Next();
        };

        ArrayOf<CompanionTarget> targets = await new RegistryCompanionProvider()
            .DiscoverAsync(session.Context(maxTargets: 1), CancellationToken.None).ConfigureAwait(false);

        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0].NodeId, Is.EqualTo(first));
        Assert.That(session.Released, Has.Count.EqualTo(1));
        Assert.That(session.Released[0], Is.EqualTo(point));
    }

    [Test]
    public void CancellationAfterBrowseAcquiresAContinuationStillReleasesIt()
    {
        var session = new IndustrialCompanionTestSession();
        using var cancellation = new CancellationTokenSource();
        ByteString point = ByteString.From("cancelled-page"u8);
        session.BrowseHandler = (_, _) =>
        {
            cancellation.Cancel();
            return new BrowseResult
            {
                ContinuationPoint = point,
                References = [new ReferenceDescription { NodeClass = NodeClass.Object }]
            };
        };
        session.NextHandler = (release, _, token) =>
        {
            Assert.That(release, Is.True);
            Assert.That(token.IsCancellationRequested, Is.False);
            return Next();
        };
        Assert.That(
            async () => await new RegistryCompanionProvider()
                .DiscoverAsync(session.Context(), cancellation.Token).ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(session.Released, Has.Count.EqualTo(1));
        Assert.That(session.Released[0], Is.EqualTo(point));
    }

    [Test]
    public void BrowseNextFailurePreservesOriginalFailureAndReleasesPreviousContinuation()
    {
        ByteString point = ByteString.From("page"u8);
        var failure = new ServiceResultException(StatusCodes.BadTimeout);
        var session = new IndustrialCompanionTestSession
        {
            BrowseHandler = (_, _) => new BrowseResult { ContinuationPoint = point },
            NextHandler = (release, _, _) => release ? throw new InvalidOperationException("cleanup") : throw failure
        };
        Assert.That(
            async () => await new RegistryCompanionProvider().DiscoverAsync(session.Context(), CancellationToken.None)
                .ConfigureAwait(false),
            Throws.Exception.SameAs(failure));
        Assert.That(session.Released, Has.Count.EqualTo(1));
        Assert.That(session.Released[0], Is.EqualTo(point));
    }

    [Test]
    public void EndlessEmptyContinuationPagesHitTheRequestBudgetAndAreReleased()
    {
        ByteString point = ByteString.From("endless"u8);
        int pages = 0;
        var session = new IndustrialCompanionTestSession
        {
            BrowseHandler = (_, _) => new BrowseResult { ContinuationPoint = point },
            NextHandler = (release, _, _) =>
            {
                pages++;
                return release ? Next() : Next(new BrowseResult { ContinuationPoint = point });
            }
        };
        Assert.That(
            async () => await new RegistryCompanionProvider()
                .DiscoverAsync(session.Context(maxTargets: 1), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        Assert.That(pages, Is.LessThanOrEqualTo(257));
        Assert.That(session.Released, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SubtypeMatchingUsesTheServerTypeAncestryAsync()
    {
        var session = new IndustrialCompanionTestSession();
        NodeId subtype = session.Id("domain-registry-type");
        NodeId instance = session.Id("domain-registry");
        session.AddObject(instance, new ExpandedNodeId(subtype));
        session.AddChild(ObjectIds.ObjectsFolder, instance);
        session.SetReferences(subtype, BrowseDirection.Inverse, ReferenceTypeIds.HasSubtype,
        [
            new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(session.Resolve(Opc.Ua.XRegistry.ObjectTypeIds.RegistryType)),
                NodeClass = NodeClass.ObjectType
            }
        ]);
        var provider = new RegistryCompanionProvider();
        ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(session.Context(), CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0].NodeId, Is.EqualTo(instance));
        Assert.That(targets[0].TypeName, Is.EqualTo("Registry"));
    }

    [Test]
    public void NonmatchingObjectGraphCannotGrowBeyondTheDiscoveryBudget()
    {
        var session = new IndustrialCompanionTestSession();
        for (int index = 0; index < 129; index++)
        {
            NodeId node = session.Id("folder-" + index);
            session.AddObject(node, ObjectTypeIds.FolderType);
            session.AddChild(ObjectIds.ObjectsFolder, node);
        }
        Assert.That(
            async () => await new RegistryCompanionProvider()
                .DiscoverAsync(session.Context(maxTargets: 1), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [TestCase("di")]
    [TestCase("isa95")]
    [TestCase("wot")]
    [TestCase("xregistry")]
    public void InspectionRejectsChangedObjectClassBeforeAnyMethodCall(string providerId)
    {
        var session = new IndustrialCompanionTestSession();
        (ICompanionProvider provider, ExpandedNodeId type, string kind) = Provider(providerId);
        NodeId instance = session.Id("changed");
        session.AddObject(instance, type);
        session.SetValue(instance, Variant.From((int)NodeClass.Variable), Attributes.NodeClass);
        var target = new CompanionTarget(providerId, instance, "Changed", kind);
        Assert.That(
            async () => await provider.InspectAsync(session.Context(), target, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [TestCase("di")]
    [TestCase("isa95")]
    [TestCase("wot")]
    [TestCase("xregistry")]
    public void InspectionChecksTheActualTypeRatherThanTrustingTheTargetLabel(string providerId)
    {
        var session = new IndustrialCompanionTestSession();
        (ICompanionProvider provider, _, string kind) = Provider(providerId);
        NodeId instance = session.Id("wrong-type");
        session.AddObject(instance, ObjectTypeIds.FolderType);
        var target = new CompanionTarget(providerId, instance, "Wrong type", kind);
        Assert.That(
            async () => await provider.InspectAsync(session.Context(), target, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadTypeMismatch));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [Test]
    public async Task CrossServerReferencesAreNotReinterpretedAsLocalNodeIdsAsync()
    {
        var session = new IndustrialCompanionTestSession();
        NodeId local = session.Id("local");
        session.AddObject(local, Opc.Ua.XRegistry.ObjectTypeIds.RegistryType);
        session.AddChild(ObjectIds.ObjectsFolder, local);
        BrowseResult original = session.Browse(new BrowseDescription
        {
            NodeId = ObjectIds.ObjectsFolder,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
        });
        session.SetReferences(ObjectIds.ObjectsFolder, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
        [
            new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(local).WithServerIndex(2),
                NodeClass = NodeClass.Object,
                TypeDefinition = new ExpandedNodeId(session.Resolve(Opc.Ua.XRegistry.ObjectTypeIds.RegistryType))
            },
            .. original.References
        ]);
        ArrayOf<CompanionTarget> targets = await new RegistryCompanionProvider()
            .DiscoverAsync(session.Context(), CancellationToken.None).ConfigureAwait(false);
        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0].NodeId, Is.EqualTo(local));
    }

    [Test]
    public void AFailedBrowseNextResponseReleasesBothKnownContinuationTokens()
    {
        ByteString first = ByteString.From("first"u8);
        ByteString second = ByteString.From("second"u8);
        var session = new IndustrialCompanionTestSession
        {
            BrowseHandler = (_, _) => new BrowseResult { ContinuationPoint = first },
            NextHandler = (release, _, _) => release ? Next() : Next(
                new BrowseResult { StatusCode = StatusCodes.BadTimeout, ContinuationPoint = second })
        };
        Assert.That(
            async () => await new RegistryCompanionProvider()
                .DiscoverAsync(session.Context(), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadTimeout));
        Assert.That(session.Released, Has.Count.EqualTo(2));
        Assert.That(session.Released.Contains(first), Is.True);
        Assert.That(session.Released.Contains(second), Is.True);
    }

    [TestCase(1)]
    [TestCase(32)]
    public void SampleSuffixIncludesBothAllowedLengthBoundaries(int length)
    {
        string id = IndustrialCompanionAccess.SampleId(new string('a', length));
        Assert.That(id, Does.StartWith("ualens-sample-"));
        Assert.That(id, Has.Length.EqualTo(14 + length));
    }

    [TestCase(0)]
    [TestCase(33)]
    public void SampleSuffixRejectsTheImmediatelyAdjacentLengths(int length)
    {
        Assert.That(() => IndustrialCompanionAccess.SampleId(new string('a', length)), Throws.ArgumentException);
    }

    private static readonly StatusCode[] s_browseFailures =
        [StatusCodes.BadUserAccessDenied, StatusCodes.BadTimeout, StatusCodes.BadSessionClosed];

    private static (ICompanionProvider Provider, ExpandedNodeId Type, string Kind) Provider(string id)
    {
        return id switch
        {
            "di" => (new DeviceCompanionProvider(), Opc.Ua.Di.ObjectTypeIds.DeviceType, "DI device"),
            "isa95" => (new Isa95CompanionProvider(), Opc.Ua.ISA95.ObjectTypeIds.EquipmentType, "Equipment"),
            "wot" => (new WotCompanionProvider(), Opc.Ua.WotCon.ObjectTypeIds.WoTAssetConnectionManagementType,
                "WoT asset management"),
            "xregistry" => (new RegistryCompanionProvider(), Opc.Ua.XRegistry.ObjectTypeIds.RegistryType, "Registry"),
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
    }

    private static NodeId Root(IndustrialCompanionTestSession session, string providerId)
    {
        return providerId == "di"
            ? NodeId.Create(Opc.Ua.Di.Objects.DeviceSet, Opc.Ua.Di.Namespaces.OpcUaDi, session.NamespaceUris)
            : ObjectIds.ObjectsFolder;
    }

    private static BrowseNextResponse Next(BrowseResult? result = null)
    {
        return new BrowseNextResponse
        {
            ResponseHeader = new ResponseHeader(),
            Results = [result ?? new BrowseResult { StatusCode = StatusCodes.Good }]
        };
    }
}

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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;

namespace UaLens.Tests.Companions;

[TestFixture]
[Category("IndustrialCompanions")]
public sealed class IndustrialIsa95CompanionProviderTests
{
    [Test]
    public async Task DiscoversV1AndV2EndpointsWithoutConflatingTheirNamespaceIdentityAsync()
    {
        var session = new IndustrialCompanionTestSession();
        (CompanionTarget v1, _, _) = JobSet(session, false);
        (CompanionTarget v2, _, _) = JobSet(session, true);
        ArrayOf<CompanionTarget> targets = await new Isa95CompanionProvider()
            .DiscoverAsync(session.Context(), CancellationToken.None).ConfigureAwait(false);
        Assert.That(targets, Has.Count.EqualTo(6));
        Assert.That(targets.Contains(target =>
            target.NodeId == v1.NodeId && target.TypeName == "V1 order receiver"), Is.True);
        Assert.That(targets.Contains(target =>
            target.NodeId == v2.NodeId && target.TypeName == "V2 order receiver"), Is.True);
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StoreSampleUsesTheMatchingVersionClientWithoutStartingTheJobAsync(bool version2)
    {
        var session = new IndustrialCompanionTestSession();
        (CompanionTarget order, _, _) = JobSet(session, version2);
        using var cancellation = new CancellationTokenSource();
        session.CallHandler = (request, token) =>
        {
            Assert.That(request.ObjectId, Is.EqualTo(order.NodeId));
            Assert.That(token, Is.EqualTo(cancellation.Token));
            Assert.That(request.InputArguments, Has.Count.EqualTo(2));
            if (version2)
            {
                Assert.That(request.MethodId, Is.EqualTo(session.Resolve(
                    V2.MethodIds.ISA95JobOrderReceiverObjectType_Store)));
                Assert.That(request.InputArguments[0].TryGetStructure<V2.ISA95JobOrderDataType>(
                    session.MessageContext, out V2.ISA95JobOrderDataType? job), Is.True);
                Assert.That(job!.JobOrderID, Is.EqualTo("ualens-sample-one"));
            }
            else
            {
                Assert.That(request.MethodId, Is.EqualTo(session.Resolve(
                    V1.MethodIds.ISA95JobOrderReceiverObjectType_ReceiveJobOrder)));
                Assert.That(request.InputArguments[0].TryGetValue(out int command), Is.True);
                Assert.That(command, Is.EqualTo((int)V1.ISA95JobOrderCommandEnum.Store));
                Assert.That(request.InputArguments[1].TryGetStructure<V1.ISA95JobOrderDataType>(
                    session.MessageContext, out V1.ISA95JobOrderDataType? job), Is.True);
                Assert.That(job!.ID, Is.EqualTo("ualens-sample-one"));
            }
            return Good([Variant.From(0UL)]);
        };
        CompanionOperationResult result = await new Isa95CompanionProvider()
            .ExecuteAsync(session.Context(), order, "store-sample-job", "one", cancellation.Token)
            .ConfigureAwait(false);

        Assert.That(Field(result.Values, "ISA-95 return status").TryGetValue(out ulong status), Is.True);
        Assert.That(status, Is.Zero);
        Assert.That(result.Summary, Does.Contain("No Start"));
        Assert.That(session.Calls, Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StoreSamplePropagatesNonzeroDomainReturnStatus(bool version2)
    {
        var session = new IndustrialCompanionTestSession();
        (CompanionTarget order, _, _) = JobSet(session, version2);
        session.CallHandler = (_, _) => Good([Variant.From(17UL)]);
        Assert.That(
            async () => await new Isa95CompanionProvider()
                .ExecuteAsync(session.Context(), order, "store-sample-job", "one", CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
        Assert.That(session.Calls, Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RequestSampleResponseForwardsToTheActualResponseProviderAsync(bool version2)
    {
        var session = new IndustrialCompanionTestSession();
        (_, CompanionTarget provider, _) = JobSet(session, version2);
        session.CallHandler = (request, _) =>
        {
            Assert.That(request.ObjectId, Is.EqualTo(provider.NodeId));
            Assert.That(request.InputArguments[0].TryGetValue(out string? id), Is.True);
            Assert.That(id, Is.EqualTo("ualens-sample-one"));
            if (version2)
            {
                Assert.That(request.MethodId, Is.EqualTo(session.Resolve(
                    V2.MethodIds.ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderID)));
                return Good(
                [
                    Variant.FromStructure(new V2.ISA95JobResponseDataType
                    {
                        JobResponseID = "response1",
                        JobOrderID = "ualens-sample-one"
                    }),
                    Variant.From(0UL)
                ]);
            }
            Assert.That(request.MethodId, Is.EqualTo(session.Resolve(
                V1.MethodIds.ISA95JobResponseProviderObjectType_RequestJobResponse)));
            return Good(
            [
                Variant.FromStructure(ArrayOf.Wrapped(
                [
                    new V1.ISA95JobResponseDataType { ID = "response1", JobOrderID = "ualens-sample-one" },
                    new V1.ISA95JobResponseDataType { ID = "response2", JobOrderID = "ualens-sample-one" }
                ])),
                Variant.From(0UL)
            ]);
        };
        CompanionOperationResult result = await new Isa95CompanionProvider()
            .ExecuteAsync(session.Context(), provider, "request-sample-response", "one", CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(Field(result.Values, "Response count").TryGetValue(out int count), Is.True);
        Assert.That(count, Is.EqualTo(version2 ? 1 : 2));
        Assert.That(session.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void RequestDoesNotAcceptAResponseForAnotherJob()
    {
        var session = new IndustrialCompanionTestSession();
        (_, CompanionTarget provider, _) = JobSet(session, true);
        session.CallHandler = (_, _) => Good(
        [
            Variant.FromStructure(new V2.ISA95JobResponseDataType { JobOrderID = "another-job" }),
            Variant.From(0UL)
        ]);
        Assert.That(
            async () => await new Isa95CompanionProvider()
                .ExecuteAsync(session.Context(), provider, "request-sample-response", "one", CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadUnexpectedError));
    }

    [Test]
    public async Task IncompleteEndpointSetRemainsInspectableButCannotStoreASampleAsync()
    {
        var session = new IndustrialCompanionTestSession();
        NodeId orderId = session.Id("isolated-order");
        session.AddObject(orderId, V2.ObjectTypeIds.ISA95JobOrderReceiverObjectType);
        session.AddChild(ObjectIds.ObjectsFolder, orderId);
        var target = new CompanionTarget("isa95", orderId, "Isolated", "V2 order receiver");
        var provider = new Isa95CompanionProvider();
        CompanionInspection inspection = await provider.InspectAsync(session.Context(), target, CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(inspection.Operations, Has.Count.EqualTo(1));
        Assert.That(inspection.Summary, Does.Contain("exactly one"));
        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context(), target, "store-sample-job", "one", CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [Test]
    public async Task InspectionOffersOnlyGatedStoreAndLeavesJobStateUnchangedAsync()
    {
        var session = new IndustrialCompanionTestSession();
        (CompanionTarget order, CompanionTarget response, _) = JobSet(session, true);
        var provider = new Isa95CompanionProvider();
        CompanionInspection inspection = await provider.InspectAsync(session.Context(), order, CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(inspection.Operations.Contains(operation => operation.Id == "store-sample-job" &&
            operation.Safety == CompanionOperationSafety.SampleMutation), Is.True);
        Assert.That(Field(inspection.Values, "Job response provider").TryGetValue(out NodeId responseId), Is.True);
        Assert.That(responseId, Is.EqualTo(response.NodeId));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [Test]
    public void AmbiguousEndpointSetCannotSilentlyChooseTheFirstReceiver()
    {
        var session = new IndustrialCompanionTestSession();
        (CompanionTarget order, _, _) = JobSet(session, true);
        NodeId duplicate = session.Id("duplicate-order");
        session.AddObject(duplicate, V2.ObjectTypeIds.ISA95JobOrderReceiverObjectType);
        session.AddChild(session.Id("V2 job-control"), duplicate);
        Assert.That(
            async () => await new Isa95CompanionProvider()
                .ExecuteAsync(session.Context(), order, "store-sample-job", "one", CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [Test]
    public async Task CommonEquipmentInspectionPreservesTheEnumWireValueAsync()
    {
        var session = new IndustrialCompanionTestSession();
        NodeId equipment = session.Id("equipment");
        session.AddObject(equipment, Opc.Ua.ISA95.ObjectTypeIds.EquipmentType);
        session.AddProperty(equipment, Opc.Ua.ISA95.Namespaces.ISA95, "EquipmentLevel", Variant.From(3));
        var target = new CompanionTarget("isa95", equipment, "Equipment", "Equipment");
        CompanionInspection inspection = await new Isa95CompanionProvider()
            .InspectAsync(session.Context(), target, CancellationToken.None).ConfigureAwait(false);
        Assert.That(Field(inspection.Values, "EquipmentLevel").TryGetValue(out int level), Is.True);
        Assert.That(level, Is.EqualTo(3));
        Assert.That(Field(inspection.Values, "Source NodeId").TryGetValue(out NodeId source), Is.True);
        Assert.That(source, Is.EqualTo(equipment));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [Test]
    public void TypedJobInventoryHonorsTheFieldBudget()
    {
        var session = new IndustrialCompanionTestSession();
        (CompanionTarget order, _, _) = JobSet(session, false);
        session.AddProperty(order.NodeId, V1.Namespaces.ISA95JobControlV1, "JobOrderList",
            Variant.FromStructure(ArrayOf.Wrapped(
            [
                new V1.ISA95JobOrderDataType { ID = "one" },
                new V1.ISA95JobOrderDataType { ID = "two" },
                new V1.ISA95JobOrderDataType { ID = "three" }
            ])));
        Assert.That(
            async () => await new Isa95CompanionProvider()
                .InspectAsync(session.Context(maxFields: 2), order, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [TestCase("")]
    [TestCase("../production")]
    [TestCase("production/job")]
    [TestCase("UPPERCASE")]
    [TestCase("abcdefghijklmnopqrstuvwxyz0123456789")]
    public void SampleInputCannotAddressArbitraryProductionJobs(string input)
    {
        var session = new IndustrialCompanionTestSession();
        (CompanionTarget order, _, _) = JobSet(session, true);
        Assert.That(
            async () => await new Isa95CompanionProvider()
                .ExecuteAsync(session.Context(), order, "store-sample-job", input, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.ArgumentException);
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    private static (CompanionTarget Order, CompanionTarget Provider, CompanionTarget Receiver) JobSet(
        IndustrialCompanionTestSession session,
        bool version2)
    {
        string prefix = version2 ? "V2 " : "V1 ";
        NodeId parent = session.Id(prefix + "job-control");
        session.AddObject(parent, ObjectTypeIds.FolderType);
        session.AddChild(ObjectIds.ObjectsFolder, parent);
        NodeId order = session.Id(prefix + "order");
        NodeId provider = session.Id(prefix + "provider");
        NodeId receiver = session.Id(prefix + "receiver");
        session.AddObject(order, version2
            ? V2.ObjectTypeIds.ISA95JobOrderReceiverObjectType
            : V1.ObjectTypeIds.ISA95JobOrderReceiverObjectType);
        session.AddObject(provider, version2
            ? V2.ObjectTypeIds.ISA95JobResponseProviderObjectType
            : V1.ObjectTypeIds.ISA95JobResponseProviderObjectType);
        session.AddObject(receiver, version2
            ? V2.ObjectTypeIds.ISA95JobResponseReceiverObjectType
            : V1.ObjectTypeIds.ISA95JobResponseReceiverObjectType);
        session.AddChild(parent, order);
        session.AddChild(parent, provider);
        session.AddChild(parent, receiver);
        return (
            new CompanionTarget("isa95", order, prefix + "order", prefix + "order receiver"),
            new CompanionTarget("isa95", provider, prefix + "provider", prefix + "response provider"),
            new CompanionTarget("isa95", receiver, prefix + "receiver", prefix + "response receiver"));
    }
}

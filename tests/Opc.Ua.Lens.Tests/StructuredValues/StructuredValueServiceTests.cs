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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.StructuredValues;

namespace UaLens.Tests.StructuredValues;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class StructuredValueServiceTests
{
    [TestCaseSource(nameof(s_missingDefinitionStatuses))]
    public async Task AuthoritativeMissingDefinitionIsCachedAsync(StatusCode status)
    {
        using var context = new StructuredValueTestContext();
        var dataType = new NodeId(5000u, context.NamespaceIndex);
        context.Reader = (ids, _) =>
        {
            Assert.That(ids.Count, Is.EqualTo(1));
            Assert.That(ids[0].NodeId, Is.EqualTo(dataType));
            Assert.That(ids[0].AttributeId, Is.EqualTo(Attributes.DataTypeDefinition));
            return ValueTask.FromResult(StructuredValueTestContext.Reply(Variant.Null, status));
        };

        Assert.That(await context.Service.ResolveAsync(dataType).ConfigureAwait(false), Is.Null);
        Assert.That(await context.Service.ResolveAsync(dataType).ConfigureAwait(false), Is.Null);
        Assert.That(context.Reads, Is.EqualTo(1));
    }

    [Test]
    public async Task NullExtensionObjectIsAnAuthoritativeMissingDefinitionAsync()
    {
        using var context = new StructuredValueTestContext();
        context.Reader = (_, _) => ValueTask.FromResult(
            StructuredValueTestContext.Reply(Variant.From(ExtensionObject.Null)));
        Assert.That(await context.Service.ResolveAsync(
            new NodeId(5000u, context.NamespaceIndex)).ConfigureAwait(false), Is.Null);
    }

    [Test]
    public async Task GeneratedDefinitionCanBeResolvedWhenServerDoesNotExposeTheAttributeAsync()
    {
        using var context = new StructuredValueTestContext();
        context.Reader = (_, _) => ValueTask.FromResult(
            StructuredValueTestContext.Reply(Variant.Null, StatusCodes.BadAttributeIdInvalid));

        DataTypeDefinition? definition = await context.Service.ResolveAsync(DataTypeIds.Argument).ConfigureAwait(false);

        Assert.That(definition, Is.InstanceOf<StructureDefinition>());
        Assert.That(((StructureDefinition)definition!).Fields.Count, Is.EqualTo(5));
    }

    [TestCaseSource(nameof(s_failedReadStatuses))]
    public async Task BadAndUncertainReadsPropagateAndAreRetriedAsync(StatusCode status)
    {
        using var context = new StructuredValueTestContext();
        var definition = new StructureDefinition();
        context.Reader = (_, _) => ValueTask.FromResult(context.Reads == 1
            ? StructuredValueTestContext.Reply(Variant.Null, status)
            : StructuredValueTestContext.Reply(Variant.FromStructure(definition)));

        await Assert.ThatAsync(
            () => context.Service.ResolveAsync(DataTypeIds.Argument),
            Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(status)).ConfigureAwait(false);
        Assert.That(await context.Service.ResolveAsync(DataTypeIds.Argument).ConfigureAwait(false),
            Is.InstanceOf<StructureDefinition>());
        Assert.That(context.Reads, Is.EqualTo(2), "A generated schema must not conceal a denied or failed read.");
    }

    [Test]
    public async Task TransportExceptionIsNotTurnedIntoMissingSchemaAsync()
    {
        using var context = new StructuredValueTestContext();
        var dataType = new NodeId(5000u, context.NamespaceIndex);
        context.Reader = (_, _) => context.Reads == 1
            ? ValueTask.FromException<ReadResponse>(new IOException("Connection interrupted."))
            : ValueTask.FromResult(StructuredValueTestContext.Reply(Variant.Null));

        await Assert.ThatAsync(() => context.Service.ResolveAsync(dataType), Throws.TypeOf<IOException>())
            .ConfigureAwait(false);
        Assert.That(await context.Service.ResolveAsync(dataType).ConfigureAwait(false), Is.Null);
        Assert.That(context.Reads, Is.EqualTo(2));
    }

    [Test]
    public async Task ServiceLevelUnsupportedReadIsNotAnAuthoritativeMissingAttributeAsync()
    {
        using var context = new StructuredValueTestContext();
        context.Reader = (_, _) => ValueTask.FromResult(new ReadResponse
        {
            ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadNotSupported }
        });
        await Assert.ThatAsync(() => context.Service.ResolveAsync(DataTypeIds.Argument),
            Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
        context.Reader = (_, _) => ValueTask.FromResult(StructuredValueTestContext.Reply(Variant.Null));
        Assert.That(await context.Service.ResolveAsync(DataTypeIds.Argument).ConfigureAwait(false),
            Is.InstanceOf<StructureDefinition>());
        Assert.That(context.Reads, Is.EqualTo(2));
    }

    [TestCase(0)]
    [TestCase(2)]
    public async Task InvalidResultCountIsRejectedAndNotCachedAsync(int resultCount)
    {
        using var context = new StructuredValueTestContext();
        context.Reader = (_, _) => ValueTask.FromResult(new ReadResponse
        {
            ResponseHeader = new ResponseHeader(),
            Results = new DataValue[resultCount]
        });
        await Assert.ThatAsync(() => context.Service.ResolveAsync(DataTypeIds.Argument),
            Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
        context.Reader = (_, _) => ValueTask.FromResult(StructuredValueTestContext.Reply(Variant.Null));
        Assert.That(await context.Service.ResolveAsync(DataTypeIds.Argument).ConfigureAwait(false),
            Is.InstanceOf<StructureDefinition>());
        Assert.That(context.Reads, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NonDefinitionPayloadIsRejectedAndRetriedAsync(bool encodeable)
    {
        using var context = new StructuredValueTestContext();
        Variant invalid = encodeable ? Variant.FromStructure(new Argument()) : Variant.From("not metadata");
        context.Reader = (_, _) => ValueTask.FromResult(StructuredValueTestContext.Reply(
            context.Reads == 1 ? invalid : Variant.FromStructure(new EnumDefinition())));

        await Assert.ThatAsync(() => context.Service.ResolveAsync(DataTypeIds.NamingRuleType),
            Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
        Assert.That(await context.Service.ResolveAsync(DataTypeIds.NamingRuleType).ConfigureAwait(false),
            Is.InstanceOf<EnumDefinition>());
        Assert.That(context.Reads, Is.EqualTo(2));
    }

    [Test]
    public async Task CancellationAfterAReadCompletesDoesNotPopulateTheCacheAsync()
    {
        using var context = new StructuredValueTestContext();
        using var cancellation = new CancellationTokenSource();
        var reply = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Reader = (_, token) =>
        {
            Assert.That(token, Is.EqualTo(cancellation.Token));
            return new ValueTask<ReadResponse>(reply.Task);
        };
        Task<DataTypeDefinition?> pending = context.Service.ResolveAsync(DataTypeIds.Argument, cancellation.Token);
        cancellation.Cancel();
        reply.SetResult(StructuredValueTestContext.Reply(Variant.FromStructure(new StructureDefinition())));
        await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        context.Reader = (_, _) => ValueTask.FromResult(
            StructuredValueTestContext.Reply(Variant.FromStructure(new StructureDefinition())));
        Assert.That(await context.Service.ResolveAsync(DataTypeIds.Argument).ConfigureAwait(false),
            Is.InstanceOf<StructureDefinition>());
        Assert.That(context.Reads, Is.EqualTo(2));
    }

    [Test]
    public async Task CanceledCacheHitStillPropagatesCancellationAsync()
    {
        using var context = new StructuredValueTestContext();
        context.Reader = (_, _) => ValueTask.FromResult(StructuredValueTestContext.Reply(Variant.Null));
        await context.Service.ResolveAsync(DataTypeIds.Argument).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThatAsync(() => context.Service.ResolveAsync(DataTypeIds.Argument, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(context.Reads, Is.EqualTo(1));
    }

    [TestCase("session")]
    [TestCase("namespaceAppend")]
    [TestCase("namespaceRemap")]
    [TestCase("context")]
    [TestCase("server")]
    [TestCase("endpoint")]
    [TestCase("refresh")]
    public async Task RealSessionAndNamespaceGenerationsInvalidateCachedDefinitionsAsync(string change)
    {
        using var context = new StructuredValueTestContext();
        context.Reader = (_, _) => ValueTask.FromResult(StructuredValueTestContext.Reply(
            Variant.FromStructure(new StructureDefinition
            {
                Fields = [StructuredValueTestContext.Field($"Read{context.Reads}", DataTypeIds.Int32)]
            })));
        await context.Service.ResolveAsync(DataTypeIds.Argument).ConfigureAwait(false);
        switch (change)
        {
            case "session":
                context.SessionId = new NodeId(2u);
                break;
            case "namespaceAppend":
                context.MessageContext.NamespaceUris.Append("urn:additional");
                break;
            case "namespaceRemap":
                context.MessageContext.NamespaceUris.Update([Namespaces.OpcUa, "urn:replacement"]);
                break;
            case "context":
                context.MessageContext = new ServiceMessageContext(
                    context.MessageContext, context.MessageContext.Telemetry);
                break;
            case "server":
                context.Endpoint.Server.ApplicationUri = "urn:other:server";
                break;
            case "endpoint":
                context.Endpoint.EndpointUrl = "opc.tcp://other.unit.test:4840";
                break;
            case "refresh":
                context.Service.Refresh();
                break;
        }

        var definition = (StructureDefinition)(await context.Service.ResolveAsync(DataTypeIds.Argument)
            .ConfigureAwait(false))!;
        Assert.That(definition.Fields[0].Name, Is.EqualTo("Read2"));
        Assert.That(context.Reads, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InFlightReadFromAnOldGenerationCannotRepopulateTheCacheAsync(bool explicitRefresh)
    {
        using var context = new StructuredValueTestContext();
        var reply = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Reader = (_, _) => new ValueTask<ReadResponse>(reply.Task);
        Task<DataTypeDefinition?> pending = context.Service.ResolveAsync(DataTypeIds.Argument);
        if (explicitRefresh)
        {
            context.Service.Refresh();
        }
        else
        {
            context.SessionId = new NodeId(2u);
        }
        reply.SetResult(StructuredValueTestContext.Reply(Variant.FromStructure(new StructureDefinition())));

        await Assert.ThatAsync(() => pending, Throws.TypeOf<ServiceResultException>()
            .With.Property(nameof(ServiceResultException.StatusCode))
            .EqualTo(StatusCodes.BadInvalidState))
            .ConfigureAwait(false);
        context.Reader = (_, _) => ValueTask.FromResult(StructuredValueTestContext.Reply(Variant.Null));
        Assert.That(await context.Service.ResolveAsync(DataTypeIds.Argument).ConfigureAwait(false),
            Is.InstanceOf<StructureDefinition>());
        Assert.That(context.Reads, Is.EqualTo(2));
    }

    [Test]
    public async Task CallersCannotMutateCachedDefinitionsAsync()
    {
        using var context = new StructuredValueTestContext();
        context.Reader = (_, _) => ValueTask.FromResult(StructuredValueTestContext.Reply(Variant.FromStructure(
            new StructureDefinition { Fields = [StructuredValueTestContext.Field("Original", DataTypeIds.Int32)] })));
        var first = (StructureDefinition)(await context.Service.ResolveAsync(DataTypeIds.Argument)
            .ConfigureAwait(false))!;
        first.Fields[0].Name = "Changed";

        var second = (StructureDefinition)(await context.Service.ResolveAsync(DataTypeIds.Argument)
            .ConfigureAwait(false))!;
        Assert.That(second.Fields[0].Name, Is.EqualTo("Original"));
        Assert.That(context.Reads, Is.EqualTo(1));
    }

    [Test]
    public async Task RefreshDoesNotReuseServerDerivedFactoryMetadataWhenTheDefinitionDisappearsAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeType type = context.Register(
            "ServerDefined", StructureType.Structure,
            [StructuredValueTestContext.Field("OldField", DataTypeIds.Int32)]);
        context.Reader = (_, _) => ValueTask.FromResult(context.Reads == 1
            ? StructuredValueTestContext.Reply(Variant.FromStructure(type.Definition))
            : StructuredValueTestContext.Reply(Variant.Null, StatusCodes.BadAttributeIdInvalid));
        Assert.That(await context.Service.ResolveAsync(type.DataTypeId).ConfigureAwait(false),
            Is.InstanceOf<StructureDefinition>());
        context.Service.Refresh();

        Assert.That(await context.Service.ResolveAsync(type.DataTypeId).ConfigureAwait(false), Is.Null);
        Assert.That(context.Reads, Is.EqualTo(2));
    }

    [Test]
    public async Task MissingDefinitionCanAppearAfterAnExplicitRefreshAsync()
    {
        using var context = new StructuredValueTestContext();
        var dataTypeId = new NodeId(5000u, context.NamespaceIndex);
        context.Reader = (_, _) => ValueTask.FromResult(context.Reads == 1
            ? StructuredValueTestContext.Reply(Variant.Null, StatusCodes.BadAttributeIdInvalid)
            : StructuredValueTestContext.Reply(Variant.FromStructure(new StructureDefinition())));
        Assert.That(await context.Service.ResolveAsync(dataTypeId).ConfigureAwait(false), Is.Null);
        context.Service.Refresh();

        Assert.That(await context.Service.ResolveAsync(dataTypeId).ConfigureAwait(false),
            Is.InstanceOf<StructureDefinition>());
        Assert.That(context.Reads, Is.EqualTo(2));
    }

    private static readonly StatusCode[] s_missingDefinitionStatuses =
    [
        StatusCodes.BadAttributeIdInvalid,
        StatusCodes.BadNotSupported,
        StatusCodes.Good
    ];

    private static readonly StatusCode[] s_failedReadStatuses =
    [
        StatusCodes.BadUserAccessDenied,
        StatusCodes.BadCommunicationError,
        StatusCodes.BadTimeout,
        StatusCodes.BadNodeIdUnknown,
        StatusCodes.Uncertain
    ];
}

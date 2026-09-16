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
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Schema;
using UaLens.Connection;
using UaLens.Plugins.Models;
using UaLens.ViewModels;

namespace UaLens.Tests.StructuredValues;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class ModelInspectorStateTests
{
    [Test]
    public async Task ConstructorSeedsTheSelectedVariablePortablyWithoutCreatingAViewOrReadingAsync()
    {
        using var context = new StructuredValueTestContext();
        var connection = new ConnectionService(context.MessageContext.Telemetry);
        await using (connection.ConfigureAwait(false))
        {
            var browser = new BrowserViewModel(context.MessageContext.Telemetry, connection);
            var node = new NodeViewModel(
                browser, NodeId.Null, new NodeId("Selected", context.NamespaceIndex),
                "Selected variable", NodeClass.Variable);
            Mock<IModelInspectorBackend> backend = Backend(context);
            var plugin = new ModelInspectorPlugin(
                backend.Object, () => context.Session.Object, () => node, context.MessageContext.Telemetry);
            await using (plugin.ConfigureAwait(false))
            {
                Assert.That(plugin.Target, Is.EqualTo("nsu=urn:unit:test:structured;s=Selected"));
                Assert.That(plugin.TargetName, Is.EqualTo("Selected variable"));
                Assert.That(plugin.HasCreatedView, Is.False);
                Assert.That(plugin.EditingEnabled, Is.False);
                backend.Verify(value => value.ReadAsync(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }
    }

    [Test]
    public void PortableTargetSurvivesNamespaceReorderingWithoutGrowingTheServerTable()
    {
        var original = new NamespaceTable();
        original.Update([Namespaces.OpcUa, "urn:other", "urn:model"]);
        string target = ModelInspectorStateCodec.NormalizeTarget("ns=2;s=Temperature", original);
        Assert.That(target, Is.EqualTo("nsu=urn:model;s=Temperature"));
        var replacement = new NamespaceTable();
        replacement.Update([Namespaces.OpcUa, "urn:model", "urn:other"]);

        NodeId resolved = ModelInspectorStateCodec.ResolveTarget(target, replacement);

        Assert.That(resolved, Is.EqualTo(new NodeId("Temperature", 1)));
        Assert.That(replacement.ToArray(), Has.Length.EqualTo(3));
        Assert.That(() => ModelInspectorStateCodec.ResolveTarget("nsu=urn:absent;s=Temperature", replacement),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(replacement.ToArray(), Has.Length.EqualTo(3));
    }

    [TestCase("ns=2;s=Temperature")]
    [TestCase("svr=1;i=2258")]
    [TestCase("not a node")]
    [TestCase("i=0")]
    public void InvalidOrNonportableSavedTargetsAreRejected(string target)
    {
        var state = new ModelInspectorState(1, "Models", target, "Target", UaSchemaFormat.JsonCompact);
        Assert.That(() => ModelInspectorStateCodec.Capture(state), Throws.TypeOf<JsonException>());
    }

    [TestCase("""{"version":2,"title":"Models","target":"i=2258","targetName":"","schemaFormat":2}""")]
    [TestCase("""{"version":1,"title":null,"target":"i=2258","targetName":"","schemaFormat":2}""")]
    [TestCase("""{"version":1,"title":"Models","target":"i=2258","targetName":"","schemaFormat":99}""")]
    [TestCase("""{"version":1,"title":"Models","target":"i=2258","targetName":"","schemaFormat":2,"armed":true}""")]
    [TestCase("""{"version":1,"title":"Models","target":"i=2258","targetName":"","schemaFormat":2,"credentials":{}}""")]
    [TestCase("null")]
    public void MalformedConfigurationIsSurfacedRatherThanSilentlyDefaulted(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.That(() => ModelInspectorStateCodec.Restore(document.RootElement), Throws.TypeOf<JsonException>());
    }

    [Test]
    public async Task ConstructorConnectionAndRestoreAreLazyAndNeverReadOrMutateAsync()
    {
        using var context = new StructuredValueTestContext();
        Mock<IModelInspectorBackend> backend = Backend(context);
        var plugin = new ModelInspectorPlugin(
            backend.Object, () => context.Session.Object, () => null, context.MessageContext.Telemetry);
        await using (plugin.ConfigureAwait(false))
        {
            var saved = new ModelInspectorState(
                1, "Saved Models", "nsu=urn:model;s=Value", "Saved target", UaSchemaFormat.Bsd);
            await plugin.RestoreStateAsync(ModelInspectorStateCodec.Capture(saved)).ConfigureAwait(false);
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(plugin.HasCreatedView, Is.False);
            Assert.That(plugin.Target, Is.EqualTo(saved.Target));
            Assert.That(plugin.TargetName, Is.EqualTo(saved.TargetName));
            Assert.That(plugin.EditingEnabled, Is.False);
            Assert.That(plugin.ConfirmWrite, Is.False);
            Assert.That(plugin.Inspection, Is.Null);
            Assert.That(plugin.CanWrite, Is.False);
            Assert.That(plugin.CanCall, Is.False);
            Assert.That(ModelInspectorStateCodec.Restore(plugin.CaptureState()), Is.EqualTo(saved));
            backend.Verify(value => value.ReadAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            backend.Verify(value => value.WriteAsync(
                It.IsAny<ModelInspection>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ReadAndRefreshAreExplicitAndDoNotArmWritesOrPersistEvidenceAsync()
    {
        using var context = new StructuredValueTestContext();
        Mock<IModelInspectorBackend> backend = Backend(context);
        ModelInspection inspection = Inspection(context);
        backend.Setup(value => value.ReadAsync(
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(inspection);
        var plugin = new ModelInspectorPlugin(
            backend.Object, () => context.Session.Object, () => null, context.MessageContext.Telemetry)
        {
            Target = inspection.PortableTarget
        };
        await using (plugin.ConfigureAwait(false))
        {
            await plugin.ReadCommand.ExecuteAsync(null).ConfigureAwait(false);
            Assert.That(plugin.ReadState, Is.EqualTo(ModelReadState.Available));
            Assert.That(plugin.ValuePreview, Is.EqualTo("7"));
            Assert.That(plugin.CanEdit, Is.True, "A primitive with authoritative no-schema remains editable.");
            Assert.That(plugin.CanWrite, Is.False);
            plugin.EditingEnabled = true;
            plugin.ConfirmWrite = true;
            await plugin.RefreshMetadataCommand.ExecuteAsync(null).ConfigureAwait(false);
            Assert.That(plugin.EditingEnabled, Is.False);
            Assert.That(plugin.ConfirmWrite, Is.False);

            JsonElement state = plugin.CaptureState();
            Assert.That(state.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(s_stateProperties));
            Assert.That(plugin.HasCreatedView, Is.False);
            backend.Verify(value => value.ReadAsync(
                inspection.PortableTarget, false, It.IsAny<CancellationToken>()), Times.Once);
            backend.Verify(value => value.ReadAsync(
                inspection.PortableTarget, true, It.IsAny<CancellationToken>()), Times.Once);
            backend.Verify(value => value.WriteAsync(
                It.IsAny<ModelInspection>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(false, (int)ModelReadState.Available, true)]
    [TestCase(true, (int)ModelReadState.Unavailable, false)]
    public async Task MissingDefinitionKeepsPrimitiveReadAvailableAndOpaqueReadUnavailableAsync(
        bool opaque, int expectedState, bool canEdit)
    {
        using var context = new StructuredValueTestContext();
        Mock<IModelInspectorBackend> backend = Backend(context);
        Variant value = opaque
            ? Variant.From(new ExtensionObject(new ExpandedNodeId(987654u), ByteString.From([1, 2, 3])))
            : Variant.From(42.5d);
        ModelInspection inspection = Inspection(context) with
        {
            DataType = opaque ? DataTypeIds.Structure : DataTypeIds.Double,
            DataTypeName = new QualifiedName(opaque ? "Structure" : "Double"),
            Value = new DataValue(value)
        };
        backend.Setup(item => item.ReadAsync(
            inspection.PortableTarget, false, It.IsAny<CancellationToken>())).ReturnsAsync(inspection);
        var plugin = new ModelInspectorPlugin(
            backend.Object, () => context.Session.Object, () => null, context.MessageContext.Telemetry)
        {
            Target = inspection.PortableTarget
        };
        await using (plugin.ConfigureAwait(false))
        {
            await plugin.ReadCommand.ExecuteAsync(null).ConfigureAwait(false);

            Assert.That(plugin.Inspection, Is.SameAs(inspection));
            Assert.That(plugin.ReadState, Is.EqualTo((ModelReadState)expectedState));
            Assert.That(plugin.CanEdit, Is.EqualTo(canEdit));
            Assert.That(plugin.CanPreviewSchema, Is.False);
            if (!opaque)
            {
                Assert.That(plugin.ValuePreview, Is.EqualTo("42.5"));
            }

            plugin.EditingEnabled = true;
            Assert.That(plugin.CanEditDraft, Is.EqualTo(canEdit));
            Assert.That(plugin.CanWrite, Is.False, "Reading a value must not confirm a server write.");
            backend.Verify(item => item.WriteAsync(
                It.IsAny<ModelInspection>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OptionSetsAndFreshTypedMatricesEnableLocalEditingWithoutArmingWritesAsync(bool matrix)
    {
        using var context = new StructuredValueTestContext();
        Mock<IModelInspectorBackend> backend = Backend(context);
        ModelInspection inspection = Inspection(context) with
        {
            DataType = matrix ? DataTypeIds.Int32 : DataTypeIds.UInt32,
            ValueRank = matrix ? 2 : ValueRanks.Scalar,
            Value = new DataValue(matrix ? Variant.Null : Variant.From(0x80000000u)),
            Definition = matrix ? null : new EnumDefinition
            {
                IsOptionSet = true,
                Fields = [new EnumField { Name = "Enabled", Value = 0 }]
            },
            ArrayDimensions = matrix ? (ArrayOf<uint>)[2u, 3u] : default
        };
        backend.Setup(item => item.ReadAsync(
            inspection.PortableTarget, false, It.IsAny<CancellationToken>())).ReturnsAsync(inspection);
        var plugin = new ModelInspectorPlugin(
            backend.Object, () => context.Session.Object, () => null, context.MessageContext.Telemetry)
        {
            Target = inspection.PortableTarget
        };
        await using (plugin.ConfigureAwait(false))
        {
            await plugin.ReadCommand.ExecuteAsync(null).ConfigureAwait(false);
            Assert.That(plugin.ReadState, Is.EqualTo(ModelReadState.Available));
            Assert.That(plugin.CanEdit, Is.True);
            Assert.That(plugin.IsStructuredValue, Is.True);
            Assert.That(plugin.CanWrite, Is.False);
            plugin.EditingEnabled = true;
            Assert.That(plugin.CanEditDraft, Is.True);
            Assert.That(plugin.CanWrite, Is.False);
            Assert.That(plugin.DefinitionPreview, Does.Not.Contain("read-only limitation"));
            backend.Verify(item => item.WriteAsync(
                It.IsAny<ModelInspection>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ReadFailuresAreDistinctFromAuthoritativeMissingSchemaAndRetryWorksAsync(bool denied)
    {
        using var context = new StructuredValueTestContext();
        Mock<IModelInspectorBackend> backend = Backend(context);
        Exception failure = denied
            ? new ServiceResultException(StatusCodes.BadUserAccessDenied)
            : new IOException("Transport interrupted.");
        backend.SetupSequence(value => value.ReadAsync(
            It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure)
            .ReturnsAsync(Inspection(context));
        var plugin = new ModelInspectorPlugin(
            backend.Object, () => context.Session.Object, () => null, context.MessageContext.Telemetry)
        {
            Target = "i=2258"
        };
        await using (plugin.ConfigureAwait(false))
        {
            await plugin.ReadCommand.ExecuteAsync(null).ConfigureAwait(false);
            Assert.That(plugin.ReadState, Is.EqualTo(denied ? ModelReadState.Denied : ModelReadState.Failed));
            Assert.That(plugin.Inspection, Is.Null);
            Assert.That(plugin.CanWrite, Is.False);
            await plugin.ReadCommand.ExecuteAsync(null).ConfigureAwait(false);
            Assert.That(plugin.ReadState, Is.EqualTo(ModelReadState.Available));
            Assert.That(plugin.Inspection, Is.Not.Null);
        }
    }

    [Test]
    public async Task WriteRequiresExplicitArmingAndSuccessfulWriteDisarmsTheDocumentAsync()
    {
        using var context = new StructuredValueTestContext();
        Mock<IModelInspectorBackend> backend = Backend(context);
        ModelInspection inspection = Inspection(context);
        backend.Setup(value => value.ReadAsync(
            It.IsAny<string>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(inspection);
        Variant sent = Variant.Null;
        backend.Setup(value => value.WriteAsync(inspection, It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
            .Callback((ModelInspection _, Variant value, CancellationToken _) => sent = value)
            .ReturnsAsync(StatusCodes.Good);
        var plugin = new ModelInspectorPlugin(
            backend.Object, () => context.Session.Object, () => null, context.MessageContext.Telemetry)
        {
            Target = inspection.PortableTarget
        };
        await using (plugin.ConfigureAwait(false))
        {
            Assert.That(() => plugin.WritePreparedValueAsync(Variant.From(12)),
                Throws.TypeOf<InvalidOperationException>());
            await plugin.ReadCommand.ExecuteAsync(null).ConfigureAwait(false);
            Assert.That(() => plugin.WritePreparedValueAsync(Variant.From(12)),
                Throws.TypeOf<InvalidOperationException>());
            plugin.EditingEnabled = true;
            plugin.ConfirmWrite = true;
            await plugin.WritePreparedValueAsync(Variant.From(12)).ConfigureAwait(false);

            Assert.That(sent.TryGetValue(out int actual), Is.True);
            Assert.That(actual, Is.EqualTo(12));
            Assert.That(inspection.Value.WrappedValue.TryGetValue(out int original), Is.True);
            Assert.That(original, Is.EqualTo(7));
            Assert.That(plugin.ConfirmWrite, Is.False);
            Assert.That(plugin.EditingEnabled, Is.False);
            Assert.That(plugin.CanWrite, Is.False);
            backend.Verify(value => value.WriteAsync(inspection, It.IsAny<Variant>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task AReadOnlyVariableCannotBeArmedIntoAWritableTargetAsync()
    {
        using var context = new StructuredValueTestContext();
        Mock<IModelInspectorBackend> backend = Backend(context);
        backend.Setup(value => value.ReadAsync(
            It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Inspection(context) with { CanWrite = false });
        var plugin = new ModelInspectorPlugin(
            backend.Object, () => context.Session.Object, () => null, context.MessageContext.Telemetry)
        {
            Target = "i=2258"
        };
        await using (plugin.ConfigureAwait(false))
        {
            await plugin.ReadCommand.ExecuteAsync(null).ConfigureAwait(false);
            plugin.EditingEnabled = true;
            plugin.ConfirmWrite = true;
            Assert.That(plugin.CanWrite, Is.False);
            Assert.That(() => plugin.WritePreparedValueAsync(Variant.From(12)),
                Throws.TypeOf<InvalidOperationException>());
            backend.Verify(value => value.WriteAsync(
                It.IsAny<ModelInspection>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task RebindCancelsAndAwaitsTheOldReadBeforeReleasingItsBackendAsync()
    {
        using var context = new StructuredValueTestContext();
        Mock<IModelInspectorBackend> backend = Backend(context);
        var bound = new List<ISession?>();
        backend.Setup(value => value.BindAsync(It.IsAny<ISession?>(), It.IsAny<CancellationToken>()))
            .Callback((ISession? session, CancellationToken _) => bound.Add(session))
            .Returns(Task.CompletedTask);
        var completion = new TaskCompletionSource<ModelInspection>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        backend.Setup(value => value.ReadAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .Callback((string _, bool _, CancellationToken cancellation) => token = cancellation)
            .Returns(completion.Task);
        ISession? primary = context.Session.Object;
        var plugin = new ModelInspectorPlugin(
            backend.Object, () => primary, () => null, context.MessageContext.Telemetry)
        { Target = "i=2258" };
        await using (plugin.ConfigureAwait(false))
        {
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);
            int initialBindings = bound.Count;
            Task read = plugin.ReadCommand.ExecuteAsync(null);
            primary = null;
            Task rebind = plugin.OnConnectionStateChangedAsync(CancellationToken.None);

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(rebind.IsCompleted, Is.False);
            Assert.That(bound, Has.Count.EqualTo(initialBindings),
                "The old backend must remain owned until read drains.");
            completion.SetResult(Inspection(context));
            await Task.WhenAll(read, rebind).ConfigureAwait(false);
            Assert.That(plugin.Inspection, Is.Null);
            Assert.That(plugin.ValuePreview, Is.Empty);
            Assert.That(plugin.EditingEnabled, Is.False);
            Assert.That(bound, Has.Count.GreaterThan(initialBindings));
        }
    }

    [Test]
    public async Task DisposeAwaitsCanceledWorkAndDoesNotReplayAnyMutationAsync()
    {
        using var context = new StructuredValueTestContext();
        Mock<IModelInspectorBackend> backend = Backend(context);
        var completion = new TaskCompletionSource<ModelInspection>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Setup(value => value.ReadAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .Returns(completion.Task);
        bool released = false;
        backend.Setup(value => value.DisposeAsync()).Callback(() => released = true).Returns(ValueTask.CompletedTask);
        var plugin = new ModelInspectorPlugin(
            backend.Object, () => context.Session.Object, () => null, context.MessageContext.Telemetry)
        {
            Target = "i=2258"
        };
        Task read = plugin.ReadCommand.ExecuteAsync(null);
        Task dispose = plugin.DisposeAsync().AsTask();
        Assert.That(dispose.IsCompleted, Is.False);
        Assert.That(released, Is.False);
        completion.SetResult(Inspection(context));
        await Task.WhenAll(read, dispose).ConfigureAwait(false);
        Assert.That(released, Is.True);
        Assert.That(plugin.CanRead, Is.False);
        Assert.That(plugin.CanWrite, Is.False);
        Assert.That(plugin.Inspection, Is.Null);
        backend.Verify(value => value.WriteAsync(
            It.IsAny<ModelInspection>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UnavailableSchemaCannotBeExportedAndChangingFormatInvalidatesThePreviewAsync()
    {
        using var context = new StructuredValueTestContext();
        Mock<IModelInspectorBackend> backend = Backend(context);
        ModelInspection inspection = Inspection(context) with { Definition = new StructureDefinition() };
        backend.Setup(value => value.ReadAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inspection);
        backend.SetupSequence(value => value.CreateSchemaAsync(
            inspection, UaSchemaFormat.JsonCompact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelSchemaPreview(
                false, "Missing nested definition.", string.Empty, string.Empty))
            .ReturnsAsync(new ModelSchemaPreview(true, "{\"type\":\"object\"}", "application/schema+json", "json"));
        var plugin = new ModelInspectorPlugin(
            backend.Object, () => context.Session.Object, () => null, context.MessageContext.Telemetry)
        {
            Target = inspection.PortableTarget
        };
        await using (plugin.ConfigureAwait(false))
        {
            await plugin.ReadCommand.ExecuteAsync(null).ConfigureAwait(false);
            await plugin.PreviewSchemaCommand.ExecuteAsync(null).ConfigureAwait(false);
            Assert.That(plugin.CanExportSchema, Is.False);
            Assert.That(plugin.SchemaStatus, Does.Contain("Missing nested"));
            await plugin.PreviewSchemaCommand.ExecuteAsync(null).ConfigureAwait(false);
            Assert.That(plugin.CanExportSchema, Is.True);
            plugin.SelectedSchemaFormat = plugin.SchemaFormats[2];
            Assert.That(plugin.CanExportSchema, Is.False);
            Assert.That(plugin.SchemaPreview, Is.Empty);
        }
    }

    [Test]
    public async Task MalformedRestoreDoesNotOverwriteExistingIntentAsync()
    {
        using var context = new StructuredValueTestContext();
        var plugin = new ModelInspectorPlugin(
            Backend(context).Object, () => context.Session.Object, () => null, context.MessageContext.Telemetry)
        {
            Target = "i=2258",
            Title = "Original"
        };
        await using (plugin.ConfigureAwait(false))
        {
            using JsonDocument malformed = JsonDocument.Parse("""{"version":99}""");
            await Assert.ThatAsync(() => plugin.RestoreStateAsync(malformed.RootElement),
                Throws.TypeOf<JsonException>()).ConfigureAwait(false);
            Assert.That(plugin.Target, Is.EqualTo("i=2258"));
            Assert.That(plugin.Title, Is.EqualTo("Original"));
            Assert.That(plugin.HasCreatedView, Is.False);
        }
    }

    private static Mock<IModelInspectorBackend> Backend(StructuredValueTestContext context)
    {
        var backend = new Mock<IModelInspectorBackend>(MockBehavior.Strict);
        backend.SetupGet(value => value.IsBound).Returns(true);
        backend.SetupGet(value => value.Values).Returns(context.Service);
        backend.SetupGet(value => value.Session).Returns(context.Session.Object);
        backend.Setup(value => value.BindAsync(It.IsAny<ISession?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        backend.Setup(value => value.EnsureCurrent(It.IsAny<ModelInspection>()));
        backend.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return backend;
    }

    private static ModelInspection Inspection(StructuredValueTestContext context)
    {
        return new ModelInspection(
            1, context.SessionId, context.MessageContext.NamespaceUris.ToArray(), "i=2258", new NodeId(2258u),
            "Test value", NodeClass.Variable, DataTypeIds.Int32, new QualifiedName("Int32"), ValueRanks.Scalar,
            new DataValue(Variant.From(7)), true, false, null);
    }

    private static readonly string[] s_stateProperties = ["version", "title", "target", "targetName", "schemaFormat"];
}

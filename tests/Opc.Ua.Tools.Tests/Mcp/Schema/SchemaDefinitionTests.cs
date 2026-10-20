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

#if NET10_0
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Schema;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Direct definition-policy tests, including exact boundaries and finite dependency resolution.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class SchemaDefinitionTests
    {
        /// <summary>
        /// Defaults implement the approved finite budgets, rather than merely documenting them.
        /// </summary>
        [Test]
        public void DefaultsUseApprovedFiniteBudgets()
        {
            var options = new McpSchemaOptions();

            Assert.That(options.MaxTypes, Is.EqualTo(512));
            Assert.That(options.MaxFieldsPerType, Is.EqualTo(1024));
            Assert.That(options.MaxDepth, Is.EqualTo(32));
            Assert.That(options.MaxSchemaBytes, Is.EqualTo(16 * 1024 * 1024));
            Assert.That(options.OperationTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(options.Validate, Throws.Nothing);
        }

        /// <summary>
        /// Every host limit rejects its invalid lower and upper adjacent partition.
        /// </summary>
        [TestCase("types", 0)]
        [TestCase("types", 513)]
        [TestCase("fields", 0)]
        [TestCase("fields", 1025)]
        [TestCase("depth", 0)]
        [TestCase("depth", 33)]
        [TestCase("bytes", 0)]
        [TestCase("bytes", (16 * 1024 * 1024) + 1)]
        [TestCase("timeout", 0)]
        [TestCase("timeout", 30_001)]
        public void InvalidHostBudgetsAreRejected(string kind, int value)
        {
            var options = new McpSchemaOptions();
            switch (kind)
            {
                case "types":
                    options.MaxTypes = value;
                    break;
                case "fields":
                    options.MaxFieldsPerType = value;
                    break;
                case "depth":
                    options.MaxDepth = value;
                    break;
                case "bytes":
                    options.MaxSchemaBytes = value;
                    break;
                case "timeout":
                    options.OperationTimeout = TimeSpan.FromMilliseconds(value);
                    break;
            }

            Assert.That(options.Validate, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// The finite registry has no unbounded factory fallback and does not confuse name and identifier namespaces.
        /// </summary>
        [Test]
        public async Task SnapshotPreservesIdentifierNamespaceAndHasNoResolverFallbackAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId id = fixture.Register("Sample", SchemaTestContext.Structure(
                SchemaTestContext.Field("Values", DataTypeIds.Int32, 2)));
            SchemaDefinitionSnapshot snapshot = fixture.Snapshot();

            await snapshot.LoadAsync(null, [id], CancellationToken.None).ConfigureAwait(false);

            Assert.That(snapshot.Resolver, Is.SameAs(snapshot.Registry));
            Assert.That(snapshot.Registry.TryResolve(id, out UaTypeDescription? type), Is.True);
            Assert.That(type!.NamespaceUri, Is.EqualTo(SchemaTestContext.TypeNamespaceUri));
            Assert.That(type.BrowseName.NamespaceIndex, Is.EqualTo(fixture.NameNamespace));
            Assert.That(snapshot.Registry.TryResolve(new NodeId("Sample", fixture.NameNamespace), out _), Is.False);
            Assert.That(snapshot.Registry.TryResolve(DataTypeIds.ReadRequest, out _), Is.False);
            Assert.That(((StructureDefinition)type.Definition).Fields[0].ValueRank, Is.EqualTo(2));
            Assert.That(snapshot.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Exactly 512 distinct definitions fit; the immediately adjacent closure fails before generation.
        /// </summary>
        [TestCase(512, false)]
        [TestCase(513, true)]
        public async Task TypeClosureEnforcesExactMaximumAsync(int count, bool failure)
        {
            await using var fixture = new SchemaTestContext();
            var fields = new StructureField[count - 1];
            for (int index = 1; index < count; index++)
            {
                NodeId child = fixture.Register("Child" + index, SchemaTestContext.Structure());
                fields[index - 1] = SchemaTestContext.Field("Field" + index, child);
            }
            NodeId root = fixture.Register("Root", SchemaTestContext.Structure(fields));
            SchemaDefinitionSnapshot snapshot = fixture.Snapshot();
            if (failure)
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await snapshot.LoadAsync(null, [root], CancellationToken.None).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(snapshot.Count, Is.EqualTo(512));
            }
            else
            {
                await snapshot.LoadAsync(null, [root], CancellationToken.None).ConfigureAwait(false);
                Assert.That(snapshot.Count, Is.EqualTo(512));
                Assert.That(snapshot.Registry.TryResolve(fixture.Id("Child511"), out _), Is.True);
            }
        }

        /// <summary>
        /// Field limits are checked before any schema provider expands the structure.
        /// </summary>
        [TestCase(1024, false)]
        [TestCase(1025, true)]
        public void DefinitionFieldCountEnforcesExactMaximum(int count, bool failure)
        {
            StructureDefinition definition = SchemaTestContext.Structure(
                [.. Enumerable.Range(0, count).Select(index =>
                    SchemaTestContext.Field("Field" + index, DataTypeIds.Int32))]);
            if (failure)
            {
                ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                    SchemaDefinitionSnapshot.ValidateDefinition(definition, new McpSchemaOptions()));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            else
            {
                Assert.That(() => SchemaDefinitionSnapshot.ValidateDefinition(definition, new McpSchemaOptions()),
                    Throws.Nothing);
            }
        }

        /// <summary>
        /// Dependency depth is measured from the selected root and checked at the exact limit.
        /// </summary>
        [TestCase(32, false)]
        [TestCase(33, true)]
        public async Task DependencyDepthEnforcesExactMaximumAsync(int depth, bool failure)
        {
            await using var fixture = new SchemaTestContext();
            NodeId child = fixture.Register("Leaf", SchemaTestContext.Structure());
            for (int index = 0; index < depth; index++)
            {
                child = fixture.Register("Parent" + index,
                    SchemaTestContext.Structure(SchemaTestContext.Field("Child", child)));
            }
            SchemaDefinitionSnapshot snapshot = fixture.Snapshot();
            if (failure)
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await snapshot.LoadAsync(null, [child], CancellationToken.None).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            else
            {
                await snapshot.LoadAsync(null, [child], CancellationToken.None).ConfigureAwait(false);
                Assert.That(snapshot.Count, Is.EqualTo(33));
            }
        }

        /// <summary>
        /// Valid self-reference is finite and builtin fields do not consume definition-count slots.
        /// </summary>
        [Test]
        public async Task SelfReferenceAndBuiltinEdgesRemainFiniteAsync()
        {
            await using var fixture = new SchemaTestContext(new McpSchemaOptions { MaxTypes = 1 });
            NodeId id = fixture.Id("Recursive");
            fixture.Register("Recursive", SchemaTestContext.Structure(
                SchemaTestContext.Field("Next", id),
                SchemaTestContext.Field("Value", DataTypeIds.Int32)));
            SchemaDefinitionSnapshot snapshot = fixture.Snapshot();

            await snapshot.LoadAsync(null, [id], CancellationToken.None).ConfigureAwait(false);

            Assert.That(snapshot.Count, Is.EqualTo(1));
            Assert.That(snapshot.Registry.TryResolve(id, out _), Is.True);
        }

        /// <summary>
        /// A previously visited dependency still consumes its full depth when reached through a longer path.
        /// </summary>
        [Test]
        public async Task SharedDependenciesCannotBypassDepthLimitAsync()
        {
            await using var fixture = new SchemaTestContext(new McpSchemaOptions { MaxDepth = 2 });
            NodeId leaf = fixture.Register("Leaf", SchemaTestContext.Structure());
            NodeId shared = fixture.Register("Shared",
                SchemaTestContext.Structure(SchemaTestContext.Field("Leaf", leaf)));
            NodeId branch = fixture.Register("Branch",
                SchemaTestContext.Structure(SchemaTestContext.Field("Shared", shared)));
            NodeId root = fixture.Register("Root", SchemaTestContext.Structure(
                SchemaTestContext.Field("Direct", shared), SchemaTestContext.Field("Longer", branch)));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Snapshot().LoadAsync(null, [root], CancellationToken.None).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Unknown standard and custom definitions are both refusals rather than guessed empty schemas.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnknownFieldDefinitionsFailForEveryNamespaceAsync(bool standard)
        {
            await using var fixture = new SchemaTestContext();
            NodeId missing = standard ? new NodeId(uint.MaxValue) : fixture.Id("Missing");
            NodeId root = fixture.Register("Broken",
                SchemaTestContext.Structure(SchemaTestContext.Field("Value", missing)));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Snapshot().LoadAsync(null, [root], CancellationToken.None).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadDataTypeIdUnknown));
        }

        /// <summary>
        /// A registered binary encoding alias is not misreported as the actual DataType identifier.
        /// </summary>
        [Test]
        public async Task EncodingAliasCannotMasqueradeAsDataTypeAsync()
        {
            await using var fixture = new SchemaTestContext();

            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                fixture.Snapshot().InspectRegistered(ObjectIds.Argument_Encoding_DefaultBinary));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(error.Message, Does.Contain("encoding alias"));
        }

        /// <summary>
        /// Unsupported kinds, repeated field names and invalid discriminators cannot become successful schemas.
        /// </summary>
        [Test]
        public void UnsupportedDefinitionShapesAreRejected()
        {
            var options = new McpSchemaOptions();
            Assert.That(() => SchemaDefinitionSnapshot.ValidateDefinition(
                new Mock<DataTypeDefinition>().Object, options),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(() => SchemaDefinitionSnapshot.ValidateDefinition(SchemaTestContext.Structure(
                SchemaTestContext.Field("Same", DataTypeIds.Int32),
                SchemaTestContext.Field("Same", DataTypeIds.Double)), options),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(() => SchemaDefinitionSnapshot.ValidateDefinition(
                SchemaTestContext.Structure([null!]), options), Throws.TypeOf<ServiceResultException>());
            StructureDefinition unknown = SchemaTestContext.Structure();
            unknown.StructureType = (StructureType)999;
            Assert.That(() => SchemaDefinitionSnapshot.ValidateDefinition(unknown, options),
                Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Rank bounds are independent of field count and reject invalid negative ranks.
        /// </summary>
        [TestCase(32, false)]
        [TestCase(33, true)]
        [TestCase(-4, true)]
        public void ArrayRankEnforcesIndependentBound(int rank, bool failure)
        {
            if (failure)
            {
                Assert.That(() => SchemaDefinitionSnapshot.ValidateRank(rank, new McpSchemaOptions()),
                    Throws.TypeOf<ServiceResultException>());
            }
            else
            {
                Assert.That(() => SchemaDefinitionSnapshot.ValidateRank(rank, new McpSchemaOptions()), Throws.Nothing);
            }
        }

        /// <summary>
        /// A pre-cancelled operation resolves no definitions.
        /// </summary>
        [Test]
        public async Task CancelledSnapshotDoesNotCollectDefinitionsAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Sample", SchemaTestContext.Structure());
            SchemaDefinitionSnapshot snapshot = fixture.Snapshot();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await snapshot.LoadAsync(null, [root], cancellation.Token).ConfigureAwait(false));

            Assert.That(snapshot.Count, Is.Zero);
        }
    }
}
#endif

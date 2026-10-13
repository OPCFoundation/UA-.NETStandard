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
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion.ClientWorkflows
{
    /// <summary>
    /// Tests typed value partitions, namespace refusal and exact scalar/inline ceilings at the workflow boundary.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class ClientWorkflowInputTests
    {
        /// <summary>
        /// Typed numeric arrays remain arrays even at their empty/singleton boundaries.
        /// </summary>
        [TestCase("[]", 0)]
        [TestCase("[0]", 1)]
        [TestCase("[-1,0,2147483647]", 3)]
        public async Task TypedIntegerArrayPreservesEmptySingletonAndInteriorPartitionsAsync(string json, int count)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            WorkflowDataValuesInput input = Values(ClientWorkflowHistoryTests.Value(BuiltInType.Int32, json));
            CallToolResult result = await InsertAsync(fixture, input).ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateDataDetails? details), Is.True);
            Assert.That(details!.UpdateValues[0].WrappedValue.TryGetValue(out ArrayOf<int> values), Is.True);
            Assert.That(values.IsNull, Is.False);
            Assert.That(values.Count, Is.EqualTo(count));
            if (count == 3)
            {
                Assert.That(values.ToArray(), Is.EqualTo([-1, 0, int.MaxValue]));
            }
        }

        /// <summary>
        /// Null, false, zero and empty string remain different typed values.
        /// </summary>
        [TestCase(BuiltInType.Null, "null")]
        [TestCase(BuiltInType.Boolean, "false")]
        [TestCase(BuiltInType.Int32, "0")]
        [TestCase(BuiltInType.UInt64, "18446744073709551615")]
        [TestCase(BuiltInType.String, "\"\"")]
        public async Task TypedScalarPartitionsAreNotConflatedAsync(BuiltInType type, string json)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await InsertAsync(fixture, Values(ClientWorkflowHistoryTests.Value(type, json)))
                .ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateDataDetails? details), Is.True);
            Variant actual = details!.UpdateValues[0].WrappedValue;
            Variant expected = type switch
            {
                BuiltInType.Null => Variant.Null,
                BuiltInType.Boolean => Variant.From(false),
                BuiltInType.Int32 => Variant.From(0),
                BuiltInType.UInt64 => Variant.From(ulong.MaxValue),
                _ => Variant.From(string.Empty)
            };
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Standard non-finite UA JSON tokens round-trip as their actual IEEE values, not null or zero.
        /// </summary>
        [TestCase("\"NaN\"")]
        [TestCase("\"Infinity\"")]
        [TestCase("\"-Infinity\"")]
        public async Task NonFiniteFloatingTokensPreserveTheirIeeeMeaningAsync(string json)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await InsertAsync(
                fixture, Values(ClientWorkflowHistoryTests.Value(BuiltInType.Double, json))).ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateDataDetails? details), Is.True);
            Assert.That(details!.UpdateValues[0].WrappedValue.TryGetValue(out double value), Is.True);
            if (json == "\"NaN\"")
            {
                Assert.That(double.IsNaN(value), Is.True);
            }
            else
            {
                Assert.That(value,
                    Is.EqualTo(json == "\"Infinity\"" ? double.PositiveInfinity : double.NegativeInfinity));
            }
        }

        /// <summary>
        /// Invalid types and scalar kinds fail before any historic side effect.
        /// </summary>
        [TestCase(BuiltInType.Int32, "\"123\"")]
        [TestCase(BuiltInType.Boolean, "0")]
        [TestCase(BuiltInType.Int32, "2147483648")]
        [TestCase(BuiltInType.ByteString, "\"not base64\"")]
        [TestCase(BuiltInType.NodeId, "\"nsu=urn:not-advertised;s=node\"")]
        [TestCase(BuiltInType.NodeId, "\"ns=65534;s=node\"")]
        [TestCase(BuiltInType.ExtensionObject, "{}")]
        [TestCase(BuiltInType.Double, "\"1.0\"")]
        public async Task InvalidTypedValuePartitionsFailBeforeDispatchAsync(BuiltInType type, string json)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await InsertAsync(fixture, Values(ClientWorkflowHistoryTests.Value(type, json)))
                .ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            Assert.That(fixture.HistoryUpdates, Is.Empty);
        }

        /// <summary>
        /// Scalar range validation preserves both sides of the exact numeric limit.
        /// </summary>
        [TestCase("254", true)]
        [TestCase("255", true)]
        [TestCase("256", false)]
        [TestCase("-1", false)]
        public async Task ByteValueChecksExactRangeBoundaryAsync(string json, bool accepted)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await InsertAsync(
                fixture, Values(ClientWorkflowHistoryTests.Value(BuiltInType.Byte, json))).ConfigureAwait(false);
            Assert.That(result.IsError, Is.EqualTo(!accepted));
            Assert.That(fixture.HistoryUpdates, Has.Count.EqualTo(accepted ? 1 : 0));
            if (accepted)
            {
                Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateDataDetails? details), Is.True);
                Assert.That(details!.UpdateValues[0].WrappedValue.TryGetValue(out byte value), Is.True);
                Assert.That(value, Is.EqualTo(byte.Parse(json, System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// The host's inline byte ceiling is checked on the complete serialized batch before constructing UA updates.
        /// </summary>
        [TestCase(-1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public async Task InlineBatchCeilingHasExactBoundaryAndImmediateNeighborsAsync(int difference, bool accepted)
        {
            WorkflowDataValuesInput input = Values(ClientWorkflowHistoryTests.Value(BuiltInType.String, "\"é\""));
            int encodedBytes = JsonSerializer.SerializeToUtf8Bytes(
                input, WorkflowInputJsonContext.Default.WorkflowDataValuesInput).Length;
            await using ClientWorkflowTestContext fixture = await ClientWorkflowTestContext.CreateAsync(
                options: new ClientWorkflowOptions { MaxInlineBytes = encodedBytes + difference })
                .ConfigureAwait(false);
            CallToolResult result = await InsertAsync(fixture, input).ConfigureAwait(false);
            Assert.That(result.IsError, Is.EqualTo(!accepted));
            Assert.That(fixture.HistoryUpdates, Has.Count.EqualTo(accepted ? 1 : 0));
        }

        /// <summary>
        /// A missing annotation property is unsupported, not a successful mutation of the original variable.
        /// </summary>
        [Test]
        public async Task MissingAnnotationTargetRefusesWithoutWritingOriginalVariableAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.History.DeleteAnnotationsAsync(
                fixture.Remote.Id("signal").ToString(), ["2026-01-01T00:00:00Z"], "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadNotSupported);
            Assert.That(fixture.HistoryUpdates, Is.Empty);
        }

        /// <summary>
        /// Shared semantic guards apply to the actual resolved annotation node, not only its public variable.
        /// </summary>
        [Test]
        public async Task ProtectedAnnotationTargetCannotBypassSharedGuardAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId signal = fixture.Remote.Id("signal");
            NodeId target = fixture.Remote.Id("protected-annotations");
            fixture.Remote.Type(target, ObjectTypeIds.ServerConfigurationType);
            fixture.Remote.Child(signal, new QualifiedName(BrowseNames.Annotations), target);
            CallToolResult result = await fixture.History.InsertAnnotationsAsync(signal.ToString(),
                [new WorkflowAnnotationInput { AnnotationTime = "2026-01-01T00:00:00Z", Message = "input" }],
                "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.HistoryUpdates, Is.Empty);
        }

        /// <summary>
        /// Permission replacement rejects duplicate roles rather than silently merging or clearing an entry.
        /// </summary>
        [Test]
        public async Task DuplicatePermissionRoleIsRefusedBeforeWriteAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string role = fixture.Remote.Id("role").ToString();
            CallToolResult result = await fixture.Roles.WritePermissionsAsync(
                fixture.Remote.Id("variable").ToString(),
                [
                    new WorkflowRolePermissionInput { RoleId = role, Permissions = 1 },
                    new WorkflowRolePermissionInput { RoleId = role, Permissions = 2 }
                ], "selected").ConfigureAwait(false);
            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.Writes, Is.Empty);
        }

        /// <summary>
        /// Clearing permissions is a deliberate empty-array write, never a null/omitted request.
        /// </summary>
        [Test]
        public async Task ExplicitEmptyPermissionArrayClearsExactlyTheDeclaredAttributeAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Roles.WritePermissionsAsync(
                fixture.Remote.Id("variable").ToString(), [], "selected")
                .ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.Writes.Single().AttributeId, Is.EqualTo(Attributes.RolePermissions));
            Assert.That(fixture.Writes.Single().Value.WrappedValue.TryGetStructure(
                out ArrayOf<RolePermissionType> permissions), Is.True);
            Assert.That(permissions.IsEmpty, Is.True);
            Assert.That(permissions.IsNull, Is.False);
        }

        /// <summary>
        /// Creates one deliberate historical sample for typed input tests.
        /// </summary>
        private static WorkflowDataValuesInput Values(WorkflowValueInput value)
        {
            bool array = value.Value.ValueKind == JsonValueKind.Array;
            return new WorkflowDataValuesInput
            {
                Values =
                [
                    new WorkflowDataValueInput
                    {
                        Type = value.Type,
                        Value = array ? default : value.Value,
                        ArrayLength = array ? value.Value.GetArrayLength() : null,
                        SourceTimestamp = "2026-01-01T00:00:00Z"
                    }
                ],
                ArrayElements = array ? value.Value.EnumerateArray().Select((element, index) =>
                    new WorkflowArrayElementInput { ValueIndex = 0, ElementIndex = index, Value = element })
                    .ToArrayOf() : []
            };
        }

        /// <summary>
        /// Crosses the real flat tool interface while sharing only repetitive test input setup.
        /// </summary>
        private static Task<CallToolResult> InsertAsync(
            ClientWorkflowTestContext fixture, WorkflowDataValuesInput input)
        {
            return fixture.History.InsertValuesAsync(fixture.Remote.Id("signal").ToString(),
                input.Values, input.ArrayElements, "selected");
        }
    }
}
#endif

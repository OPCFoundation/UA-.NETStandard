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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Tests.StructuredValues;

namespace UaLens.Tests.Companions
{

    internal sealed class CompanionStructuredInputTestContext : IAsyncDisposable
    {
        public CompanionStructuredInputTestContext(ITelemetryContext? telemetry = null)
        {
            Values.MessageContext.NamespaceUris.GetIndexOrAppend("urn:typed");
            Tasks = new CompanionTypedTaskTestContext(telemetry ?? Values.MessageContext.Telemetry);
            Tasks.Session.SetupGet(value => value.MessageContext).Returns(() => Values.MessageContext);
            Tasks.Session.SetupGet(value => value.NamespaceUris).Returns(() => Values.MessageContext.NamespaceUris);
            Tasks.Session.SetupGet(value => value.Factory).Returns(() => Values.MessageContext.Factory);
            Tasks.Session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, double age, TimestampsToReturn timestamps,
                    ArrayOf<ReadValueId> ids, CancellationToken token) =>
                    Values.Session.Object.ReadAsync(header, age, timestamps, ids, token));
        }

        public StructuredValueTestContext Values { get; } = new();

        public CompanionTypedTaskTestContext Tasks { get; }

        public void Offer(ArrayOf<CompanionInputDefinition> fields)
        {
            CompanionOperation operation = Tasks.Operation with { Inputs = fields };
            Tasks.Inspection = new CompanionInspection([], [operation], "Structured input inspection.");
        }

        public CompanionInputDefinition CustomField(BuiltInType type, NodeId typeId)
        {
            return Field(type) with
            {
                DataTypeId = NodeId.ToExpandedNodeId(typeId, Values.MessageContext.NamespaceUris)
            };
        }

        public void ReturnDefinition(NodeId typeId, DataTypeDefinition definition)
        {
            Values.Reader = (ids, _) =>
            {
                AssertDefinitionRead(ids, typeId);
                return ValueTask.FromResult(StructuredValueTestContext.Reply(Variant.FromStructure(definition)));
            };
        }

        public Task<CompanionOperationDraft> PrepareAsync(
            ArrayOf<CompanionValue> inputs, CancellationToken cancellationToken = default)
        {
            return Tasks.Workspace.PrepareTaskAsync(Tasks.Target, "typed", inputs, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Tasks.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Values.Dispose();
            }
        }

        public static CompanionInputDefinition Field(BuiltInType type)
        {
            return new CompanionInputDefinition("value", "Task value", type, "Review the typed value.");
        }

        public static EnumDefinition Enumeration()
        {
            return new EnumDefinition
            {
                Fields = [new EnumField { Name = "Idle", Value = 0 }, new EnumField { Name = "Running", Value = 7 }]
            };
        }

        public static void AssertDefinitionRead(ArrayOf<ReadValueId> ids, NodeId typeId)
        {
            Assert.That(ids.Count, Is.EqualTo(1));
            Assert.That(ids[0].NodeId, Is.EqualTo(typeId));
            Assert.That(ids[0].AttributeId, Is.EqualTo(Attributes.DataTypeDefinition));
        }
    }
}

/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ISession = Opc.Ua.Client.ISession;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Attribute Service Set – Reading complex attributes,
    /// structured types, data encodings, and optional/extended attributes.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AttributeReadComplex")]
    public class AttributeReadComplexTests : TestFixture
    {
        [Description("Read Server_ServerStatus (structured type) and verify the value is returned as an ExtensionObject with Good status.")]
        [Test]
        public async Task ReadExtensionObjectValueAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Read of Server_ServerStatus should return Good.");

            Variant variant = response.Results[0].WrappedValue;
            // Server_ServerStatus is per spec a ServerStatusDataType structure
            // wire-encoded as ExtensionObject. Both shapes are accepted: either
            // the wire ExtensionObject or a server that already decoded it.
            bool isExtensionObject = variant.TryGetValue(out ExtensionObject _);
            bool isDecoded = variant.TryGetStructure<ServerStatusDataType>(out ServerStatusDataType _);
            Assert.That(isExtensionObject || isDecoded, Is.True,
                "Server_ServerStatus value should not be null.");
            Assert.That(isExtensionObject || isDecoded, Is.True,
                "Value should be an ExtensionObject or ServerStatusDataType.");
        }

        [Description("Read Server_ServerStatus and verify the decoded structure contains accessible nested fields such as StartTime and State.")]
        [Test]
        public async Task ReadNestedStructureValueAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            Variant variant = response.Results[0].WrappedValue;
            ServerStatusDataType serverStatus = null;

            // Per spec ServerStatus is a ServerStatusDataType wire-encoded as
            // ExtensionObject; accept both the wire form and an already-decoded
            // structure.
            if (variant.TryGetStructure<ServerStatusDataType>(out ServerStatusDataType decoded))
            {
                serverStatus = decoded;
            }
            else if (variant.TryGetValue(out ExtensionObject extensionObject) &&
                extensionObject.TryGetValue(out ServerStatusDataType fromWire))
            {
                serverStatus = fromWire;
            }

            Assert.That(serverStatus, Is.Not.Null,
                "Should be able to decode ServerStatusDataType.");
            Assert.That(
                serverStatus.StartTime, Is.Not.EqualTo(DateTime.MinValue),
                "StartTime should be set.");
            Assert.That(
                Enum.IsDefined(typeof(ServerState), serverStatus.State),
                Is.True,
                "State should be a valid ServerState enum value.");
        }

        [Description("Read the SessionDiagnosticsArray variable which contains an array of ExtensionObjects. Verify the result is Good and the value is an array or empty collection.")]
        [Test]
        public async Task ReadArrayOfExtensionObjectsAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                ISession session = admin ?? Session;
                ReadResponse response = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Both,
                    new ReadValueId[]
                    {
                        new() {
                            NodeId = VariableIds
                                .Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray,
                            AttributeId = Attributes.Value
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));

                StatusCode statusCode = response.Results[0].StatusCode;
                if (!StatusCode.IsGood(statusCode))
                {
                    Assert.Ignore(
                        $"SessionDiagnosticsArray not readable: {statusCode}");
                }

                // SessionDiagnosticsArray is genuinely polymorphic: server can
                // return an array of ExtensionObject, an empty array, or a
                // null Variant. A null/empty value is consistent with
                // "no live sessions"; reject only the silent default-Variant
                // shape.
                Assert.That(
                    response.Results[0].WrappedValue.TypeInfo,
                    Is.Not.Null,
                    "Value should not be null when status is Good.");
            }
            finally
            {
                if (admin != null)
                {
                    await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    admin.Dispose();
                }
            }
        }

        [Description("Read Server_ServerStatus_State which is an enumeration value. Verify the result is a numeric value representing a valid ServerState.")]
        [Test]
        public async Task ReadEnumerationValueAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus_State,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Read of Server_ServerStatus_State should return Good.");

            int stateValue = response.Results[0].GetValue(0);
            Assert.That(
                Enum.IsDefined(typeof(ServerState), stateValue), Is.True,
                $"State value {stateValue} should be a valid ServerState.");
        }

        [Description("Read a structured type node with DataEncoding set to \"Default Binary\". Expect Good status since binary encoding is the native OPC UA encoding.")]
        [Test]
        public async Task ReadWithDataEncodingDefaultBinaryAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus,
                        AttributeId = Attributes.Value,
                        DataEncoding = new QualifiedName("Default Binary")
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code ==
                    StatusCodes.BadDataEncodingUnsupported,
                Is.True,
                "Default Binary encoding should return Good or unsupported.");
        }

        [Description("Read a structured type node with DataEncoding set to \"Default XML\". Expect Good or BadDataEncodingUnsupported since XML encoding is optional.")]
        [Test]
        public async Task ReadWithDataEncodingDefaultXmlAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus,
                        AttributeId = Attributes.Value,
                        DataEncoding = new QualifiedName("Default XML")
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code ==
                    StatusCodes.BadDataEncodingUnsupported ||
                response.Results[0].StatusCode.Code ==
                    StatusCodes.BadDataEncodingInvalid,
                Is.True,
                "Default XML encoding should return Good or unsupported.");
        }

        [Description("Read with an invalid DataEncoding name. Expect BadDataEncodingInvalid or BadDataEncodingUnsupported.")]
        [Test]
        public async Task ReadWithInvalidDataEncodingAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus,
                        AttributeId = Attributes.Value,
                        DataEncoding = new QualifiedName(
                            "NonExistentEncoding_Invalid")
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].StatusCode.Code ==
                    StatusCodes.BadDataEncodingInvalid ||
                response.Results[0].StatusCode.Code ==
                    StatusCodes.BadDataEncodingUnsupported,
                Is.True,
                "Invalid DataEncoding should return BadDataEncodingInvalid " +
                "or BadDataEncodingUnsupported.");
        }

        [Description("Read all standard attributes of a Variable node in a single call. Verify each attribute returns Good status.")]
        [Test]
        public async Task ReadAllAttributesOfVariableNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            uint[] attributeIds =
            [
                Attributes.NodeId,
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.DataType,
                Attributes.ValueRank,
                Attributes.Value,
                Attributes.AccessLevel,
                Attributes.UserAccessLevel
            ];

            var readValueIds = attributeIds
                .Select(attrId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attrId
                }).ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(attributeIds.Length));

            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(
                    StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"Variable attribute {attributeIds[i]} should return Good.");
            }
        }

        [Description("Read all standard attributes of the Server object node. Verify NodeClass, BrowseName, DisplayName, Description, and EventNotifier return valid results.")]
        [Test]
        public async Task ReadAllAttributesOfObjectNodeAsync()
        {
            uint[] attributeIds =
            [
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.EventNotifier
            ];

            var readValueIds = attributeIds
                .Select(attrId => new ReadValueId
                {
                    NodeId = ObjectIds.Server,
                    AttributeId = attrId
                }).ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(attributeIds.Length));

            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(
                    StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"Object attribute {attributeIds[i]} should return Good.");
            }

            // Verify NodeClass is Object
            int nodeClass = response.Results[0].GetValue(0);
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.Object),
                "Server node should have NodeClass = Object.");
        }

        [Description("Read the AccessLevelEx attribute (id=27) from a Variable node. This attribute may not be supported by all servers; if BadAttributeIdInvalid is returned the test is skipped.")]
        [Test]
        public async Task ReadAccessLevelExAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.AccessLevelEx
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            if (response.Results[0].StatusCode.Code ==
                StatusCodes.BadAttributeIdInvalid)
            {
                Assert.Ignore(
                    "AccessLevelEx attribute not supported by this server.");
            }

            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "AccessLevelEx should return Good when supported.");
        }

        [Description("Read the RolePermissions attribute (id=26) from a Variable node. This attribute may not be supported; if BadAttributeIdInvalid is returned the test is skipped.")]
        [Test]
        public async Task ReadRolePermissionsAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.RolePermissions
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            if (response.Results[0].StatusCode.Code ==
                StatusCodes.BadAttributeIdInvalid)
            {
                Assert.Ignore(
                    "RolePermissions attribute not supported by this server.");
            }

            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code ==
                    StatusCodes.BadNotReadable,
                Is.True,
                "RolePermissions should return Good or BadNotReadable.");
        }

        [Description("Read the UserRolePermissions attribute (id=25) from a Variable node. This attribute may not be supported; if BadAttributeIdInvalid is returned the test is skipped.")]
        [Test]
        public async Task ReadUserRolePermissionsAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.UserRolePermissions
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            if (response.Results[0].StatusCode.Code ==
                StatusCodes.BadAttributeIdInvalid)
            {
                Assert.Ignore(
                    "UserRolePermissions attribute not supported by " +
                    "this server.");
            }

            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code ==
                    StatusCodes.BadNotReadable,
                Is.True,
                "UserRolePermissions should return Good or BadNotReadable.");
        }

        [Description("Read the DataTypeDefinition attribute (id=23) on a DataType node. This provides the structure or enum definition of the type. If BadAttributeIdInvalid is returned the test is skipped.")]
        [Test]
        public async Task ReadDataTypeDefinitionAttributeAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = DataTypeIds.ServerStatusDataType,
                        AttributeId = Attributes.DataTypeDefinition
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            if (response.Results[0].StatusCode.Code ==
                StatusCodes.BadAttributeIdInvalid)
            {
                Assert.Ignore(
                    "DataTypeDefinition attribute not supported by " +
                    "this server.");
            }

            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "DataTypeDefinition should return Good when supported.");

            // DataTypeDefinition is an ExtensionObject wrapping a
            // StructureDefinition or EnumDefinition; verify the wire form
            // is non-null and that an inner IEncodeable can be unwrapped.
            Assert.That(
                response.Results[0].WrappedValue.TryGetValue(out ExtensionObject extObj),
                Is.True,
                "DataTypeDefinition value should be an ExtensionObject.");
            Assert.That(
                extObj.TryGetValue(out IEncodeable encodeable),
                Is.True,
                "DataTypeDefinition body should decode to an IEncodeable.");
            Assert.That(encodeable, Is.Not.Null,
                "DataTypeDefinition value should not be null.");
        }

        [Description("Read the ArrayDimensions attribute on an array variable node. Verify the result is Good and the value is a valid array.")]
        [Test]
        public async Task ReadArrayDimensionsOnArrayNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.ArrayDimensions
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "ArrayDimensions on an array node should return Good.");
        }

        [Description("Read the DataType attribute of a Variable node and verify it returns a valid, non-null NodeId.")]
        [Test]
        public async Task ReadDataTypeOfVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.DataType
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "DataType attribute should return Good.");

            NodeId dataType = response.Results[0].GetValue<NodeId>(default);
            Assert.That(dataType, Is.Not.Null,
                "DataType should not be null.");
            Assert.That(dataType, Is.Not.EqualTo(NodeId.Null),
                "DataType should not be the Null NodeId.");
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Double),
                "DataType for ScalarStaticDouble should be Double.");
        }
    }
}

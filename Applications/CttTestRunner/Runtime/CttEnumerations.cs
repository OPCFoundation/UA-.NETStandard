/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Jint;

namespace Opc.Ua.CttTestRunner.Runtime
{
    /// <summary>
    /// Registers OPC UA enumerations as JavaScript objects.
    /// </summary>
    public static class CttEnumerations
    {
        public static void Register(Engine engine)
        {
            // TimestampsToReturn
            engine.Execute(@"
                var TimestampsToReturn = { Source: 0, Server: 1, Both: 2, Neither: 3, Invalid: 4 };
            ");

            // MonitoringMode
            engine.Execute(@"
                var MonitoringMode = { Disabled: 0, Sampling: 1, Reporting: 2 };
            ");

            // BrowseDirection
            engine.Execute(@"
                var BrowseDirection = { Forward: 0, Inverse: 1, Both: 2, Invalid: 3 };
            ");

            // Attribute IDs
            engine.Execute(@"
                var Attribute = {
                    NodeId: 1, NodeClass: 2, BrowseName: 3, DisplayName: 4,
                    Description: 5, WriteMask: 6, UserWriteMask: 7,
                    IsAbstract: 8, Symmetric: 9, InverseName: 10,
                    ContainsNoLoops: 11, EventNotifier: 12,
                    Value: 13, DataType: 14, ValueRank: 15,
                    ArrayDimensions: 16, AccessLevel: 17, UserAccessLevel: 18,
                    MinimumSamplingInterval: 19, Historizing: 20,
                    Executable: 21, UserExecutable: 22,
                    DataTypeDefinition: 23, RolePermissions: 24,
                    UserRolePermissions: 25, AccessRestrictions: 26,
                    AccessLevelEx: 27
                };
            ");

            // NodeClass
            engine.Execute(@"
                var NodeClass = {
                    Unspecified: 0, Object: 1, Variable: 2, Method: 4,
                    ObjectType: 8, VariableType: 16, ReferenceType: 32,
                    DataType: 64, View: 128
                };
            ");

            // MessageSecurityMode
            engine.Execute(@"
                var MessageSecurityMode = {
                    Invalid: 0, None: 1, Sign: 2, SignAndEncrypt: 3
                };
            ");

            // UserTokenType
            engine.Execute(@"
                var UserTokenType = {
                    Anonymous: 0, UserName: 1, Certificate: 2, IssuedToken: 3
                };
            ");

            // ApplicationType
            engine.Execute(@"
                var ApplicationType = {
                    Server: 0, Client: 1, ClientAndServer: 2, DiscoveryServer: 3
                };
            ");

            // BrowseResultMask
            engine.Execute(@"
                var BrowseResultMask = {
                    None: 0, ReferenceTypeId: 1, IsForward: 2,
                    NodeClass: 4, BrowseName: 8, DisplayName: 16,
                    TypeDefinition: 32, All: 63
                };
            ");

            // BuiltInType
            engine.Execute(@"
                var BuiltInType = {
                    Null: 0, Boolean: 1, SByte: 2, Byte: 3,
                    Int16: 4, UInt16: 5, Int32: 6, UInt32: 7,
                    Int64: 8, UInt64: 9, Float: 10, Double: 11,
                    String: 12, DateTime: 13, Guid: 14, ByteString: 15,
                    XmlElement: 16, NodeId: 17, ExpandedNodeId: 18,
                    StatusCode: 19, QualifiedName: 20, LocalizedText: 21,
                    ExtensionObject: 22, DataValue: 23, Variant: 24,
                    DiagnosticInfo: 25, Number: 26, Integer: 27,
                    UInteger: 28, Enumeration: 29
                };
            ");

            // StatusCode constants (most commonly used)
            engine.Execute(@"
                var StatusCode = {
                    Good: 0x00000000,
                    Uncertain: 0x40000000,
                    Bad: 0x80000000,
                    BadNodeIdInvalid: 0x80330000,
                    BadNodeIdUnknown: 0x80340000,
                    BadAttributeIdInvalid: 0x80350000,
                    BadNotReadable: 0x803A0000,
                    BadNotWritable: 0x803B0000,
                    BadOutOfRange: 0x803C0000,
                    BadNotSupported: 0x803D0000,
                    BadNotFound: 0x803E0000,
                    BadTypeMismatch: 0x80740000,
                    BadInvalidArgument: 0x80AB0000,
                    BadNotImplemented: 0x80400000,
                    BadMonitoredItemFilterUnsupported: 0x80440000,
                    BadUserAccessDenied: 0x801F0000,
                    BadSessionIdInvalid: 0x80250000,
                    BadNothingToDo: 0x800F0000,
                    BadTooManyOperations: 0x80100000,
                    BadServiceUnsupported: 0x800B0000,
                    BadSecurityModeRejected: 0x80310000,
                    BadSecurityPolicyRejected: 0x80550000,
                    BadCertificateInvalid: 0x80120000,
                    BadIdentityTokenInvalid: 0x80200000,
                    BadIdentityTokenRejected: 0x80210000,
                    BadWriteNotSupported: 0x80730000,
                    BadIndexRangeInvalid: 0x80360000,
                    BadIndexRangeNoData: 0x80370000,
                    BadHistoryOperationUnsupported: 0x80860000,
                    BadContinuationPointInvalid: 0x804A0000,
                    BadNoContinuationPoints: 0x804B0000
                };
            ");

            // DeadbandType
            engine.Execute(@"
                var DeadbandType = { None: 0, Absolute: 1, Percent: 2 };
            ");

            // DataChangeTrigger
            engine.Execute(@"
                var DataChangeTrigger = {
                    Status: 0, StatusValue: 1, StatusValueTimestamp: 2
                };
            ");
        }
    }
}

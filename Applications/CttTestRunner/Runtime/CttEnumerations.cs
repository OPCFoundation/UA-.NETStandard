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

            // IdentifierType — NodeId identifier types
            engine.Execute(@"
                var IdentifierType = {
                    Numeric: 0, String: 1, Guid: 2, Opaque: 3
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

            // HistoryUpdateType
            engine.Execute(@"
                var HistoryUpdateType = {
                    Insert: 1, Replace: 2, Update: 3, Delete: 4
                };
            ");

            // PerformInsertReplace (alias)
            engine.Execute(@"
                var PerformInsertReplace = {
                    Insert: 1, Replace: 2, Update: 3, Remove: 4
                };
            ");

            // FilterOperator
            engine.Execute(@"
                var FilterOperator = {
                    Equals: 0, IsNull: 1, GreaterThan: 2, LessThan: 3,
                    GreaterThanOrEqual: 4, LessThanOrEqual: 5, Like: 6,
                    Not: 7, Between: 8, InList: 9, And: 10, Or: 11,
                    Cast: 12, InView: 13, OfType: 14, RelatedTo: 15,
                    BitwiseAnd: 16, BitwiseOr: 17
                };
            ");

            // AggregateType
            engine.Execute(@"
                var AggregateType = {
                    Interpolative: 0, Average: 1, TimeAverage: 2, Total: 3,
                    Minimum: 4, Maximum: 5, MinimumActualTime: 6,
                    MaximumActualTime: 7, Range: 8, Count: 9
                };
            ");

            // ServerState
            engine.Execute(@"
                var ServerState = {
                    Running: 0, Failed: 1, NoConfiguration: 2,
                    Suspended: 3, Shutdown: 4, Test: 5,
                    CommunicationFault: 6, Unknown: 7
                };
            ");

            // NodeAttributesMask
            engine.Execute(@"
                var NodeAttributesMask = {
                    None: 0, AccessLevel: 1, ArrayDimensions: 2,
                    BrowseName: 4, ContainsNoLoops: 8, DataType: 16,
                    Description: 32, DisplayName: 64, EventNotifier: 128,
                    Executable: 256, Historizing: 512, InverseName: 1024,
                    IsAbstract: 2048, MinimumSamplingInterval: 4096,
                    NodeClass: 8192, NodeId: 16384, Symmetric: 32768,
                    UserAccessLevel: 65536, UserExecutable: 131072,
                    UserWriteMask: 262144, ValueRank: 524288,
                    WriteMask: 1048576, Value: 2097152,
                    DataTypeDefinition: 4194304, RolePermissions: 8388608,
                    AccessRestrictions: 16777216, AccessLevelEx: 33554432,
                    All: 67108863
                };
            ");

            // WriteMask enum (same values as NodeAttributesMask)
            engine.Execute("var WriteMask = NodeAttributesMask;");

            // AccessLevelType
            engine.Execute(@"
                var AccessLevelType = {
                    None: 0, CurrentRead: 1, CurrentWrite: 2,
                    HistoryRead: 4, HistoryWrite: 8,
                    SemanticChange: 16, StatusWrite: 32,
                    TimestampWrite: 64
                };
            ");

            // EventNotifierType
            engine.Execute(@"
                var EventNotifierType = {
                    None: 0, SubscribeToEvents: 1, HistoryRead: 4,
                    HistoryWrite: 8
                };
            ");

            // RedundancySupport
            engine.Execute(@"
                var RedundancySupport = {
                    None: 0, Cold: 1, Warm: 2, Hot: 3,
                    Transparent: 4, HotAndMirrored: 5
                };
            ");

            // ComplianceLevel
            engine.Execute(@"
                var ComplianceLevel = {
                    Untested: 0, Partial: 1, SelfTested: 2, Certified: 3
                };
            ");

            // PerformUpdateType
            engine.Execute(@"
                var PerformUpdateType = {
                    Insert: 1, Replace: 2, Update: 3, Remove: 4,
                    Validate: function(v) { return v >= 1 && v <= 4; }
                };
            ");

            // ExceptionDeviationType
            engine.Execute(@"
                var ExceptionDeviationType = {
                    AbsoluteValue: 0, PercentOfValue: 1,
                    PercentOfRange: 2, PercentOfEURange: 3,
                    Unknown: 4
                };
            ");

            // StructureType
            engine.Execute(@"
                var StructureType = {
                    Structure: 0, StructureWithOptionalFields: 1,
                    Union: 2, StructureWithSubtypedValues: 3,
                    UnionWithSubtypedValues: 4
                };
            ");

            // SecurityTokenRequestType
            engine.Execute(@"
                var SecurityTokenRequestType = { Issue: 0, Renew: 1 };
            ");

            // NamingRuleType
            engine.Execute(@"
                var NamingRuleType = {
                    Mandatory: 1, Optional: 2, Constraint: 3
                };
            ");

            // ModelChangeStructureVerbMask
            engine.Execute(@"
                var ModelChangeStructureVerbMask = {
                    NodeAdded: 1, NodeDeleted: 2, ReferenceAdded: 4,
                    ReferenceDeleted: 8, DataTypeChanged: 16
                };
            ");

            // AxisScaleEnumeration
            engine.Execute(@"
                var AxisScaleEnumeration = { Linear: 0, Log: 1, Ln: 2 };
            ");

            // SecurityPolicy — C++ bound enum in original CTT
            engine.Execute(@"
                var SecurityPolicy = {
                    None: 'http://opcfoundation.org/UA/SecurityPolicy#None',
                    Basic128Rsa15: 'http://opcfoundation.org/UA/SecurityPolicy#Basic128Rsa15',
                    Basic192Rsa15: 'http://opcfoundation.org/UA/SecurityPolicy#Basic192Rsa15',
                    Basic256: 'http://opcfoundation.org/UA/SecurityPolicy#Basic256',
                    Basic256Rsa15: 'http://opcfoundation.org/UA/SecurityPolicy#Basic256Rsa15',
                    Basic256Sha256: 'http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256',
                    Aes128Sha256RsaOaep: 'http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep',
                    Aes256Sha256RsaPss: 'http://opcfoundation.org/UA/SecurityPolicy#Aes256_Sha256_RsaPss',
                    Validate: function(v) { return typeof v === 'string' && v.indexOf('SecurityPolicy') >= 0; },
                    policyFromString: function(s) { return s; },
                    policyToString: function(p) { return p; }
                };
                SecurityPolicy.length = 8;
            ");

            // Bit helper
            engine.Execute(@"
                var Bit = {
                    IsOn: function(value, bit) { return (value & (1 << bit)) !== 0; },
                    IsOff: function(value, bit) { return (value & (1 << bit)) === 0; }
                };
            ");

            // OPCF namespace
            engine.Execute(@"
                var OPCF = {};
                OPCF.HA = {};
            ");

            // Constants — C++ bound numeric and DateTime limits
            engine.Execute(@"
                var Constants = {
                    Byte_Max: function() { return 255; },
                    Byte_Min: function() { return 0; },
                    SByte_Max: function() { return 127; },
                    SByte_Min: function() { return -128; },
                    Int16_Max: function() { return 32767; },
                    Int16_Min: function() { return -32768; },
                    UInt16_Max: function() { return 65535; },
                    UInt16_Min: function() { return 0; },
                    Int32_Max: function() { return 2147483647; },
                    Int32_Min: function() { return -2147483648; },
                    UInt32_Max: function() { return 4294967295; },
                    UInt32_Min: function() { return 0; },
                    Int64_Max: function() { return 9223372036854775807; },
                    Int64_Min: function() { return -9223372036854775808; },
                    UInt64_Max: function() { return 18446744073709551615; },
                    UInt64_Min: function() { return 0; },
                    Float_Max: function() { return 3.4028235e+38; },
                    Float_Min: function() { return -3.4028235e+38; },
                    Double_Max: function() { return 1.7976931348623157e+308; },
                    Double_Min: function() { return -1.7976931348623157e+308; },
                    DateTime_Min: function() { var dt = new UaDateTime(); dt._date = new Date(0); return dt; },
                    DateTime_Max: function() { var dt = new UaDateTime(); dt._date = new Date(9999, 11, 31); return dt; }
                };
            ");

            // Aggregate bits — these will be overridden by redefiners.js with const
            // Only set the ones redefiners.js does NOT define
            engine.Execute(@"
                var AggregateBit = {
                    Raw: 0x00, Calculated: 0x01, Interpolated: 0x02,
                    DataSourceMask: 0x03, Partial: 0x04, ExtraData: 0x08,
                    MultipleValues: 0x10
                };
            ");

            // KeyPairCollection — used by some CTT helpers
            engine.Execute(@"
                function KeyPairCollection() {
                    this._keys = [];
                    this._values = [];
                    this.Set = function(key, value) {
                        var idx = this._keys.indexOf(key);
                        if (idx >= 0) { this._values[idx] = value; }
                        else { this._keys.push(key); this._values.push(value); }
                    };
                    this.Get = function(key) {
                        var idx = this._keys.indexOf(key);
                        return idx >= 0 ? this._values[idx] : undefined;
                    };
                    this.Keys = function() { return this._keys.slice(); };
                    this.Values = function() { return this._values.slice(); };
                    this.Count = function() { return this._keys.length; };
                    this.Remove = function(key) {
                        var idx = this._keys.indexOf(key);
                        if (idx >= 0) { this._keys.splice(idx,1); this._values.splice(idx,1); }
                    };
                    this.Contains = function(key) { return this._keys.indexOf(key) >= 0; };
                    this.toString = function() { return 'KeyPairCollection[' + this._keys.length + ']'; };
                }
            ");

            // gOpcServer stub — the embedded OPC server in the C++ CTT.
            // We stub all methods needed by Helpers.js and ClassBased scripts.
            engine.Execute(@"
                function UaOpcServer() {
                    this.startServer = function() { return true; };
                    this.stopServer = function() { return true; };
                    this.getConfigPath = function() { return '.'; };
                    this.cleanUpPubSubFiles = function() {};
                    this.importNodeSet = function() { return true; };
                    this.getEndpointDescription = function() { return new UaEndpointDescription(); };
                    this.getNameSpaceIndexFromUri = function() { return 0; };
                    this.addObjectNode = function() { var sc = new UaStatusCode(); return sc; };
                    this.addVariableNode = function() { var sc = new UaStatusCode(); return sc; };
                    this.addMethodNode = function() { var sc = new UaStatusCode(); return sc; };
                    this.addDataTypeNode = function() { var sc = new UaStatusCode(); return sc; };
                    this.addReferenceTypeNode = function() { var sc = new UaStatusCode(); return sc; };
                    this.addVariableTypeNode = function() { var sc = new UaStatusCode(); return sc; };
                    this.addViewNode = function() { var sc = new UaStatusCode(); return sc; };
                    this.addReference = function() { var sc = new UaStatusCode(); return sc; };
                    this.deleteNode = function() { var sc = new UaStatusCode(); return sc; };
                    this.deleteReference = function() { var sc = new UaStatusCode(); return sc; };
                    this.createGenericMethod = function() { return true; };
                    this.addGenericMethodInfo = function() {};
                    this.getMethodInfoMethodStatus = function() { return 0; };
                    this.getMethodInfoMethodInputArguments = function() { return []; };
                    this.setMethodInfoMethodStatus = function() {};
                    this.setMethodInfoMode = function() {};
                    this.setMethodInfoScriptOutputArguments = function() {};
                    this.setMethodInfoScriptResult = function() {};
                    this.setMethodInfoStandardOutputArguments = function() {};
                    this.setMethodInfoStandardResult = function() {};
                    this.getApplicationInstanceCertificateLocation = function() { return ''; };
                    this.getRawPubSubMessageCacheSize = function() { return 0; };
                    this.getRawPubSubMessageFromCache = function() { return null; };
                    this.pubSubConfiguration2DataTypeFromFile = function() { return null; };
                    this.pubSubConfiguration2DataTypeToBinaryBlob = function() { return null; };
                    this.setPubSubConfiguration2DataType = function() {};
                }
            ");

            // ExpectedAndAcceptedResults helper
            engine.Execute(@"
                function ExpectedAndAcceptedResults(statusCodes) {
                    this.ExpectedResults = statusCodes || [];
                    this.containsStatusCode = function(sc) {
                        for (var i = 0; i < this.ExpectedResults.length; i++) {
                            if (this.ExpectedResults[i] === sc) return true;
                        }
                        return false;
                    };
                    this.addExpectedResult = function(sc) {
                        this.ExpectedResults.push(sc);
                    };
                    this.toString = function() {
                        return 'ExpectedAndAcceptedResults[' + this.ExpectedResults.length + ']';
                    };
                }
            ");
        }
    }
}

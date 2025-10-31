/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;

#pragma warning disable 1591

namespace Opc.Ua
{
    #region DataType Identifiers
    /// <exclude />


    public static partial class DataTypes
    {
        public const uint BaseDataType = 24;

        public const uint Number = 26;

        public const uint Integer = 27;

        public const uint UInteger = 28;

        public const uint Enumeration = 29;

        public const uint Boolean = 1;

        public const uint SByte = 2;

        public const uint Byte = 3;

        public const uint Int16 = 4;

        public const uint UInt16 = 5;

        public const uint Int32 = 6;

        public const uint UInt32 = 7;

        public const uint Int64 = 8;

        public const uint UInt64 = 9;

        public const uint Float = 10;

        public const uint Double = 11;

        public const uint String = 12;

        public const uint DateTime = 13;

        public const uint Guid = 14;

        public const uint ByteString = 15;

        public const uint XmlElement = 16;

        public const uint NodeId = 17;

        public const uint ExpandedNodeId = 18;

        public const uint StatusCode = 19;

        public const uint QualifiedName = 20;

        public const uint LocalizedText = 21;

        public const uint Structure = 22;

        public const uint DiagnosticInfo = 25;

        public const uint Union = 12756;

        public const uint KeyValuePair = 14533;

        public const uint AdditionalParametersType = 16313;

        public const uint EphemeralKeyType = 17548;

        public const uint EndpointType = 15528;

        public const uint BitFieldDefinition = 32421;

        public const uint RationalNumber = 18806;

        public const uint Vector = 18807;

        public const uint ThreeDVector = 18808;

        public const uint CartesianCoordinates = 18809;

        public const uint ThreeDCartesianCoordinates = 18810;

        public const uint Orientation = 18811;

        public const uint ThreeDOrientation = 18812;

        public const uint Frame = 18813;

        public const uint ThreeDFrame = 18814;

        public const uint IdentityMappingRuleType = 15634;

        public const uint CurrencyUnitType = 23498;

        public const uint AnnotationDataType = 32434;

        public const uint LinearConversionDataType = 32435;

        public const uint QuantityDimension = 32438;

        public const uint TrustListDataType = 12554;

        public const uint BaseConfigurationDataType = 15434;

        public const uint BaseConfigurationRecordDataType = 15435;

        public const uint CertificateGroupDataType = 15436;

        public const uint ConfigurationUpdateTargetType = 15538;

        public const uint TransactionErrorType = 32285;

        public const uint ApplicationConfigurationDataType = 23743;

        public const uint ApplicationIdentityDataType = 15556;

        public const uint EndpointDataType = 15557;

        public const uint ServerEndpointDataType = 15558;

        public const uint SecuritySettingsDataType = 15559;

        public const uint UserTokenSettingsDataType = 15560;

        public const uint ServiceCertificateDataType = 23724;

        public const uint AuthorizationServiceConfigurationDataType = 23744;

        public const uint DecimalDataType = 17861;

        public const uint DataTypeSchemaHeader = 15534;

        public const uint DataTypeDescription = 14525;

        public const uint StructureDescription = 15487;

        public const uint EnumDescription = 15488;

        public const uint SimpleTypeDescription = 15005;

        public const uint UABinaryFileDataType = 15006;

        public const uint PortableQualifiedName = 24105;

        public const uint PortableNodeId = 24106;

        public const uint UnsignedRationalNumber = 24107;

        public const uint PubSubState = 14647;

        public const uint DataSetMetaDataType = 14523;

        public const uint FieldMetaData = 14524;

        public const uint ConfigurationVersionDataType = 14593;

        public const uint PublishedDataSetDataType = 15578;

        public const uint PublishedDataSetSourceDataType = 15580;

        public const uint PublishedVariableDataType = 14273;

        public const uint PublishedDataItemsDataType = 15581;

        public const uint PublishedEventsDataType = 15582;

        public const uint PublishedDataSetCustomSourceDataType = 25269;

        public const uint ActionTargetDataType = 18593;

        public const uint PublishedActionDataType = 18594;

        public const uint ActionMethodDataType = 18597;

        public const uint PublishedActionMethodDataType = 18793;

        public const uint DataSetWriterDataType = 15597;

        public const uint DataSetWriterTransportDataType = 15598;

        public const uint DataSetWriterMessageDataType = 15605;

        public const uint PubSubGroupDataType = 15609;

        public const uint WriterGroupDataType = 15480;

        public const uint WriterGroupTransportDataType = 15611;

        public const uint WriterGroupMessageDataType = 15616;

        public const uint PubSubConnectionDataType = 15617;

        public const uint ConnectionTransportDataType = 15618;

        public const uint NetworkAddressDataType = 15502;

        public const uint NetworkAddressUrlDataType = 15510;

        public const uint ReaderGroupDataType = 15520;

        public const uint ReaderGroupTransportDataType = 15621;

        public const uint ReaderGroupMessageDataType = 15622;

        public const uint DataSetReaderDataType = 15623;

        public const uint DataSetReaderTransportDataType = 15628;

        public const uint DataSetReaderMessageDataType = 15629;

        public const uint SubscribedDataSetDataType = 15630;

        public const uint TargetVariablesDataType = 15631;

        public const uint FieldTargetDataType = 14744;

        public const uint SubscribedDataSetMirrorDataType = 15635;

        public const uint PubSubConfigurationDataType = 15530;

        public const uint StandaloneSubscribedDataSetRefDataType = 23599;

        public const uint StandaloneSubscribedDataSetDataType = 23600;

        public const uint SecurityGroupDataType = 23601;

        public const uint PubSubKeyPushTargetDataType = 25270;

        public const uint PubSubConfiguration2DataType = 23602;

        public const uint UadpWriterGroupMessageDataType = 15645;

        public const uint UadpDataSetWriterMessageDataType = 15652;

        public const uint UadpDataSetReaderMessageDataType = 15653;

        public const uint JsonWriterGroupMessageDataType = 15657;

        public const uint JsonDataSetWriterMessageDataType = 15664;

        public const uint JsonDataSetReaderMessageDataType = 15665;

        public const uint QosDataType = 23603;

        public const uint TransmitQosDataType = 23604;

        public const uint TransmitQosPriorityDataType = 23605;

        public const uint ReceiveQosDataType = 23608;

        public const uint ReceiveQosPriorityDataType = 23609;

        public const uint DatagramConnectionTransportDataType = 17467;

        public const uint DatagramConnectionTransport2DataType = 23612;

        public const uint DatagramWriterGroupTransportDataType = 15532;

        public const uint DatagramWriterGroupTransport2DataType = 23613;

        public const uint DatagramDataSetReaderTransportDataType = 23614;

        public const uint DtlsPubSubConnectionDataType = 18794;

        public const uint BrokerConnectionTransportDataType = 15007;

        public const uint BrokerWriterGroupTransportDataType = 15667;

        public const uint BrokerDataSetWriterTransportDataType = 15669;

        public const uint BrokerDataSetReaderTransportDataType = 15670;

        public const uint PubSubConfigurationRefDataType = 25519;

        public const uint PubSubConfigurationValueDataType = 25520;

        public const uint JsonNetworkMessage = 19311;

        public const uint JsonDataSetMessage = 19312;

        public const uint JsonDataSetMetaDataMessage = 19313;

        public const uint JsonApplicationDescriptionMessage = 19314;

        public const uint JsonServerEndpointsMessage = 19315;

        public const uint JsonStatusMessage = 19316;

        public const uint JsonPubSubConnectionMessage = 19317;

        public const uint JsonActionMetaDataMessage = 19318;

        public const uint JsonActionResponderMessage = 19319;

        public const uint JsonActionNetworkMessage = 19320;

        public const uint JsonActionRequestMessage = 19321;

        public const uint JsonActionResponseMessage = 19322;

        public const uint AliasNameDataType = 23468;

        public const uint UserManagementDataType = 24281;

        public const uint PriorityMappingEntryType = 25220;

        public const uint LldpManagementAddressTxPortType = 18953;

        public const uint LldpManagementAddressType = 18954;

        public const uint LldpTlvType = 18955;

        public const uint ReferenceDescriptionDataType = 32659;

        public const uint ReferenceListEntryDataType = 32660;

        public const uint LogRecord = 19361;

        public const uint LogRecordsDataType = 19745;

        public const uint SpanContextDataType = 19746;

        public const uint TraceContextDataType = 19747;

        public const uint NameValuePair = 19748;

        public const uint IdType = 256;

        public const uint NodeClass = 257;

        public const uint PermissionType = 94;

        public const uint AccessRestrictionType = 95;

        public const uint RolePermissionType = 96;

        public const uint DataTypeDefinition = 97;

        public const uint StructureType = 98;

        public const uint StructureField = 101;

        public const uint StructureDefinition = 99;

        public const uint EnumDefinition = 100;

        public const uint Node = 258;

        public const uint InstanceNode = 11879;

        public const uint TypeNode = 11880;

        public const uint ObjectNode = 261;

        public const uint ObjectTypeNode = 264;

        public const uint VariableNode = 267;

        public const uint VariableTypeNode = 270;

        public const uint ReferenceTypeNode = 273;

        public const uint MethodNode = 276;

        public const uint ViewNode = 279;

        public const uint DataTypeNode = 282;

        public const uint ReferenceNode = 285;

        public const uint Argument = 296;

        public const uint EnumValueType = 7594;

        public const uint EnumField = 102;

        public const uint OptionSet = 12755;

        public const uint Duration = 290;

        public const uint UtcTime = 294;

        public const uint LocaleId = 295;

        public const uint TimeZoneDataType = 8912;

        public const uint Index = 17588;

        public const uint IntegerId = 288;

        public const uint ApplicationDescription = 308;

        public const uint RequestHeader = 389;

        public const uint ResponseHeader = 392;

        public const uint ServiceFault = 395;

        public const uint SessionlessInvokeRequestType = 15901;

        public const uint SessionlessInvokeResponseType = 20999;

        public const uint FindServersRequest = 420;

        public const uint FindServersResponse = 423;

        public const uint ServerOnNetwork = 12189;

        public const uint FindServersOnNetworkRequest = 12190;

        public const uint FindServersOnNetworkResponse = 12191;

        public const uint UserTokenPolicy = 304;

        public const uint EndpointDescription = 312;

        public const uint GetEndpointsRequest = 426;

        public const uint GetEndpointsResponse = 429;

        public const uint RegisteredServer = 432;

        public const uint RegisterServerRequest = 435;

        public const uint RegisterServerResponse = 438;

        public const uint DiscoveryConfiguration = 12890;

        public const uint MdnsDiscoveryConfiguration = 12891;

        public const uint RegisterServer2Request = 12193;

        public const uint RegisterServer2Response = 12194;

        public const uint ChannelSecurityToken = 441;

        public const uint OpenSecureChannelRequest = 444;

        public const uint OpenSecureChannelResponse = 447;

        public const uint CloseSecureChannelRequest = 450;

        public const uint CloseSecureChannelResponse = 453;

        public const uint SignedSoftwareCertificate = 344;

        public const uint SignatureData = 456;

        public const uint CreateSessionRequest = 459;

        public const uint CreateSessionResponse = 462;

        public const uint UserIdentityToken = 316;

        public const uint AnonymousIdentityToken = 319;

        public const uint UserNameIdentityToken = 322;

        public const uint X509IdentityToken = 325;

        public const uint IssuedIdentityToken = 938;

        public const uint ActivateSessionRequest = 465;

        public const uint ActivateSessionResponse = 468;

        public const uint CloseSessionRequest = 471;

        public const uint CloseSessionResponse = 474;

        public const uint CancelRequest = 477;

        public const uint CancelResponse = 480;

        public const uint NodeAttributes = 349;

        public const uint ObjectAttributes = 352;

        public const uint VariableAttributes = 355;

        public const uint MethodAttributes = 358;

        public const uint ObjectTypeAttributes = 361;

        public const uint VariableTypeAttributes = 364;

        public const uint ReferenceTypeAttributes = 367;

        public const uint DataTypeAttributes = 370;

        public const uint ViewAttributes = 373;

        public const uint GenericAttributeValue = 17606;

        public const uint GenericAttributes = 17607;

        public const uint AddNodesItem = 376;

        public const uint AddNodesResult = 483;

        public const uint AddNodesRequest = 486;

        public const uint AddNodesResponse = 489;

        public const uint AddReferencesItem = 379;

        public const uint AddReferencesRequest = 492;

        public const uint AddReferencesResponse = 495;

        public const uint DeleteNodesItem = 382;

        public const uint DeleteNodesRequest = 498;

        public const uint DeleteNodesResponse = 501;

        public const uint DeleteReferencesItem = 385;

        public const uint DeleteReferencesRequest = 504;

        public const uint DeleteReferencesResponse = 507;

        public const uint ViewDescription = 511;

        public const uint BrowseDescription = 514;

        public const uint ReferenceDescription = 518;

        public const uint BrowseResult = 522;

        public const uint BrowseRequest = 525;

        public const uint BrowseResponse = 528;

        public const uint BrowseNextRequest = 531;

        public const uint BrowseNextResponse = 534;

        public const uint RelativePathElement = 537;

        public const uint RelativePath = 540;

        public const uint BrowsePath = 543;

        public const uint BrowsePathTarget = 546;

        public const uint BrowsePathResult = 549;

        public const uint TranslateBrowsePathsToNodeIdsRequest = 552;

        public const uint TranslateBrowsePathsToNodeIdsResponse = 555;

        public const uint RegisterNodesRequest = 558;

        public const uint RegisterNodesResponse = 561;

        public const uint UnregisterNodesRequest = 564;

        public const uint UnregisterNodesResponse = 567;

        public const uint Counter = 289;

        public const uint EndpointConfiguration = 331;

        public const uint QueryDataDescription = 570;

        public const uint NodeTypeDescription = 573;

        public const uint QueryDataSet = 577;

        public const uint NodeReference = 580;

        public const uint ContentFilterElement = 583;

        public const uint ContentFilter = 586;

        public const uint FilterOperand = 589;

        public const uint ElementOperand = 592;

        public const uint LiteralOperand = 595;

        public const uint AttributeOperand = 598;

        public const uint SimpleAttributeOperand = 601;

        public const uint ContentFilterElementResult = 604;

        public const uint ContentFilterResult = 607;

        public const uint ParsingResult = 610;

        public const uint QueryFirstRequest = 613;

        public const uint QueryFirstResponse = 616;

        public const uint QueryNextRequest = 619;

        public const uint QueryNextResponse = 622;

        public const uint ReadValueId = 626;

        public const uint ReadRequest = 629;

        public const uint ReadResponse = 632;

        public const uint HistoryReadValueId = 635;

        public const uint HistoryReadResult = 638;

        public const uint HistoryReadDetails = 641;

        public const uint ReadEventDetails = 644;

        public const uint ReadEventDetails2 = 32799;

        public const uint SortRuleElement = 18648;

        public const uint ReadEventDetailsSorted = 18649;

        public const uint ReadRawModifiedDetails = 647;

        public const uint ReadProcessedDetails = 650;

        public const uint ReadAtTimeDetails = 653;

        public const uint ReadAnnotationDataDetails = 23497;

        public const uint HistoryData = 656;

        public const uint ModificationInfo = 11216;

        public const uint HistoryModifiedData = 11217;

        public const uint HistoryEvent = 659;

        public const uint HistoryModifiedEvent = 32824;

        public const uint HistoryReadRequest = 662;

        public const uint HistoryReadResponse = 665;

        public const uint WriteValue = 668;

        public const uint WriteRequest = 671;

        public const uint WriteResponse = 674;

        public const uint HistoryUpdateDetails = 677;

        public const uint UpdateDataDetails = 680;

        public const uint UpdateStructureDataDetails = 11295;

        public const uint UpdateEventDetails = 683;

        public const uint DeleteRawModifiedDetails = 686;

        public const uint DeleteAtTimeDetails = 689;

        public const uint DeleteEventDetails = 692;

        public const uint HistoryUpdateResult = 695;

        public const uint HistoryUpdateRequest = 698;

        public const uint HistoryUpdateResponse = 701;

        public const uint CallMethodRequest = 704;

        public const uint CallMethodResult = 707;

        public const uint CallRequest = 710;

        public const uint CallResponse = 713;

        public const uint MonitoringFilter = 719;

        public const uint DataChangeFilter = 722;

        public const uint EventFilter = 725;

        public const uint AggregateConfiguration = 948;

        public const uint AggregateFilter = 728;

        public const uint MonitoringFilterResult = 731;

        public const uint EventFilterResult = 734;

        public const uint AggregateFilterResult = 737;

        public const uint MonitoringParameters = 740;

        public const uint MonitoredItemCreateRequest = 743;

        public const uint MonitoredItemCreateResult = 746;

        public const uint CreateMonitoredItemsRequest = 749;

        public const uint CreateMonitoredItemsResponse = 752;

        public const uint MonitoredItemModifyRequest = 755;

        public const uint MonitoredItemModifyResult = 758;

        public const uint ModifyMonitoredItemsRequest = 761;

        public const uint ModifyMonitoredItemsResponse = 764;

        public const uint SetMonitoringModeRequest = 767;

        public const uint SetMonitoringModeResponse = 770;

        public const uint SetTriggeringRequest = 773;

        public const uint SetTriggeringResponse = 776;

        public const uint DeleteMonitoredItemsRequest = 779;

        public const uint DeleteMonitoredItemsResponse = 782;

        public const uint CreateSubscriptionRequest = 785;

        public const uint CreateSubscriptionResponse = 788;

        public const uint ModifySubscriptionRequest = 791;

        public const uint ModifySubscriptionResponse = 794;

        public const uint SetPublishingModeRequest = 797;

        public const uint SetPublishingModeResponse = 800;

        public const uint NotificationMessage = 803;

        public const uint NotificationData = 945;

        public const uint DataChangeNotification = 809;

        public const uint MonitoredItemNotification = 806;

        public const uint EventNotificationList = 914;

        public const uint EventFieldList = 917;

        public const uint HistoryEventFieldList = 920;

        public const uint StatusChangeNotification = 818;

        public const uint SubscriptionAcknowledgement = 821;

        public const uint PublishRequest = 824;

        public const uint PublishResponse = 827;

        public const uint RepublishRequest = 830;

        public const uint RepublishResponse = 833;

        public const uint TransferResult = 836;

        public const uint TransferSubscriptionsRequest = 839;

        public const uint TransferSubscriptionsResponse = 842;

        public const uint DeleteSubscriptionsRequest = 845;

        public const uint DeleteSubscriptionsResponse = 848;

        public const uint BuildInfo = 338;

        public const uint RedundantServerDataType = 853;

        public const uint EndpointUrlListDataType = 11943;

        public const uint NetworkGroupDataType = 11944;

        public const uint SamplingIntervalDiagnosticsDataType = 856;

        public const uint ServerDiagnosticsSummaryDataType = 859;

        public const uint ServerStatusDataType = 862;

        public const uint SessionDiagnosticsDataType = 865;

        public const uint SessionSecurityDiagnosticsDataType = 868;

        public const uint ServiceCounterDataType = 871;

        public const uint StatusResult = 299;

        public const uint SubscriptionDiagnosticsDataType = 874;

        public const uint ModelChangeStructureDataType = 877;

        public const uint SemanticChangeStructureDataType = 897;

        public const uint Range = 884;

        public const uint EUInformation = 887;

        public const uint ComplexNumberType = 12171;

        public const uint DoubleComplexNumberType = 12172;

        public const uint AxisInformation = 12079;

        public const uint XVType = 12080;

        public const uint ProgramDiagnosticDataType = 894;

        public const uint ProgramDiagnostic2DataType = 24033;

        public const uint Annotation = 891;
    }
    #endregion

    #region Object Identifiers
    /// <exclude />


    public static partial class Objects
    {

        public const uint ModellingRule_Mandatory = 78;

        public const uint ModellingRule_Optional = 80;

        public const uint ModellingRule_ExposesItsArray = 83;

        public const uint ModellingRule_OptionalPlaceholder = 11508;

        public const uint ModellingRule_MandatoryPlaceholder = 11510;

        public const uint XmlSchema_TypeSystem = 92;

        public const uint OPCBinarySchema_TypeSystem = 93;

        public const uint WellKnownRole_Anonymous = 15644;

        public const uint Union_Encoding_DefaultBinary = 12766;

        public const uint KeyValuePair_Encoding_DefaultBinary = 14846;

        public const uint AdditionalParametersType_Encoding_DefaultBinary = 17537;

        public const uint EphemeralKeyType_Encoding_DefaultBinary = 17549;

        public const uint EndpointType_Encoding_DefaultBinary = 15671;

        public const uint BitFieldDefinition_Encoding_DefaultBinary = 32422;

        public const uint RationalNumber_Encoding_DefaultBinary = 18815;

        public const uint Vector_Encoding_DefaultBinary = 18816;

        public const uint ThreeDVector_Encoding_DefaultBinary = 18817;

        public const uint CartesianCoordinates_Encoding_DefaultBinary = 18818;

        public const uint ThreeDCartesianCoordinates_Encoding_DefaultBinary = 18819;

        public const uint Orientation_Encoding_DefaultBinary = 18820;

        public const uint ThreeDOrientation_Encoding_DefaultBinary = 18821;

        public const uint Frame_Encoding_DefaultBinary = 18822;

        public const uint ThreeDFrame_Encoding_DefaultBinary = 18823;

        public const uint IdentityMappingRuleType_Encoding_DefaultBinary = 15736;

        public const uint CurrencyUnitType_Encoding_DefaultBinary = 23507;

        public const uint AnnotationDataType_Encoding_DefaultBinary = 32560;

        public const uint LinearConversionDataType_Encoding_DefaultBinary = 32561;

        public const uint QuantityDimension_Encoding_DefaultBinary = 32562;

        public const uint TrustListDataType_Encoding_DefaultBinary = 12680;

        public const uint BaseConfigurationDataType_Encoding_DefaultBinary = 16538;

        public const uint BaseConfigurationRecordDataType_Encoding_DefaultBinary = 16539;

        public const uint CertificateGroupDataType_Encoding_DefaultBinary = 16540;

        public const uint ConfigurationUpdateTargetType_Encoding_DefaultBinary = 16541;

        public const uint TransactionErrorType_Encoding_DefaultBinary = 32382;

        public const uint ApplicationConfigurationDataType_Encoding_DefaultBinary = 23754;

        public const uint ApplicationIdentityDataType_Encoding_DefaultBinary = 16543;

        public const uint EndpointDataType_Encoding_DefaultBinary = 16544;

        public const uint ServerEndpointDataType_Encoding_DefaultBinary = 16545;

        public const uint SecuritySettingsDataType_Encoding_DefaultBinary = 16546;

        public const uint UserTokenSettingsDataType_Encoding_DefaultBinary = 16547;

        public const uint ServiceCertificateDataType_Encoding_DefaultBinary = 23725;

        public const uint AuthorizationServiceConfigurationDataType_Encoding_DefaultBinary = 23755;

        public const uint DecimalDataType_Encoding_DefaultBinary = 17863;

        public const uint DataTypeSchemaHeader_Encoding_DefaultBinary = 15676;

        public const uint DataTypeDescription_Encoding_DefaultBinary = 125;

        public const uint StructureDescription_Encoding_DefaultBinary = 126;

        public const uint EnumDescription_Encoding_DefaultBinary = 127;

        public const uint SimpleTypeDescription_Encoding_DefaultBinary = 15421;

        public const uint UABinaryFileDataType_Encoding_DefaultBinary = 15422;

        public const uint PortableQualifiedName_Encoding_DefaultBinary = 24108;

        public const uint PortableNodeId_Encoding_DefaultBinary = 24109;

        public const uint UnsignedRationalNumber_Encoding_DefaultBinary = 24110;

        public const uint DataSetMetaDataType_Encoding_DefaultBinary = 124;

        public const uint FieldMetaData_Encoding_DefaultBinary = 14839;

        public const uint ConfigurationVersionDataType_Encoding_DefaultBinary = 14847;

        public const uint PublishedDataSetDataType_Encoding_DefaultBinary = 15677;

        public const uint PublishedDataSetSourceDataType_Encoding_DefaultBinary = 15678;

        public const uint PublishedVariableDataType_Encoding_DefaultBinary = 14323;

        public const uint PublishedDataItemsDataType_Encoding_DefaultBinary = 15679;

        public const uint PublishedEventsDataType_Encoding_DefaultBinary = 15681;

        public const uint PublishedDataSetCustomSourceDataType_Encoding_DefaultBinary = 25529;

        public const uint ActionTargetDataType_Encoding_DefaultBinary = 18598;

        public const uint PublishedActionDataType_Encoding_DefaultBinary = 18599;

        public const uint ActionMethodDataType_Encoding_DefaultBinary = 18600;

        public const uint PublishedActionMethodDataType_Encoding_DefaultBinary = 18795;

        public const uint DataSetWriterDataType_Encoding_DefaultBinary = 15682;

        public const uint DataSetWriterTransportDataType_Encoding_DefaultBinary = 15683;

        public const uint DataSetWriterMessageDataType_Encoding_DefaultBinary = 15688;

        public const uint PubSubGroupDataType_Encoding_DefaultBinary = 15689;

        public const uint WriterGroupDataType_Encoding_DefaultBinary = 21150;

        public const uint WriterGroupTransportDataType_Encoding_DefaultBinary = 15691;

        public const uint WriterGroupMessageDataType_Encoding_DefaultBinary = 15693;

        public const uint PubSubConnectionDataType_Encoding_DefaultBinary = 15694;

        public const uint ConnectionTransportDataType_Encoding_DefaultBinary = 15695;

        public const uint NetworkAddressDataType_Encoding_DefaultBinary = 21151;

        public const uint NetworkAddressUrlDataType_Encoding_DefaultBinary = 21152;

        public const uint ReaderGroupDataType_Encoding_DefaultBinary = 21153;

        public const uint ReaderGroupTransportDataType_Encoding_DefaultBinary = 15701;

        public const uint ReaderGroupMessageDataType_Encoding_DefaultBinary = 15702;

        public const uint DataSetReaderDataType_Encoding_DefaultBinary = 15703;

        public const uint DataSetReaderTransportDataType_Encoding_DefaultBinary = 15705;

        public const uint DataSetReaderMessageDataType_Encoding_DefaultBinary = 15706;

        public const uint SubscribedDataSetDataType_Encoding_DefaultBinary = 15707;

        public const uint TargetVariablesDataType_Encoding_DefaultBinary = 15712;

        public const uint FieldTargetDataType_Encoding_DefaultBinary = 14848;

        public const uint SubscribedDataSetMirrorDataType_Encoding_DefaultBinary = 15713;

        public const uint PubSubConfigurationDataType_Encoding_DefaultBinary = 21154;

        public const uint StandaloneSubscribedDataSetRefDataType_Encoding_DefaultBinary = 23851;

        public const uint StandaloneSubscribedDataSetDataType_Encoding_DefaultBinary = 23852;

        public const uint SecurityGroupDataType_Encoding_DefaultBinary = 23853;

        public const uint PubSubKeyPushTargetDataType_Encoding_DefaultBinary = 25530;

        public const uint PubSubConfiguration2DataType_Encoding_DefaultBinary = 23854;

        public const uint UadpWriterGroupMessageDataType_Encoding_DefaultBinary = 15715;

        public const uint UadpDataSetWriterMessageDataType_Encoding_DefaultBinary = 15717;

        public const uint UadpDataSetReaderMessageDataType_Encoding_DefaultBinary = 15718;

        public const uint JsonWriterGroupMessageDataType_Encoding_DefaultBinary = 15719;

        public const uint JsonDataSetWriterMessageDataType_Encoding_DefaultBinary = 15724;

        public const uint JsonDataSetReaderMessageDataType_Encoding_DefaultBinary = 15725;

        public const uint QosDataType_Encoding_DefaultBinary = 23855;

        public const uint TransmitQosDataType_Encoding_DefaultBinary = 23856;

        public const uint TransmitQosPriorityDataType_Encoding_DefaultBinary = 23857;

        public const uint ReceiveQosDataType_Encoding_DefaultBinary = 23860;

        public const uint ReceiveQosPriorityDataType_Encoding_DefaultBinary = 23861;

        public const uint DatagramConnectionTransportDataType_Encoding_DefaultBinary = 17468;

        public const uint DatagramConnectionTransport2DataType_Encoding_DefaultBinary = 23864;

        public const uint DatagramWriterGroupTransportDataType_Encoding_DefaultBinary = 21155;

        public const uint DatagramWriterGroupTransport2DataType_Encoding_DefaultBinary = 23865;

        public const uint DatagramDataSetReaderTransportDataType_Encoding_DefaultBinary = 23866;

        public const uint DtlsPubSubConnectionDataType_Encoding_DefaultBinary = 18930;

        public const uint BrokerConnectionTransportDataType_Encoding_DefaultBinary = 15479;

        public const uint BrokerWriterGroupTransportDataType_Encoding_DefaultBinary = 15727;

        public const uint BrokerDataSetWriterTransportDataType_Encoding_DefaultBinary = 15729;

        public const uint BrokerDataSetReaderTransportDataType_Encoding_DefaultBinary = 15733;

        public const uint PubSubConfigurationRefDataType_Encoding_DefaultBinary = 25531;

        public const uint PubSubConfigurationValueDataType_Encoding_DefaultBinary = 25532;

        public const uint AliasNameDataType_Encoding_DefaultBinary = 23499;

        public const uint UserManagementDataType_Encoding_DefaultBinary = 24292;

        public const uint PriorityMappingEntryType_Encoding_DefaultBinary = 25239;

        public const uint LldpManagementAddressTxPortType_Encoding_DefaultBinary = 19079;

        public const uint LldpManagementAddressType_Encoding_DefaultBinary = 19080;

        public const uint LldpTlvType_Encoding_DefaultBinary = 19081;

        public const uint ReferenceDescriptionDataType_Encoding_DefaultBinary = 32661;

        public const uint ReferenceListEntryDataType_Encoding_DefaultBinary = 32662;

        public const uint LogRecord_Encoding_DefaultBinary = 19379;

        public const uint LogRecordsDataType_Encoding_DefaultBinary = 19753;

        public const uint SpanContextDataType_Encoding_DefaultBinary = 19754;

        public const uint TraceContextDataType_Encoding_DefaultBinary = 19755;

        public const uint NameValuePair_Encoding_DefaultBinary = 19756;

        public const uint RolePermissionType_Encoding_DefaultBinary = 128;

        public const uint DataTypeDefinition_Encoding_DefaultBinary = 121;

        public const uint StructureField_Encoding_DefaultBinary = 14844;

        public const uint StructureDefinition_Encoding_DefaultBinary = 122;

        public const uint EnumDefinition_Encoding_DefaultBinary = 123;

        public const uint Node_Encoding_DefaultBinary = 260;

        public const uint InstanceNode_Encoding_DefaultBinary = 11889;

        public const uint TypeNode_Encoding_DefaultBinary = 11890;

        public const uint ObjectNode_Encoding_DefaultBinary = 263;

        public const uint ObjectTypeNode_Encoding_DefaultBinary = 266;

        public const uint VariableNode_Encoding_DefaultBinary = 269;

        public const uint VariableTypeNode_Encoding_DefaultBinary = 272;

        public const uint ReferenceTypeNode_Encoding_DefaultBinary = 275;

        public const uint MethodNode_Encoding_DefaultBinary = 278;

        public const uint ViewNode_Encoding_DefaultBinary = 281;

        public const uint DataTypeNode_Encoding_DefaultBinary = 284;

        public const uint ReferenceNode_Encoding_DefaultBinary = 287;

        public const uint Argument_Encoding_DefaultBinary = 298;

        public const uint EnumValueType_Encoding_DefaultBinary = 8251;

        public const uint EnumField_Encoding_DefaultBinary = 14845;

        public const uint OptionSet_Encoding_DefaultBinary = 12765;

        public const uint TimeZoneDataType_Encoding_DefaultBinary = 8917;

        public const uint ApplicationDescription_Encoding_DefaultBinary = 310;

        public const uint RequestHeader_Encoding_DefaultBinary = 391;

        public const uint ResponseHeader_Encoding_DefaultBinary = 394;

        public const uint ServiceFault_Encoding_DefaultBinary = 397;

        public const uint SessionlessInvokeRequestType_Encoding_DefaultBinary = 15903;

        public const uint SessionlessInvokeResponseType_Encoding_DefaultBinary = 21001;

        public const uint FindServersRequest_Encoding_DefaultBinary = 422;

        public const uint FindServersResponse_Encoding_DefaultBinary = 425;

        public const uint ServerOnNetwork_Encoding_DefaultBinary = 12207;

        public const uint FindServersOnNetworkRequest_Encoding_DefaultBinary = 12208;

        public const uint FindServersOnNetworkResponse_Encoding_DefaultBinary = 12209;

        public const uint UserTokenPolicy_Encoding_DefaultBinary = 306;

        public const uint EndpointDescription_Encoding_DefaultBinary = 314;

        public const uint GetEndpointsRequest_Encoding_DefaultBinary = 428;

        public const uint GetEndpointsResponse_Encoding_DefaultBinary = 431;

        public const uint RegisteredServer_Encoding_DefaultBinary = 434;

        public const uint RegisterServerRequest_Encoding_DefaultBinary = 437;

        public const uint RegisterServerResponse_Encoding_DefaultBinary = 440;

        public const uint DiscoveryConfiguration_Encoding_DefaultBinary = 12900;

        public const uint MdnsDiscoveryConfiguration_Encoding_DefaultBinary = 12901;

        public const uint RegisterServer2Request_Encoding_DefaultBinary = 12211;

        public const uint RegisterServer2Response_Encoding_DefaultBinary = 12212;

        public const uint ChannelSecurityToken_Encoding_DefaultBinary = 443;

        public const uint OpenSecureChannelRequest_Encoding_DefaultBinary = 446;

        public const uint OpenSecureChannelResponse_Encoding_DefaultBinary = 449;

        public const uint CloseSecureChannelRequest_Encoding_DefaultBinary = 452;

        public const uint CloseSecureChannelResponse_Encoding_DefaultBinary = 455;

        public const uint SignedSoftwareCertificate_Encoding_DefaultBinary = 346;

        public const uint SignatureData_Encoding_DefaultBinary = 458;

        public const uint CreateSessionRequest_Encoding_DefaultBinary = 461;

        public const uint CreateSessionResponse_Encoding_DefaultBinary = 464;

        public const uint UserIdentityToken_Encoding_DefaultBinary = 318;

        public const uint AnonymousIdentityToken_Encoding_DefaultBinary = 321;

        public const uint UserNameIdentityToken_Encoding_DefaultBinary = 324;

        public const uint X509IdentityToken_Encoding_DefaultBinary = 327;

        public const uint IssuedIdentityToken_Encoding_DefaultBinary = 940;

        public const uint ActivateSessionRequest_Encoding_DefaultBinary = 467;

        public const uint ActivateSessionResponse_Encoding_DefaultBinary = 470;

        public const uint CloseSessionRequest_Encoding_DefaultBinary = 473;

        public const uint CloseSessionResponse_Encoding_DefaultBinary = 476;

        public const uint CancelRequest_Encoding_DefaultBinary = 479;

        public const uint CancelResponse_Encoding_DefaultBinary = 482;

        public const uint NodeAttributes_Encoding_DefaultBinary = 351;

        public const uint ObjectAttributes_Encoding_DefaultBinary = 354;

        public const uint VariableAttributes_Encoding_DefaultBinary = 357;

        public const uint MethodAttributes_Encoding_DefaultBinary = 360;

        public const uint ObjectTypeAttributes_Encoding_DefaultBinary = 363;

        public const uint VariableTypeAttributes_Encoding_DefaultBinary = 366;

        public const uint ReferenceTypeAttributes_Encoding_DefaultBinary = 369;

        public const uint DataTypeAttributes_Encoding_DefaultBinary = 372;

        public const uint ViewAttributes_Encoding_DefaultBinary = 375;

        public const uint GenericAttributeValue_Encoding_DefaultBinary = 17610;

        public const uint GenericAttributes_Encoding_DefaultBinary = 17611;

        public const uint AddNodesItem_Encoding_DefaultBinary = 378;

        public const uint AddNodesResult_Encoding_DefaultBinary = 485;

        public const uint AddNodesRequest_Encoding_DefaultBinary = 488;

        public const uint AddNodesResponse_Encoding_DefaultBinary = 491;

        public const uint AddReferencesItem_Encoding_DefaultBinary = 381;

        public const uint AddReferencesRequest_Encoding_DefaultBinary = 494;

        public const uint AddReferencesResponse_Encoding_DefaultBinary = 497;

        public const uint DeleteNodesItem_Encoding_DefaultBinary = 384;

        public const uint DeleteNodesRequest_Encoding_DefaultBinary = 500;

        public const uint DeleteNodesResponse_Encoding_DefaultBinary = 503;

        public const uint DeleteReferencesItem_Encoding_DefaultBinary = 387;

        public const uint DeleteReferencesRequest_Encoding_DefaultBinary = 506;

        public const uint DeleteReferencesResponse_Encoding_DefaultBinary = 509;

        public const uint ViewDescription_Encoding_DefaultBinary = 513;

        public const uint BrowseDescription_Encoding_DefaultBinary = 516;

        public const uint ReferenceDescription_Encoding_DefaultBinary = 520;

        public const uint BrowseResult_Encoding_DefaultBinary = 524;

        public const uint BrowseRequest_Encoding_DefaultBinary = 527;

        public const uint BrowseResponse_Encoding_DefaultBinary = 530;

        public const uint BrowseNextRequest_Encoding_DefaultBinary = 533;

        public const uint BrowseNextResponse_Encoding_DefaultBinary = 536;

        public const uint RelativePathElement_Encoding_DefaultBinary = 539;

        public const uint RelativePath_Encoding_DefaultBinary = 542;

        public const uint BrowsePath_Encoding_DefaultBinary = 545;

        public const uint BrowsePathTarget_Encoding_DefaultBinary = 548;

        public const uint BrowsePathResult_Encoding_DefaultBinary = 551;

        public const uint TranslateBrowsePathsToNodeIdsRequest_Encoding_DefaultBinary = 554;

        public const uint TranslateBrowsePathsToNodeIdsResponse_Encoding_DefaultBinary = 557;

        public const uint RegisterNodesRequest_Encoding_DefaultBinary = 560;

        public const uint RegisterNodesResponse_Encoding_DefaultBinary = 563;

        public const uint UnregisterNodesRequest_Encoding_DefaultBinary = 566;

        public const uint UnregisterNodesResponse_Encoding_DefaultBinary = 569;

        public const uint EndpointConfiguration_Encoding_DefaultBinary = 333;

        public const uint QueryDataDescription_Encoding_DefaultBinary = 572;

        public const uint NodeTypeDescription_Encoding_DefaultBinary = 575;

        public const uint QueryDataSet_Encoding_DefaultBinary = 579;

        public const uint NodeReference_Encoding_DefaultBinary = 582;

        public const uint ContentFilterElement_Encoding_DefaultBinary = 585;

        public const uint ContentFilter_Encoding_DefaultBinary = 588;

        public const uint FilterOperand_Encoding_DefaultBinary = 591;

        public const uint ElementOperand_Encoding_DefaultBinary = 594;

        public const uint LiteralOperand_Encoding_DefaultBinary = 597;

        public const uint AttributeOperand_Encoding_DefaultBinary = 600;

        public const uint SimpleAttributeOperand_Encoding_DefaultBinary = 603;

        public const uint ContentFilterElementResult_Encoding_DefaultBinary = 606;

        public const uint ContentFilterResult_Encoding_DefaultBinary = 609;

        public const uint ParsingResult_Encoding_DefaultBinary = 612;

        public const uint QueryFirstRequest_Encoding_DefaultBinary = 615;

        public const uint QueryFirstResponse_Encoding_DefaultBinary = 618;

        public const uint QueryNextRequest_Encoding_DefaultBinary = 621;

        public const uint QueryNextResponse_Encoding_DefaultBinary = 624;

        public const uint ReadValueId_Encoding_DefaultBinary = 628;

        public const uint ReadRequest_Encoding_DefaultBinary = 631;

        public const uint ReadResponse_Encoding_DefaultBinary = 634;

        public const uint HistoryReadValueId_Encoding_DefaultBinary = 637;

        public const uint HistoryReadResult_Encoding_DefaultBinary = 640;

        public const uint HistoryReadDetails_Encoding_DefaultBinary = 643;

        public const uint ReadEventDetails_Encoding_DefaultBinary = 646;

        public const uint ReadEventDetails2_Encoding_DefaultBinary = 32800;

        public const uint SortRuleElement_Encoding_DefaultBinary = 18650;

        public const uint ReadEventDetailsSorted_Encoding_DefaultBinary = 18651;

        public const uint ReadRawModifiedDetails_Encoding_DefaultBinary = 649;

        public const uint ReadProcessedDetails_Encoding_DefaultBinary = 652;

        public const uint ReadAtTimeDetails_Encoding_DefaultBinary = 655;

        public const uint ReadAnnotationDataDetails_Encoding_DefaultBinary = 23500;

        public const uint HistoryData_Encoding_DefaultBinary = 658;

        public const uint ModificationInfo_Encoding_DefaultBinary = 11226;

        public const uint HistoryModifiedData_Encoding_DefaultBinary = 11227;

        public const uint HistoryEvent_Encoding_DefaultBinary = 661;

        public const uint HistoryModifiedEvent_Encoding_DefaultBinary = 32825;

        public const uint HistoryReadRequest_Encoding_DefaultBinary = 664;

        public const uint HistoryReadResponse_Encoding_DefaultBinary = 667;

        public const uint WriteValue_Encoding_DefaultBinary = 670;

        public const uint WriteRequest_Encoding_DefaultBinary = 673;

        public const uint WriteResponse_Encoding_DefaultBinary = 676;

        public const uint HistoryUpdateDetails_Encoding_DefaultBinary = 679;

        public const uint UpdateDataDetails_Encoding_DefaultBinary = 682;

        public const uint UpdateStructureDataDetails_Encoding_DefaultBinary = 11300;

        public const uint UpdateEventDetails_Encoding_DefaultBinary = 685;

        public const uint DeleteRawModifiedDetails_Encoding_DefaultBinary = 688;

        public const uint DeleteAtTimeDetails_Encoding_DefaultBinary = 691;

        public const uint DeleteEventDetails_Encoding_DefaultBinary = 694;

        public const uint HistoryUpdateResult_Encoding_DefaultBinary = 697;

        public const uint HistoryUpdateRequest_Encoding_DefaultBinary = 700;

        public const uint HistoryUpdateResponse_Encoding_DefaultBinary = 703;

        public const uint CallMethodRequest_Encoding_DefaultBinary = 706;

        public const uint CallMethodResult_Encoding_DefaultBinary = 709;

        public const uint CallRequest_Encoding_DefaultBinary = 712;

        public const uint CallResponse_Encoding_DefaultBinary = 715;

        public const uint MonitoringFilter_Encoding_DefaultBinary = 721;

        public const uint DataChangeFilter_Encoding_DefaultBinary = 724;

        public const uint EventFilter_Encoding_DefaultBinary = 727;

        public const uint AggregateConfiguration_Encoding_DefaultBinary = 950;

        public const uint AggregateFilter_Encoding_DefaultBinary = 730;

        public const uint MonitoringFilterResult_Encoding_DefaultBinary = 733;

        public const uint EventFilterResult_Encoding_DefaultBinary = 736;

        public const uint AggregateFilterResult_Encoding_DefaultBinary = 739;

        public const uint MonitoringParameters_Encoding_DefaultBinary = 742;

        public const uint MonitoredItemCreateRequest_Encoding_DefaultBinary = 745;

        public const uint MonitoredItemCreateResult_Encoding_DefaultBinary = 748;

        public const uint CreateMonitoredItemsRequest_Encoding_DefaultBinary = 751;

        public const uint CreateMonitoredItemsResponse_Encoding_DefaultBinary = 754;

        public const uint MonitoredItemModifyRequest_Encoding_DefaultBinary = 757;

        public const uint MonitoredItemModifyResult_Encoding_DefaultBinary = 760;

        public const uint ModifyMonitoredItemsRequest_Encoding_DefaultBinary = 763;

        public const uint ModifyMonitoredItemsResponse_Encoding_DefaultBinary = 766;

        public const uint SetMonitoringModeRequest_Encoding_DefaultBinary = 769;

        public const uint SetMonitoringModeResponse_Encoding_DefaultBinary = 772;

        public const uint SetTriggeringRequest_Encoding_DefaultBinary = 775;

        public const uint SetTriggeringResponse_Encoding_DefaultBinary = 778;

        public const uint DeleteMonitoredItemsRequest_Encoding_DefaultBinary = 781;

        public const uint DeleteMonitoredItemsResponse_Encoding_DefaultBinary = 784;

        public const uint CreateSubscriptionRequest_Encoding_DefaultBinary = 787;

        public const uint CreateSubscriptionResponse_Encoding_DefaultBinary = 790;

        public const uint ModifySubscriptionRequest_Encoding_DefaultBinary = 793;

        public const uint ModifySubscriptionResponse_Encoding_DefaultBinary = 796;

        public const uint SetPublishingModeRequest_Encoding_DefaultBinary = 799;

        public const uint SetPublishingModeResponse_Encoding_DefaultBinary = 802;

        public const uint NotificationMessage_Encoding_DefaultBinary = 805;

        public const uint NotificationData_Encoding_DefaultBinary = 947;

        public const uint DataChangeNotification_Encoding_DefaultBinary = 811;

        public const uint MonitoredItemNotification_Encoding_DefaultBinary = 808;

        public const uint EventNotificationList_Encoding_DefaultBinary = 916;

        public const uint EventFieldList_Encoding_DefaultBinary = 919;

        public const uint HistoryEventFieldList_Encoding_DefaultBinary = 922;

        public const uint StatusChangeNotification_Encoding_DefaultBinary = 820;

        public const uint SubscriptionAcknowledgement_Encoding_DefaultBinary = 823;

        public const uint PublishRequest_Encoding_DefaultBinary = 826;

        public const uint PublishResponse_Encoding_DefaultBinary = 829;

        public const uint RepublishRequest_Encoding_DefaultBinary = 832;

        public const uint RepublishResponse_Encoding_DefaultBinary = 835;

        public const uint TransferResult_Encoding_DefaultBinary = 838;

        public const uint TransferSubscriptionsRequest_Encoding_DefaultBinary = 841;

        public const uint TransferSubscriptionsResponse_Encoding_DefaultBinary = 844;

        public const uint DeleteSubscriptionsRequest_Encoding_DefaultBinary = 847;

        public const uint DeleteSubscriptionsResponse_Encoding_DefaultBinary = 850;

        public const uint BuildInfo_Encoding_DefaultBinary = 340;

        public const uint RedundantServerDataType_Encoding_DefaultBinary = 855;

        public const uint EndpointUrlListDataType_Encoding_DefaultBinary = 11957;

        public const uint NetworkGroupDataType_Encoding_DefaultBinary = 11958;

        public const uint SamplingIntervalDiagnosticsDataType_Encoding_DefaultBinary = 858;

        public const uint ServerDiagnosticsSummaryDataType_Encoding_DefaultBinary = 861;

        public const uint ServerStatusDataType_Encoding_DefaultBinary = 864;

        public const uint SessionDiagnosticsDataType_Encoding_DefaultBinary = 867;

        public const uint SessionSecurityDiagnosticsDataType_Encoding_DefaultBinary = 870;

        public const uint ServiceCounterDataType_Encoding_DefaultBinary = 873;

        public const uint StatusResult_Encoding_DefaultBinary = 301;

        public const uint SubscriptionDiagnosticsDataType_Encoding_DefaultBinary = 876;

        public const uint ModelChangeStructureDataType_Encoding_DefaultBinary = 879;

        public const uint SemanticChangeStructureDataType_Encoding_DefaultBinary = 899;

        public const uint Range_Encoding_DefaultBinary = 886;

        public const uint EUInformation_Encoding_DefaultBinary = 889;

        public const uint ComplexNumberType_Encoding_DefaultBinary = 12181;

        public const uint DoubleComplexNumberType_Encoding_DefaultBinary = 12182;

        public const uint AxisInformation_Encoding_DefaultBinary = 12089;

        public const uint XVType_Encoding_DefaultBinary = 12090;

        public const uint ProgramDiagnosticDataType_Encoding_DefaultBinary = 896;

        public const uint ProgramDiagnostic2DataType_Encoding_DefaultBinary = 24034;

        public const uint Annotation_Encoding_DefaultBinary = 893;

        public const uint Union_Encoding_DefaultXml = 12758;

        public const uint KeyValuePair_Encoding_DefaultXml = 14802;

        public const uint AdditionalParametersType_Encoding_DefaultXml = 17541;

        public const uint EphemeralKeyType_Encoding_DefaultXml = 17553;

        public const uint EndpointType_Encoding_DefaultXml = 15949;

        public const uint BitFieldDefinition_Encoding_DefaultXml = 32426;

        public const uint RationalNumber_Encoding_DefaultXml = 18851;

        public const uint Vector_Encoding_DefaultXml = 18852;

        public const uint ThreeDVector_Encoding_DefaultXml = 18853;

        public const uint CartesianCoordinates_Encoding_DefaultXml = 18854;

        public const uint ThreeDCartesianCoordinates_Encoding_DefaultXml = 18855;

        public const uint Orientation_Encoding_DefaultXml = 18856;

        public const uint ThreeDOrientation_Encoding_DefaultXml = 18857;

        public const uint Frame_Encoding_DefaultXml = 18858;

        public const uint ThreeDFrame_Encoding_DefaultXml = 18859;

        public const uint IdentityMappingRuleType_Encoding_DefaultXml = 15728;

        public const uint CurrencyUnitType_Encoding_DefaultXml = 23520;

        public const uint AnnotationDataType_Encoding_DefaultXml = 32572;

        public const uint LinearConversionDataType_Encoding_DefaultXml = 32573;

        public const uint QuantityDimension_Encoding_DefaultXml = 32574;

        public const uint TrustListDataType_Encoding_DefaultXml = 12676;

        public const uint BaseConfigurationDataType_Encoding_DefaultXml = 16587;

        public const uint BaseConfigurationRecordDataType_Encoding_DefaultXml = 16588;

        public const uint CertificateGroupDataType_Encoding_DefaultXml = 16589;

        public const uint ConfigurationUpdateTargetType_Encoding_DefaultXml = 16590;

        public const uint TransactionErrorType_Encoding_DefaultXml = 32386;

        public const uint ApplicationConfigurationDataType_Encoding_DefaultXml = 23762;

        public const uint ApplicationIdentityDataType_Encoding_DefaultXml = 16592;

        public const uint EndpointDataType_Encoding_DefaultXml = 16593;

        public const uint ServerEndpointDataType_Encoding_DefaultXml = 16594;

        public const uint SecuritySettingsDataType_Encoding_DefaultXml = 16595;

        public const uint UserTokenSettingsDataType_Encoding_DefaultXml = 16596;

        public const uint ServiceCertificateDataType_Encoding_DefaultXml = 23735;

        public const uint AuthorizationServiceConfigurationDataType_Encoding_DefaultXml = 23763;

        public const uint DecimalDataType_Encoding_DefaultXml = 17862;

        public const uint DataTypeSchemaHeader_Encoding_DefaultXml = 15950;

        public const uint DataTypeDescription_Encoding_DefaultXml = 14796;

        public const uint StructureDescription_Encoding_DefaultXml = 15589;

        public const uint EnumDescription_Encoding_DefaultXml = 15590;

        public const uint SimpleTypeDescription_Encoding_DefaultXml = 15529;

        public const uint UABinaryFileDataType_Encoding_DefaultXml = 15531;

        public const uint PortableQualifiedName_Encoding_DefaultXml = 24120;

        public const uint PortableNodeId_Encoding_DefaultXml = 24121;

        public const uint UnsignedRationalNumber_Encoding_DefaultXml = 24122;

        public const uint DataSetMetaDataType_Encoding_DefaultXml = 14794;

        public const uint FieldMetaData_Encoding_DefaultXml = 14795;

        public const uint ConfigurationVersionDataType_Encoding_DefaultXml = 14803;

        public const uint PublishedDataSetDataType_Encoding_DefaultXml = 15951;

        public const uint PublishedDataSetSourceDataType_Encoding_DefaultXml = 15952;

        public const uint PublishedVariableDataType_Encoding_DefaultXml = 14319;

        public const uint PublishedDataItemsDataType_Encoding_DefaultXml = 15953;

        public const uint PublishedEventsDataType_Encoding_DefaultXml = 15954;

        public const uint PublishedDataSetCustomSourceDataType_Encoding_DefaultXml = 25545;

        public const uint ActionTargetDataType_Encoding_DefaultXml = 18610;

        public const uint PublishedActionDataType_Encoding_DefaultXml = 18611;

        public const uint ActionMethodDataType_Encoding_DefaultXml = 18612;

        public const uint PublishedActionMethodDataType_Encoding_DefaultXml = 18937;

        public const uint DataSetWriterDataType_Encoding_DefaultXml = 15955;

        public const uint DataSetWriterTransportDataType_Encoding_DefaultXml = 15956;

        public const uint DataSetWriterMessageDataType_Encoding_DefaultXml = 15987;

        public const uint PubSubGroupDataType_Encoding_DefaultXml = 15988;

        public const uint WriterGroupDataType_Encoding_DefaultXml = 21174;

        public const uint WriterGroupTransportDataType_Encoding_DefaultXml = 15990;

        public const uint WriterGroupMessageDataType_Encoding_DefaultXml = 15991;

        public const uint PubSubConnectionDataType_Encoding_DefaultXml = 15992;

        public const uint ConnectionTransportDataType_Encoding_DefaultXml = 15993;

        public const uint NetworkAddressDataType_Encoding_DefaultXml = 21175;

        public const uint NetworkAddressUrlDataType_Encoding_DefaultXml = 21176;

        public const uint ReaderGroupDataType_Encoding_DefaultXml = 21177;

        public const uint ReaderGroupTransportDataType_Encoding_DefaultXml = 15995;

        public const uint ReaderGroupMessageDataType_Encoding_DefaultXml = 15996;

        public const uint DataSetReaderDataType_Encoding_DefaultXml = 16007;

        public const uint DataSetReaderTransportDataType_Encoding_DefaultXml = 16008;

        public const uint DataSetReaderMessageDataType_Encoding_DefaultXml = 16009;

        public const uint SubscribedDataSetDataType_Encoding_DefaultXml = 16010;

        public const uint TargetVariablesDataType_Encoding_DefaultXml = 16011;

        public const uint FieldTargetDataType_Encoding_DefaultXml = 14804;

        public const uint SubscribedDataSetMirrorDataType_Encoding_DefaultXml = 16012;

        public const uint PubSubConfigurationDataType_Encoding_DefaultXml = 21178;

        public const uint StandaloneSubscribedDataSetRefDataType_Encoding_DefaultXml = 23919;

        public const uint StandaloneSubscribedDataSetDataType_Encoding_DefaultXml = 23920;

        public const uint SecurityGroupDataType_Encoding_DefaultXml = 23921;

        public const uint PubSubKeyPushTargetDataType_Encoding_DefaultXml = 25546;

        public const uint PubSubConfiguration2DataType_Encoding_DefaultXml = 23922;

        public const uint UadpWriterGroupMessageDataType_Encoding_DefaultXml = 16014;

        public const uint UadpDataSetWriterMessageDataType_Encoding_DefaultXml = 16015;

        public const uint UadpDataSetReaderMessageDataType_Encoding_DefaultXml = 16016;

        public const uint JsonWriterGroupMessageDataType_Encoding_DefaultXml = 16017;

        public const uint JsonDataSetWriterMessageDataType_Encoding_DefaultXml = 16018;

        public const uint JsonDataSetReaderMessageDataType_Encoding_DefaultXml = 16019;

        public const uint QosDataType_Encoding_DefaultXml = 23923;

        public const uint TransmitQosDataType_Encoding_DefaultXml = 23924;

        public const uint TransmitQosPriorityDataType_Encoding_DefaultXml = 23925;

        public const uint ReceiveQosDataType_Encoding_DefaultXml = 23928;

        public const uint ReceiveQosPriorityDataType_Encoding_DefaultXml = 23929;

        public const uint DatagramConnectionTransportDataType_Encoding_DefaultXml = 17472;

        public const uint DatagramConnectionTransport2DataType_Encoding_DefaultXml = 23932;

        public const uint DatagramWriterGroupTransportDataType_Encoding_DefaultXml = 21179;

        public const uint DatagramWriterGroupTransport2DataType_Encoding_DefaultXml = 23933;

        public const uint DatagramDataSetReaderTransportDataType_Encoding_DefaultXml = 23934;

        public const uint DtlsPubSubConnectionDataType_Encoding_DefaultXml = 18938;

        public const uint BrokerConnectionTransportDataType_Encoding_DefaultXml = 15579;

        public const uint BrokerWriterGroupTransportDataType_Encoding_DefaultXml = 16021;

        public const uint BrokerDataSetWriterTransportDataType_Encoding_DefaultXml = 16022;

        public const uint BrokerDataSetReaderTransportDataType_Encoding_DefaultXml = 16023;

        public const uint PubSubConfigurationRefDataType_Encoding_DefaultXml = 25547;

        public const uint PubSubConfigurationValueDataType_Encoding_DefaultXml = 25548;

        public const uint AliasNameDataType_Encoding_DefaultXml = 23505;

        public const uint UserManagementDataType_Encoding_DefaultXml = 24296;

        public const uint PriorityMappingEntryType_Encoding_DefaultXml = 25243;

        public const uint LldpManagementAddressTxPortType_Encoding_DefaultXml = 19100;

        public const uint LldpManagementAddressType_Encoding_DefaultXml = 19101;

        public const uint LldpTlvType_Encoding_DefaultXml = 19102;

        public const uint ReferenceDescriptionDataType_Encoding_DefaultXml = 32669;

        public const uint ReferenceListEntryDataType_Encoding_DefaultXml = 32670;

        public const uint LogRecord_Encoding_DefaultXml = 19383;

        public const uint LogRecordsDataType_Encoding_DefaultXml = 19773;

        public const uint SpanContextDataType_Encoding_DefaultXml = 19774;

        public const uint TraceContextDataType_Encoding_DefaultXml = 19775;

        public const uint NameValuePair_Encoding_DefaultXml = 19776;

        public const uint RolePermissionType_Encoding_DefaultXml = 16126;

        public const uint DataTypeDefinition_Encoding_DefaultXml = 14797;

        public const uint StructureField_Encoding_DefaultXml = 14800;

        public const uint StructureDefinition_Encoding_DefaultXml = 14798;

        public const uint EnumDefinition_Encoding_DefaultXml = 14799;

        public const uint Node_Encoding_DefaultXml = 259;

        public const uint InstanceNode_Encoding_DefaultXml = 11887;

        public const uint TypeNode_Encoding_DefaultXml = 11888;

        public const uint ObjectNode_Encoding_DefaultXml = 262;

        public const uint ObjectTypeNode_Encoding_DefaultXml = 265;

        public const uint VariableNode_Encoding_DefaultXml = 268;

        public const uint VariableTypeNode_Encoding_DefaultXml = 271;

        public const uint ReferenceTypeNode_Encoding_DefaultXml = 274;

        public const uint MethodNode_Encoding_DefaultXml = 277;

        public const uint ViewNode_Encoding_DefaultXml = 280;

        public const uint DataTypeNode_Encoding_DefaultXml = 283;

        public const uint ReferenceNode_Encoding_DefaultXml = 286;

        public const uint Argument_Encoding_DefaultXml = 297;

        public const uint EnumValueType_Encoding_DefaultXml = 7616;

        public const uint EnumField_Encoding_DefaultXml = 14801;

        public const uint OptionSet_Encoding_DefaultXml = 12757;

        public const uint TimeZoneDataType_Encoding_DefaultXml = 8913;

        public const uint ApplicationDescription_Encoding_DefaultXml = 309;

        public const uint RequestHeader_Encoding_DefaultXml = 390;

        public const uint ResponseHeader_Encoding_DefaultXml = 393;

        public const uint ServiceFault_Encoding_DefaultXml = 396;

        public const uint SessionlessInvokeRequestType_Encoding_DefaultXml = 15902;

        public const uint SessionlessInvokeResponseType_Encoding_DefaultXml = 21000;

        public const uint FindServersRequest_Encoding_DefaultXml = 421;

        public const uint FindServersResponse_Encoding_DefaultXml = 424;

        public const uint ServerOnNetwork_Encoding_DefaultXml = 12195;

        public const uint FindServersOnNetworkRequest_Encoding_DefaultXml = 12196;

        public const uint FindServersOnNetworkResponse_Encoding_DefaultXml = 12197;

        public const uint UserTokenPolicy_Encoding_DefaultXml = 305;

        public const uint EndpointDescription_Encoding_DefaultXml = 313;

        public const uint GetEndpointsRequest_Encoding_DefaultXml = 427;

        public const uint GetEndpointsResponse_Encoding_DefaultXml = 430;

        public const uint RegisteredServer_Encoding_DefaultXml = 433;

        public const uint RegisterServerRequest_Encoding_DefaultXml = 436;

        public const uint RegisterServerResponse_Encoding_DefaultXml = 439;

        public const uint DiscoveryConfiguration_Encoding_DefaultXml = 12892;

        public const uint MdnsDiscoveryConfiguration_Encoding_DefaultXml = 12893;

        public const uint RegisterServer2Request_Encoding_DefaultXml = 12199;

        public const uint RegisterServer2Response_Encoding_DefaultXml = 12200;

        public const uint ChannelSecurityToken_Encoding_DefaultXml = 442;

        public const uint OpenSecureChannelRequest_Encoding_DefaultXml = 445;

        public const uint OpenSecureChannelResponse_Encoding_DefaultXml = 448;

        public const uint CloseSecureChannelRequest_Encoding_DefaultXml = 451;

        public const uint CloseSecureChannelResponse_Encoding_DefaultXml = 454;

        public const uint SignedSoftwareCertificate_Encoding_DefaultXml = 345;

        public const uint SignatureData_Encoding_DefaultXml = 457;

        public const uint CreateSessionRequest_Encoding_DefaultXml = 460;

        public const uint CreateSessionResponse_Encoding_DefaultXml = 463;

        public const uint UserIdentityToken_Encoding_DefaultXml = 317;

        public const uint AnonymousIdentityToken_Encoding_DefaultXml = 320;

        public const uint UserNameIdentityToken_Encoding_DefaultXml = 323;

        public const uint X509IdentityToken_Encoding_DefaultXml = 326;

        public const uint IssuedIdentityToken_Encoding_DefaultXml = 939;

        public const uint ActivateSessionRequest_Encoding_DefaultXml = 466;

        public const uint ActivateSessionResponse_Encoding_DefaultXml = 469;

        public const uint CloseSessionRequest_Encoding_DefaultXml = 472;

        public const uint CloseSessionResponse_Encoding_DefaultXml = 475;

        public const uint CancelRequest_Encoding_DefaultXml = 478;

        public const uint CancelResponse_Encoding_DefaultXml = 481;

        public const uint NodeAttributes_Encoding_DefaultXml = 350;

        public const uint ObjectAttributes_Encoding_DefaultXml = 353;

        public const uint VariableAttributes_Encoding_DefaultXml = 356;

        public const uint MethodAttributes_Encoding_DefaultXml = 359;

        public const uint ObjectTypeAttributes_Encoding_DefaultXml = 362;

        public const uint VariableTypeAttributes_Encoding_DefaultXml = 365;

        public const uint ReferenceTypeAttributes_Encoding_DefaultXml = 368;

        public const uint DataTypeAttributes_Encoding_DefaultXml = 371;

        public const uint ViewAttributes_Encoding_DefaultXml = 374;

        public const uint GenericAttributeValue_Encoding_DefaultXml = 17608;

        public const uint GenericAttributes_Encoding_DefaultXml = 17609;

        public const uint AddNodesItem_Encoding_DefaultXml = 377;

        public const uint AddNodesResult_Encoding_DefaultXml = 484;

        public const uint AddNodesRequest_Encoding_DefaultXml = 487;

        public const uint AddNodesResponse_Encoding_DefaultXml = 490;

        public const uint AddReferencesItem_Encoding_DefaultXml = 380;

        public const uint AddReferencesRequest_Encoding_DefaultXml = 493;

        public const uint AddReferencesResponse_Encoding_DefaultXml = 496;

        public const uint DeleteNodesItem_Encoding_DefaultXml = 383;

        public const uint DeleteNodesRequest_Encoding_DefaultXml = 499;

        public const uint DeleteNodesResponse_Encoding_DefaultXml = 502;

        public const uint DeleteReferencesItem_Encoding_DefaultXml = 386;

        public const uint DeleteReferencesRequest_Encoding_DefaultXml = 505;

        public const uint DeleteReferencesResponse_Encoding_DefaultXml = 508;

        public const uint ViewDescription_Encoding_DefaultXml = 512;

        public const uint BrowseDescription_Encoding_DefaultXml = 515;

        public const uint ReferenceDescription_Encoding_DefaultXml = 519;

        public const uint BrowseResult_Encoding_DefaultXml = 523;

        public const uint BrowseRequest_Encoding_DefaultXml = 526;

        public const uint BrowseResponse_Encoding_DefaultXml = 529;

        public const uint BrowseNextRequest_Encoding_DefaultXml = 532;

        public const uint BrowseNextResponse_Encoding_DefaultXml = 535;

        public const uint RelativePathElement_Encoding_DefaultXml = 538;

        public const uint RelativePath_Encoding_DefaultXml = 541;

        public const uint BrowsePath_Encoding_DefaultXml = 544;

        public const uint BrowsePathTarget_Encoding_DefaultXml = 547;

        public const uint BrowsePathResult_Encoding_DefaultXml = 550;

        public const uint TranslateBrowsePathsToNodeIdsRequest_Encoding_DefaultXml = 553;

        public const uint TranslateBrowsePathsToNodeIdsResponse_Encoding_DefaultXml = 556;

        public const uint RegisterNodesRequest_Encoding_DefaultXml = 559;

        public const uint RegisterNodesResponse_Encoding_DefaultXml = 562;

        public const uint UnregisterNodesRequest_Encoding_DefaultXml = 565;

        public const uint UnregisterNodesResponse_Encoding_DefaultXml = 568;

        public const uint EndpointConfiguration_Encoding_DefaultXml = 332;

        public const uint QueryDataDescription_Encoding_DefaultXml = 571;

        public const uint NodeTypeDescription_Encoding_DefaultXml = 574;

        public const uint QueryDataSet_Encoding_DefaultXml = 578;

        public const uint NodeReference_Encoding_DefaultXml = 581;

        public const uint ContentFilterElement_Encoding_DefaultXml = 584;

        public const uint ContentFilter_Encoding_DefaultXml = 587;

        public const uint FilterOperand_Encoding_DefaultXml = 590;

        public const uint ElementOperand_Encoding_DefaultXml = 593;

        public const uint LiteralOperand_Encoding_DefaultXml = 596;

        public const uint AttributeOperand_Encoding_DefaultXml = 599;

        public const uint SimpleAttributeOperand_Encoding_DefaultXml = 602;

        public const uint ContentFilterElementResult_Encoding_DefaultXml = 605;

        public const uint ContentFilterResult_Encoding_DefaultXml = 608;

        public const uint ParsingResult_Encoding_DefaultXml = 611;

        public const uint QueryFirstRequest_Encoding_DefaultXml = 614;

        public const uint QueryFirstResponse_Encoding_DefaultXml = 617;

        public const uint QueryNextRequest_Encoding_DefaultXml = 620;

        public const uint QueryNextResponse_Encoding_DefaultXml = 623;

        public const uint ReadValueId_Encoding_DefaultXml = 627;

        public const uint ReadRequest_Encoding_DefaultXml = 630;

        public const uint ReadResponse_Encoding_DefaultXml = 633;

        public const uint HistoryReadValueId_Encoding_DefaultXml = 636;

        public const uint HistoryReadResult_Encoding_DefaultXml = 639;

        public const uint HistoryReadDetails_Encoding_DefaultXml = 642;

        public const uint ReadEventDetails_Encoding_DefaultXml = 645;

        public const uint ReadEventDetails2_Encoding_DefaultXml = 32801;

        public const uint SortRuleElement_Encoding_DefaultXml = 18652;

        public const uint ReadEventDetailsSorted_Encoding_DefaultXml = 18653;

        public const uint ReadRawModifiedDetails_Encoding_DefaultXml = 648;

        public const uint ReadProcessedDetails_Encoding_DefaultXml = 651;

        public const uint ReadAtTimeDetails_Encoding_DefaultXml = 654;

        public const uint ReadAnnotationDataDetails_Encoding_DefaultXml = 23506;

        public const uint HistoryData_Encoding_DefaultXml = 657;

        public const uint ModificationInfo_Encoding_DefaultXml = 11218;

        public const uint HistoryModifiedData_Encoding_DefaultXml = 11219;

        public const uint HistoryEvent_Encoding_DefaultXml = 660;

        public const uint HistoryModifiedEvent_Encoding_DefaultXml = 32829;

        public const uint HistoryReadRequest_Encoding_DefaultXml = 663;

        public const uint HistoryReadResponse_Encoding_DefaultXml = 666;

        public const uint WriteValue_Encoding_DefaultXml = 669;

        public const uint WriteRequest_Encoding_DefaultXml = 672;

        public const uint WriteResponse_Encoding_DefaultXml = 675;

        public const uint HistoryUpdateDetails_Encoding_DefaultXml = 678;

        public const uint UpdateDataDetails_Encoding_DefaultXml = 681;

        public const uint UpdateStructureDataDetails_Encoding_DefaultXml = 11296;

        public const uint UpdateEventDetails_Encoding_DefaultXml = 684;

        public const uint DeleteRawModifiedDetails_Encoding_DefaultXml = 687;

        public const uint DeleteAtTimeDetails_Encoding_DefaultXml = 690;

        public const uint DeleteEventDetails_Encoding_DefaultXml = 693;

        public const uint HistoryUpdateResult_Encoding_DefaultXml = 696;

        public const uint HistoryUpdateRequest_Encoding_DefaultXml = 699;

        public const uint HistoryUpdateResponse_Encoding_DefaultXml = 702;

        public const uint CallMethodRequest_Encoding_DefaultXml = 705;

        public const uint CallMethodResult_Encoding_DefaultXml = 708;

        public const uint CallRequest_Encoding_DefaultXml = 711;

        public const uint CallResponse_Encoding_DefaultXml = 714;

        public const uint MonitoringFilter_Encoding_DefaultXml = 720;

        public const uint DataChangeFilter_Encoding_DefaultXml = 723;

        public const uint EventFilter_Encoding_DefaultXml = 726;

        public const uint AggregateConfiguration_Encoding_DefaultXml = 949;

        public const uint AggregateFilter_Encoding_DefaultXml = 729;

        public const uint MonitoringFilterResult_Encoding_DefaultXml = 732;

        public const uint EventFilterResult_Encoding_DefaultXml = 735;

        public const uint AggregateFilterResult_Encoding_DefaultXml = 738;

        public const uint MonitoringParameters_Encoding_DefaultXml = 741;

        public const uint MonitoredItemCreateRequest_Encoding_DefaultXml = 744;

        public const uint MonitoredItemCreateResult_Encoding_DefaultXml = 747;

        public const uint CreateMonitoredItemsRequest_Encoding_DefaultXml = 750;

        public const uint CreateMonitoredItemsResponse_Encoding_DefaultXml = 753;

        public const uint MonitoredItemModifyRequest_Encoding_DefaultXml = 756;

        public const uint MonitoredItemModifyResult_Encoding_DefaultXml = 759;

        public const uint ModifyMonitoredItemsRequest_Encoding_DefaultXml = 762;

        public const uint ModifyMonitoredItemsResponse_Encoding_DefaultXml = 765;

        public const uint SetMonitoringModeRequest_Encoding_DefaultXml = 768;

        public const uint SetMonitoringModeResponse_Encoding_DefaultXml = 771;

        public const uint SetTriggeringRequest_Encoding_DefaultXml = 774;

        public const uint SetTriggeringResponse_Encoding_DefaultXml = 777;

        public const uint DeleteMonitoredItemsRequest_Encoding_DefaultXml = 780;

        public const uint DeleteMonitoredItemsResponse_Encoding_DefaultXml = 783;

        public const uint CreateSubscriptionRequest_Encoding_DefaultXml = 786;

        public const uint CreateSubscriptionResponse_Encoding_DefaultXml = 789;

        public const uint ModifySubscriptionRequest_Encoding_DefaultXml = 792;

        public const uint ModifySubscriptionResponse_Encoding_DefaultXml = 795;

        public const uint SetPublishingModeRequest_Encoding_DefaultXml = 798;

        public const uint SetPublishingModeResponse_Encoding_DefaultXml = 801;

        public const uint NotificationMessage_Encoding_DefaultXml = 804;

        public const uint NotificationData_Encoding_DefaultXml = 946;

        public const uint DataChangeNotification_Encoding_DefaultXml = 810;

        public const uint MonitoredItemNotification_Encoding_DefaultXml = 807;

        public const uint EventNotificationList_Encoding_DefaultXml = 915;

        public const uint EventFieldList_Encoding_DefaultXml = 918;

        public const uint HistoryEventFieldList_Encoding_DefaultXml = 921;

        public const uint StatusChangeNotification_Encoding_DefaultXml = 819;

        public const uint SubscriptionAcknowledgement_Encoding_DefaultXml = 822;

        public const uint PublishRequest_Encoding_DefaultXml = 825;

        public const uint PublishResponse_Encoding_DefaultXml = 828;

        public const uint RepublishRequest_Encoding_DefaultXml = 831;

        public const uint RepublishResponse_Encoding_DefaultXml = 834;

        public const uint TransferResult_Encoding_DefaultXml = 837;

        public const uint TransferSubscriptionsRequest_Encoding_DefaultXml = 840;

        public const uint TransferSubscriptionsResponse_Encoding_DefaultXml = 843;

        public const uint DeleteSubscriptionsRequest_Encoding_DefaultXml = 846;

        public const uint DeleteSubscriptionsResponse_Encoding_DefaultXml = 849;

        public const uint BuildInfo_Encoding_DefaultXml = 339;

        public const uint RedundantServerDataType_Encoding_DefaultXml = 854;

        public const uint EndpointUrlListDataType_Encoding_DefaultXml = 11949;

        public const uint NetworkGroupDataType_Encoding_DefaultXml = 11950;

        public const uint SamplingIntervalDiagnosticsDataType_Encoding_DefaultXml = 857;

        public const uint ServerDiagnosticsSummaryDataType_Encoding_DefaultXml = 860;

        public const uint ServerStatusDataType_Encoding_DefaultXml = 863;

        public const uint SessionDiagnosticsDataType_Encoding_DefaultXml = 866;

        public const uint SessionSecurityDiagnosticsDataType_Encoding_DefaultXml = 869;

        public const uint ServiceCounterDataType_Encoding_DefaultXml = 872;

        public const uint StatusResult_Encoding_DefaultXml = 300;

        public const uint SubscriptionDiagnosticsDataType_Encoding_DefaultXml = 875;

        public const uint ModelChangeStructureDataType_Encoding_DefaultXml = 878;

        public const uint SemanticChangeStructureDataType_Encoding_DefaultXml = 898;

        public const uint Range_Encoding_DefaultXml = 885;

        public const uint EUInformation_Encoding_DefaultXml = 888;

        public const uint ComplexNumberType_Encoding_DefaultXml = 12173;

        public const uint DoubleComplexNumberType_Encoding_DefaultXml = 12174;

        public const uint AxisInformation_Encoding_DefaultXml = 12081;

        public const uint XVType_Encoding_DefaultXml = 12082;

        public const uint ProgramDiagnosticDataType_Encoding_DefaultXml = 895;

        public const uint ProgramDiagnostic2DataType_Encoding_DefaultXml = 24038;

        public const uint Annotation_Encoding_DefaultXml = 892;

        public const uint Union_Encoding_DefaultJson = 15085;

        public const uint KeyValuePair_Encoding_DefaultJson = 15041;

        public const uint AdditionalParametersType_Encoding_DefaultJson = 17547;

        public const uint EphemeralKeyType_Encoding_DefaultJson = 17557;

        public const uint EndpointType_Encoding_DefaultJson = 16150;

        public const uint BitFieldDefinition_Encoding_DefaultJson = 32430;

        public const uint RationalNumber_Encoding_DefaultJson = 19064;

        public const uint Vector_Encoding_DefaultJson = 19065;

        public const uint ThreeDVector_Encoding_DefaultJson = 19066;

        public const uint CartesianCoordinates_Encoding_DefaultJson = 19067;

        public const uint ThreeDCartesianCoordinates_Encoding_DefaultJson = 19068;

        public const uint Orientation_Encoding_DefaultJson = 19069;

        public const uint ThreeDOrientation_Encoding_DefaultJson = 19070;

        public const uint Frame_Encoding_DefaultJson = 19071;

        public const uint ThreeDFrame_Encoding_DefaultJson = 19072;

        public const uint IdentityMappingRuleType_Encoding_DefaultJson = 15042;

        public const uint CurrencyUnitType_Encoding_DefaultJson = 23528;

        public const uint AnnotationDataType_Encoding_DefaultJson = 32584;

        public const uint LinearConversionDataType_Encoding_DefaultJson = 32585;

        public const uint QuantityDimension_Encoding_DefaultJson = 32586;

        public const uint TrustListDataType_Encoding_DefaultJson = 15044;

        public const uint BaseConfigurationDataType_Encoding_DefaultJson = 16632;

        public const uint BaseConfigurationRecordDataType_Encoding_DefaultJson = 16633;

        public const uint CertificateGroupDataType_Encoding_DefaultJson = 16634;

        public const uint ConfigurationUpdateTargetType_Encoding_DefaultJson = 16635;

        public const uint TransactionErrorType_Encoding_DefaultJson = 32390;

        public const uint ApplicationConfigurationDataType_Encoding_DefaultJson = 23776;

        public const uint ApplicationIdentityDataType_Encoding_DefaultJson = 16637;

        public const uint EndpointDataType_Encoding_DefaultJson = 16642;

        public const uint ServerEndpointDataType_Encoding_DefaultJson = 16643;

        public const uint SecuritySettingsDataType_Encoding_DefaultJson = 16644;

        public const uint UserTokenSettingsDataType_Encoding_DefaultJson = 16645;

        public const uint ServiceCertificateDataType_Encoding_DefaultJson = 23739;

        public const uint AuthorizationServiceConfigurationDataType_Encoding_DefaultJson = 23777;

        public const uint DecimalDataType_Encoding_DefaultJson = 15045;

        public const uint DataTypeSchemaHeader_Encoding_DefaultJson = 16151;

        public const uint DataTypeDescription_Encoding_DefaultJson = 15057;

        public const uint StructureDescription_Encoding_DefaultJson = 15058;

        public const uint EnumDescription_Encoding_DefaultJson = 15059;

        public const uint SimpleTypeDescription_Encoding_DefaultJson = 15700;

        public const uint UABinaryFileDataType_Encoding_DefaultJson = 15714;

        public const uint PortableQualifiedName_Encoding_DefaultJson = 24132;

        public const uint PortableNodeId_Encoding_DefaultJson = 24133;

        public const uint UnsignedRationalNumber_Encoding_DefaultJson = 24134;

        public const uint DataSetMetaDataType_Encoding_DefaultJson = 15050;

        public const uint FieldMetaData_Encoding_DefaultJson = 15051;

        public const uint ConfigurationVersionDataType_Encoding_DefaultJson = 15049;

        public const uint PublishedDataSetDataType_Encoding_DefaultJson = 16152;

        public const uint PublishedDataSetSourceDataType_Encoding_DefaultJson = 16153;

        public const uint PublishedVariableDataType_Encoding_DefaultJson = 15060;

        public const uint PublishedDataItemsDataType_Encoding_DefaultJson = 16154;

        public const uint PublishedEventsDataType_Encoding_DefaultJson = 16155;

        public const uint PublishedDataSetCustomSourceDataType_Encoding_DefaultJson = 25561;

        public const uint ActionTargetDataType_Encoding_DefaultJson = 18622;

        public const uint PublishedActionDataType_Encoding_DefaultJson = 18623;

        public const uint ActionMethodDataType_Encoding_DefaultJson = 18624;

        public const uint PublishedActionMethodDataType_Encoding_DefaultJson = 18945;

        public const uint DataSetWriterDataType_Encoding_DefaultJson = 16156;

        public const uint DataSetWriterTransportDataType_Encoding_DefaultJson = 16157;

        public const uint DataSetWriterMessageDataType_Encoding_DefaultJson = 16158;

        public const uint PubSubGroupDataType_Encoding_DefaultJson = 16159;

        public const uint WriterGroupDataType_Encoding_DefaultJson = 21198;

        public const uint WriterGroupTransportDataType_Encoding_DefaultJson = 16161;

        public const uint WriterGroupMessageDataType_Encoding_DefaultJson = 16280;

        public const uint PubSubConnectionDataType_Encoding_DefaultJson = 16281;

        public const uint ConnectionTransportDataType_Encoding_DefaultJson = 16282;

        public const uint NetworkAddressDataType_Encoding_DefaultJson = 21199;

        public const uint NetworkAddressUrlDataType_Encoding_DefaultJson = 21200;

        public const uint ReaderGroupDataType_Encoding_DefaultJson = 21201;

        public const uint ReaderGroupTransportDataType_Encoding_DefaultJson = 16284;

        public const uint ReaderGroupMessageDataType_Encoding_DefaultJson = 16285;

        public const uint DataSetReaderDataType_Encoding_DefaultJson = 16286;

        public const uint DataSetReaderTransportDataType_Encoding_DefaultJson = 16287;

        public const uint DataSetReaderMessageDataType_Encoding_DefaultJson = 16288;

        public const uint SubscribedDataSetDataType_Encoding_DefaultJson = 16308;

        public const uint TargetVariablesDataType_Encoding_DefaultJson = 16310;

        public const uint FieldTargetDataType_Encoding_DefaultJson = 15061;

        public const uint SubscribedDataSetMirrorDataType_Encoding_DefaultJson = 16311;

        public const uint PubSubConfigurationDataType_Encoding_DefaultJson = 21202;

        public const uint StandaloneSubscribedDataSetRefDataType_Encoding_DefaultJson = 23987;

        public const uint StandaloneSubscribedDataSetDataType_Encoding_DefaultJson = 23988;

        public const uint SecurityGroupDataType_Encoding_DefaultJson = 23989;

        public const uint PubSubKeyPushTargetDataType_Encoding_DefaultJson = 25562;

        public const uint PubSubConfiguration2DataType_Encoding_DefaultJson = 23990;

        public const uint UadpWriterGroupMessageDataType_Encoding_DefaultJson = 16323;

        public const uint UadpDataSetWriterMessageDataType_Encoding_DefaultJson = 16391;

        public const uint UadpDataSetReaderMessageDataType_Encoding_DefaultJson = 16392;

        public const uint JsonWriterGroupMessageDataType_Encoding_DefaultJson = 16393;

        public const uint JsonDataSetWriterMessageDataType_Encoding_DefaultJson = 16394;

        public const uint JsonDataSetReaderMessageDataType_Encoding_DefaultJson = 16404;

        public const uint QosDataType_Encoding_DefaultJson = 23991;

        public const uint TransmitQosDataType_Encoding_DefaultJson = 23992;

        public const uint TransmitQosPriorityDataType_Encoding_DefaultJson = 23993;

        public const uint ReceiveQosDataType_Encoding_DefaultJson = 23996;

        public const uint ReceiveQosPriorityDataType_Encoding_DefaultJson = 23997;

        public const uint DatagramConnectionTransportDataType_Encoding_DefaultJson = 17476;

        public const uint DatagramConnectionTransport2DataType_Encoding_DefaultJson = 24000;

        public const uint DatagramWriterGroupTransportDataType_Encoding_DefaultJson = 21203;

        public const uint DatagramWriterGroupTransport2DataType_Encoding_DefaultJson = 24001;

        public const uint DatagramDataSetReaderTransportDataType_Encoding_DefaultJson = 24002;

        public const uint DtlsPubSubConnectionDataType_Encoding_DefaultJson = 18946;

        public const uint BrokerConnectionTransportDataType_Encoding_DefaultJson = 15726;

        public const uint BrokerWriterGroupTransportDataType_Encoding_DefaultJson = 16524;

        public const uint BrokerDataSetWriterTransportDataType_Encoding_DefaultJson = 16525;

        public const uint BrokerDataSetReaderTransportDataType_Encoding_DefaultJson = 16526;

        public const uint PubSubConfigurationRefDataType_Encoding_DefaultJson = 25563;

        public const uint PubSubConfigurationValueDataType_Encoding_DefaultJson = 25564;

        public const uint AliasNameDataType_Encoding_DefaultJson = 23511;

        public const uint UserManagementDataType_Encoding_DefaultJson = 24300;

        public const uint PriorityMappingEntryType_Encoding_DefaultJson = 25247;

        public const uint LldpManagementAddressTxPortType_Encoding_DefaultJson = 19299;

        public const uint LldpManagementAddressType_Encoding_DefaultJson = 19300;

        public const uint LldpTlvType_Encoding_DefaultJson = 19301;

        public const uint ReferenceDescriptionDataType_Encoding_DefaultJson = 32677;

        public const uint ReferenceListEntryDataType_Encoding_DefaultJson = 32678;

        public const uint LogRecord_Encoding_DefaultJson = 19387;

        public const uint LogRecordsDataType_Encoding_DefaultJson = 19803;

        public const uint SpanContextDataType_Encoding_DefaultJson = 19804;

        public const uint TraceContextDataType_Encoding_DefaultJson = 19805;

        public const uint NameValuePair_Encoding_DefaultJson = 19806;

        public const uint RolePermissionType_Encoding_DefaultJson = 15062;

        public const uint DataTypeDefinition_Encoding_DefaultJson = 15063;

        public const uint StructureField_Encoding_DefaultJson = 15065;

        public const uint StructureDefinition_Encoding_DefaultJson = 15066;

        public const uint EnumDefinition_Encoding_DefaultJson = 15067;

        public const uint Node_Encoding_DefaultJson = 15068;

        public const uint InstanceNode_Encoding_DefaultJson = 15069;

        public const uint TypeNode_Encoding_DefaultJson = 15070;

        public const uint ObjectNode_Encoding_DefaultJson = 15071;

        public const uint ObjectTypeNode_Encoding_DefaultJson = 15073;

        public const uint VariableNode_Encoding_DefaultJson = 15074;

        public const uint VariableTypeNode_Encoding_DefaultJson = 15075;

        public const uint ReferenceTypeNode_Encoding_DefaultJson = 15076;

        public const uint MethodNode_Encoding_DefaultJson = 15077;

        public const uint ViewNode_Encoding_DefaultJson = 15078;

        public const uint DataTypeNode_Encoding_DefaultJson = 15079;

        public const uint ReferenceNode_Encoding_DefaultJson = 15080;

        public const uint Argument_Encoding_DefaultJson = 15081;

        public const uint EnumValueType_Encoding_DefaultJson = 15082;

        public const uint EnumField_Encoding_DefaultJson = 15083;

        public const uint OptionSet_Encoding_DefaultJson = 15084;

        public const uint TimeZoneDataType_Encoding_DefaultJson = 15086;

        public const uint ApplicationDescription_Encoding_DefaultJson = 15087;

        public const uint RequestHeader_Encoding_DefaultJson = 15088;

        public const uint ResponseHeader_Encoding_DefaultJson = 15089;

        public const uint ServiceFault_Encoding_DefaultJson = 15090;

        public const uint SessionlessInvokeRequestType_Encoding_DefaultJson = 15091;

        public const uint SessionlessInvokeResponseType_Encoding_DefaultJson = 15092;

        public const uint FindServersRequest_Encoding_DefaultJson = 15093;

        public const uint FindServersResponse_Encoding_DefaultJson = 15094;

        public const uint ServerOnNetwork_Encoding_DefaultJson = 15095;

        public const uint FindServersOnNetworkRequest_Encoding_DefaultJson = 15096;

        public const uint FindServersOnNetworkResponse_Encoding_DefaultJson = 15097;

        public const uint UserTokenPolicy_Encoding_DefaultJson = 15098;

        public const uint EndpointDescription_Encoding_DefaultJson = 15099;

        public const uint GetEndpointsRequest_Encoding_DefaultJson = 15100;

        public const uint GetEndpointsResponse_Encoding_DefaultJson = 15101;

        public const uint RegisteredServer_Encoding_DefaultJson = 15102;

        public const uint RegisterServerRequest_Encoding_DefaultJson = 15103;

        public const uint RegisterServerResponse_Encoding_DefaultJson = 15104;

        public const uint DiscoveryConfiguration_Encoding_DefaultJson = 15105;

        public const uint MdnsDiscoveryConfiguration_Encoding_DefaultJson = 15106;

        public const uint RegisterServer2Request_Encoding_DefaultJson = 15107;

        public const uint RegisterServer2Response_Encoding_DefaultJson = 15130;

        public const uint ChannelSecurityToken_Encoding_DefaultJson = 15131;

        public const uint OpenSecureChannelRequest_Encoding_DefaultJson = 15132;

        public const uint OpenSecureChannelResponse_Encoding_DefaultJson = 15133;

        public const uint CloseSecureChannelRequest_Encoding_DefaultJson = 15134;

        public const uint CloseSecureChannelResponse_Encoding_DefaultJson = 15135;

        public const uint SignedSoftwareCertificate_Encoding_DefaultJson = 15136;

        public const uint SignatureData_Encoding_DefaultJson = 15137;

        public const uint CreateSessionRequest_Encoding_DefaultJson = 15138;

        public const uint CreateSessionResponse_Encoding_DefaultJson = 15139;

        public const uint UserIdentityToken_Encoding_DefaultJson = 15140;

        public const uint AnonymousIdentityToken_Encoding_DefaultJson = 15141;

        public const uint UserNameIdentityToken_Encoding_DefaultJson = 15142;

        public const uint X509IdentityToken_Encoding_DefaultJson = 15143;

        public const uint IssuedIdentityToken_Encoding_DefaultJson = 15144;

        public const uint ActivateSessionRequest_Encoding_DefaultJson = 15145;

        public const uint ActivateSessionResponse_Encoding_DefaultJson = 15146;

        public const uint CloseSessionRequest_Encoding_DefaultJson = 15147;

        public const uint CloseSessionResponse_Encoding_DefaultJson = 15148;

        public const uint CancelRequest_Encoding_DefaultJson = 15149;

        public const uint CancelResponse_Encoding_DefaultJson = 15150;

        public const uint NodeAttributes_Encoding_DefaultJson = 15151;

        public const uint ObjectAttributes_Encoding_DefaultJson = 15152;

        public const uint VariableAttributes_Encoding_DefaultJson = 15153;

        public const uint MethodAttributes_Encoding_DefaultJson = 15157;

        public const uint ObjectTypeAttributes_Encoding_DefaultJson = 15158;

        public const uint VariableTypeAttributes_Encoding_DefaultJson = 15159;

        public const uint ReferenceTypeAttributes_Encoding_DefaultJson = 15160;

        public const uint DataTypeAttributes_Encoding_DefaultJson = 15161;

        public const uint ViewAttributes_Encoding_DefaultJson = 15162;

        public const uint GenericAttributeValue_Encoding_DefaultJson = 15163;

        public const uint GenericAttributes_Encoding_DefaultJson = 15164;

        public const uint AddNodesItem_Encoding_DefaultJson = 15165;

        public const uint AddNodesResult_Encoding_DefaultJson = 15166;

        public const uint AddNodesRequest_Encoding_DefaultJson = 15167;

        public const uint AddNodesResponse_Encoding_DefaultJson = 15168;

        public const uint AddReferencesItem_Encoding_DefaultJson = 15169;

        public const uint AddReferencesRequest_Encoding_DefaultJson = 15170;

        public const uint AddReferencesResponse_Encoding_DefaultJson = 15171;

        public const uint DeleteNodesItem_Encoding_DefaultJson = 15172;

        public const uint DeleteNodesRequest_Encoding_DefaultJson = 15173;

        public const uint DeleteNodesResponse_Encoding_DefaultJson = 15174;

        public const uint DeleteReferencesItem_Encoding_DefaultJson = 15175;

        public const uint DeleteReferencesRequest_Encoding_DefaultJson = 15176;

        public const uint DeleteReferencesResponse_Encoding_DefaultJson = 15177;

        public const uint ViewDescription_Encoding_DefaultJson = 15179;

        public const uint BrowseDescription_Encoding_DefaultJson = 15180;

        public const uint ReferenceDescription_Encoding_DefaultJson = 15182;

        public const uint BrowseResult_Encoding_DefaultJson = 15183;

        public const uint BrowseRequest_Encoding_DefaultJson = 15184;

        public const uint BrowseResponse_Encoding_DefaultJson = 15185;

        public const uint BrowseNextRequest_Encoding_DefaultJson = 15186;

        public const uint BrowseNextResponse_Encoding_DefaultJson = 15187;

        public const uint RelativePathElement_Encoding_DefaultJson = 15188;

        public const uint RelativePath_Encoding_DefaultJson = 15189;

        public const uint BrowsePath_Encoding_DefaultJson = 15190;

        public const uint BrowsePathTarget_Encoding_DefaultJson = 15191;

        public const uint BrowsePathResult_Encoding_DefaultJson = 15192;

        public const uint TranslateBrowsePathsToNodeIdsRequest_Encoding_DefaultJson = 15193;

        public const uint TranslateBrowsePathsToNodeIdsResponse_Encoding_DefaultJson = 15194;

        public const uint RegisterNodesRequest_Encoding_DefaultJson = 15195;

        public const uint RegisterNodesResponse_Encoding_DefaultJson = 15196;

        public const uint UnregisterNodesRequest_Encoding_DefaultJson = 15197;

        public const uint UnregisterNodesResponse_Encoding_DefaultJson = 15198;

        public const uint EndpointConfiguration_Encoding_DefaultJson = 15199;

        public const uint QueryDataDescription_Encoding_DefaultJson = 15200;

        public const uint NodeTypeDescription_Encoding_DefaultJson = 15201;

        public const uint QueryDataSet_Encoding_DefaultJson = 15202;

        public const uint NodeReference_Encoding_DefaultJson = 15203;

        public const uint ContentFilterElement_Encoding_DefaultJson = 15204;

        public const uint ContentFilter_Encoding_DefaultJson = 15205;

        public const uint FilterOperand_Encoding_DefaultJson = 15206;

        public const uint ElementOperand_Encoding_DefaultJson = 15207;

        public const uint LiteralOperand_Encoding_DefaultJson = 15208;

        public const uint AttributeOperand_Encoding_DefaultJson = 15209;

        public const uint SimpleAttributeOperand_Encoding_DefaultJson = 15210;

        public const uint ContentFilterElementResult_Encoding_DefaultJson = 15211;

        public const uint ContentFilterResult_Encoding_DefaultJson = 15228;

        public const uint ParsingResult_Encoding_DefaultJson = 15236;

        public const uint QueryFirstRequest_Encoding_DefaultJson = 15244;

        public const uint QueryFirstResponse_Encoding_DefaultJson = 15252;

        public const uint QueryNextRequest_Encoding_DefaultJson = 15254;

        public const uint QueryNextResponse_Encoding_DefaultJson = 15255;

        public const uint ReadValueId_Encoding_DefaultJson = 15256;

        public const uint ReadRequest_Encoding_DefaultJson = 15257;

        public const uint ReadResponse_Encoding_DefaultJson = 15258;

        public const uint HistoryReadValueId_Encoding_DefaultJson = 15259;

        public const uint HistoryReadResult_Encoding_DefaultJson = 15260;

        public const uint HistoryReadDetails_Encoding_DefaultJson = 15261;

        public const uint ReadEventDetails_Encoding_DefaultJson = 15262;

        public const uint ReadEventDetails2_Encoding_DefaultJson = 32802;

        public const uint SortRuleElement_Encoding_DefaultJson = 18654;

        public const uint ReadEventDetailsSorted_Encoding_DefaultJson = 18655;

        public const uint ReadRawModifiedDetails_Encoding_DefaultJson = 15263;

        public const uint ReadProcessedDetails_Encoding_DefaultJson = 15264;

        public const uint ReadAtTimeDetails_Encoding_DefaultJson = 15269;

        public const uint ReadAnnotationDataDetails_Encoding_DefaultJson = 23512;

        public const uint HistoryData_Encoding_DefaultJson = 15270;

        public const uint ModificationInfo_Encoding_DefaultJson = 15271;

        public const uint HistoryModifiedData_Encoding_DefaultJson = 15272;

        public const uint HistoryEvent_Encoding_DefaultJson = 15273;

        public const uint HistoryModifiedEvent_Encoding_DefaultJson = 32833;

        public const uint HistoryReadRequest_Encoding_DefaultJson = 15274;

        public const uint HistoryReadResponse_Encoding_DefaultJson = 15275;

        public const uint WriteValue_Encoding_DefaultJson = 15276;

        public const uint WriteRequest_Encoding_DefaultJson = 15277;

        public const uint WriteResponse_Encoding_DefaultJson = 15278;

        public const uint HistoryUpdateDetails_Encoding_DefaultJson = 15279;

        public const uint UpdateDataDetails_Encoding_DefaultJson = 15280;

        public const uint UpdateStructureDataDetails_Encoding_DefaultJson = 15281;

        public const uint UpdateEventDetails_Encoding_DefaultJson = 15282;

        public const uint DeleteRawModifiedDetails_Encoding_DefaultJson = 15283;

        public const uint DeleteAtTimeDetails_Encoding_DefaultJson = 15284;

        public const uint DeleteEventDetails_Encoding_DefaultJson = 15285;

        public const uint HistoryUpdateResult_Encoding_DefaultJson = 15286;

        public const uint HistoryUpdateRequest_Encoding_DefaultJson = 15287;

        public const uint HistoryUpdateResponse_Encoding_DefaultJson = 15288;

        public const uint CallMethodRequest_Encoding_DefaultJson = 15289;

        public const uint CallMethodResult_Encoding_DefaultJson = 15290;

        public const uint CallRequest_Encoding_DefaultJson = 15291;

        public const uint CallResponse_Encoding_DefaultJson = 15292;

        public const uint MonitoringFilter_Encoding_DefaultJson = 15293;

        public const uint DataChangeFilter_Encoding_DefaultJson = 15294;

        public const uint EventFilter_Encoding_DefaultJson = 15295;

        public const uint AggregateConfiguration_Encoding_DefaultJson = 15304;

        public const uint AggregateFilter_Encoding_DefaultJson = 15312;

        public const uint MonitoringFilterResult_Encoding_DefaultJson = 15313;

        public const uint EventFilterResult_Encoding_DefaultJson = 15314;

        public const uint AggregateFilterResult_Encoding_DefaultJson = 15315;

        public const uint MonitoringParameters_Encoding_DefaultJson = 15320;

        public const uint MonitoredItemCreateRequest_Encoding_DefaultJson = 15321;

        public const uint MonitoredItemCreateResult_Encoding_DefaultJson = 15322;

        public const uint CreateMonitoredItemsRequest_Encoding_DefaultJson = 15323;

        public const uint CreateMonitoredItemsResponse_Encoding_DefaultJson = 15324;

        public const uint MonitoredItemModifyRequest_Encoding_DefaultJson = 15325;

        public const uint MonitoredItemModifyResult_Encoding_DefaultJson = 15326;

        public const uint ModifyMonitoredItemsRequest_Encoding_DefaultJson = 15327;

        public const uint ModifyMonitoredItemsResponse_Encoding_DefaultJson = 15328;

        public const uint SetMonitoringModeRequest_Encoding_DefaultJson = 15329;

        public const uint SetMonitoringModeResponse_Encoding_DefaultJson = 15331;

        public const uint SetTriggeringRequest_Encoding_DefaultJson = 15332;

        public const uint SetTriggeringResponse_Encoding_DefaultJson = 15333;

        public const uint DeleteMonitoredItemsRequest_Encoding_DefaultJson = 15335;

        public const uint DeleteMonitoredItemsResponse_Encoding_DefaultJson = 15336;

        public const uint CreateSubscriptionRequest_Encoding_DefaultJson = 15337;

        public const uint CreateSubscriptionResponse_Encoding_DefaultJson = 15338;

        public const uint ModifySubscriptionRequest_Encoding_DefaultJson = 15339;

        public const uint ModifySubscriptionResponse_Encoding_DefaultJson = 15340;

        public const uint SetPublishingModeRequest_Encoding_DefaultJson = 15341;

        public const uint SetPublishingModeResponse_Encoding_DefaultJson = 15342;

        public const uint NotificationMessage_Encoding_DefaultJson = 15343;

        public const uint NotificationData_Encoding_DefaultJson = 15344;

        public const uint DataChangeNotification_Encoding_DefaultJson = 15345;

        public const uint MonitoredItemNotification_Encoding_DefaultJson = 15346;

        public const uint EventNotificationList_Encoding_DefaultJson = 15347;

        public const uint EventFieldList_Encoding_DefaultJson = 15348;

        public const uint HistoryEventFieldList_Encoding_DefaultJson = 15349;

        public const uint StatusChangeNotification_Encoding_DefaultJson = 15350;

        public const uint SubscriptionAcknowledgement_Encoding_DefaultJson = 15351;

        public const uint PublishRequest_Encoding_DefaultJson = 15352;

        public const uint PublishResponse_Encoding_DefaultJson = 15353;

        public const uint RepublishRequest_Encoding_DefaultJson = 15354;

        public const uint RepublishResponse_Encoding_DefaultJson = 15355;

        public const uint TransferResult_Encoding_DefaultJson = 15356;

        public const uint TransferSubscriptionsRequest_Encoding_DefaultJson = 15357;

        public const uint TransferSubscriptionsResponse_Encoding_DefaultJson = 15358;

        public const uint DeleteSubscriptionsRequest_Encoding_DefaultJson = 15359;

        public const uint DeleteSubscriptionsResponse_Encoding_DefaultJson = 15360;

        public const uint BuildInfo_Encoding_DefaultJson = 15361;

        public const uint RedundantServerDataType_Encoding_DefaultJson = 15362;

        public const uint EndpointUrlListDataType_Encoding_DefaultJson = 15363;

        public const uint NetworkGroupDataType_Encoding_DefaultJson = 15364;

        public const uint SamplingIntervalDiagnosticsDataType_Encoding_DefaultJson = 15365;

        public const uint ServerDiagnosticsSummaryDataType_Encoding_DefaultJson = 15366;

        public const uint ServerStatusDataType_Encoding_DefaultJson = 15367;

        public const uint SessionDiagnosticsDataType_Encoding_DefaultJson = 15368;

        public const uint SessionSecurityDiagnosticsDataType_Encoding_DefaultJson = 15369;

        public const uint ServiceCounterDataType_Encoding_DefaultJson = 15370;

        public const uint StatusResult_Encoding_DefaultJson = 15371;

        public const uint SubscriptionDiagnosticsDataType_Encoding_DefaultJson = 15372;

        public const uint ModelChangeStructureDataType_Encoding_DefaultJson = 15373;

        public const uint SemanticChangeStructureDataType_Encoding_DefaultJson = 15374;

        public const uint Range_Encoding_DefaultJson = 15375;

        public const uint EUInformation_Encoding_DefaultJson = 15376;

        public const uint ComplexNumberType_Encoding_DefaultJson = 15377;

        public const uint DoubleComplexNumberType_Encoding_DefaultJson = 15378;

        public const uint AxisInformation_Encoding_DefaultJson = 15379;

        public const uint XVType_Encoding_DefaultJson = 15380;

        public const uint ProgramDiagnosticDataType_Encoding_DefaultJson = 15381;

        public const uint ProgramDiagnostic2DataType_Encoding_DefaultJson = 24042;

        public const uint Annotation_Encoding_DefaultJson = 15382;
    }
    #endregion

    #region ObjectType Identifiers
    /// <exclude />


    public static partial class ObjectTypes
    {
        public const uint BaseObjectType = 58;

        public const uint DataTypeEncodingType = 76;
    }
    #endregion

    #region ReferenceType Identifiers
    /// <exclude />


    public static partial class ReferenceTypes
    {
        public const uint References = 31;

        public const uint NonHierarchicalReferences = 32;

        public const uint HierarchicalReferences = 33;

        public const uint Organizes = 35;

        public const uint HasEventSource = 36;

        public const uint HasModellingRule = 37;

        public const uint HasEncoding = 38;

        public const uint HasDescription = 39;

        public const uint HasTypeDefinition = 40;

        public const uint GeneratesEvent = 41;

        public const uint AlwaysGeneratesEvent = 3065;

        public const uint HasSubtype = 45;

        public const uint HasProperty = 46;

        public const uint HasComponent = 47;

        public const uint HasNotifier = 48;

        public const uint HasOrderedComponent = 49;

        public const uint FromState = 51;

        public const uint ToState = 52;

        public const uint HasCause = 53;

        public const uint HasEffect = 54;

        public const uint HasGuard = 15112;

        public const uint HasDictionaryEntry = 17597;

        public const uint HasInterface = 17603;

        public const uint HasAddIn = 17604;

        public const uint HasTrueSubState = 9004;

        public const uint HasFalseSubState = 9005;

        public const uint HasAlarmSuppressionGroup = 16361;

        public const uint AlarmGroupMember = 16362;

        public const uint AlarmSuppressionGroupMember = 32059;

        public const uint HasCondition = 9006;
    }
    #endregion

    #region VariableType Identifiers
    /// <exclude />


    public static partial class VariableTypes
    {
        public const uint BaseVariableType = 62;

        public const uint BaseDataVariableType = 63;

        public const uint PropertyType = 68;

        public const uint DataTypeDictionaryType = 72;
    }
    #endregion

    #region DataType Node Identifiers
    /// <exclude />


    public static partial class DataTypeIds
    {
        public static readonly NodeId BaseDataType = new NodeId(Opc.Ua.DataTypes.BaseDataType);

        public static readonly NodeId Number = new NodeId(Opc.Ua.DataTypes.Number);

        public static readonly NodeId Integer = new NodeId(Opc.Ua.DataTypes.Integer);

        public static readonly NodeId UInteger = new NodeId(Opc.Ua.DataTypes.UInteger);

        public static readonly NodeId Enumeration = new NodeId(Opc.Ua.DataTypes.Enumeration);

        public static readonly NodeId Boolean = new NodeId(Opc.Ua.DataTypes.Boolean);

        public static readonly NodeId SByte = new NodeId(Opc.Ua.DataTypes.SByte);

        public static readonly NodeId Byte = new NodeId(Opc.Ua.DataTypes.Byte);

        public static readonly NodeId Int16 = new NodeId(Opc.Ua.DataTypes.Int16);

        public static readonly NodeId UInt16 = new NodeId(Opc.Ua.DataTypes.UInt16);

        public static readonly NodeId Int32 = new NodeId(Opc.Ua.DataTypes.Int32);

        public static readonly NodeId UInt32 = new NodeId(Opc.Ua.DataTypes.UInt32);

        public static readonly NodeId Int64 = new NodeId(Opc.Ua.DataTypes.Int64);

        public static readonly NodeId UInt64 = new NodeId(Opc.Ua.DataTypes.UInt64);

        public static readonly NodeId Float = new NodeId(Opc.Ua.DataTypes.Float);

        public static readonly NodeId Double = new NodeId(Opc.Ua.DataTypes.Double);

        public static readonly NodeId String = new NodeId(Opc.Ua.DataTypes.String);

        public static readonly NodeId DateTime = new NodeId(Opc.Ua.DataTypes.DateTime);

        public static readonly NodeId Guid = new NodeId(Opc.Ua.DataTypes.Guid);

        public static readonly NodeId ByteString = new NodeId(Opc.Ua.DataTypes.ByteString);

        public static readonly NodeId XmlElement = new NodeId(Opc.Ua.DataTypes.XmlElement);

        public static readonly NodeId NodeId = new NodeId(Opc.Ua.DataTypes.NodeId);

        public static readonly NodeId ExpandedNodeId = new NodeId(Opc.Ua.DataTypes.ExpandedNodeId);

        public static readonly NodeId StatusCode = new NodeId(Opc.Ua.DataTypes.StatusCode);

        public static readonly NodeId QualifiedName = new NodeId(Opc.Ua.DataTypes.QualifiedName);

        public static readonly NodeId LocalizedText = new NodeId(Opc.Ua.DataTypes.LocalizedText);

        public static readonly NodeId Structure = new NodeId(Opc.Ua.DataTypes.Structure);

        public static readonly NodeId Union = new NodeId(Opc.Ua.DataTypes.Union);

        public static readonly NodeId KeyValuePair = new NodeId(Opc.Ua.DataTypes.KeyValuePair);

        public static readonly NodeId AdditionalParametersType = new NodeId(Opc.Ua.DataTypes.AdditionalParametersType);

        public static readonly NodeId EphemeralKeyType = new NodeId(Opc.Ua.DataTypes.EphemeralKeyType);

        public static readonly NodeId EndpointType = new NodeId(Opc.Ua.DataTypes.EndpointType);

        public static readonly NodeId BitFieldDefinition = new NodeId(Opc.Ua.DataTypes.BitFieldDefinition);

        public static readonly NodeId RationalNumber = new NodeId(Opc.Ua.DataTypes.RationalNumber);

        public static readonly NodeId Vector = new NodeId(Opc.Ua.DataTypes.Vector);

        public static readonly NodeId ThreeDVector = new NodeId(Opc.Ua.DataTypes.ThreeDVector);

        public static readonly NodeId CartesianCoordinates = new NodeId(Opc.Ua.DataTypes.CartesianCoordinates);

        public static readonly NodeId ThreeDCartesianCoordinates = new NodeId(Opc.Ua.DataTypes.ThreeDCartesianCoordinates);

        public static readonly NodeId Orientation = new NodeId(Opc.Ua.DataTypes.Orientation);

        public static readonly NodeId ThreeDOrientation = new NodeId(Opc.Ua.DataTypes.ThreeDOrientation);

        public static readonly NodeId Frame = new NodeId(Opc.Ua.DataTypes.Frame);

        public static readonly NodeId ThreeDFrame = new NodeId(Opc.Ua.DataTypes.ThreeDFrame);

        public static readonly NodeId IdentityMappingRuleType = new NodeId(Opc.Ua.DataTypes.IdentityMappingRuleType);

        public static readonly NodeId CurrencyUnitType = new NodeId(Opc.Ua.DataTypes.CurrencyUnitType);

        public static readonly NodeId AnnotationDataType = new NodeId(Opc.Ua.DataTypes.AnnotationDataType);

        public static readonly NodeId LinearConversionDataType = new NodeId(Opc.Ua.DataTypes.LinearConversionDataType);

        public static readonly NodeId QuantityDimension = new NodeId(Opc.Ua.DataTypes.QuantityDimension);

        public static readonly NodeId TrustListDataType = new NodeId(Opc.Ua.DataTypes.TrustListDataType);

        public static readonly NodeId BaseConfigurationDataType = new NodeId(Opc.Ua.DataTypes.BaseConfigurationDataType);

        public static readonly NodeId BaseConfigurationRecordDataType = new NodeId(Opc.Ua.DataTypes.BaseConfigurationRecordDataType);

        public static readonly NodeId CertificateGroupDataType = new NodeId(Opc.Ua.DataTypes.CertificateGroupDataType);

        public static readonly NodeId ConfigurationUpdateTargetType = new NodeId(Opc.Ua.DataTypes.ConfigurationUpdateTargetType);

        public static readonly NodeId TransactionErrorType = new NodeId(Opc.Ua.DataTypes.TransactionErrorType);

        public static readonly NodeId ApplicationConfigurationDataType = new NodeId(Opc.Ua.DataTypes.ApplicationConfigurationDataType);

        public static readonly NodeId ApplicationIdentityDataType = new NodeId(Opc.Ua.DataTypes.ApplicationIdentityDataType);

        public static readonly NodeId EndpointDataType = new NodeId(Opc.Ua.DataTypes.EndpointDataType);

        public static readonly NodeId ServerEndpointDataType = new NodeId(Opc.Ua.DataTypes.ServerEndpointDataType);

        public static readonly NodeId SecuritySettingsDataType = new NodeId(Opc.Ua.DataTypes.SecuritySettingsDataType);

        public static readonly NodeId UserTokenSettingsDataType = new NodeId(Opc.Ua.DataTypes.UserTokenSettingsDataType);

        public static readonly NodeId ServiceCertificateDataType = new NodeId(Opc.Ua.DataTypes.ServiceCertificateDataType);

        public static readonly NodeId AuthorizationServiceConfigurationDataType = new NodeId(Opc.Ua.DataTypes.AuthorizationServiceConfigurationDataType);

        public static readonly NodeId DecimalDataType = new NodeId(Opc.Ua.DataTypes.DecimalDataType);

        public static readonly NodeId DataTypeSchemaHeader = new NodeId(Opc.Ua.DataTypes.DataTypeSchemaHeader);

        public static readonly NodeId DataTypeDescription = new NodeId(Opc.Ua.DataTypes.DataTypeDescription);

        public static readonly NodeId StructureDescription = new NodeId(Opc.Ua.DataTypes.StructureDescription);

        public static readonly NodeId EnumDescription = new NodeId(Opc.Ua.DataTypes.EnumDescription);

        public static readonly NodeId SimpleTypeDescription = new NodeId(Opc.Ua.DataTypes.SimpleTypeDescription);

        public static readonly NodeId UABinaryFileDataType = new NodeId(Opc.Ua.DataTypes.UABinaryFileDataType);

        public static readonly NodeId PortableQualifiedName = new NodeId(Opc.Ua.DataTypes.PortableQualifiedName);

        public static readonly NodeId PortableNodeId = new NodeId(Opc.Ua.DataTypes.PortableNodeId);

        public static readonly NodeId UnsignedRationalNumber = new NodeId(Opc.Ua.DataTypes.UnsignedRationalNumber);

        public static readonly NodeId DataSetMetaDataType = new NodeId(Opc.Ua.DataTypes.DataSetMetaDataType);

        public static readonly NodeId FieldMetaData = new NodeId(Opc.Ua.DataTypes.FieldMetaData);

        public static readonly NodeId ConfigurationVersionDataType = new NodeId(Opc.Ua.DataTypes.ConfigurationVersionDataType);

        public static readonly NodeId PublishedDataSetDataType = new NodeId(Opc.Ua.DataTypes.PublishedDataSetDataType);

        public static readonly NodeId PublishedDataSetSourceDataType = new NodeId(Opc.Ua.DataTypes.PublishedDataSetSourceDataType);

        public static readonly NodeId PublishedVariableDataType = new NodeId(Opc.Ua.DataTypes.PublishedVariableDataType);

        public static readonly NodeId PublishedDataItemsDataType = new NodeId(Opc.Ua.DataTypes.PublishedDataItemsDataType);

        public static readonly NodeId PublishedEventsDataType = new NodeId(Opc.Ua.DataTypes.PublishedEventsDataType);

        public static readonly NodeId PublishedDataSetCustomSourceDataType = new NodeId(Opc.Ua.DataTypes.PublishedDataSetCustomSourceDataType);

        public static readonly NodeId ActionTargetDataType = new NodeId(Opc.Ua.DataTypes.ActionTargetDataType);

        public static readonly NodeId PublishedActionDataType = new NodeId(Opc.Ua.DataTypes.PublishedActionDataType);

        public static readonly NodeId ActionMethodDataType = new NodeId(Opc.Ua.DataTypes.ActionMethodDataType);

        public static readonly NodeId PublishedActionMethodDataType = new NodeId(Opc.Ua.DataTypes.PublishedActionMethodDataType);

        public static readonly NodeId DataSetWriterDataType = new NodeId(Opc.Ua.DataTypes.DataSetWriterDataType);

        public static readonly NodeId DataSetWriterTransportDataType = new NodeId(Opc.Ua.DataTypes.DataSetWriterTransportDataType);

        public static readonly NodeId DataSetWriterMessageDataType = new NodeId(Opc.Ua.DataTypes.DataSetWriterMessageDataType);

        public static readonly NodeId PubSubGroupDataType = new NodeId(Opc.Ua.DataTypes.PubSubGroupDataType);

        public static readonly NodeId WriterGroupDataType = new NodeId(Opc.Ua.DataTypes.WriterGroupDataType);

        public static readonly NodeId WriterGroupTransportDataType = new NodeId(Opc.Ua.DataTypes.WriterGroupTransportDataType);

        public static readonly NodeId WriterGroupMessageDataType = new NodeId(Opc.Ua.DataTypes.WriterGroupMessageDataType);

        public static readonly NodeId PubSubConnectionDataType = new NodeId(Opc.Ua.DataTypes.PubSubConnectionDataType);

        public static readonly NodeId ConnectionTransportDataType = new NodeId(Opc.Ua.DataTypes.ConnectionTransportDataType);

        public static readonly NodeId NetworkAddressDataType = new NodeId(Opc.Ua.DataTypes.NetworkAddressDataType);

        public static readonly NodeId NetworkAddressUrlDataType = new NodeId(Opc.Ua.DataTypes.NetworkAddressUrlDataType);

        public static readonly NodeId ReaderGroupDataType = new NodeId(Opc.Ua.DataTypes.ReaderGroupDataType);

        public static readonly NodeId ReaderGroupTransportDataType = new NodeId(Opc.Ua.DataTypes.ReaderGroupTransportDataType);

        public static readonly NodeId ReaderGroupMessageDataType = new NodeId(Opc.Ua.DataTypes.ReaderGroupMessageDataType);

        public static readonly NodeId DataSetReaderDataType = new NodeId(Opc.Ua.DataTypes.DataSetReaderDataType);

        public static readonly NodeId DataSetReaderTransportDataType = new NodeId(Opc.Ua.DataTypes.DataSetReaderTransportDataType);

        public static readonly NodeId DataSetReaderMessageDataType = new NodeId(Opc.Ua.DataTypes.DataSetReaderMessageDataType);

        public static readonly NodeId SubscribedDataSetDataType = new NodeId(Opc.Ua.DataTypes.SubscribedDataSetDataType);

        public static readonly NodeId TargetVariablesDataType = new NodeId(Opc.Ua.DataTypes.TargetVariablesDataType);

        public static readonly NodeId FieldTargetDataType = new NodeId(Opc.Ua.DataTypes.FieldTargetDataType);

        public static readonly NodeId SubscribedDataSetMirrorDataType = new NodeId(Opc.Ua.DataTypes.SubscribedDataSetMirrorDataType);

        public static readonly NodeId PubSubConfigurationDataType = new NodeId(Opc.Ua.DataTypes.PubSubConfigurationDataType);

        public static readonly NodeId StandaloneSubscribedDataSetRefDataType = new NodeId(Opc.Ua.DataTypes.StandaloneSubscribedDataSetRefDataType);

        public static readonly NodeId StandaloneSubscribedDataSetDataType = new NodeId(Opc.Ua.DataTypes.StandaloneSubscribedDataSetDataType);

        public static readonly NodeId SecurityGroupDataType = new NodeId(Opc.Ua.DataTypes.SecurityGroupDataType);

        public static readonly NodeId PubSubKeyPushTargetDataType = new NodeId(Opc.Ua.DataTypes.PubSubKeyPushTargetDataType);

        public static readonly NodeId PubSubConfiguration2DataType = new NodeId(Opc.Ua.DataTypes.PubSubConfiguration2DataType);

        public static readonly NodeId UadpWriterGroupMessageDataType = new NodeId(Opc.Ua.DataTypes.UadpWriterGroupMessageDataType);

        public static readonly NodeId UadpDataSetWriterMessageDataType = new NodeId(Opc.Ua.DataTypes.UadpDataSetWriterMessageDataType);

        public static readonly NodeId UadpDataSetReaderMessageDataType = new NodeId(Opc.Ua.DataTypes.UadpDataSetReaderMessageDataType);

        public static readonly NodeId JsonWriterGroupMessageDataType = new NodeId(Opc.Ua.DataTypes.JsonWriterGroupMessageDataType);

        public static readonly NodeId JsonDataSetWriterMessageDataType = new NodeId(Opc.Ua.DataTypes.JsonDataSetWriterMessageDataType);

        public static readonly NodeId JsonDataSetReaderMessageDataType = new NodeId(Opc.Ua.DataTypes.JsonDataSetReaderMessageDataType);

        public static readonly NodeId QosDataType = new NodeId(Opc.Ua.DataTypes.QosDataType);

        public static readonly NodeId TransmitQosDataType = new NodeId(Opc.Ua.DataTypes.TransmitQosDataType);

        public static readonly NodeId TransmitQosPriorityDataType = new NodeId(Opc.Ua.DataTypes.TransmitQosPriorityDataType);

        public static readonly NodeId ReceiveQosDataType = new NodeId(Opc.Ua.DataTypes.ReceiveQosDataType);

        public static readonly NodeId ReceiveQosPriorityDataType = new NodeId(Opc.Ua.DataTypes.ReceiveQosPriorityDataType);

        public static readonly NodeId DatagramConnectionTransportDataType = new NodeId(Opc.Ua.DataTypes.DatagramConnectionTransportDataType);

        public static readonly NodeId DatagramConnectionTransport2DataType = new NodeId(Opc.Ua.DataTypes.DatagramConnectionTransport2DataType);

        public static readonly NodeId DatagramWriterGroupTransportDataType = new NodeId(Opc.Ua.DataTypes.DatagramWriterGroupTransportDataType);

        public static readonly NodeId DatagramWriterGroupTransport2DataType = new NodeId(Opc.Ua.DataTypes.DatagramWriterGroupTransport2DataType);

        public static readonly NodeId DatagramDataSetReaderTransportDataType = new NodeId(Opc.Ua.DataTypes.DatagramDataSetReaderTransportDataType);

        public static readonly NodeId DtlsPubSubConnectionDataType = new NodeId(Opc.Ua.DataTypes.DtlsPubSubConnectionDataType);

        public static readonly NodeId BrokerConnectionTransportDataType = new NodeId(Opc.Ua.DataTypes.BrokerConnectionTransportDataType);

        public static readonly NodeId BrokerWriterGroupTransportDataType = new NodeId(Opc.Ua.DataTypes.BrokerWriterGroupTransportDataType);

        public static readonly NodeId BrokerDataSetWriterTransportDataType = new NodeId(Opc.Ua.DataTypes.BrokerDataSetWriterTransportDataType);

        public static readonly NodeId BrokerDataSetReaderTransportDataType = new NodeId(Opc.Ua.DataTypes.BrokerDataSetReaderTransportDataType);

        public static readonly NodeId PubSubConfigurationRefDataType = new NodeId(Opc.Ua.DataTypes.PubSubConfigurationRefDataType);

        public static readonly NodeId PubSubConfigurationValueDataType = new NodeId(Opc.Ua.DataTypes.PubSubConfigurationValueDataType);

        public static readonly NodeId JsonNetworkMessage = new NodeId(Opc.Ua.DataTypes.JsonNetworkMessage);

        public static readonly NodeId JsonDataSetMessage = new NodeId(Opc.Ua.DataTypes.JsonDataSetMessage);

        public static readonly NodeId JsonDataSetMetaDataMessage = new NodeId(Opc.Ua.DataTypes.JsonDataSetMetaDataMessage);

        public static readonly NodeId JsonApplicationDescriptionMessage = new NodeId(Opc.Ua.DataTypes.JsonApplicationDescriptionMessage);

        public static readonly NodeId JsonServerEndpointsMessage = new NodeId(Opc.Ua.DataTypes.JsonServerEndpointsMessage);

        public static readonly NodeId JsonStatusMessage = new NodeId(Opc.Ua.DataTypes.JsonStatusMessage);

        public static readonly NodeId JsonPubSubConnectionMessage = new NodeId(Opc.Ua.DataTypes.JsonPubSubConnectionMessage);

        public static readonly NodeId JsonActionMetaDataMessage = new NodeId(Opc.Ua.DataTypes.JsonActionMetaDataMessage);

        public static readonly NodeId JsonActionResponderMessage = new NodeId(Opc.Ua.DataTypes.JsonActionResponderMessage);

        public static readonly NodeId JsonActionNetworkMessage = new NodeId(Opc.Ua.DataTypes.JsonActionNetworkMessage);

        public static readonly NodeId JsonActionRequestMessage = new NodeId(Opc.Ua.DataTypes.JsonActionRequestMessage);

        public static readonly NodeId JsonActionResponseMessage = new NodeId(Opc.Ua.DataTypes.JsonActionResponseMessage);

        public static readonly NodeId AliasNameDataType = new NodeId(Opc.Ua.DataTypes.AliasNameDataType);

        public static readonly NodeId UserManagementDataType = new NodeId(Opc.Ua.DataTypes.UserManagementDataType);

        public static readonly NodeId PriorityMappingEntryType = new NodeId(Opc.Ua.DataTypes.PriorityMappingEntryType);

        public static readonly NodeId LldpManagementAddressTxPortType = new NodeId(Opc.Ua.DataTypes.LldpManagementAddressTxPortType);

        public static readonly NodeId LldpManagementAddressType = new NodeId(Opc.Ua.DataTypes.LldpManagementAddressType);

        public static readonly NodeId LldpTlvType = new NodeId(Opc.Ua.DataTypes.LldpTlvType);

        public static readonly NodeId ReferenceDescriptionDataType = new NodeId(Opc.Ua.DataTypes.ReferenceDescriptionDataType);

        public static readonly NodeId ReferenceListEntryDataType = new NodeId(Opc.Ua.DataTypes.ReferenceListEntryDataType);

        public static readonly NodeId LogRecord = new NodeId(Opc.Ua.DataTypes.LogRecord);

        public static readonly NodeId LogRecordsDataType = new NodeId(Opc.Ua.DataTypes.LogRecordsDataType);

        public static readonly NodeId SpanContextDataType = new NodeId(Opc.Ua.DataTypes.SpanContextDataType);

        public static readonly NodeId TraceContextDataType = new NodeId(Opc.Ua.DataTypes.TraceContextDataType);

        public static readonly NodeId NameValuePair = new NodeId(Opc.Ua.DataTypes.NameValuePair);

        public static readonly NodeId RolePermissionType = new NodeId(Opc.Ua.DataTypes.RolePermissionType);

        public static readonly NodeId DataTypeDefinition = new NodeId(Opc.Ua.DataTypes.DataTypeDefinition);

        public static readonly NodeId StructureField = new NodeId(Opc.Ua.DataTypes.StructureField);

        public static readonly NodeId StructureDefinition = new NodeId(Opc.Ua.DataTypes.StructureDefinition);

        public static readonly NodeId EnumDefinition = new NodeId(Opc.Ua.DataTypes.EnumDefinition);

        public static readonly NodeId Node = new NodeId(Opc.Ua.DataTypes.Node);

        public static readonly NodeId InstanceNode = new NodeId(Opc.Ua.DataTypes.InstanceNode);

        public static readonly NodeId TypeNode = new NodeId(Opc.Ua.DataTypes.TypeNode);

        public static readonly NodeId ObjectNode = new NodeId(Opc.Ua.DataTypes.ObjectNode);

        public static readonly NodeId ObjectTypeNode = new NodeId(Opc.Ua.DataTypes.ObjectTypeNode);

        public static readonly NodeId VariableNode = new NodeId(Opc.Ua.DataTypes.VariableNode);

        public static readonly NodeId VariableTypeNode = new NodeId(Opc.Ua.DataTypes.VariableTypeNode);

        public static readonly NodeId ReferenceTypeNode = new NodeId(Opc.Ua.DataTypes.ReferenceTypeNode);

        public static readonly NodeId MethodNode = new NodeId(Opc.Ua.DataTypes.MethodNode);

        public static readonly NodeId ViewNode = new NodeId(Opc.Ua.DataTypes.ViewNode);

        public static readonly NodeId DataTypeNode = new NodeId(Opc.Ua.DataTypes.DataTypeNode);

        public static readonly NodeId ReferenceNode = new NodeId(Opc.Ua.DataTypes.ReferenceNode);

        public static readonly NodeId Argument = new NodeId(Opc.Ua.DataTypes.Argument);

        public static readonly NodeId EnumValueType = new NodeId(Opc.Ua.DataTypes.EnumValueType);

        public static readonly NodeId EnumField = new NodeId(Opc.Ua.DataTypes.EnumField);

        public static readonly NodeId OptionSet = new NodeId(Opc.Ua.DataTypes.OptionSet);

        public static readonly NodeId TimeZoneDataType = new NodeId(Opc.Ua.DataTypes.TimeZoneDataType);

        public static readonly NodeId ApplicationDescription = new NodeId(Opc.Ua.DataTypes.ApplicationDescription);

        public static readonly NodeId RequestHeader = new NodeId(Opc.Ua.DataTypes.RequestHeader);

        public static readonly NodeId ResponseHeader = new NodeId(Opc.Ua.DataTypes.ResponseHeader);

        public static readonly NodeId ServiceFault = new NodeId(Opc.Ua.DataTypes.ServiceFault);

        public static readonly NodeId SessionlessInvokeRequestType = new NodeId(Opc.Ua.DataTypes.SessionlessInvokeRequestType);

        public static readonly NodeId SessionlessInvokeResponseType = new NodeId(Opc.Ua.DataTypes.SessionlessInvokeResponseType);

        public static readonly NodeId FindServersRequest = new NodeId(Opc.Ua.DataTypes.FindServersRequest);

        public static readonly NodeId FindServersResponse = new NodeId(Opc.Ua.DataTypes.FindServersResponse);

        public static readonly NodeId ServerOnNetwork = new NodeId(Opc.Ua.DataTypes.ServerOnNetwork);

        public static readonly NodeId FindServersOnNetworkRequest = new NodeId(Opc.Ua.DataTypes.FindServersOnNetworkRequest);

        public static readonly NodeId FindServersOnNetworkResponse = new NodeId(Opc.Ua.DataTypes.FindServersOnNetworkResponse);

        public static readonly NodeId UserTokenPolicy = new NodeId(Opc.Ua.DataTypes.UserTokenPolicy);

        public static readonly NodeId EndpointDescription = new NodeId(Opc.Ua.DataTypes.EndpointDescription);

        public static readonly NodeId GetEndpointsRequest = new NodeId(Opc.Ua.DataTypes.GetEndpointsRequest);

        public static readonly NodeId GetEndpointsResponse = new NodeId(Opc.Ua.DataTypes.GetEndpointsResponse);

        public static readonly NodeId RegisteredServer = new NodeId(Opc.Ua.DataTypes.RegisteredServer);

        public static readonly NodeId RegisterServerRequest = new NodeId(Opc.Ua.DataTypes.RegisterServerRequest);

        public static readonly NodeId RegisterServerResponse = new NodeId(Opc.Ua.DataTypes.RegisterServerResponse);

        public static readonly NodeId DiscoveryConfiguration = new NodeId(Opc.Ua.DataTypes.DiscoveryConfiguration);

        public static readonly NodeId MdnsDiscoveryConfiguration = new NodeId(Opc.Ua.DataTypes.MdnsDiscoveryConfiguration);

        public static readonly NodeId RegisterServer2Request = new NodeId(Opc.Ua.DataTypes.RegisterServer2Request);

        public static readonly NodeId RegisterServer2Response = new NodeId(Opc.Ua.DataTypes.RegisterServer2Response);

        public static readonly NodeId ChannelSecurityToken = new NodeId(Opc.Ua.DataTypes.ChannelSecurityToken);

        public static readonly NodeId OpenSecureChannelRequest = new NodeId(Opc.Ua.DataTypes.OpenSecureChannelRequest);

        public static readonly NodeId OpenSecureChannelResponse = new NodeId(Opc.Ua.DataTypes.OpenSecureChannelResponse);

        public static readonly NodeId CloseSecureChannelRequest = new NodeId(Opc.Ua.DataTypes.CloseSecureChannelRequest);

        public static readonly NodeId CloseSecureChannelResponse = new NodeId(Opc.Ua.DataTypes.CloseSecureChannelResponse);

        public static readonly NodeId SignedSoftwareCertificate = new NodeId(Opc.Ua.DataTypes.SignedSoftwareCertificate);

        public static readonly NodeId SignatureData = new NodeId(Opc.Ua.DataTypes.SignatureData);

        public static readonly NodeId CreateSessionRequest = new NodeId(Opc.Ua.DataTypes.CreateSessionRequest);

        public static readonly NodeId CreateSessionResponse = new NodeId(Opc.Ua.DataTypes.CreateSessionResponse);

        public static readonly NodeId UserIdentityToken = new NodeId(Opc.Ua.DataTypes.UserIdentityToken);

        public static readonly NodeId AnonymousIdentityToken = new NodeId(Opc.Ua.DataTypes.AnonymousIdentityToken);

        public static readonly NodeId UserNameIdentityToken = new NodeId(Opc.Ua.DataTypes.UserNameIdentityToken);

        public static readonly NodeId X509IdentityToken = new NodeId(Opc.Ua.DataTypes.X509IdentityToken);

        public static readonly NodeId IssuedIdentityToken = new NodeId(Opc.Ua.DataTypes.IssuedIdentityToken);

        public static readonly NodeId ActivateSessionRequest = new NodeId(Opc.Ua.DataTypes.ActivateSessionRequest);

        public static readonly NodeId ActivateSessionResponse = new NodeId(Opc.Ua.DataTypes.ActivateSessionResponse);

        public static readonly NodeId CloseSessionRequest = new NodeId(Opc.Ua.DataTypes.CloseSessionRequest);

        public static readonly NodeId CloseSessionResponse = new NodeId(Opc.Ua.DataTypes.CloseSessionResponse);

        public static readonly NodeId CancelRequest = new NodeId(Opc.Ua.DataTypes.CancelRequest);

        public static readonly NodeId CancelResponse = new NodeId(Opc.Ua.DataTypes.CancelResponse);

        public static readonly NodeId NodeAttributes = new NodeId(Opc.Ua.DataTypes.NodeAttributes);

        public static readonly NodeId ObjectAttributes = new NodeId(Opc.Ua.DataTypes.ObjectAttributes);

        public static readonly NodeId VariableAttributes = new NodeId(Opc.Ua.DataTypes.VariableAttributes);

        public static readonly NodeId MethodAttributes = new NodeId(Opc.Ua.DataTypes.MethodAttributes);

        public static readonly NodeId ObjectTypeAttributes = new NodeId(Opc.Ua.DataTypes.ObjectTypeAttributes);

        public static readonly NodeId VariableTypeAttributes = new NodeId(Opc.Ua.DataTypes.VariableTypeAttributes);

        public static readonly NodeId ReferenceTypeAttributes = new NodeId(Opc.Ua.DataTypes.ReferenceTypeAttributes);

        public static readonly NodeId DataTypeAttributes = new NodeId(Opc.Ua.DataTypes.DataTypeAttributes);

        public static readonly NodeId ViewAttributes = new NodeId(Opc.Ua.DataTypes.ViewAttributes);

        public static readonly NodeId GenericAttributeValue = new NodeId(Opc.Ua.DataTypes.GenericAttributeValue);

        public static readonly NodeId GenericAttributes = new NodeId(Opc.Ua.DataTypes.GenericAttributes);

        public static readonly NodeId AddNodesItem = new NodeId(Opc.Ua.DataTypes.AddNodesItem);

        public static readonly NodeId AddNodesResult = new NodeId(Opc.Ua.DataTypes.AddNodesResult);

        public static readonly NodeId AddNodesRequest = new NodeId(Opc.Ua.DataTypes.AddNodesRequest);

        public static readonly NodeId AddNodesResponse = new NodeId(Opc.Ua.DataTypes.AddNodesResponse);

        public static readonly NodeId AddReferencesItem = new NodeId(Opc.Ua.DataTypes.AddReferencesItem);

        public static readonly NodeId AddReferencesRequest = new NodeId(Opc.Ua.DataTypes.AddReferencesRequest);

        public static readonly NodeId AddReferencesResponse = new NodeId(Opc.Ua.DataTypes.AddReferencesResponse);

        public static readonly NodeId DeleteNodesItem = new NodeId(Opc.Ua.DataTypes.DeleteNodesItem);

        public static readonly NodeId DeleteNodesRequest = new NodeId(Opc.Ua.DataTypes.DeleteNodesRequest);

        public static readonly NodeId DeleteNodesResponse = new NodeId(Opc.Ua.DataTypes.DeleteNodesResponse);

        public static readonly NodeId DeleteReferencesItem = new NodeId(Opc.Ua.DataTypes.DeleteReferencesItem);

        public static readonly NodeId DeleteReferencesRequest = new NodeId(Opc.Ua.DataTypes.DeleteReferencesRequest);

        public static readonly NodeId DeleteReferencesResponse = new NodeId(Opc.Ua.DataTypes.DeleteReferencesResponse);

        public static readonly NodeId ViewDescription = new NodeId(Opc.Ua.DataTypes.ViewDescription);

        public static readonly NodeId BrowseDescription = new NodeId(Opc.Ua.DataTypes.BrowseDescription);

        public static readonly NodeId ReferenceDescription = new NodeId(Opc.Ua.DataTypes.ReferenceDescription);

        public static readonly NodeId BrowseResult = new NodeId(Opc.Ua.DataTypes.BrowseResult);

        public static readonly NodeId BrowseRequest = new NodeId(Opc.Ua.DataTypes.BrowseRequest);

        public static readonly NodeId BrowseResponse = new NodeId(Opc.Ua.DataTypes.BrowseResponse);

        public static readonly NodeId BrowseNextRequest = new NodeId(Opc.Ua.DataTypes.BrowseNextRequest);

        public static readonly NodeId BrowseNextResponse = new NodeId(Opc.Ua.DataTypes.BrowseNextResponse);

        public static readonly NodeId RelativePathElement = new NodeId(Opc.Ua.DataTypes.RelativePathElement);

        public static readonly NodeId RelativePath = new NodeId(Opc.Ua.DataTypes.RelativePath);

        public static readonly NodeId BrowsePath = new NodeId(Opc.Ua.DataTypes.BrowsePath);

        public static readonly NodeId BrowsePathTarget = new NodeId(Opc.Ua.DataTypes.BrowsePathTarget);

        public static readonly NodeId BrowsePathResult = new NodeId(Opc.Ua.DataTypes.BrowsePathResult);

        public static readonly NodeId TranslateBrowsePathsToNodeIdsRequest = new NodeId(Opc.Ua.DataTypes.TranslateBrowsePathsToNodeIdsRequest);

        public static readonly NodeId TranslateBrowsePathsToNodeIdsResponse = new NodeId(Opc.Ua.DataTypes.TranslateBrowsePathsToNodeIdsResponse);

        public static readonly NodeId RegisterNodesRequest = new NodeId(Opc.Ua.DataTypes.RegisterNodesRequest);

        public static readonly NodeId RegisterNodesResponse = new NodeId(Opc.Ua.DataTypes.RegisterNodesResponse);

        public static readonly NodeId UnregisterNodesRequest = new NodeId(Opc.Ua.DataTypes.UnregisterNodesRequest);

        public static readonly NodeId UnregisterNodesResponse = new NodeId(Opc.Ua.DataTypes.UnregisterNodesResponse);

        public static readonly NodeId EndpointConfiguration = new NodeId(Opc.Ua.DataTypes.EndpointConfiguration);

        public static readonly NodeId QueryDataDescription = new NodeId(Opc.Ua.DataTypes.QueryDataDescription);

        public static readonly NodeId NodeTypeDescription = new NodeId(Opc.Ua.DataTypes.NodeTypeDescription);

        public static readonly NodeId QueryDataSet = new NodeId(Opc.Ua.DataTypes.QueryDataSet);

        public static readonly NodeId NodeReference = new NodeId(Opc.Ua.DataTypes.NodeReference);

        public static readonly NodeId ContentFilterElement = new NodeId(Opc.Ua.DataTypes.ContentFilterElement);

        public static readonly NodeId ContentFilter = new NodeId(Opc.Ua.DataTypes.ContentFilter);

        public static readonly NodeId FilterOperand = new NodeId(Opc.Ua.DataTypes.FilterOperand);

        public static readonly NodeId ElementOperand = new NodeId(Opc.Ua.DataTypes.ElementOperand);

        public static readonly NodeId LiteralOperand = new NodeId(Opc.Ua.DataTypes.LiteralOperand);

        public static readonly NodeId AttributeOperand = new NodeId(Opc.Ua.DataTypes.AttributeOperand);

        public static readonly NodeId SimpleAttributeOperand = new NodeId(Opc.Ua.DataTypes.SimpleAttributeOperand);

        public static readonly NodeId ContentFilterElementResult = new NodeId(Opc.Ua.DataTypes.ContentFilterElementResult);

        public static readonly NodeId ContentFilterResult = new NodeId(Opc.Ua.DataTypes.ContentFilterResult);

        public static readonly NodeId ParsingResult = new NodeId(Opc.Ua.DataTypes.ParsingResult);

        public static readonly NodeId QueryFirstRequest = new NodeId(Opc.Ua.DataTypes.QueryFirstRequest);

        public static readonly NodeId QueryFirstResponse = new NodeId(Opc.Ua.DataTypes.QueryFirstResponse);

        public static readonly NodeId QueryNextRequest = new NodeId(Opc.Ua.DataTypes.QueryNextRequest);

        public static readonly NodeId QueryNextResponse = new NodeId(Opc.Ua.DataTypes.QueryNextResponse);

        public static readonly NodeId ReadValueId = new NodeId(Opc.Ua.DataTypes.ReadValueId);

        public static readonly NodeId ReadRequest = new NodeId(Opc.Ua.DataTypes.ReadRequest);

        public static readonly NodeId ReadResponse = new NodeId(Opc.Ua.DataTypes.ReadResponse);

        public static readonly NodeId HistoryReadValueId = new NodeId(Opc.Ua.DataTypes.HistoryReadValueId);

        public static readonly NodeId HistoryReadResult = new NodeId(Opc.Ua.DataTypes.HistoryReadResult);

        public static readonly NodeId HistoryReadDetails = new NodeId(Opc.Ua.DataTypes.HistoryReadDetails);

        public static readonly NodeId ReadEventDetails = new NodeId(Opc.Ua.DataTypes.ReadEventDetails);

        public static readonly NodeId ReadEventDetails2 = new NodeId(Opc.Ua.DataTypes.ReadEventDetails2);

        public static readonly NodeId SortRuleElement = new NodeId(Opc.Ua.DataTypes.SortRuleElement);

        public static readonly NodeId ReadEventDetailsSorted = new NodeId(Opc.Ua.DataTypes.ReadEventDetailsSorted);

        public static readonly NodeId ReadRawModifiedDetails = new NodeId(Opc.Ua.DataTypes.ReadRawModifiedDetails);

        public static readonly NodeId ReadProcessedDetails = new NodeId(Opc.Ua.DataTypes.ReadProcessedDetails);

        public static readonly NodeId ReadAtTimeDetails = new NodeId(Opc.Ua.DataTypes.ReadAtTimeDetails);

        public static readonly NodeId ReadAnnotationDataDetails = new NodeId(Opc.Ua.DataTypes.ReadAnnotationDataDetails);

        public static readonly NodeId HistoryData = new NodeId(Opc.Ua.DataTypes.HistoryData);

        public static readonly NodeId ModificationInfo = new NodeId(Opc.Ua.DataTypes.ModificationInfo);

        public static readonly NodeId HistoryModifiedData = new NodeId(Opc.Ua.DataTypes.HistoryModifiedData);

        public static readonly NodeId HistoryEvent = new NodeId(Opc.Ua.DataTypes.HistoryEvent);

        public static readonly NodeId HistoryModifiedEvent = new NodeId(Opc.Ua.DataTypes.HistoryModifiedEvent);

        public static readonly NodeId HistoryReadRequest = new NodeId(Opc.Ua.DataTypes.HistoryReadRequest);

        public static readonly NodeId HistoryReadResponse = new NodeId(Opc.Ua.DataTypes.HistoryReadResponse);

        public static readonly NodeId WriteValue = new NodeId(Opc.Ua.DataTypes.WriteValue);

        public static readonly NodeId WriteRequest = new NodeId(Opc.Ua.DataTypes.WriteRequest);

        public static readonly NodeId WriteResponse = new NodeId(Opc.Ua.DataTypes.WriteResponse);

        public static readonly NodeId HistoryUpdateDetails = new NodeId(Opc.Ua.DataTypes.HistoryUpdateDetails);

        public static readonly NodeId UpdateDataDetails = new NodeId(Opc.Ua.DataTypes.UpdateDataDetails);

        public static readonly NodeId UpdateStructureDataDetails = new NodeId(Opc.Ua.DataTypes.UpdateStructureDataDetails);

        public static readonly NodeId UpdateEventDetails = new NodeId(Opc.Ua.DataTypes.UpdateEventDetails);

        public static readonly NodeId DeleteRawModifiedDetails = new NodeId(Opc.Ua.DataTypes.DeleteRawModifiedDetails);

        public static readonly NodeId DeleteAtTimeDetails = new NodeId(Opc.Ua.DataTypes.DeleteAtTimeDetails);

        public static readonly NodeId DeleteEventDetails = new NodeId(Opc.Ua.DataTypes.DeleteEventDetails);

        public static readonly NodeId HistoryUpdateResult = new NodeId(Opc.Ua.DataTypes.HistoryUpdateResult);

        public static readonly NodeId HistoryUpdateRequest = new NodeId(Opc.Ua.DataTypes.HistoryUpdateRequest);

        public static readonly NodeId HistoryUpdateResponse = new NodeId(Opc.Ua.DataTypes.HistoryUpdateResponse);

        public static readonly NodeId CallMethodRequest = new NodeId(Opc.Ua.DataTypes.CallMethodRequest);

        public static readonly NodeId CallMethodResult = new NodeId(Opc.Ua.DataTypes.CallMethodResult);

        public static readonly NodeId CallRequest = new NodeId(Opc.Ua.DataTypes.CallRequest);

        public static readonly NodeId CallResponse = new NodeId(Opc.Ua.DataTypes.CallResponse);

        public static readonly NodeId MonitoringFilter = new NodeId(Opc.Ua.DataTypes.MonitoringFilter);

        public static readonly NodeId DataChangeFilter = new NodeId(Opc.Ua.DataTypes.DataChangeFilter);

        public static readonly NodeId EventFilter = new NodeId(Opc.Ua.DataTypes.EventFilter);

        public static readonly NodeId AggregateConfiguration = new NodeId(Opc.Ua.DataTypes.AggregateConfiguration);

        public static readonly NodeId AggregateFilter = new NodeId(Opc.Ua.DataTypes.AggregateFilter);

        public static readonly NodeId MonitoringFilterResult = new NodeId(Opc.Ua.DataTypes.MonitoringFilterResult);

        public static readonly NodeId EventFilterResult = new NodeId(Opc.Ua.DataTypes.EventFilterResult);

        public static readonly NodeId AggregateFilterResult = new NodeId(Opc.Ua.DataTypes.AggregateFilterResult);

        public static readonly NodeId MonitoringParameters = new NodeId(Opc.Ua.DataTypes.MonitoringParameters);

        public static readonly NodeId MonitoredItemCreateRequest = new NodeId(Opc.Ua.DataTypes.MonitoredItemCreateRequest);

        public static readonly NodeId MonitoredItemCreateResult = new NodeId(Opc.Ua.DataTypes.MonitoredItemCreateResult);

        public static readonly NodeId CreateMonitoredItemsRequest = new NodeId(Opc.Ua.DataTypes.CreateMonitoredItemsRequest);

        public static readonly NodeId CreateMonitoredItemsResponse = new NodeId(Opc.Ua.DataTypes.CreateMonitoredItemsResponse);

        public static readonly NodeId MonitoredItemModifyRequest = new NodeId(Opc.Ua.DataTypes.MonitoredItemModifyRequest);

        public static readonly NodeId MonitoredItemModifyResult = new NodeId(Opc.Ua.DataTypes.MonitoredItemModifyResult);

        public static readonly NodeId ModifyMonitoredItemsRequest = new NodeId(Opc.Ua.DataTypes.ModifyMonitoredItemsRequest);

        public static readonly NodeId ModifyMonitoredItemsResponse = new NodeId(Opc.Ua.DataTypes.ModifyMonitoredItemsResponse);

        public static readonly NodeId SetMonitoringModeRequest = new NodeId(Opc.Ua.DataTypes.SetMonitoringModeRequest);

        public static readonly NodeId SetMonitoringModeResponse = new NodeId(Opc.Ua.DataTypes.SetMonitoringModeResponse);

        public static readonly NodeId SetTriggeringRequest = new NodeId(Opc.Ua.DataTypes.SetTriggeringRequest);

        public static readonly NodeId SetTriggeringResponse = new NodeId(Opc.Ua.DataTypes.SetTriggeringResponse);

        public static readonly NodeId DeleteMonitoredItemsRequest = new NodeId(Opc.Ua.DataTypes.DeleteMonitoredItemsRequest);

        public static readonly NodeId DeleteMonitoredItemsResponse = new NodeId(Opc.Ua.DataTypes.DeleteMonitoredItemsResponse);

        public static readonly NodeId CreateSubscriptionRequest = new NodeId(Opc.Ua.DataTypes.CreateSubscriptionRequest);

        public static readonly NodeId CreateSubscriptionResponse = new NodeId(Opc.Ua.DataTypes.CreateSubscriptionResponse);

        public static readonly NodeId ModifySubscriptionRequest = new NodeId(Opc.Ua.DataTypes.ModifySubscriptionRequest);

        public static readonly NodeId ModifySubscriptionResponse = new NodeId(Opc.Ua.DataTypes.ModifySubscriptionResponse);

        public static readonly NodeId SetPublishingModeRequest = new NodeId(Opc.Ua.DataTypes.SetPublishingModeRequest);

        public static readonly NodeId SetPublishingModeResponse = new NodeId(Opc.Ua.DataTypes.SetPublishingModeResponse);

        public static readonly NodeId NotificationMessage = new NodeId(Opc.Ua.DataTypes.NotificationMessage);

        public static readonly NodeId NotificationData = new NodeId(Opc.Ua.DataTypes.NotificationData);

        public static readonly NodeId DataChangeNotification = new NodeId(Opc.Ua.DataTypes.DataChangeNotification);

        public static readonly NodeId MonitoredItemNotification = new NodeId(Opc.Ua.DataTypes.MonitoredItemNotification);

        public static readonly NodeId EventNotificationList = new NodeId(Opc.Ua.DataTypes.EventNotificationList);

        public static readonly NodeId EventFieldList = new NodeId(Opc.Ua.DataTypes.EventFieldList);

        public static readonly NodeId HistoryEventFieldList = new NodeId(Opc.Ua.DataTypes.HistoryEventFieldList);

        public static readonly NodeId StatusChangeNotification = new NodeId(Opc.Ua.DataTypes.StatusChangeNotification);

        public static readonly NodeId SubscriptionAcknowledgement = new NodeId(Opc.Ua.DataTypes.SubscriptionAcknowledgement);

        public static readonly NodeId PublishRequest = new NodeId(Opc.Ua.DataTypes.PublishRequest);

        public static readonly NodeId PublishResponse = new NodeId(Opc.Ua.DataTypes.PublishResponse);

        public static readonly NodeId RepublishRequest = new NodeId(Opc.Ua.DataTypes.RepublishRequest);

        public static readonly NodeId RepublishResponse = new NodeId(Opc.Ua.DataTypes.RepublishResponse);

        public static readonly NodeId TransferResult = new NodeId(Opc.Ua.DataTypes.TransferResult);

        public static readonly NodeId TransferSubscriptionsRequest = new NodeId(Opc.Ua.DataTypes.TransferSubscriptionsRequest);

        public static readonly NodeId TransferSubscriptionsResponse = new NodeId(Opc.Ua.DataTypes.TransferSubscriptionsResponse);

        public static readonly NodeId DeleteSubscriptionsRequest = new NodeId(Opc.Ua.DataTypes.DeleteSubscriptionsRequest);

        public static readonly NodeId DeleteSubscriptionsResponse = new NodeId(Opc.Ua.DataTypes.DeleteSubscriptionsResponse);

        public static readonly NodeId BuildInfo = new NodeId(Opc.Ua.DataTypes.BuildInfo);

        public static readonly NodeId RedundantServerDataType = new NodeId(Opc.Ua.DataTypes.RedundantServerDataType);

        public static readonly NodeId EndpointUrlListDataType = new NodeId(Opc.Ua.DataTypes.EndpointUrlListDataType);

        public static readonly NodeId NetworkGroupDataType = new NodeId(Opc.Ua.DataTypes.NetworkGroupDataType);

        public static readonly NodeId SamplingIntervalDiagnosticsDataType = new NodeId(Opc.Ua.DataTypes.SamplingIntervalDiagnosticsDataType);

        public static readonly NodeId ServerDiagnosticsSummaryDataType = new NodeId(Opc.Ua.DataTypes.ServerDiagnosticsSummaryDataType);

        public static readonly NodeId ServerStatusDataType = new NodeId(Opc.Ua.DataTypes.ServerStatusDataType);

        public static readonly NodeId SessionDiagnosticsDataType = new NodeId(Opc.Ua.DataTypes.SessionDiagnosticsDataType);

        public static readonly NodeId SessionSecurityDiagnosticsDataType = new NodeId(Opc.Ua.DataTypes.SessionSecurityDiagnosticsDataType);

        public static readonly NodeId ServiceCounterDataType = new NodeId(Opc.Ua.DataTypes.ServiceCounterDataType);

        public static readonly NodeId StatusResult = new NodeId(Opc.Ua.DataTypes.StatusResult);

        public static readonly NodeId SubscriptionDiagnosticsDataType = new NodeId(Opc.Ua.DataTypes.SubscriptionDiagnosticsDataType);

        public static readonly NodeId ModelChangeStructureDataType = new NodeId(Opc.Ua.DataTypes.ModelChangeStructureDataType);

        public static readonly NodeId SemanticChangeStructureDataType = new NodeId(Opc.Ua.DataTypes.SemanticChangeStructureDataType);

        public static readonly NodeId Range = new NodeId(Opc.Ua.DataTypes.Range);

        public static readonly NodeId EUInformation = new NodeId(Opc.Ua.DataTypes.EUInformation);

        public static readonly NodeId ComplexNumberType = new NodeId(Opc.Ua.DataTypes.ComplexNumberType);

        public static readonly NodeId DoubleComplexNumberType = new NodeId(Opc.Ua.DataTypes.DoubleComplexNumberType);

        public static readonly NodeId AxisInformation = new NodeId(Opc.Ua.DataTypes.AxisInformation);

        public static readonly NodeId XVType = new NodeId(Opc.Ua.DataTypes.XVType);

        public static readonly NodeId ProgramDiagnosticDataType = new NodeId(Opc.Ua.DataTypes.ProgramDiagnosticDataType);

        public static readonly NodeId ProgramDiagnostic2DataType = new NodeId(Opc.Ua.DataTypes.ProgramDiagnostic2DataType);

        public static readonly NodeId Annotation = new NodeId(Opc.Ua.DataTypes.Annotation);
    }
    #endregion

    #region Object Node Identifiers
    /// <exclude />


    public static partial class ObjectIds
    {

        public static readonly NodeId ModellingRule_Mandatory = new NodeId(Opc.Ua.Objects.ModellingRule_Mandatory);

        public static readonly NodeId ModellingRule_Optional = new NodeId(Opc.Ua.Objects.ModellingRule_Optional);

        public static readonly NodeId ModellingRule_ExposesItsArray = new NodeId(Opc.Ua.Objects.ModellingRule_ExposesItsArray);

        public static readonly NodeId ModellingRule_OptionalPlaceholder = new NodeId(Opc.Ua.Objects.ModellingRule_OptionalPlaceholder);

        public static readonly NodeId ModellingRule_MandatoryPlaceholder = new NodeId(Opc.Ua.Objects.ModellingRule_MandatoryPlaceholder);

        public static readonly NodeId XmlSchema_TypeSystem = new NodeId(Opc.Ua.Objects.XmlSchema_TypeSystem);

        public static readonly NodeId OPCBinarySchema_TypeSystem = new NodeId(Opc.Ua.Objects.OPCBinarySchema_TypeSystem);

        public static readonly NodeId Union_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.Union_Encoding_DefaultBinary);

        public static readonly NodeId KeyValuePair_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.KeyValuePair_Encoding_DefaultBinary);

        public static readonly NodeId AdditionalParametersType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AdditionalParametersType_Encoding_DefaultBinary);

        public static readonly NodeId EphemeralKeyType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EphemeralKeyType_Encoding_DefaultBinary);

        public static readonly NodeId EndpointType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EndpointType_Encoding_DefaultBinary);

        public static readonly NodeId BitFieldDefinition_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BitFieldDefinition_Encoding_DefaultBinary);

        public static readonly NodeId RationalNumber_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RationalNumber_Encoding_DefaultBinary);

        public static readonly NodeId Vector_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.Vector_Encoding_DefaultBinary);

        public static readonly NodeId ThreeDVector_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ThreeDVector_Encoding_DefaultBinary);

        public static readonly NodeId CartesianCoordinates_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CartesianCoordinates_Encoding_DefaultBinary);

        public static readonly NodeId ThreeDCartesianCoordinates_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ThreeDCartesianCoordinates_Encoding_DefaultBinary);

        public static readonly NodeId Orientation_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.Orientation_Encoding_DefaultBinary);

        public static readonly NodeId ThreeDOrientation_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ThreeDOrientation_Encoding_DefaultBinary);

        public static readonly NodeId Frame_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.Frame_Encoding_DefaultBinary);

        public static readonly NodeId ThreeDFrame_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ThreeDFrame_Encoding_DefaultBinary);

        public static readonly NodeId IdentityMappingRuleType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.IdentityMappingRuleType_Encoding_DefaultBinary);

        public static readonly NodeId CurrencyUnitType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CurrencyUnitType_Encoding_DefaultBinary);

        public static readonly NodeId AnnotationDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AnnotationDataType_Encoding_DefaultBinary);

        public static readonly NodeId LinearConversionDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.LinearConversionDataType_Encoding_DefaultBinary);

        public static readonly NodeId QuantityDimension_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.QuantityDimension_Encoding_DefaultBinary);

        public static readonly NodeId TrustListDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TrustListDataType_Encoding_DefaultBinary);

        public static readonly NodeId BaseConfigurationDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BaseConfigurationDataType_Encoding_DefaultBinary);

        public static readonly NodeId BaseConfigurationRecordDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BaseConfigurationRecordDataType_Encoding_DefaultBinary);

        public static readonly NodeId CertificateGroupDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CertificateGroupDataType_Encoding_DefaultBinary);

        public static readonly NodeId ConfigurationUpdateTargetType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ConfigurationUpdateTargetType_Encoding_DefaultBinary);

        public static readonly NodeId TransactionErrorType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TransactionErrorType_Encoding_DefaultBinary);

        public static readonly NodeId ApplicationConfigurationDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ApplicationConfigurationDataType_Encoding_DefaultBinary);

        public static readonly NodeId ApplicationIdentityDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ApplicationIdentityDataType_Encoding_DefaultBinary);

        public static readonly NodeId EndpointDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EndpointDataType_Encoding_DefaultBinary);

        public static readonly NodeId ServerEndpointDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ServerEndpointDataType_Encoding_DefaultBinary);

        public static readonly NodeId SecuritySettingsDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SecuritySettingsDataType_Encoding_DefaultBinary);

        public static readonly NodeId UserTokenSettingsDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UserTokenSettingsDataType_Encoding_DefaultBinary);

        public static readonly NodeId ServiceCertificateDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ServiceCertificateDataType_Encoding_DefaultBinary);

        public static readonly NodeId AuthorizationServiceConfigurationDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AuthorizationServiceConfigurationDataType_Encoding_DefaultBinary);

        public static readonly NodeId DecimalDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DecimalDataType_Encoding_DefaultBinary);

        public static readonly NodeId DataTypeSchemaHeader_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataTypeSchemaHeader_Encoding_DefaultBinary);

        public static readonly NodeId DataTypeDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataTypeDescription_Encoding_DefaultBinary);

        public static readonly NodeId StructureDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.StructureDescription_Encoding_DefaultBinary);

        public static readonly NodeId EnumDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EnumDescription_Encoding_DefaultBinary);

        public static readonly NodeId SimpleTypeDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SimpleTypeDescription_Encoding_DefaultBinary);

        public static readonly NodeId UABinaryFileDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UABinaryFileDataType_Encoding_DefaultBinary);

        public static readonly NodeId PortableQualifiedName_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PortableQualifiedName_Encoding_DefaultBinary);

        public static readonly NodeId PortableNodeId_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PortableNodeId_Encoding_DefaultBinary);

        public static readonly NodeId UnsignedRationalNumber_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UnsignedRationalNumber_Encoding_DefaultBinary);

        public static readonly NodeId DataSetMetaDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataSetMetaDataType_Encoding_DefaultBinary);

        public static readonly NodeId FieldMetaData_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.FieldMetaData_Encoding_DefaultBinary);

        public static readonly NodeId ConfigurationVersionDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ConfigurationVersionDataType_Encoding_DefaultBinary);

        public static readonly NodeId PublishedDataSetDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PublishedDataSetDataType_Encoding_DefaultBinary);

        public static readonly NodeId PublishedDataSetSourceDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PublishedDataSetSourceDataType_Encoding_DefaultBinary);

        public static readonly NodeId PublishedVariableDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PublishedVariableDataType_Encoding_DefaultBinary);

        public static readonly NodeId PublishedDataItemsDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PublishedDataItemsDataType_Encoding_DefaultBinary);

        public static readonly NodeId PublishedEventsDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PublishedEventsDataType_Encoding_DefaultBinary);

        public static readonly NodeId PublishedDataSetCustomSourceDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PublishedDataSetCustomSourceDataType_Encoding_DefaultBinary);

        public static readonly NodeId ActionTargetDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ActionTargetDataType_Encoding_DefaultBinary);

        public static readonly NodeId PublishedActionDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PublishedActionDataType_Encoding_DefaultBinary);

        public static readonly NodeId ActionMethodDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ActionMethodDataType_Encoding_DefaultBinary);

        public static readonly NodeId PublishedActionMethodDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PublishedActionMethodDataType_Encoding_DefaultBinary);

        public static readonly NodeId DataSetWriterDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataSetWriterDataType_Encoding_DefaultBinary);

        public static readonly NodeId DataSetWriterTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataSetWriterTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId DataSetWriterMessageDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataSetWriterMessageDataType_Encoding_DefaultBinary);

        public static readonly NodeId PubSubGroupDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PubSubGroupDataType_Encoding_DefaultBinary);

        public static readonly NodeId WriterGroupDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.WriterGroupDataType_Encoding_DefaultBinary);

        public static readonly NodeId WriterGroupTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.WriterGroupTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId WriterGroupMessageDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.WriterGroupMessageDataType_Encoding_DefaultBinary);

        public static readonly NodeId PubSubConnectionDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PubSubConnectionDataType_Encoding_DefaultBinary);

        public static readonly NodeId ConnectionTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ConnectionTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId NetworkAddressDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.NetworkAddressDataType_Encoding_DefaultBinary);

        public static readonly NodeId NetworkAddressUrlDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.NetworkAddressUrlDataType_Encoding_DefaultBinary);

        public static readonly NodeId ReaderGroupDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReaderGroupDataType_Encoding_DefaultBinary);

        public static readonly NodeId ReaderGroupTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReaderGroupTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId ReaderGroupMessageDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReaderGroupMessageDataType_Encoding_DefaultBinary);

        public static readonly NodeId DataSetReaderDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataSetReaderDataType_Encoding_DefaultBinary);

        public static readonly NodeId DataSetReaderTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataSetReaderTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId DataSetReaderMessageDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataSetReaderMessageDataType_Encoding_DefaultBinary);

        public static readonly NodeId SubscribedDataSetDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SubscribedDataSetDataType_Encoding_DefaultBinary);

        public static readonly NodeId TargetVariablesDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TargetVariablesDataType_Encoding_DefaultBinary);

        public static readonly NodeId FieldTargetDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.FieldTargetDataType_Encoding_DefaultBinary);

        public static readonly NodeId SubscribedDataSetMirrorDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SubscribedDataSetMirrorDataType_Encoding_DefaultBinary);

        public static readonly NodeId PubSubConfigurationDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PubSubConfigurationDataType_Encoding_DefaultBinary);

        public static readonly NodeId StandaloneSubscribedDataSetRefDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.StandaloneSubscribedDataSetRefDataType_Encoding_DefaultBinary);

        public static readonly NodeId StandaloneSubscribedDataSetDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.StandaloneSubscribedDataSetDataType_Encoding_DefaultBinary);

        public static readonly NodeId SecurityGroupDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SecurityGroupDataType_Encoding_DefaultBinary);

        public static readonly NodeId PubSubKeyPushTargetDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PubSubKeyPushTargetDataType_Encoding_DefaultBinary);

        public static readonly NodeId PubSubConfiguration2DataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PubSubConfiguration2DataType_Encoding_DefaultBinary);

        public static readonly NodeId UadpWriterGroupMessageDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UadpWriterGroupMessageDataType_Encoding_DefaultBinary);

        public static readonly NodeId UadpDataSetWriterMessageDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UadpDataSetWriterMessageDataType_Encoding_DefaultBinary);

        public static readonly NodeId UadpDataSetReaderMessageDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UadpDataSetReaderMessageDataType_Encoding_DefaultBinary);

        public static readonly NodeId JsonWriterGroupMessageDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.JsonWriterGroupMessageDataType_Encoding_DefaultBinary);

        public static readonly NodeId JsonDataSetWriterMessageDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.JsonDataSetWriterMessageDataType_Encoding_DefaultBinary);

        public static readonly NodeId JsonDataSetReaderMessageDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.JsonDataSetReaderMessageDataType_Encoding_DefaultBinary);

        public static readonly NodeId QosDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.QosDataType_Encoding_DefaultBinary);

        public static readonly NodeId TransmitQosDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TransmitQosDataType_Encoding_DefaultBinary);

        public static readonly NodeId TransmitQosPriorityDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TransmitQosPriorityDataType_Encoding_DefaultBinary);

        public static readonly NodeId ReceiveQosDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReceiveQosDataType_Encoding_DefaultBinary);

        public static readonly NodeId ReceiveQosPriorityDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReceiveQosPriorityDataType_Encoding_DefaultBinary);

        public static readonly NodeId DatagramConnectionTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DatagramConnectionTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId DatagramConnectionTransport2DataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DatagramConnectionTransport2DataType_Encoding_DefaultBinary);

        public static readonly NodeId DatagramWriterGroupTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DatagramWriterGroupTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId DatagramWriterGroupTransport2DataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DatagramWriterGroupTransport2DataType_Encoding_DefaultBinary);

        public static readonly NodeId DatagramDataSetReaderTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DatagramDataSetReaderTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId DtlsPubSubConnectionDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DtlsPubSubConnectionDataType_Encoding_DefaultBinary);

        public static readonly NodeId BrokerConnectionTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrokerConnectionTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId BrokerWriterGroupTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrokerWriterGroupTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId BrokerDataSetWriterTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrokerDataSetWriterTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId BrokerDataSetReaderTransportDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrokerDataSetReaderTransportDataType_Encoding_DefaultBinary);

        public static readonly NodeId PubSubConfigurationRefDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PubSubConfigurationRefDataType_Encoding_DefaultBinary);

        public static readonly NodeId PubSubConfigurationValueDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PubSubConfigurationValueDataType_Encoding_DefaultBinary);

        public static readonly NodeId AliasNameDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AliasNameDataType_Encoding_DefaultBinary);

        public static readonly NodeId UserManagementDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UserManagementDataType_Encoding_DefaultBinary);

        public static readonly NodeId PriorityMappingEntryType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PriorityMappingEntryType_Encoding_DefaultBinary);

        public static readonly NodeId LldpManagementAddressTxPortType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.LldpManagementAddressTxPortType_Encoding_DefaultBinary);

        public static readonly NodeId LldpManagementAddressType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.LldpManagementAddressType_Encoding_DefaultBinary);

        public static readonly NodeId LldpTlvType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.LldpTlvType_Encoding_DefaultBinary);

        public static readonly NodeId ReferenceDescriptionDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReferenceDescriptionDataType_Encoding_DefaultBinary);

        public static readonly NodeId ReferenceListEntryDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReferenceListEntryDataType_Encoding_DefaultBinary);

        public static readonly NodeId LogRecord_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.LogRecord_Encoding_DefaultBinary);

        public static readonly NodeId LogRecordsDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.LogRecordsDataType_Encoding_DefaultBinary);

        public static readonly NodeId SpanContextDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SpanContextDataType_Encoding_DefaultBinary);

        public static readonly NodeId TraceContextDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TraceContextDataType_Encoding_DefaultBinary);

        public static readonly NodeId NameValuePair_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.NameValuePair_Encoding_DefaultBinary);

        public static readonly NodeId RolePermissionType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RolePermissionType_Encoding_DefaultBinary);

        public static readonly NodeId DataTypeDefinition_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataTypeDefinition_Encoding_DefaultBinary);

        public static readonly NodeId StructureField_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.StructureField_Encoding_DefaultBinary);

        public static readonly NodeId StructureDefinition_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.StructureDefinition_Encoding_DefaultBinary);

        public static readonly NodeId EnumDefinition_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EnumDefinition_Encoding_DefaultBinary);

        public static readonly NodeId Node_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.Node_Encoding_DefaultBinary);

        public static readonly NodeId InstanceNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.InstanceNode_Encoding_DefaultBinary);

        public static readonly NodeId TypeNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TypeNode_Encoding_DefaultBinary);

        public static readonly NodeId ObjectNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ObjectNode_Encoding_DefaultBinary);

        public static readonly NodeId ObjectTypeNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ObjectTypeNode_Encoding_DefaultBinary);

        public static readonly NodeId VariableNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.VariableNode_Encoding_DefaultBinary);

        public static readonly NodeId VariableTypeNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.VariableTypeNode_Encoding_DefaultBinary);

        public static readonly NodeId ReferenceTypeNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReferenceTypeNode_Encoding_DefaultBinary);

        public static readonly NodeId MethodNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MethodNode_Encoding_DefaultBinary);

        public static readonly NodeId ViewNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ViewNode_Encoding_DefaultBinary);

        public static readonly NodeId DataTypeNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataTypeNode_Encoding_DefaultBinary);

        public static readonly NodeId ReferenceNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReferenceNode_Encoding_DefaultBinary);

        public static readonly NodeId Argument_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.Argument_Encoding_DefaultBinary);

        public static readonly NodeId EnumValueType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EnumValueType_Encoding_DefaultBinary);

        public static readonly NodeId EnumField_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EnumField_Encoding_DefaultBinary);

        public static readonly NodeId OptionSet_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.OptionSet_Encoding_DefaultBinary);

        public static readonly NodeId TimeZoneDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TimeZoneDataType_Encoding_DefaultBinary);

        public static readonly NodeId ApplicationDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ApplicationDescription_Encoding_DefaultBinary);

        public static readonly NodeId RequestHeader_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RequestHeader_Encoding_DefaultBinary);

        public static readonly NodeId ResponseHeader_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ResponseHeader_Encoding_DefaultBinary);

        public static readonly NodeId ServiceFault_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ServiceFault_Encoding_DefaultBinary);

        public static readonly NodeId SessionlessInvokeRequestType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SessionlessInvokeRequestType_Encoding_DefaultBinary);

        public static readonly NodeId SessionlessInvokeResponseType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SessionlessInvokeResponseType_Encoding_DefaultBinary);

        public static readonly NodeId FindServersRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.FindServersRequest_Encoding_DefaultBinary);

        public static readonly NodeId FindServersResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.FindServersResponse_Encoding_DefaultBinary);

        public static readonly NodeId ServerOnNetwork_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ServerOnNetwork_Encoding_DefaultBinary);

        public static readonly NodeId FindServersOnNetworkRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.FindServersOnNetworkRequest_Encoding_DefaultBinary);

        public static readonly NodeId FindServersOnNetworkResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.FindServersOnNetworkResponse_Encoding_DefaultBinary);

        public static readonly NodeId UserTokenPolicy_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UserTokenPolicy_Encoding_DefaultBinary);

        public static readonly NodeId EndpointDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EndpointDescription_Encoding_DefaultBinary);

        public static readonly NodeId GetEndpointsRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.GetEndpointsRequest_Encoding_DefaultBinary);

        public static readonly NodeId GetEndpointsResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.GetEndpointsResponse_Encoding_DefaultBinary);

        public static readonly NodeId RegisteredServer_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RegisteredServer_Encoding_DefaultBinary);

        public static readonly NodeId RegisterServerRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RegisterServerRequest_Encoding_DefaultBinary);

        public static readonly NodeId RegisterServerResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RegisterServerResponse_Encoding_DefaultBinary);

        public static readonly NodeId DiscoveryConfiguration_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DiscoveryConfiguration_Encoding_DefaultBinary);

        public static readonly NodeId MdnsDiscoveryConfiguration_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MdnsDiscoveryConfiguration_Encoding_DefaultBinary);

        public static readonly NodeId RegisterServer2Request_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RegisterServer2Request_Encoding_DefaultBinary);

        public static readonly NodeId RegisterServer2Response_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RegisterServer2Response_Encoding_DefaultBinary);

        public static readonly NodeId ChannelSecurityToken_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ChannelSecurityToken_Encoding_DefaultBinary);

        public static readonly NodeId OpenSecureChannelRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.OpenSecureChannelRequest_Encoding_DefaultBinary);

        public static readonly NodeId OpenSecureChannelResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.OpenSecureChannelResponse_Encoding_DefaultBinary);

        public static readonly NodeId CloseSecureChannelRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CloseSecureChannelRequest_Encoding_DefaultBinary);

        public static readonly NodeId CloseSecureChannelResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CloseSecureChannelResponse_Encoding_DefaultBinary);

        public static readonly NodeId SignedSoftwareCertificate_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SignedSoftwareCertificate_Encoding_DefaultBinary);

        public static readonly NodeId SignatureData_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SignatureData_Encoding_DefaultBinary);

        public static readonly NodeId CreateSessionRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CreateSessionRequest_Encoding_DefaultBinary);

        public static readonly NodeId CreateSessionResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CreateSessionResponse_Encoding_DefaultBinary);

        public static readonly NodeId UserIdentityToken_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UserIdentityToken_Encoding_DefaultBinary);

        public static readonly NodeId AnonymousIdentityToken_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AnonymousIdentityToken_Encoding_DefaultBinary);

        public static readonly NodeId UserNameIdentityToken_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UserNameIdentityToken_Encoding_DefaultBinary);

        public static readonly NodeId X509IdentityToken_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.X509IdentityToken_Encoding_DefaultBinary);

        public static readonly NodeId IssuedIdentityToken_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.IssuedIdentityToken_Encoding_DefaultBinary);

        public static readonly NodeId ActivateSessionRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ActivateSessionRequest_Encoding_DefaultBinary);

        public static readonly NodeId ActivateSessionResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ActivateSessionResponse_Encoding_DefaultBinary);

        public static readonly NodeId CloseSessionRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CloseSessionRequest_Encoding_DefaultBinary);

        public static readonly NodeId CloseSessionResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CloseSessionResponse_Encoding_DefaultBinary);

        public static readonly NodeId CancelRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CancelRequest_Encoding_DefaultBinary);

        public static readonly NodeId CancelResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CancelResponse_Encoding_DefaultBinary);

        public static readonly NodeId NodeAttributes_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.NodeAttributes_Encoding_DefaultBinary);

        public static readonly NodeId ObjectAttributes_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ObjectAttributes_Encoding_DefaultBinary);

        public static readonly NodeId VariableAttributes_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.VariableAttributes_Encoding_DefaultBinary);

        public static readonly NodeId MethodAttributes_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MethodAttributes_Encoding_DefaultBinary);

        public static readonly NodeId ObjectTypeAttributes_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ObjectTypeAttributes_Encoding_DefaultBinary);

        public static readonly NodeId VariableTypeAttributes_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.VariableTypeAttributes_Encoding_DefaultBinary);

        public static readonly NodeId ReferenceTypeAttributes_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReferenceTypeAttributes_Encoding_DefaultBinary);

        public static readonly NodeId DataTypeAttributes_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataTypeAttributes_Encoding_DefaultBinary);

        public static readonly NodeId ViewAttributes_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ViewAttributes_Encoding_DefaultBinary);

        public static readonly NodeId GenericAttributeValue_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.GenericAttributeValue_Encoding_DefaultBinary);

        public static readonly NodeId GenericAttributes_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.GenericAttributes_Encoding_DefaultBinary);

        public static readonly NodeId AddNodesItem_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AddNodesItem_Encoding_DefaultBinary);

        public static readonly NodeId AddNodesResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AddNodesResult_Encoding_DefaultBinary);

        public static readonly NodeId AddNodesRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AddNodesRequest_Encoding_DefaultBinary);

        public static readonly NodeId AddNodesResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AddNodesResponse_Encoding_DefaultBinary);

        public static readonly NodeId AddReferencesItem_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AddReferencesItem_Encoding_DefaultBinary);

        public static readonly NodeId AddReferencesRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AddReferencesRequest_Encoding_DefaultBinary);

        public static readonly NodeId AddReferencesResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AddReferencesResponse_Encoding_DefaultBinary);

        public static readonly NodeId DeleteNodesItem_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteNodesItem_Encoding_DefaultBinary);

        public static readonly NodeId DeleteNodesRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteNodesRequest_Encoding_DefaultBinary);

        public static readonly NodeId DeleteNodesResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteNodesResponse_Encoding_DefaultBinary);

        public static readonly NodeId DeleteReferencesItem_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteReferencesItem_Encoding_DefaultBinary);

        public static readonly NodeId DeleteReferencesRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteReferencesRequest_Encoding_DefaultBinary);

        public static readonly NodeId DeleteReferencesResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteReferencesResponse_Encoding_DefaultBinary);

        public static readonly NodeId ViewDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ViewDescription_Encoding_DefaultBinary);

        public static readonly NodeId BrowseDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrowseDescription_Encoding_DefaultBinary);

        public static readonly NodeId ReferenceDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReferenceDescription_Encoding_DefaultBinary);

        public static readonly NodeId BrowseResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrowseResult_Encoding_DefaultBinary);

        public static readonly NodeId BrowseRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrowseRequest_Encoding_DefaultBinary);

        public static readonly NodeId BrowseResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrowseResponse_Encoding_DefaultBinary);

        public static readonly NodeId BrowseNextRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrowseNextRequest_Encoding_DefaultBinary);

        public static readonly NodeId BrowseNextResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrowseNextResponse_Encoding_DefaultBinary);

        public static readonly NodeId RelativePathElement_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RelativePathElement_Encoding_DefaultBinary);

        public static readonly NodeId RelativePath_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RelativePath_Encoding_DefaultBinary);

        public static readonly NodeId BrowsePath_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrowsePath_Encoding_DefaultBinary);

        public static readonly NodeId BrowsePathTarget_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrowsePathTarget_Encoding_DefaultBinary);

        public static readonly NodeId BrowsePathResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrowsePathResult_Encoding_DefaultBinary);

        public static readonly NodeId TranslateBrowsePathsToNodeIdsRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TranslateBrowsePathsToNodeIdsRequest_Encoding_DefaultBinary);

        public static readonly NodeId TranslateBrowsePathsToNodeIdsResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TranslateBrowsePathsToNodeIdsResponse_Encoding_DefaultBinary);

        public static readonly NodeId RegisterNodesRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RegisterNodesRequest_Encoding_DefaultBinary);

        public static readonly NodeId RegisterNodesResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RegisterNodesResponse_Encoding_DefaultBinary);

        public static readonly NodeId UnregisterNodesRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UnregisterNodesRequest_Encoding_DefaultBinary);

        public static readonly NodeId UnregisterNodesResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UnregisterNodesResponse_Encoding_DefaultBinary);

        public static readonly NodeId EndpointConfiguration_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EndpointConfiguration_Encoding_DefaultBinary);

        public static readonly NodeId QueryDataDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.QueryDataDescription_Encoding_DefaultBinary);

        public static readonly NodeId NodeTypeDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.NodeTypeDescription_Encoding_DefaultBinary);

        public static readonly NodeId QueryDataSet_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.QueryDataSet_Encoding_DefaultBinary);

        public static readonly NodeId NodeReference_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.NodeReference_Encoding_DefaultBinary);

        public static readonly NodeId ContentFilterElement_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ContentFilterElement_Encoding_DefaultBinary);

        public static readonly NodeId ContentFilter_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ContentFilter_Encoding_DefaultBinary);

        public static readonly NodeId FilterOperand_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.FilterOperand_Encoding_DefaultBinary);

        public static readonly NodeId ElementOperand_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ElementOperand_Encoding_DefaultBinary);

        public static readonly NodeId LiteralOperand_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.LiteralOperand_Encoding_DefaultBinary);

        public static readonly NodeId AttributeOperand_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AttributeOperand_Encoding_DefaultBinary);

        public static readonly NodeId SimpleAttributeOperand_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SimpleAttributeOperand_Encoding_DefaultBinary);

        public static readonly NodeId ContentFilterElementResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ContentFilterElementResult_Encoding_DefaultBinary);

        public static readonly NodeId ContentFilterResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ContentFilterResult_Encoding_DefaultBinary);

        public static readonly NodeId ParsingResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ParsingResult_Encoding_DefaultBinary);

        public static readonly NodeId QueryFirstRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.QueryFirstRequest_Encoding_DefaultBinary);

        public static readonly NodeId QueryFirstResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.QueryFirstResponse_Encoding_DefaultBinary);

        public static readonly NodeId QueryNextRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.QueryNextRequest_Encoding_DefaultBinary);

        public static readonly NodeId QueryNextResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.QueryNextResponse_Encoding_DefaultBinary);

        public static readonly NodeId ReadValueId_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReadValueId_Encoding_DefaultBinary);

        public static readonly NodeId ReadRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReadRequest_Encoding_DefaultBinary);

        public static readonly NodeId ReadResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReadResponse_Encoding_DefaultBinary);

        public static readonly NodeId HistoryReadValueId_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryReadValueId_Encoding_DefaultBinary);

        public static readonly NodeId HistoryReadResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryReadResult_Encoding_DefaultBinary);

        public static readonly NodeId HistoryReadDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryReadDetails_Encoding_DefaultBinary);

        public static readonly NodeId ReadEventDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReadEventDetails_Encoding_DefaultBinary);

        public static readonly NodeId ReadEventDetails2_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReadEventDetails2_Encoding_DefaultBinary);

        public static readonly NodeId SortRuleElement_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SortRuleElement_Encoding_DefaultBinary);

        public static readonly NodeId ReadEventDetailsSorted_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReadEventDetailsSorted_Encoding_DefaultBinary);

        public static readonly NodeId ReadRawModifiedDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReadRawModifiedDetails_Encoding_DefaultBinary);

        public static readonly NodeId ReadProcessedDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReadProcessedDetails_Encoding_DefaultBinary);

        public static readonly NodeId ReadAtTimeDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReadAtTimeDetails_Encoding_DefaultBinary);

        public static readonly NodeId ReadAnnotationDataDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReadAnnotationDataDetails_Encoding_DefaultBinary);

        public static readonly NodeId HistoryData_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryData_Encoding_DefaultBinary);

        public static readonly NodeId ModificationInfo_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ModificationInfo_Encoding_DefaultBinary);

        public static readonly NodeId HistoryModifiedData_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryModifiedData_Encoding_DefaultBinary);

        public static readonly NodeId HistoryEvent_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryEvent_Encoding_DefaultBinary);

        public static readonly NodeId HistoryModifiedEvent_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryModifiedEvent_Encoding_DefaultBinary);

        public static readonly NodeId HistoryReadRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryReadRequest_Encoding_DefaultBinary);

        public static readonly NodeId HistoryReadResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryReadResponse_Encoding_DefaultBinary);

        public static readonly NodeId WriteValue_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.WriteValue_Encoding_DefaultBinary);

        public static readonly NodeId WriteRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.WriteRequest_Encoding_DefaultBinary);

        public static readonly NodeId WriteResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.WriteResponse_Encoding_DefaultBinary);

        public static readonly NodeId HistoryUpdateDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryUpdateDetails_Encoding_DefaultBinary);

        public static readonly NodeId UpdateDataDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UpdateDataDetails_Encoding_DefaultBinary);

        public static readonly NodeId UpdateStructureDataDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UpdateStructureDataDetails_Encoding_DefaultBinary);

        public static readonly NodeId UpdateEventDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.UpdateEventDetails_Encoding_DefaultBinary);

        public static readonly NodeId DeleteRawModifiedDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteRawModifiedDetails_Encoding_DefaultBinary);

        public static readonly NodeId DeleteAtTimeDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteAtTimeDetails_Encoding_DefaultBinary);

        public static readonly NodeId DeleteEventDetails_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteEventDetails_Encoding_DefaultBinary);

        public static readonly NodeId HistoryUpdateResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryUpdateResult_Encoding_DefaultBinary);

        public static readonly NodeId HistoryUpdateRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryUpdateRequest_Encoding_DefaultBinary);

        public static readonly NodeId HistoryUpdateResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryUpdateResponse_Encoding_DefaultBinary);

        public static readonly NodeId CallMethodRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CallMethodRequest_Encoding_DefaultBinary);

        public static readonly NodeId CallMethodResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CallMethodResult_Encoding_DefaultBinary);

        public static readonly NodeId CallRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CallRequest_Encoding_DefaultBinary);

        public static readonly NodeId CallResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CallResponse_Encoding_DefaultBinary);

        public static readonly NodeId MonitoringFilter_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MonitoringFilter_Encoding_DefaultBinary);

        public static readonly NodeId DataChangeFilter_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataChangeFilter_Encoding_DefaultBinary);

        public static readonly NodeId EventFilter_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EventFilter_Encoding_DefaultBinary);

        public static readonly NodeId AggregateConfiguration_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AggregateConfiguration_Encoding_DefaultBinary);

        public static readonly NodeId AggregateFilter_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AggregateFilter_Encoding_DefaultBinary);

        public static readonly NodeId MonitoringFilterResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MonitoringFilterResult_Encoding_DefaultBinary);

        public static readonly NodeId EventFilterResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EventFilterResult_Encoding_DefaultBinary);

        public static readonly NodeId AggregateFilterResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AggregateFilterResult_Encoding_DefaultBinary);

        public static readonly NodeId MonitoringParameters_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MonitoringParameters_Encoding_DefaultBinary);

        public static readonly NodeId MonitoredItemCreateRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MonitoredItemCreateRequest_Encoding_DefaultBinary);

        public static readonly NodeId MonitoredItemCreateResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MonitoredItemCreateResult_Encoding_DefaultBinary);

        public static readonly NodeId CreateMonitoredItemsRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CreateMonitoredItemsRequest_Encoding_DefaultBinary);

        public static readonly NodeId CreateMonitoredItemsResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CreateMonitoredItemsResponse_Encoding_DefaultBinary);

        public static readonly NodeId MonitoredItemModifyRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MonitoredItemModifyRequest_Encoding_DefaultBinary);

        public static readonly NodeId MonitoredItemModifyResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MonitoredItemModifyResult_Encoding_DefaultBinary);

        public static readonly NodeId ModifyMonitoredItemsRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ModifyMonitoredItemsRequest_Encoding_DefaultBinary);

        public static readonly NodeId ModifyMonitoredItemsResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ModifyMonitoredItemsResponse_Encoding_DefaultBinary);

        public static readonly NodeId SetMonitoringModeRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SetMonitoringModeRequest_Encoding_DefaultBinary);

        public static readonly NodeId SetMonitoringModeResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SetMonitoringModeResponse_Encoding_DefaultBinary);

        public static readonly NodeId SetTriggeringRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SetTriggeringRequest_Encoding_DefaultBinary);

        public static readonly NodeId SetTriggeringResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SetTriggeringResponse_Encoding_DefaultBinary);

        public static readonly NodeId DeleteMonitoredItemsRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteMonitoredItemsRequest_Encoding_DefaultBinary);

        public static readonly NodeId DeleteMonitoredItemsResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteMonitoredItemsResponse_Encoding_DefaultBinary);

        public static readonly NodeId CreateSubscriptionRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CreateSubscriptionRequest_Encoding_DefaultBinary);

        public static readonly NodeId CreateSubscriptionResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.CreateSubscriptionResponse_Encoding_DefaultBinary);

        public static readonly NodeId ModifySubscriptionRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ModifySubscriptionRequest_Encoding_DefaultBinary);

        public static readonly NodeId ModifySubscriptionResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ModifySubscriptionResponse_Encoding_DefaultBinary);

        public static readonly NodeId SetPublishingModeRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SetPublishingModeRequest_Encoding_DefaultBinary);

        public static readonly NodeId SetPublishingModeResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SetPublishingModeResponse_Encoding_DefaultBinary);

        public static readonly NodeId NotificationMessage_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.NotificationMessage_Encoding_DefaultBinary);

        public static readonly NodeId NotificationData_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.NotificationData_Encoding_DefaultBinary);

        public static readonly NodeId DataChangeNotification_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataChangeNotification_Encoding_DefaultBinary);

        public static readonly NodeId MonitoredItemNotification_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MonitoredItemNotification_Encoding_DefaultBinary);

        public static readonly NodeId EventNotificationList_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EventNotificationList_Encoding_DefaultBinary);

        public static readonly NodeId EventFieldList_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EventFieldList_Encoding_DefaultBinary);

        public static readonly NodeId HistoryEventFieldList_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.HistoryEventFieldList_Encoding_DefaultBinary);

        public static readonly NodeId StatusChangeNotification_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.StatusChangeNotification_Encoding_DefaultBinary);

        public static readonly NodeId SubscriptionAcknowledgement_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SubscriptionAcknowledgement_Encoding_DefaultBinary);

        public static readonly NodeId PublishRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PublishRequest_Encoding_DefaultBinary);

        public static readonly NodeId PublishResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.PublishResponse_Encoding_DefaultBinary);

        public static readonly NodeId RepublishRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RepublishRequest_Encoding_DefaultBinary);

        public static readonly NodeId RepublishResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RepublishResponse_Encoding_DefaultBinary);

        public static readonly NodeId TransferResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TransferResult_Encoding_DefaultBinary);

        public static readonly NodeId TransferSubscriptionsRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TransferSubscriptionsRequest_Encoding_DefaultBinary);

        public static readonly NodeId TransferSubscriptionsResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TransferSubscriptionsResponse_Encoding_DefaultBinary);

        public static readonly NodeId DeleteSubscriptionsRequest_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteSubscriptionsRequest_Encoding_DefaultBinary);

        public static readonly NodeId DeleteSubscriptionsResponse_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DeleteSubscriptionsResponse_Encoding_DefaultBinary);

        public static readonly NodeId BuildInfo_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BuildInfo_Encoding_DefaultBinary);

        public static readonly NodeId RedundantServerDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RedundantServerDataType_Encoding_DefaultBinary);

        public static readonly NodeId EndpointUrlListDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EndpointUrlListDataType_Encoding_DefaultBinary);

        public static readonly NodeId NetworkGroupDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.NetworkGroupDataType_Encoding_DefaultBinary);

        public static readonly NodeId SamplingIntervalDiagnosticsDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SamplingIntervalDiagnosticsDataType_Encoding_DefaultBinary);

        public static readonly NodeId ServerDiagnosticsSummaryDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ServerDiagnosticsSummaryDataType_Encoding_DefaultBinary);

        public static readonly NodeId ServerStatusDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ServerStatusDataType_Encoding_DefaultBinary);

        public static readonly NodeId SessionDiagnosticsDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SessionDiagnosticsDataType_Encoding_DefaultBinary);

        public static readonly NodeId SessionSecurityDiagnosticsDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SessionSecurityDiagnosticsDataType_Encoding_DefaultBinary);

        public static readonly NodeId ServiceCounterDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ServiceCounterDataType_Encoding_DefaultBinary);

        public static readonly NodeId StatusResult_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.StatusResult_Encoding_DefaultBinary);

        public static readonly NodeId SubscriptionDiagnosticsDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SubscriptionDiagnosticsDataType_Encoding_DefaultBinary);

        public static readonly NodeId ModelChangeStructureDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ModelChangeStructureDataType_Encoding_DefaultBinary);

        public static readonly NodeId SemanticChangeStructureDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.SemanticChangeStructureDataType_Encoding_DefaultBinary);

        public static readonly NodeId Range_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.Range_Encoding_DefaultBinary);

        public static readonly NodeId EUInformation_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EUInformation_Encoding_DefaultBinary);

        public static readonly NodeId ComplexNumberType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ComplexNumberType_Encoding_DefaultBinary);

        public static readonly NodeId DoubleComplexNumberType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DoubleComplexNumberType_Encoding_DefaultBinary);

        public static readonly NodeId AxisInformation_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.AxisInformation_Encoding_DefaultBinary);

        public static readonly NodeId XVType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.XVType_Encoding_DefaultBinary);

        public static readonly NodeId ProgramDiagnosticDataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ProgramDiagnosticDataType_Encoding_DefaultBinary);

        public static readonly NodeId ProgramDiagnostic2DataType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ProgramDiagnostic2DataType_Encoding_DefaultBinary);

        public static readonly NodeId Annotation_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.Annotation_Encoding_DefaultBinary);

        public static readonly NodeId Union_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.Union_Encoding_DefaultXml);

        public static readonly NodeId KeyValuePair_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.KeyValuePair_Encoding_DefaultXml);

        public static readonly NodeId AdditionalParametersType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AdditionalParametersType_Encoding_DefaultXml);

        public static readonly NodeId EphemeralKeyType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EphemeralKeyType_Encoding_DefaultXml);

        public static readonly NodeId EndpointType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EndpointType_Encoding_DefaultXml);

        public static readonly NodeId BitFieldDefinition_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BitFieldDefinition_Encoding_DefaultXml);

        public static readonly NodeId RationalNumber_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RationalNumber_Encoding_DefaultXml);

        public static readonly NodeId Vector_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.Vector_Encoding_DefaultXml);

        public static readonly NodeId ThreeDVector_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ThreeDVector_Encoding_DefaultXml);

        public static readonly NodeId CartesianCoordinates_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CartesianCoordinates_Encoding_DefaultXml);

        public static readonly NodeId ThreeDCartesianCoordinates_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ThreeDCartesianCoordinates_Encoding_DefaultXml);

        public static readonly NodeId Orientation_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.Orientation_Encoding_DefaultXml);

        public static readonly NodeId ThreeDOrientation_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ThreeDOrientation_Encoding_DefaultXml);

        public static readonly NodeId Frame_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.Frame_Encoding_DefaultXml);

        public static readonly NodeId ThreeDFrame_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ThreeDFrame_Encoding_DefaultXml);

        public static readonly NodeId IdentityMappingRuleType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.IdentityMappingRuleType_Encoding_DefaultXml);

        public static readonly NodeId CurrencyUnitType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CurrencyUnitType_Encoding_DefaultXml);

        public static readonly NodeId AnnotationDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AnnotationDataType_Encoding_DefaultXml);

        public static readonly NodeId LinearConversionDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.LinearConversionDataType_Encoding_DefaultXml);

        public static readonly NodeId QuantityDimension_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.QuantityDimension_Encoding_DefaultXml);

        public static readonly NodeId TrustListDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TrustListDataType_Encoding_DefaultXml);

        public static readonly NodeId BaseConfigurationDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BaseConfigurationDataType_Encoding_DefaultXml);

        public static readonly NodeId BaseConfigurationRecordDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BaseConfigurationRecordDataType_Encoding_DefaultXml);

        public static readonly NodeId CertificateGroupDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CertificateGroupDataType_Encoding_DefaultXml);

        public static readonly NodeId ConfigurationUpdateTargetType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ConfigurationUpdateTargetType_Encoding_DefaultXml);

        public static readonly NodeId TransactionErrorType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TransactionErrorType_Encoding_DefaultXml);

        public static readonly NodeId ApplicationConfigurationDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ApplicationConfigurationDataType_Encoding_DefaultXml);

        public static readonly NodeId ApplicationIdentityDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ApplicationIdentityDataType_Encoding_DefaultXml);

        public static readonly NodeId EndpointDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EndpointDataType_Encoding_DefaultXml);

        public static readonly NodeId ServerEndpointDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ServerEndpointDataType_Encoding_DefaultXml);

        public static readonly NodeId SecuritySettingsDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SecuritySettingsDataType_Encoding_DefaultXml);

        public static readonly NodeId UserTokenSettingsDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UserTokenSettingsDataType_Encoding_DefaultXml);

        public static readonly NodeId ServiceCertificateDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ServiceCertificateDataType_Encoding_DefaultXml);

        public static readonly NodeId AuthorizationServiceConfigurationDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AuthorizationServiceConfigurationDataType_Encoding_DefaultXml);

        public static readonly NodeId DecimalDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DecimalDataType_Encoding_DefaultXml);

        public static readonly NodeId DataTypeSchemaHeader_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataTypeSchemaHeader_Encoding_DefaultXml);

        public static readonly NodeId DataTypeDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataTypeDescription_Encoding_DefaultXml);

        public static readonly NodeId StructureDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.StructureDescription_Encoding_DefaultXml);

        public static readonly NodeId EnumDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EnumDescription_Encoding_DefaultXml);

        public static readonly NodeId SimpleTypeDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SimpleTypeDescription_Encoding_DefaultXml);

        public static readonly NodeId UABinaryFileDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UABinaryFileDataType_Encoding_DefaultXml);

        public static readonly NodeId PortableQualifiedName_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PortableQualifiedName_Encoding_DefaultXml);

        public static readonly NodeId PortableNodeId_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PortableNodeId_Encoding_DefaultXml);

        public static readonly NodeId UnsignedRationalNumber_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UnsignedRationalNumber_Encoding_DefaultXml);

        public static readonly NodeId DataSetMetaDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataSetMetaDataType_Encoding_DefaultXml);

        public static readonly NodeId FieldMetaData_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.FieldMetaData_Encoding_DefaultXml);

        public static readonly NodeId ConfigurationVersionDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ConfigurationVersionDataType_Encoding_DefaultXml);

        public static readonly NodeId PublishedDataSetDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PublishedDataSetDataType_Encoding_DefaultXml);

        public static readonly NodeId PublishedDataSetSourceDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PublishedDataSetSourceDataType_Encoding_DefaultXml);

        public static readonly NodeId PublishedVariableDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PublishedVariableDataType_Encoding_DefaultXml);

        public static readonly NodeId PublishedDataItemsDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PublishedDataItemsDataType_Encoding_DefaultXml);

        public static readonly NodeId PublishedEventsDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PublishedEventsDataType_Encoding_DefaultXml);

        public static readonly NodeId PublishedDataSetCustomSourceDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PublishedDataSetCustomSourceDataType_Encoding_DefaultXml);

        public static readonly NodeId ActionTargetDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ActionTargetDataType_Encoding_DefaultXml);

        public static readonly NodeId PublishedActionDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PublishedActionDataType_Encoding_DefaultXml);

        public static readonly NodeId ActionMethodDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ActionMethodDataType_Encoding_DefaultXml);

        public static readonly NodeId PublishedActionMethodDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PublishedActionMethodDataType_Encoding_DefaultXml);

        public static readonly NodeId DataSetWriterDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataSetWriterDataType_Encoding_DefaultXml);

        public static readonly NodeId DataSetWriterTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataSetWriterTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId DataSetWriterMessageDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataSetWriterMessageDataType_Encoding_DefaultXml);

        public static readonly NodeId PubSubGroupDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PubSubGroupDataType_Encoding_DefaultXml);

        public static readonly NodeId WriterGroupDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.WriterGroupDataType_Encoding_DefaultXml);

        public static readonly NodeId WriterGroupTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.WriterGroupTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId WriterGroupMessageDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.WriterGroupMessageDataType_Encoding_DefaultXml);

        public static readonly NodeId PubSubConnectionDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PubSubConnectionDataType_Encoding_DefaultXml);

        public static readonly NodeId ConnectionTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ConnectionTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId NetworkAddressDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.NetworkAddressDataType_Encoding_DefaultXml);

        public static readonly NodeId NetworkAddressUrlDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.NetworkAddressUrlDataType_Encoding_DefaultXml);

        public static readonly NodeId ReaderGroupDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReaderGroupDataType_Encoding_DefaultXml);

        public static readonly NodeId ReaderGroupTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReaderGroupTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId ReaderGroupMessageDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReaderGroupMessageDataType_Encoding_DefaultXml);

        public static readonly NodeId DataSetReaderDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataSetReaderDataType_Encoding_DefaultXml);

        public static readonly NodeId DataSetReaderTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataSetReaderTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId DataSetReaderMessageDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataSetReaderMessageDataType_Encoding_DefaultXml);

        public static readonly NodeId SubscribedDataSetDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SubscribedDataSetDataType_Encoding_DefaultXml);

        public static readonly NodeId TargetVariablesDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TargetVariablesDataType_Encoding_DefaultXml);

        public static readonly NodeId FieldTargetDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.FieldTargetDataType_Encoding_DefaultXml);

        public static readonly NodeId SubscribedDataSetMirrorDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SubscribedDataSetMirrorDataType_Encoding_DefaultXml);

        public static readonly NodeId PubSubConfigurationDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PubSubConfigurationDataType_Encoding_DefaultXml);

        public static readonly NodeId StandaloneSubscribedDataSetRefDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.StandaloneSubscribedDataSetRefDataType_Encoding_DefaultXml);

        public static readonly NodeId StandaloneSubscribedDataSetDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.StandaloneSubscribedDataSetDataType_Encoding_DefaultXml);

        public static readonly NodeId SecurityGroupDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SecurityGroupDataType_Encoding_DefaultXml);

        public static readonly NodeId PubSubKeyPushTargetDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PubSubKeyPushTargetDataType_Encoding_DefaultXml);

        public static readonly NodeId PubSubConfiguration2DataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PubSubConfiguration2DataType_Encoding_DefaultXml);

        public static readonly NodeId UadpWriterGroupMessageDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UadpWriterGroupMessageDataType_Encoding_DefaultXml);

        public static readonly NodeId UadpDataSetWriterMessageDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UadpDataSetWriterMessageDataType_Encoding_DefaultXml);

        public static readonly NodeId UadpDataSetReaderMessageDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UadpDataSetReaderMessageDataType_Encoding_DefaultXml);

        public static readonly NodeId JsonWriterGroupMessageDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.JsonWriterGroupMessageDataType_Encoding_DefaultXml);

        public static readonly NodeId JsonDataSetWriterMessageDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.JsonDataSetWriterMessageDataType_Encoding_DefaultXml);

        public static readonly NodeId JsonDataSetReaderMessageDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.JsonDataSetReaderMessageDataType_Encoding_DefaultXml);

        public static readonly NodeId QosDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.QosDataType_Encoding_DefaultXml);

        public static readonly NodeId TransmitQosDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TransmitQosDataType_Encoding_DefaultXml);

        public static readonly NodeId TransmitQosPriorityDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TransmitQosPriorityDataType_Encoding_DefaultXml);

        public static readonly NodeId ReceiveQosDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReceiveQosDataType_Encoding_DefaultXml);

        public static readonly NodeId ReceiveQosPriorityDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReceiveQosPriorityDataType_Encoding_DefaultXml);

        public static readonly NodeId DatagramConnectionTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DatagramConnectionTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId DatagramConnectionTransport2DataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DatagramConnectionTransport2DataType_Encoding_DefaultXml);

        public static readonly NodeId DatagramWriterGroupTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DatagramWriterGroupTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId DatagramWriterGroupTransport2DataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DatagramWriterGroupTransport2DataType_Encoding_DefaultXml);

        public static readonly NodeId DatagramDataSetReaderTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DatagramDataSetReaderTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId DtlsPubSubConnectionDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DtlsPubSubConnectionDataType_Encoding_DefaultXml);

        public static readonly NodeId BrokerConnectionTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrokerConnectionTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId BrokerWriterGroupTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrokerWriterGroupTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId BrokerDataSetWriterTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrokerDataSetWriterTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId BrokerDataSetReaderTransportDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrokerDataSetReaderTransportDataType_Encoding_DefaultXml);

        public static readonly NodeId PubSubConfigurationRefDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PubSubConfigurationRefDataType_Encoding_DefaultXml);

        public static readonly NodeId PubSubConfigurationValueDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PubSubConfigurationValueDataType_Encoding_DefaultXml);

        public static readonly NodeId AliasNameDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AliasNameDataType_Encoding_DefaultXml);

        public static readonly NodeId UserManagementDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UserManagementDataType_Encoding_DefaultXml);

        public static readonly NodeId PriorityMappingEntryType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PriorityMappingEntryType_Encoding_DefaultXml);

        public static readonly NodeId LldpManagementAddressTxPortType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.LldpManagementAddressTxPortType_Encoding_DefaultXml);

        public static readonly NodeId LldpManagementAddressType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.LldpManagementAddressType_Encoding_DefaultXml);

        public static readonly NodeId LldpTlvType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.LldpTlvType_Encoding_DefaultXml);

        public static readonly NodeId ReferenceDescriptionDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReferenceDescriptionDataType_Encoding_DefaultXml);

        public static readonly NodeId ReferenceListEntryDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReferenceListEntryDataType_Encoding_DefaultXml);

        public static readonly NodeId LogRecord_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.LogRecord_Encoding_DefaultXml);

        public static readonly NodeId LogRecordsDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.LogRecordsDataType_Encoding_DefaultXml);

        public static readonly NodeId SpanContextDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SpanContextDataType_Encoding_DefaultXml);

        public static readonly NodeId TraceContextDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TraceContextDataType_Encoding_DefaultXml);

        public static readonly NodeId NameValuePair_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.NameValuePair_Encoding_DefaultXml);

        public static readonly NodeId RolePermissionType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RolePermissionType_Encoding_DefaultXml);

        public static readonly NodeId DataTypeDefinition_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataTypeDefinition_Encoding_DefaultXml);

        public static readonly NodeId StructureField_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.StructureField_Encoding_DefaultXml);

        public static readonly NodeId StructureDefinition_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.StructureDefinition_Encoding_DefaultXml);

        public static readonly NodeId EnumDefinition_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EnumDefinition_Encoding_DefaultXml);

        public static readonly NodeId Node_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.Node_Encoding_DefaultXml);

        public static readonly NodeId InstanceNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.InstanceNode_Encoding_DefaultXml);

        public static readonly NodeId TypeNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TypeNode_Encoding_DefaultXml);

        public static readonly NodeId ObjectNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ObjectNode_Encoding_DefaultXml);

        public static readonly NodeId ObjectTypeNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ObjectTypeNode_Encoding_DefaultXml);

        public static readonly NodeId VariableNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.VariableNode_Encoding_DefaultXml);

        public static readonly NodeId VariableTypeNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.VariableTypeNode_Encoding_DefaultXml);

        public static readonly NodeId ReferenceTypeNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReferenceTypeNode_Encoding_DefaultXml);

        public static readonly NodeId MethodNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MethodNode_Encoding_DefaultXml);

        public static readonly NodeId ViewNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ViewNode_Encoding_DefaultXml);

        public static readonly NodeId DataTypeNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataTypeNode_Encoding_DefaultXml);

        public static readonly NodeId ReferenceNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReferenceNode_Encoding_DefaultXml);

        public static readonly NodeId Argument_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.Argument_Encoding_DefaultXml);

        public static readonly NodeId EnumValueType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EnumValueType_Encoding_DefaultXml);

        public static readonly NodeId EnumField_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EnumField_Encoding_DefaultXml);

        public static readonly NodeId OptionSet_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.OptionSet_Encoding_DefaultXml);

        public static readonly NodeId TimeZoneDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TimeZoneDataType_Encoding_DefaultXml);

        public static readonly NodeId ApplicationDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ApplicationDescription_Encoding_DefaultXml);

        public static readonly NodeId RequestHeader_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RequestHeader_Encoding_DefaultXml);

        public static readonly NodeId ResponseHeader_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ResponseHeader_Encoding_DefaultXml);

        public static readonly NodeId ServiceFault_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ServiceFault_Encoding_DefaultXml);

        public static readonly NodeId SessionlessInvokeRequestType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SessionlessInvokeRequestType_Encoding_DefaultXml);

        public static readonly NodeId SessionlessInvokeResponseType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SessionlessInvokeResponseType_Encoding_DefaultXml);

        public static readonly NodeId FindServersRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.FindServersRequest_Encoding_DefaultXml);

        public static readonly NodeId FindServersResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.FindServersResponse_Encoding_DefaultXml);

        public static readonly NodeId ServerOnNetwork_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ServerOnNetwork_Encoding_DefaultXml);

        public static readonly NodeId FindServersOnNetworkRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.FindServersOnNetworkRequest_Encoding_DefaultXml);

        public static readonly NodeId FindServersOnNetworkResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.FindServersOnNetworkResponse_Encoding_DefaultXml);

        public static readonly NodeId UserTokenPolicy_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UserTokenPolicy_Encoding_DefaultXml);

        public static readonly NodeId EndpointDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EndpointDescription_Encoding_DefaultXml);

        public static readonly NodeId GetEndpointsRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.GetEndpointsRequest_Encoding_DefaultXml);

        public static readonly NodeId GetEndpointsResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.GetEndpointsResponse_Encoding_DefaultXml);

        public static readonly NodeId RegisteredServer_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RegisteredServer_Encoding_DefaultXml);

        public static readonly NodeId RegisterServerRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RegisterServerRequest_Encoding_DefaultXml);

        public static readonly NodeId RegisterServerResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RegisterServerResponse_Encoding_DefaultXml);

        public static readonly NodeId DiscoveryConfiguration_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DiscoveryConfiguration_Encoding_DefaultXml);

        public static readonly NodeId MdnsDiscoveryConfiguration_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MdnsDiscoveryConfiguration_Encoding_DefaultXml);

        public static readonly NodeId RegisterServer2Request_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RegisterServer2Request_Encoding_DefaultXml);

        public static readonly NodeId RegisterServer2Response_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RegisterServer2Response_Encoding_DefaultXml);

        public static readonly NodeId ChannelSecurityToken_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ChannelSecurityToken_Encoding_DefaultXml);

        public static readonly NodeId OpenSecureChannelRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.OpenSecureChannelRequest_Encoding_DefaultXml);

        public static readonly NodeId OpenSecureChannelResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.OpenSecureChannelResponse_Encoding_DefaultXml);

        public static readonly NodeId CloseSecureChannelRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CloseSecureChannelRequest_Encoding_DefaultXml);

        public static readonly NodeId CloseSecureChannelResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CloseSecureChannelResponse_Encoding_DefaultXml);

        public static readonly NodeId SignedSoftwareCertificate_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SignedSoftwareCertificate_Encoding_DefaultXml);

        public static readonly NodeId SignatureData_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SignatureData_Encoding_DefaultXml);

        public static readonly NodeId CreateSessionRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CreateSessionRequest_Encoding_DefaultXml);

        public static readonly NodeId CreateSessionResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CreateSessionResponse_Encoding_DefaultXml);

        public static readonly NodeId UserIdentityToken_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UserIdentityToken_Encoding_DefaultXml);

        public static readonly NodeId AnonymousIdentityToken_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AnonymousIdentityToken_Encoding_DefaultXml);

        public static readonly NodeId UserNameIdentityToken_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UserNameIdentityToken_Encoding_DefaultXml);

        public static readonly NodeId X509IdentityToken_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.X509IdentityToken_Encoding_DefaultXml);

        public static readonly NodeId IssuedIdentityToken_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.IssuedIdentityToken_Encoding_DefaultXml);

        public static readonly NodeId ActivateSessionRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ActivateSessionRequest_Encoding_DefaultXml);

        public static readonly NodeId ActivateSessionResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ActivateSessionResponse_Encoding_DefaultXml);

        public static readonly NodeId CloseSessionRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CloseSessionRequest_Encoding_DefaultXml);

        public static readonly NodeId CloseSessionResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CloseSessionResponse_Encoding_DefaultXml);

        public static readonly NodeId CancelRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CancelRequest_Encoding_DefaultXml);

        public static readonly NodeId CancelResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CancelResponse_Encoding_DefaultXml);

        public static readonly NodeId NodeAttributes_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.NodeAttributes_Encoding_DefaultXml);

        public static readonly NodeId ObjectAttributes_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ObjectAttributes_Encoding_DefaultXml);

        public static readonly NodeId VariableAttributes_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.VariableAttributes_Encoding_DefaultXml);

        public static readonly NodeId MethodAttributes_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MethodAttributes_Encoding_DefaultXml);

        public static readonly NodeId ObjectTypeAttributes_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ObjectTypeAttributes_Encoding_DefaultXml);

        public static readonly NodeId VariableTypeAttributes_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.VariableTypeAttributes_Encoding_DefaultXml);

        public static readonly NodeId ReferenceTypeAttributes_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReferenceTypeAttributes_Encoding_DefaultXml);

        public static readonly NodeId DataTypeAttributes_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataTypeAttributes_Encoding_DefaultXml);

        public static readonly NodeId ViewAttributes_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ViewAttributes_Encoding_DefaultXml);

        public static readonly NodeId GenericAttributeValue_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.GenericAttributeValue_Encoding_DefaultXml);

        public static readonly NodeId GenericAttributes_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.GenericAttributes_Encoding_DefaultXml);

        public static readonly NodeId AddNodesItem_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AddNodesItem_Encoding_DefaultXml);

        public static readonly NodeId AddNodesResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AddNodesResult_Encoding_DefaultXml);

        public static readonly NodeId AddNodesRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AddNodesRequest_Encoding_DefaultXml);

        public static readonly NodeId AddNodesResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AddNodesResponse_Encoding_DefaultXml);

        public static readonly NodeId AddReferencesItem_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AddReferencesItem_Encoding_DefaultXml);

        public static readonly NodeId AddReferencesRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AddReferencesRequest_Encoding_DefaultXml);

        public static readonly NodeId AddReferencesResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AddReferencesResponse_Encoding_DefaultXml);

        public static readonly NodeId DeleteNodesItem_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteNodesItem_Encoding_DefaultXml);

        public static readonly NodeId DeleteNodesRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteNodesRequest_Encoding_DefaultXml);

        public static readonly NodeId DeleteNodesResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteNodesResponse_Encoding_DefaultXml);

        public static readonly NodeId DeleteReferencesItem_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteReferencesItem_Encoding_DefaultXml);

        public static readonly NodeId DeleteReferencesRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteReferencesRequest_Encoding_DefaultXml);

        public static readonly NodeId DeleteReferencesResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteReferencesResponse_Encoding_DefaultXml);

        public static readonly NodeId ViewDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ViewDescription_Encoding_DefaultXml);

        public static readonly NodeId BrowseDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrowseDescription_Encoding_DefaultXml);

        public static readonly NodeId ReferenceDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReferenceDescription_Encoding_DefaultXml);

        public static readonly NodeId BrowseResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrowseResult_Encoding_DefaultXml);

        public static readonly NodeId BrowseRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrowseRequest_Encoding_DefaultXml);

        public static readonly NodeId BrowseResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrowseResponse_Encoding_DefaultXml);

        public static readonly NodeId BrowseNextRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrowseNextRequest_Encoding_DefaultXml);

        public static readonly NodeId BrowseNextResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrowseNextResponse_Encoding_DefaultXml);

        public static readonly NodeId RelativePathElement_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RelativePathElement_Encoding_DefaultXml);

        public static readonly NodeId RelativePath_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RelativePath_Encoding_DefaultXml);

        public static readonly NodeId BrowsePath_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrowsePath_Encoding_DefaultXml);

        public static readonly NodeId BrowsePathTarget_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrowsePathTarget_Encoding_DefaultXml);

        public static readonly NodeId BrowsePathResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrowsePathResult_Encoding_DefaultXml);

        public static readonly NodeId TranslateBrowsePathsToNodeIdsRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TranslateBrowsePathsToNodeIdsRequest_Encoding_DefaultXml);

        public static readonly NodeId TranslateBrowsePathsToNodeIdsResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TranslateBrowsePathsToNodeIdsResponse_Encoding_DefaultXml);

        public static readonly NodeId RegisterNodesRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RegisterNodesRequest_Encoding_DefaultXml);

        public static readonly NodeId RegisterNodesResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RegisterNodesResponse_Encoding_DefaultXml);

        public static readonly NodeId UnregisterNodesRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UnregisterNodesRequest_Encoding_DefaultXml);

        public static readonly NodeId UnregisterNodesResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UnregisterNodesResponse_Encoding_DefaultXml);

        public static readonly NodeId EndpointConfiguration_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EndpointConfiguration_Encoding_DefaultXml);

        public static readonly NodeId QueryDataDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.QueryDataDescription_Encoding_DefaultXml);

        public static readonly NodeId NodeTypeDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.NodeTypeDescription_Encoding_DefaultXml);

        public static readonly NodeId QueryDataSet_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.QueryDataSet_Encoding_DefaultXml);

        public static readonly NodeId NodeReference_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.NodeReference_Encoding_DefaultXml);

        public static readonly NodeId ContentFilterElement_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ContentFilterElement_Encoding_DefaultXml);

        public static readonly NodeId ContentFilter_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ContentFilter_Encoding_DefaultXml);

        public static readonly NodeId FilterOperand_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.FilterOperand_Encoding_DefaultXml);

        public static readonly NodeId ElementOperand_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ElementOperand_Encoding_DefaultXml);

        public static readonly NodeId LiteralOperand_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.LiteralOperand_Encoding_DefaultXml);

        public static readonly NodeId AttributeOperand_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AttributeOperand_Encoding_DefaultXml);

        public static readonly NodeId SimpleAttributeOperand_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SimpleAttributeOperand_Encoding_DefaultXml);

        public static readonly NodeId ContentFilterElementResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ContentFilterElementResult_Encoding_DefaultXml);

        public static readonly NodeId ContentFilterResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ContentFilterResult_Encoding_DefaultXml);

        public static readonly NodeId ParsingResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ParsingResult_Encoding_DefaultXml);

        public static readonly NodeId QueryFirstRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.QueryFirstRequest_Encoding_DefaultXml);

        public static readonly NodeId QueryFirstResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.QueryFirstResponse_Encoding_DefaultXml);

        public static readonly NodeId QueryNextRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.QueryNextRequest_Encoding_DefaultXml);

        public static readonly NodeId QueryNextResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.QueryNextResponse_Encoding_DefaultXml);

        public static readonly NodeId ReadValueId_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReadValueId_Encoding_DefaultXml);

        public static readonly NodeId ReadRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReadRequest_Encoding_DefaultXml);

        public static readonly NodeId ReadResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReadResponse_Encoding_DefaultXml);

        public static readonly NodeId HistoryReadValueId_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryReadValueId_Encoding_DefaultXml);

        public static readonly NodeId HistoryReadResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryReadResult_Encoding_DefaultXml);

        public static readonly NodeId HistoryReadDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryReadDetails_Encoding_DefaultXml);

        public static readonly NodeId ReadEventDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReadEventDetails_Encoding_DefaultXml);

        public static readonly NodeId ReadEventDetails2_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReadEventDetails2_Encoding_DefaultXml);

        public static readonly NodeId SortRuleElement_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SortRuleElement_Encoding_DefaultXml);

        public static readonly NodeId ReadEventDetailsSorted_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReadEventDetailsSorted_Encoding_DefaultXml);

        public static readonly NodeId ReadRawModifiedDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReadRawModifiedDetails_Encoding_DefaultXml);

        public static readonly NodeId ReadProcessedDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReadProcessedDetails_Encoding_DefaultXml);

        public static readonly NodeId ReadAtTimeDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReadAtTimeDetails_Encoding_DefaultXml);

        public static readonly NodeId ReadAnnotationDataDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReadAnnotationDataDetails_Encoding_DefaultXml);

        public static readonly NodeId HistoryData_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryData_Encoding_DefaultXml);

        public static readonly NodeId ModificationInfo_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ModificationInfo_Encoding_DefaultXml);

        public static readonly NodeId HistoryModifiedData_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryModifiedData_Encoding_DefaultXml);

        public static readonly NodeId HistoryEvent_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryEvent_Encoding_DefaultXml);

        public static readonly NodeId HistoryModifiedEvent_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryModifiedEvent_Encoding_DefaultXml);

        public static readonly NodeId HistoryReadRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryReadRequest_Encoding_DefaultXml);

        public static readonly NodeId HistoryReadResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryReadResponse_Encoding_DefaultXml);

        public static readonly NodeId WriteValue_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.WriteValue_Encoding_DefaultXml);

        public static readonly NodeId WriteRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.WriteRequest_Encoding_DefaultXml);

        public static readonly NodeId WriteResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.WriteResponse_Encoding_DefaultXml);

        public static readonly NodeId HistoryUpdateDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryUpdateDetails_Encoding_DefaultXml);

        public static readonly NodeId UpdateDataDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UpdateDataDetails_Encoding_DefaultXml);

        public static readonly NodeId UpdateStructureDataDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UpdateStructureDataDetails_Encoding_DefaultXml);

        public static readonly NodeId UpdateEventDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.UpdateEventDetails_Encoding_DefaultXml);

        public static readonly NodeId DeleteRawModifiedDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteRawModifiedDetails_Encoding_DefaultXml);

        public static readonly NodeId DeleteAtTimeDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteAtTimeDetails_Encoding_DefaultXml);

        public static readonly NodeId DeleteEventDetails_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteEventDetails_Encoding_DefaultXml);

        public static readonly NodeId HistoryUpdateResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryUpdateResult_Encoding_DefaultXml);

        public static readonly NodeId HistoryUpdateRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryUpdateRequest_Encoding_DefaultXml);

        public static readonly NodeId HistoryUpdateResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryUpdateResponse_Encoding_DefaultXml);

        public static readonly NodeId CallMethodRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CallMethodRequest_Encoding_DefaultXml);

        public static readonly NodeId CallMethodResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CallMethodResult_Encoding_DefaultXml);

        public static readonly NodeId CallRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CallRequest_Encoding_DefaultXml);

        public static readonly NodeId CallResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CallResponse_Encoding_DefaultXml);

        public static readonly NodeId MonitoringFilter_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MonitoringFilter_Encoding_DefaultXml);

        public static readonly NodeId DataChangeFilter_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataChangeFilter_Encoding_DefaultXml);

        public static readonly NodeId EventFilter_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EventFilter_Encoding_DefaultXml);

        public static readonly NodeId AggregateConfiguration_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AggregateConfiguration_Encoding_DefaultXml);

        public static readonly NodeId AggregateFilter_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AggregateFilter_Encoding_DefaultXml);

        public static readonly NodeId MonitoringFilterResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MonitoringFilterResult_Encoding_DefaultXml);

        public static readonly NodeId EventFilterResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EventFilterResult_Encoding_DefaultXml);

        public static readonly NodeId AggregateFilterResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AggregateFilterResult_Encoding_DefaultXml);

        public static readonly NodeId MonitoringParameters_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MonitoringParameters_Encoding_DefaultXml);

        public static readonly NodeId MonitoredItemCreateRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MonitoredItemCreateRequest_Encoding_DefaultXml);

        public static readonly NodeId MonitoredItemCreateResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MonitoredItemCreateResult_Encoding_DefaultXml);

        public static readonly NodeId CreateMonitoredItemsRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CreateMonitoredItemsRequest_Encoding_DefaultXml);

        public static readonly NodeId CreateMonitoredItemsResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CreateMonitoredItemsResponse_Encoding_DefaultXml);

        public static readonly NodeId MonitoredItemModifyRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MonitoredItemModifyRequest_Encoding_DefaultXml);

        public static readonly NodeId MonitoredItemModifyResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MonitoredItemModifyResult_Encoding_DefaultXml);

        public static readonly NodeId ModifyMonitoredItemsRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ModifyMonitoredItemsRequest_Encoding_DefaultXml);

        public static readonly NodeId ModifyMonitoredItemsResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ModifyMonitoredItemsResponse_Encoding_DefaultXml);

        public static readonly NodeId SetMonitoringModeRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SetMonitoringModeRequest_Encoding_DefaultXml);

        public static readonly NodeId SetMonitoringModeResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SetMonitoringModeResponse_Encoding_DefaultXml);

        public static readonly NodeId SetTriggeringRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SetTriggeringRequest_Encoding_DefaultXml);

        public static readonly NodeId SetTriggeringResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SetTriggeringResponse_Encoding_DefaultXml);

        public static readonly NodeId DeleteMonitoredItemsRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteMonitoredItemsRequest_Encoding_DefaultXml);

        public static readonly NodeId DeleteMonitoredItemsResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteMonitoredItemsResponse_Encoding_DefaultXml);

        public static readonly NodeId CreateSubscriptionRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CreateSubscriptionRequest_Encoding_DefaultXml);

        public static readonly NodeId CreateSubscriptionResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.CreateSubscriptionResponse_Encoding_DefaultXml);

        public static readonly NodeId ModifySubscriptionRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ModifySubscriptionRequest_Encoding_DefaultXml);

        public static readonly NodeId ModifySubscriptionResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ModifySubscriptionResponse_Encoding_DefaultXml);

        public static readonly NodeId SetPublishingModeRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SetPublishingModeRequest_Encoding_DefaultXml);

        public static readonly NodeId SetPublishingModeResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SetPublishingModeResponse_Encoding_DefaultXml);

        public static readonly NodeId NotificationMessage_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.NotificationMessage_Encoding_DefaultXml);

        public static readonly NodeId NotificationData_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.NotificationData_Encoding_DefaultXml);

        public static readonly NodeId DataChangeNotification_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataChangeNotification_Encoding_DefaultXml);

        public static readonly NodeId MonitoredItemNotification_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MonitoredItemNotification_Encoding_DefaultXml);

        public static readonly NodeId EventNotificationList_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EventNotificationList_Encoding_DefaultXml);

        public static readonly NodeId EventFieldList_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EventFieldList_Encoding_DefaultXml);

        public static readonly NodeId HistoryEventFieldList_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.HistoryEventFieldList_Encoding_DefaultXml);

        public static readonly NodeId StatusChangeNotification_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.StatusChangeNotification_Encoding_DefaultXml);

        public static readonly NodeId SubscriptionAcknowledgement_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SubscriptionAcknowledgement_Encoding_DefaultXml);

        public static readonly NodeId PublishRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PublishRequest_Encoding_DefaultXml);

        public static readonly NodeId PublishResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.PublishResponse_Encoding_DefaultXml);

        public static readonly NodeId RepublishRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RepublishRequest_Encoding_DefaultXml);

        public static readonly NodeId RepublishResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RepublishResponse_Encoding_DefaultXml);

        public static readonly NodeId TransferResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TransferResult_Encoding_DefaultXml);

        public static readonly NodeId TransferSubscriptionsRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TransferSubscriptionsRequest_Encoding_DefaultXml);

        public static readonly NodeId TransferSubscriptionsResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TransferSubscriptionsResponse_Encoding_DefaultXml);

        public static readonly NodeId DeleteSubscriptionsRequest_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteSubscriptionsRequest_Encoding_DefaultXml);

        public static readonly NodeId DeleteSubscriptionsResponse_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DeleteSubscriptionsResponse_Encoding_DefaultXml);

        public static readonly NodeId BuildInfo_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BuildInfo_Encoding_DefaultXml);

        public static readonly NodeId RedundantServerDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RedundantServerDataType_Encoding_DefaultXml);

        public static readonly NodeId EndpointUrlListDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EndpointUrlListDataType_Encoding_DefaultXml);

        public static readonly NodeId NetworkGroupDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.NetworkGroupDataType_Encoding_DefaultXml);

        public static readonly NodeId SamplingIntervalDiagnosticsDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SamplingIntervalDiagnosticsDataType_Encoding_DefaultXml);

        public static readonly NodeId ServerDiagnosticsSummaryDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ServerDiagnosticsSummaryDataType_Encoding_DefaultXml);

        public static readonly NodeId ServerStatusDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ServerStatusDataType_Encoding_DefaultXml);

        public static readonly NodeId SessionDiagnosticsDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SessionDiagnosticsDataType_Encoding_DefaultXml);

        public static readonly NodeId SessionSecurityDiagnosticsDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SessionSecurityDiagnosticsDataType_Encoding_DefaultXml);

        public static readonly NodeId ServiceCounterDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ServiceCounterDataType_Encoding_DefaultXml);

        public static readonly NodeId StatusResult_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.StatusResult_Encoding_DefaultXml);

        public static readonly NodeId SubscriptionDiagnosticsDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SubscriptionDiagnosticsDataType_Encoding_DefaultXml);

        public static readonly NodeId ModelChangeStructureDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ModelChangeStructureDataType_Encoding_DefaultXml);

        public static readonly NodeId SemanticChangeStructureDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.SemanticChangeStructureDataType_Encoding_DefaultXml);

        public static readonly NodeId Range_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.Range_Encoding_DefaultXml);

        public static readonly NodeId EUInformation_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EUInformation_Encoding_DefaultXml);

        public static readonly NodeId ComplexNumberType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ComplexNumberType_Encoding_DefaultXml);

        public static readonly NodeId DoubleComplexNumberType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DoubleComplexNumberType_Encoding_DefaultXml);

        public static readonly NodeId AxisInformation_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.AxisInformation_Encoding_DefaultXml);

        public static readonly NodeId XVType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.XVType_Encoding_DefaultXml);

        public static readonly NodeId ProgramDiagnosticDataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ProgramDiagnosticDataType_Encoding_DefaultXml);

        public static readonly NodeId ProgramDiagnostic2DataType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ProgramDiagnostic2DataType_Encoding_DefaultXml);

        public static readonly NodeId Annotation_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.Annotation_Encoding_DefaultXml);

        public static readonly NodeId Union_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.Union_Encoding_DefaultJson);

        public static readonly NodeId KeyValuePair_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.KeyValuePair_Encoding_DefaultJson);

        public static readonly NodeId AdditionalParametersType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AdditionalParametersType_Encoding_DefaultJson);

        public static readonly NodeId EphemeralKeyType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EphemeralKeyType_Encoding_DefaultJson);

        public static readonly NodeId EndpointType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EndpointType_Encoding_DefaultJson);

        public static readonly NodeId BitFieldDefinition_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BitFieldDefinition_Encoding_DefaultJson);

        public static readonly NodeId RationalNumber_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RationalNumber_Encoding_DefaultJson);

        public static readonly NodeId Vector_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.Vector_Encoding_DefaultJson);

        public static readonly NodeId ThreeDVector_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ThreeDVector_Encoding_DefaultJson);

        public static readonly NodeId CartesianCoordinates_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CartesianCoordinates_Encoding_DefaultJson);

        public static readonly NodeId ThreeDCartesianCoordinates_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ThreeDCartesianCoordinates_Encoding_DefaultJson);

        public static readonly NodeId Orientation_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.Orientation_Encoding_DefaultJson);

        public static readonly NodeId ThreeDOrientation_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ThreeDOrientation_Encoding_DefaultJson);

        public static readonly NodeId Frame_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.Frame_Encoding_DefaultJson);

        public static readonly NodeId ThreeDFrame_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ThreeDFrame_Encoding_DefaultJson);

        public static readonly NodeId IdentityMappingRuleType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.IdentityMappingRuleType_Encoding_DefaultJson);

        public static readonly NodeId CurrencyUnitType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CurrencyUnitType_Encoding_DefaultJson);

        public static readonly NodeId AnnotationDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AnnotationDataType_Encoding_DefaultJson);

        public static readonly NodeId LinearConversionDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.LinearConversionDataType_Encoding_DefaultJson);

        public static readonly NodeId QuantityDimension_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.QuantityDimension_Encoding_DefaultJson);

        public static readonly NodeId TrustListDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TrustListDataType_Encoding_DefaultJson);

        public static readonly NodeId BaseConfigurationDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BaseConfigurationDataType_Encoding_DefaultJson);

        public static readonly NodeId BaseConfigurationRecordDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BaseConfigurationRecordDataType_Encoding_DefaultJson);

        public static readonly NodeId CertificateGroupDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CertificateGroupDataType_Encoding_DefaultJson);

        public static readonly NodeId ConfigurationUpdateTargetType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ConfigurationUpdateTargetType_Encoding_DefaultJson);

        public static readonly NodeId TransactionErrorType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TransactionErrorType_Encoding_DefaultJson);

        public static readonly NodeId ApplicationConfigurationDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ApplicationConfigurationDataType_Encoding_DefaultJson);

        public static readonly NodeId ApplicationIdentityDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ApplicationIdentityDataType_Encoding_DefaultJson);

        public static readonly NodeId EndpointDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EndpointDataType_Encoding_DefaultJson);

        public static readonly NodeId ServerEndpointDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ServerEndpointDataType_Encoding_DefaultJson);

        public static readonly NodeId SecuritySettingsDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SecuritySettingsDataType_Encoding_DefaultJson);

        public static readonly NodeId UserTokenSettingsDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UserTokenSettingsDataType_Encoding_DefaultJson);

        public static readonly NodeId ServiceCertificateDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ServiceCertificateDataType_Encoding_DefaultJson);

        public static readonly NodeId AuthorizationServiceConfigurationDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AuthorizationServiceConfigurationDataType_Encoding_DefaultJson);

        public static readonly NodeId DecimalDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DecimalDataType_Encoding_DefaultJson);

        public static readonly NodeId DataTypeSchemaHeader_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataTypeSchemaHeader_Encoding_DefaultJson);

        public static readonly NodeId DataTypeDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataTypeDescription_Encoding_DefaultJson);

        public static readonly NodeId StructureDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.StructureDescription_Encoding_DefaultJson);

        public static readonly NodeId EnumDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EnumDescription_Encoding_DefaultJson);

        public static readonly NodeId SimpleTypeDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SimpleTypeDescription_Encoding_DefaultJson);

        public static readonly NodeId UABinaryFileDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UABinaryFileDataType_Encoding_DefaultJson);

        public static readonly NodeId PortableQualifiedName_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PortableQualifiedName_Encoding_DefaultJson);

        public static readonly NodeId PortableNodeId_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PortableNodeId_Encoding_DefaultJson);

        public static readonly NodeId UnsignedRationalNumber_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UnsignedRationalNumber_Encoding_DefaultJson);

        public static readonly NodeId DataSetMetaDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataSetMetaDataType_Encoding_DefaultJson);

        public static readonly NodeId FieldMetaData_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.FieldMetaData_Encoding_DefaultJson);

        public static readonly NodeId ConfigurationVersionDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ConfigurationVersionDataType_Encoding_DefaultJson);

        public static readonly NodeId PublishedDataSetDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PublishedDataSetDataType_Encoding_DefaultJson);

        public static readonly NodeId PublishedDataSetSourceDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PublishedDataSetSourceDataType_Encoding_DefaultJson);

        public static readonly NodeId PublishedVariableDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PublishedVariableDataType_Encoding_DefaultJson);

        public static readonly NodeId PublishedDataItemsDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PublishedDataItemsDataType_Encoding_DefaultJson);

        public static readonly NodeId PublishedEventsDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PublishedEventsDataType_Encoding_DefaultJson);

        public static readonly NodeId PublishedDataSetCustomSourceDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PublishedDataSetCustomSourceDataType_Encoding_DefaultJson);

        public static readonly NodeId ActionTargetDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ActionTargetDataType_Encoding_DefaultJson);

        public static readonly NodeId PublishedActionDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PublishedActionDataType_Encoding_DefaultJson);

        public static readonly NodeId ActionMethodDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ActionMethodDataType_Encoding_DefaultJson);

        public static readonly NodeId PublishedActionMethodDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PublishedActionMethodDataType_Encoding_DefaultJson);

        public static readonly NodeId DataSetWriterDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataSetWriterDataType_Encoding_DefaultJson);

        public static readonly NodeId DataSetWriterTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataSetWriterTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId DataSetWriterMessageDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataSetWriterMessageDataType_Encoding_DefaultJson);

        public static readonly NodeId PubSubGroupDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PubSubGroupDataType_Encoding_DefaultJson);

        public static readonly NodeId WriterGroupDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.WriterGroupDataType_Encoding_DefaultJson);

        public static readonly NodeId WriterGroupTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.WriterGroupTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId WriterGroupMessageDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.WriterGroupMessageDataType_Encoding_DefaultJson);

        public static readonly NodeId PubSubConnectionDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PubSubConnectionDataType_Encoding_DefaultJson);

        public static readonly NodeId ConnectionTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ConnectionTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId NetworkAddressDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.NetworkAddressDataType_Encoding_DefaultJson);

        public static readonly NodeId NetworkAddressUrlDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.NetworkAddressUrlDataType_Encoding_DefaultJson);

        public static readonly NodeId ReaderGroupDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReaderGroupDataType_Encoding_DefaultJson);

        public static readonly NodeId ReaderGroupTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReaderGroupTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId ReaderGroupMessageDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReaderGroupMessageDataType_Encoding_DefaultJson);

        public static readonly NodeId DataSetReaderDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataSetReaderDataType_Encoding_DefaultJson);

        public static readonly NodeId DataSetReaderTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataSetReaderTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId DataSetReaderMessageDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataSetReaderMessageDataType_Encoding_DefaultJson);

        public static readonly NodeId SubscribedDataSetDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SubscribedDataSetDataType_Encoding_DefaultJson);

        public static readonly NodeId TargetVariablesDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TargetVariablesDataType_Encoding_DefaultJson);

        public static readonly NodeId FieldTargetDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.FieldTargetDataType_Encoding_DefaultJson);

        public static readonly NodeId SubscribedDataSetMirrorDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SubscribedDataSetMirrorDataType_Encoding_DefaultJson);

        public static readonly NodeId PubSubConfigurationDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PubSubConfigurationDataType_Encoding_DefaultJson);

        public static readonly NodeId StandaloneSubscribedDataSetRefDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.StandaloneSubscribedDataSetRefDataType_Encoding_DefaultJson);

        public static readonly NodeId StandaloneSubscribedDataSetDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.StandaloneSubscribedDataSetDataType_Encoding_DefaultJson);

        public static readonly NodeId SecurityGroupDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SecurityGroupDataType_Encoding_DefaultJson);

        public static readonly NodeId PubSubKeyPushTargetDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PubSubKeyPushTargetDataType_Encoding_DefaultJson);

        public static readonly NodeId PubSubConfiguration2DataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PubSubConfiguration2DataType_Encoding_DefaultJson);

        public static readonly NodeId UadpWriterGroupMessageDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UadpWriterGroupMessageDataType_Encoding_DefaultJson);

        public static readonly NodeId UadpDataSetWriterMessageDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UadpDataSetWriterMessageDataType_Encoding_DefaultJson);

        public static readonly NodeId UadpDataSetReaderMessageDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UadpDataSetReaderMessageDataType_Encoding_DefaultJson);

        public static readonly NodeId JsonWriterGroupMessageDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.JsonWriterGroupMessageDataType_Encoding_DefaultJson);

        public static readonly NodeId JsonDataSetWriterMessageDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.JsonDataSetWriterMessageDataType_Encoding_DefaultJson);

        public static readonly NodeId JsonDataSetReaderMessageDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.JsonDataSetReaderMessageDataType_Encoding_DefaultJson);

        public static readonly NodeId QosDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.QosDataType_Encoding_DefaultJson);

        public static readonly NodeId TransmitQosDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TransmitQosDataType_Encoding_DefaultJson);

        public static readonly NodeId TransmitQosPriorityDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TransmitQosPriorityDataType_Encoding_DefaultJson);

        public static readonly NodeId ReceiveQosDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReceiveQosDataType_Encoding_DefaultJson);

        public static readonly NodeId ReceiveQosPriorityDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReceiveQosPriorityDataType_Encoding_DefaultJson);

        public static readonly NodeId DatagramConnectionTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DatagramConnectionTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId DatagramConnectionTransport2DataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DatagramConnectionTransport2DataType_Encoding_DefaultJson);

        public static readonly NodeId DatagramWriterGroupTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DatagramWriterGroupTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId DatagramWriterGroupTransport2DataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DatagramWriterGroupTransport2DataType_Encoding_DefaultJson);

        public static readonly NodeId DatagramDataSetReaderTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DatagramDataSetReaderTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId DtlsPubSubConnectionDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DtlsPubSubConnectionDataType_Encoding_DefaultJson);

        public static readonly NodeId BrokerConnectionTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrokerConnectionTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId BrokerWriterGroupTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrokerWriterGroupTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId BrokerDataSetWriterTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrokerDataSetWriterTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId BrokerDataSetReaderTransportDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrokerDataSetReaderTransportDataType_Encoding_DefaultJson);

        public static readonly NodeId PubSubConfigurationRefDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PubSubConfigurationRefDataType_Encoding_DefaultJson);

        public static readonly NodeId PubSubConfigurationValueDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PubSubConfigurationValueDataType_Encoding_DefaultJson);

        public static readonly NodeId AliasNameDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AliasNameDataType_Encoding_DefaultJson);

        public static readonly NodeId UserManagementDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UserManagementDataType_Encoding_DefaultJson);

        public static readonly NodeId PriorityMappingEntryType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PriorityMappingEntryType_Encoding_DefaultJson);

        public static readonly NodeId LldpManagementAddressTxPortType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.LldpManagementAddressTxPortType_Encoding_DefaultJson);

        public static readonly NodeId LldpManagementAddressType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.LldpManagementAddressType_Encoding_DefaultJson);

        public static readonly NodeId LldpTlvType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.LldpTlvType_Encoding_DefaultJson);

        public static readonly NodeId ReferenceDescriptionDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReferenceDescriptionDataType_Encoding_DefaultJson);

        public static readonly NodeId ReferenceListEntryDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReferenceListEntryDataType_Encoding_DefaultJson);

        public static readonly NodeId LogRecord_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.LogRecord_Encoding_DefaultJson);

        public static readonly NodeId LogRecordsDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.LogRecordsDataType_Encoding_DefaultJson);

        public static readonly NodeId SpanContextDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SpanContextDataType_Encoding_DefaultJson);

        public static readonly NodeId TraceContextDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TraceContextDataType_Encoding_DefaultJson);

        public static readonly NodeId NameValuePair_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.NameValuePair_Encoding_DefaultJson);

        public static readonly NodeId RolePermissionType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RolePermissionType_Encoding_DefaultJson);

        public static readonly NodeId DataTypeDefinition_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataTypeDefinition_Encoding_DefaultJson);

        public static readonly NodeId StructureField_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.StructureField_Encoding_DefaultJson);

        public static readonly NodeId StructureDefinition_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.StructureDefinition_Encoding_DefaultJson);

        public static readonly NodeId EnumDefinition_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EnumDefinition_Encoding_DefaultJson);

        public static readonly NodeId Node_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.Node_Encoding_DefaultJson);

        public static readonly NodeId InstanceNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.InstanceNode_Encoding_DefaultJson);

        public static readonly NodeId TypeNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TypeNode_Encoding_DefaultJson);

        public static readonly NodeId ObjectNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ObjectNode_Encoding_DefaultJson);

        public static readonly NodeId ObjectTypeNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ObjectTypeNode_Encoding_DefaultJson);

        public static readonly NodeId VariableNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.VariableNode_Encoding_DefaultJson);

        public static readonly NodeId VariableTypeNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.VariableTypeNode_Encoding_DefaultJson);

        public static readonly NodeId ReferenceTypeNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReferenceTypeNode_Encoding_DefaultJson);

        public static readonly NodeId MethodNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MethodNode_Encoding_DefaultJson);

        public static readonly NodeId ViewNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ViewNode_Encoding_DefaultJson);

        public static readonly NodeId DataTypeNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataTypeNode_Encoding_DefaultJson);

        public static readonly NodeId ReferenceNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReferenceNode_Encoding_DefaultJson);

        public static readonly NodeId Argument_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.Argument_Encoding_DefaultJson);

        public static readonly NodeId EnumValueType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EnumValueType_Encoding_DefaultJson);

        public static readonly NodeId EnumField_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EnumField_Encoding_DefaultJson);

        public static readonly NodeId OptionSet_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.OptionSet_Encoding_DefaultJson);

        public static readonly NodeId TimeZoneDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TimeZoneDataType_Encoding_DefaultJson);

        public static readonly NodeId ApplicationDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ApplicationDescription_Encoding_DefaultJson);

        public static readonly NodeId RequestHeader_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RequestHeader_Encoding_DefaultJson);

        public static readonly NodeId ResponseHeader_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ResponseHeader_Encoding_DefaultJson);

        public static readonly NodeId ServiceFault_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ServiceFault_Encoding_DefaultJson);

        public static readonly NodeId SessionlessInvokeRequestType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SessionlessInvokeRequestType_Encoding_DefaultJson);

        public static readonly NodeId SessionlessInvokeResponseType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SessionlessInvokeResponseType_Encoding_DefaultJson);

        public static readonly NodeId FindServersRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.FindServersRequest_Encoding_DefaultJson);

        public static readonly NodeId FindServersResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.FindServersResponse_Encoding_DefaultJson);

        public static readonly NodeId ServerOnNetwork_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ServerOnNetwork_Encoding_DefaultJson);

        public static readonly NodeId FindServersOnNetworkRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.FindServersOnNetworkRequest_Encoding_DefaultJson);

        public static readonly NodeId FindServersOnNetworkResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.FindServersOnNetworkResponse_Encoding_DefaultJson);

        public static readonly NodeId UserTokenPolicy_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UserTokenPolicy_Encoding_DefaultJson);

        public static readonly NodeId EndpointDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EndpointDescription_Encoding_DefaultJson);

        public static readonly NodeId GetEndpointsRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.GetEndpointsRequest_Encoding_DefaultJson);

        public static readonly NodeId GetEndpointsResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.GetEndpointsResponse_Encoding_DefaultJson);

        public static readonly NodeId RegisteredServer_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RegisteredServer_Encoding_DefaultJson);

        public static readonly NodeId RegisterServerRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RegisterServerRequest_Encoding_DefaultJson);

        public static readonly NodeId RegisterServerResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RegisterServerResponse_Encoding_DefaultJson);

        public static readonly NodeId DiscoveryConfiguration_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DiscoveryConfiguration_Encoding_DefaultJson);

        public static readonly NodeId MdnsDiscoveryConfiguration_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MdnsDiscoveryConfiguration_Encoding_DefaultJson);

        public static readonly NodeId RegisterServer2Request_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RegisterServer2Request_Encoding_DefaultJson);

        public static readonly NodeId RegisterServer2Response_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RegisterServer2Response_Encoding_DefaultJson);

        public static readonly NodeId ChannelSecurityToken_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ChannelSecurityToken_Encoding_DefaultJson);

        public static readonly NodeId OpenSecureChannelRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.OpenSecureChannelRequest_Encoding_DefaultJson);

        public static readonly NodeId OpenSecureChannelResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.OpenSecureChannelResponse_Encoding_DefaultJson);

        public static readonly NodeId CloseSecureChannelRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CloseSecureChannelRequest_Encoding_DefaultJson);

        public static readonly NodeId CloseSecureChannelResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CloseSecureChannelResponse_Encoding_DefaultJson);

        public static readonly NodeId SignedSoftwareCertificate_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SignedSoftwareCertificate_Encoding_DefaultJson);

        public static readonly NodeId SignatureData_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SignatureData_Encoding_DefaultJson);

        public static readonly NodeId CreateSessionRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CreateSessionRequest_Encoding_DefaultJson);

        public static readonly NodeId CreateSessionResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CreateSessionResponse_Encoding_DefaultJson);

        public static readonly NodeId UserIdentityToken_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UserIdentityToken_Encoding_DefaultJson);

        public static readonly NodeId AnonymousIdentityToken_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AnonymousIdentityToken_Encoding_DefaultJson);

        public static readonly NodeId UserNameIdentityToken_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UserNameIdentityToken_Encoding_DefaultJson);

        public static readonly NodeId X509IdentityToken_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.X509IdentityToken_Encoding_DefaultJson);

        public static readonly NodeId IssuedIdentityToken_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.IssuedIdentityToken_Encoding_DefaultJson);

        public static readonly NodeId ActivateSessionRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ActivateSessionRequest_Encoding_DefaultJson);

        public static readonly NodeId ActivateSessionResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ActivateSessionResponse_Encoding_DefaultJson);

        public static readonly NodeId CloseSessionRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CloseSessionRequest_Encoding_DefaultJson);

        public static readonly NodeId CloseSessionResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CloseSessionResponse_Encoding_DefaultJson);

        public static readonly NodeId CancelRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CancelRequest_Encoding_DefaultJson);

        public static readonly NodeId CancelResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CancelResponse_Encoding_DefaultJson);

        public static readonly NodeId NodeAttributes_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.NodeAttributes_Encoding_DefaultJson);

        public static readonly NodeId ObjectAttributes_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ObjectAttributes_Encoding_DefaultJson);

        public static readonly NodeId VariableAttributes_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.VariableAttributes_Encoding_DefaultJson);

        public static readonly NodeId MethodAttributes_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MethodAttributes_Encoding_DefaultJson);

        public static readonly NodeId ObjectTypeAttributes_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ObjectTypeAttributes_Encoding_DefaultJson);

        public static readonly NodeId VariableTypeAttributes_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.VariableTypeAttributes_Encoding_DefaultJson);

        public static readonly NodeId ReferenceTypeAttributes_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReferenceTypeAttributes_Encoding_DefaultJson);

        public static readonly NodeId DataTypeAttributes_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataTypeAttributes_Encoding_DefaultJson);

        public static readonly NodeId ViewAttributes_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ViewAttributes_Encoding_DefaultJson);

        public static readonly NodeId GenericAttributeValue_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.GenericAttributeValue_Encoding_DefaultJson);

        public static readonly NodeId GenericAttributes_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.GenericAttributes_Encoding_DefaultJson);

        public static readonly NodeId AddNodesItem_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AddNodesItem_Encoding_DefaultJson);

        public static readonly NodeId AddNodesResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AddNodesResult_Encoding_DefaultJson);

        public static readonly NodeId AddNodesRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AddNodesRequest_Encoding_DefaultJson);

        public static readonly NodeId AddNodesResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AddNodesResponse_Encoding_DefaultJson);

        public static readonly NodeId AddReferencesItem_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AddReferencesItem_Encoding_DefaultJson);

        public static readonly NodeId AddReferencesRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AddReferencesRequest_Encoding_DefaultJson);

        public static readonly NodeId AddReferencesResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AddReferencesResponse_Encoding_DefaultJson);

        public static readonly NodeId DeleteNodesItem_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteNodesItem_Encoding_DefaultJson);

        public static readonly NodeId DeleteNodesRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteNodesRequest_Encoding_DefaultJson);

        public static readonly NodeId DeleteNodesResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteNodesResponse_Encoding_DefaultJson);

        public static readonly NodeId DeleteReferencesItem_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteReferencesItem_Encoding_DefaultJson);

        public static readonly NodeId DeleteReferencesRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteReferencesRequest_Encoding_DefaultJson);

        public static readonly NodeId DeleteReferencesResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteReferencesResponse_Encoding_DefaultJson);

        public static readonly NodeId ViewDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ViewDescription_Encoding_DefaultJson);

        public static readonly NodeId BrowseDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrowseDescription_Encoding_DefaultJson);

        public static readonly NodeId ReferenceDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReferenceDescription_Encoding_DefaultJson);

        public static readonly NodeId BrowseResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrowseResult_Encoding_DefaultJson);

        public static readonly NodeId BrowseRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrowseRequest_Encoding_DefaultJson);

        public static readonly NodeId BrowseResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrowseResponse_Encoding_DefaultJson);

        public static readonly NodeId BrowseNextRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrowseNextRequest_Encoding_DefaultJson);

        public static readonly NodeId BrowseNextResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrowseNextResponse_Encoding_DefaultJson);

        public static readonly NodeId RelativePathElement_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RelativePathElement_Encoding_DefaultJson);

        public static readonly NodeId RelativePath_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RelativePath_Encoding_DefaultJson);

        public static readonly NodeId BrowsePath_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrowsePath_Encoding_DefaultJson);

        public static readonly NodeId BrowsePathTarget_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrowsePathTarget_Encoding_DefaultJson);

        public static readonly NodeId BrowsePathResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrowsePathResult_Encoding_DefaultJson);

        public static readonly NodeId TranslateBrowsePathsToNodeIdsRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TranslateBrowsePathsToNodeIdsRequest_Encoding_DefaultJson);

        public static readonly NodeId TranslateBrowsePathsToNodeIdsResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TranslateBrowsePathsToNodeIdsResponse_Encoding_DefaultJson);

        public static readonly NodeId RegisterNodesRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RegisterNodesRequest_Encoding_DefaultJson);

        public static readonly NodeId RegisterNodesResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RegisterNodesResponse_Encoding_DefaultJson);

        public static readonly NodeId UnregisterNodesRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UnregisterNodesRequest_Encoding_DefaultJson);

        public static readonly NodeId UnregisterNodesResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UnregisterNodesResponse_Encoding_DefaultJson);

        public static readonly NodeId EndpointConfiguration_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EndpointConfiguration_Encoding_DefaultJson);

        public static readonly NodeId QueryDataDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.QueryDataDescription_Encoding_DefaultJson);

        public static readonly NodeId NodeTypeDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.NodeTypeDescription_Encoding_DefaultJson);

        public static readonly NodeId QueryDataSet_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.QueryDataSet_Encoding_DefaultJson);

        public static readonly NodeId NodeReference_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.NodeReference_Encoding_DefaultJson);

        public static readonly NodeId ContentFilterElement_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ContentFilterElement_Encoding_DefaultJson);

        public static readonly NodeId ContentFilter_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ContentFilter_Encoding_DefaultJson);

        public static readonly NodeId FilterOperand_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.FilterOperand_Encoding_DefaultJson);

        public static readonly NodeId ElementOperand_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ElementOperand_Encoding_DefaultJson);

        public static readonly NodeId LiteralOperand_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.LiteralOperand_Encoding_DefaultJson);

        public static readonly NodeId AttributeOperand_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AttributeOperand_Encoding_DefaultJson);

        public static readonly NodeId SimpleAttributeOperand_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SimpleAttributeOperand_Encoding_DefaultJson);

        public static readonly NodeId ContentFilterElementResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ContentFilterElementResult_Encoding_DefaultJson);

        public static readonly NodeId ContentFilterResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ContentFilterResult_Encoding_DefaultJson);

        public static readonly NodeId ParsingResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ParsingResult_Encoding_DefaultJson);

        public static readonly NodeId QueryFirstRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.QueryFirstRequest_Encoding_DefaultJson);

        public static readonly NodeId QueryFirstResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.QueryFirstResponse_Encoding_DefaultJson);

        public static readonly NodeId QueryNextRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.QueryNextRequest_Encoding_DefaultJson);

        public static readonly NodeId QueryNextResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.QueryNextResponse_Encoding_DefaultJson);

        public static readonly NodeId ReadValueId_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReadValueId_Encoding_DefaultJson);

        public static readonly NodeId ReadRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReadRequest_Encoding_DefaultJson);

        public static readonly NodeId ReadResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReadResponse_Encoding_DefaultJson);

        public static readonly NodeId HistoryReadValueId_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryReadValueId_Encoding_DefaultJson);

        public static readonly NodeId HistoryReadResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryReadResult_Encoding_DefaultJson);

        public static readonly NodeId HistoryReadDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryReadDetails_Encoding_DefaultJson);

        public static readonly NodeId ReadEventDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReadEventDetails_Encoding_DefaultJson);

        public static readonly NodeId ReadEventDetails2_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReadEventDetails2_Encoding_DefaultJson);

        public static readonly NodeId SortRuleElement_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SortRuleElement_Encoding_DefaultJson);

        public static readonly NodeId ReadEventDetailsSorted_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReadEventDetailsSorted_Encoding_DefaultJson);

        public static readonly NodeId ReadRawModifiedDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReadRawModifiedDetails_Encoding_DefaultJson);

        public static readonly NodeId ReadProcessedDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReadProcessedDetails_Encoding_DefaultJson);

        public static readonly NodeId ReadAtTimeDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReadAtTimeDetails_Encoding_DefaultJson);

        public static readonly NodeId ReadAnnotationDataDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReadAnnotationDataDetails_Encoding_DefaultJson);

        public static readonly NodeId HistoryData_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryData_Encoding_DefaultJson);

        public static readonly NodeId ModificationInfo_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ModificationInfo_Encoding_DefaultJson);

        public static readonly NodeId HistoryModifiedData_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryModifiedData_Encoding_DefaultJson);

        public static readonly NodeId HistoryEvent_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryEvent_Encoding_DefaultJson);

        public static readonly NodeId HistoryModifiedEvent_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryModifiedEvent_Encoding_DefaultJson);

        public static readonly NodeId HistoryReadRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryReadRequest_Encoding_DefaultJson);

        public static readonly NodeId HistoryReadResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryReadResponse_Encoding_DefaultJson);

        public static readonly NodeId WriteValue_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.WriteValue_Encoding_DefaultJson);

        public static readonly NodeId WriteRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.WriteRequest_Encoding_DefaultJson);

        public static readonly NodeId WriteResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.WriteResponse_Encoding_DefaultJson);

        public static readonly NodeId HistoryUpdateDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryUpdateDetails_Encoding_DefaultJson);

        public static readonly NodeId UpdateDataDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UpdateDataDetails_Encoding_DefaultJson);

        public static readonly NodeId UpdateStructureDataDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UpdateStructureDataDetails_Encoding_DefaultJson);

        public static readonly NodeId UpdateEventDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.UpdateEventDetails_Encoding_DefaultJson);

        public static readonly NodeId DeleteRawModifiedDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteRawModifiedDetails_Encoding_DefaultJson);

        public static readonly NodeId DeleteAtTimeDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteAtTimeDetails_Encoding_DefaultJson);

        public static readonly NodeId DeleteEventDetails_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteEventDetails_Encoding_DefaultJson);

        public static readonly NodeId HistoryUpdateResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryUpdateResult_Encoding_DefaultJson);

        public static readonly NodeId HistoryUpdateRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryUpdateRequest_Encoding_DefaultJson);

        public static readonly NodeId HistoryUpdateResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryUpdateResponse_Encoding_DefaultJson);

        public static readonly NodeId CallMethodRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CallMethodRequest_Encoding_DefaultJson);

        public static readonly NodeId CallMethodResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CallMethodResult_Encoding_DefaultJson);

        public static readonly NodeId CallRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CallRequest_Encoding_DefaultJson);

        public static readonly NodeId CallResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CallResponse_Encoding_DefaultJson);

        public static readonly NodeId MonitoringFilter_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MonitoringFilter_Encoding_DefaultJson);

        public static readonly NodeId DataChangeFilter_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataChangeFilter_Encoding_DefaultJson);

        public static readonly NodeId EventFilter_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EventFilter_Encoding_DefaultJson);

        public static readonly NodeId AggregateConfiguration_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AggregateConfiguration_Encoding_DefaultJson);

        public static readonly NodeId AggregateFilter_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AggregateFilter_Encoding_DefaultJson);

        public static readonly NodeId MonitoringFilterResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MonitoringFilterResult_Encoding_DefaultJson);

        public static readonly NodeId EventFilterResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EventFilterResult_Encoding_DefaultJson);

        public static readonly NodeId AggregateFilterResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AggregateFilterResult_Encoding_DefaultJson);

        public static readonly NodeId MonitoringParameters_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MonitoringParameters_Encoding_DefaultJson);

        public static readonly NodeId MonitoredItemCreateRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MonitoredItemCreateRequest_Encoding_DefaultJson);

        public static readonly NodeId MonitoredItemCreateResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MonitoredItemCreateResult_Encoding_DefaultJson);

        public static readonly NodeId CreateMonitoredItemsRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CreateMonitoredItemsRequest_Encoding_DefaultJson);

        public static readonly NodeId CreateMonitoredItemsResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CreateMonitoredItemsResponse_Encoding_DefaultJson);

        public static readonly NodeId MonitoredItemModifyRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MonitoredItemModifyRequest_Encoding_DefaultJson);

        public static readonly NodeId MonitoredItemModifyResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MonitoredItemModifyResult_Encoding_DefaultJson);

        public static readonly NodeId ModifyMonitoredItemsRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ModifyMonitoredItemsRequest_Encoding_DefaultJson);

        public static readonly NodeId ModifyMonitoredItemsResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ModifyMonitoredItemsResponse_Encoding_DefaultJson);

        public static readonly NodeId SetMonitoringModeRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SetMonitoringModeRequest_Encoding_DefaultJson);

        public static readonly NodeId SetMonitoringModeResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SetMonitoringModeResponse_Encoding_DefaultJson);

        public static readonly NodeId SetTriggeringRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SetTriggeringRequest_Encoding_DefaultJson);

        public static readonly NodeId SetTriggeringResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SetTriggeringResponse_Encoding_DefaultJson);

        public static readonly NodeId DeleteMonitoredItemsRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteMonitoredItemsRequest_Encoding_DefaultJson);

        public static readonly NodeId DeleteMonitoredItemsResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteMonitoredItemsResponse_Encoding_DefaultJson);

        public static readonly NodeId CreateSubscriptionRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CreateSubscriptionRequest_Encoding_DefaultJson);

        public static readonly NodeId CreateSubscriptionResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.CreateSubscriptionResponse_Encoding_DefaultJson);

        public static readonly NodeId ModifySubscriptionRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ModifySubscriptionRequest_Encoding_DefaultJson);

        public static readonly NodeId ModifySubscriptionResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ModifySubscriptionResponse_Encoding_DefaultJson);

        public static readonly NodeId SetPublishingModeRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SetPublishingModeRequest_Encoding_DefaultJson);

        public static readonly NodeId SetPublishingModeResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SetPublishingModeResponse_Encoding_DefaultJson);

        public static readonly NodeId NotificationMessage_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.NotificationMessage_Encoding_DefaultJson);

        public static readonly NodeId NotificationData_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.NotificationData_Encoding_DefaultJson);

        public static readonly NodeId DataChangeNotification_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataChangeNotification_Encoding_DefaultJson);

        public static readonly NodeId MonitoredItemNotification_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MonitoredItemNotification_Encoding_DefaultJson);

        public static readonly NodeId EventNotificationList_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EventNotificationList_Encoding_DefaultJson);

        public static readonly NodeId EventFieldList_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EventFieldList_Encoding_DefaultJson);

        public static readonly NodeId HistoryEventFieldList_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.HistoryEventFieldList_Encoding_DefaultJson);

        public static readonly NodeId StatusChangeNotification_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.StatusChangeNotification_Encoding_DefaultJson);

        public static readonly NodeId SubscriptionAcknowledgement_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SubscriptionAcknowledgement_Encoding_DefaultJson);

        public static readonly NodeId PublishRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PublishRequest_Encoding_DefaultJson);

        public static readonly NodeId PublishResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.PublishResponse_Encoding_DefaultJson);

        public static readonly NodeId RepublishRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RepublishRequest_Encoding_DefaultJson);

        public static readonly NodeId RepublishResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RepublishResponse_Encoding_DefaultJson);

        public static readonly NodeId TransferResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TransferResult_Encoding_DefaultJson);

        public static readonly NodeId TransferSubscriptionsRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TransferSubscriptionsRequest_Encoding_DefaultJson);

        public static readonly NodeId TransferSubscriptionsResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TransferSubscriptionsResponse_Encoding_DefaultJson);

        public static readonly NodeId DeleteSubscriptionsRequest_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteSubscriptionsRequest_Encoding_DefaultJson);

        public static readonly NodeId DeleteSubscriptionsResponse_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DeleteSubscriptionsResponse_Encoding_DefaultJson);

        public static readonly NodeId BuildInfo_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BuildInfo_Encoding_DefaultJson);

        public static readonly NodeId RedundantServerDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RedundantServerDataType_Encoding_DefaultJson);

        public static readonly NodeId EndpointUrlListDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EndpointUrlListDataType_Encoding_DefaultJson);

        public static readonly NodeId NetworkGroupDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.NetworkGroupDataType_Encoding_DefaultJson);

        public static readonly NodeId SamplingIntervalDiagnosticsDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SamplingIntervalDiagnosticsDataType_Encoding_DefaultJson);

        public static readonly NodeId ServerDiagnosticsSummaryDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ServerDiagnosticsSummaryDataType_Encoding_DefaultJson);

        public static readonly NodeId ServerStatusDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ServerStatusDataType_Encoding_DefaultJson);

        public static readonly NodeId SessionDiagnosticsDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SessionDiagnosticsDataType_Encoding_DefaultJson);

        public static readonly NodeId SessionSecurityDiagnosticsDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SessionSecurityDiagnosticsDataType_Encoding_DefaultJson);

        public static readonly NodeId ServiceCounterDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ServiceCounterDataType_Encoding_DefaultJson);

        public static readonly NodeId StatusResult_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.StatusResult_Encoding_DefaultJson);

        public static readonly NodeId SubscriptionDiagnosticsDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SubscriptionDiagnosticsDataType_Encoding_DefaultJson);

        public static readonly NodeId ModelChangeStructureDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ModelChangeStructureDataType_Encoding_DefaultJson);

        public static readonly NodeId SemanticChangeStructureDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.SemanticChangeStructureDataType_Encoding_DefaultJson);

        public static readonly NodeId Range_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.Range_Encoding_DefaultJson);

        public static readonly NodeId EUInformation_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EUInformation_Encoding_DefaultJson);

        public static readonly NodeId ComplexNumberType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ComplexNumberType_Encoding_DefaultJson);

        public static readonly NodeId DoubleComplexNumberType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DoubleComplexNumberType_Encoding_DefaultJson);

        public static readonly NodeId AxisInformation_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.AxisInformation_Encoding_DefaultJson);

        public static readonly NodeId XVType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.XVType_Encoding_DefaultJson);

        public static readonly NodeId ProgramDiagnosticDataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ProgramDiagnosticDataType_Encoding_DefaultJson);

        public static readonly NodeId ProgramDiagnostic2DataType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ProgramDiagnostic2DataType_Encoding_DefaultJson);

        public static readonly NodeId Annotation_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.Annotation_Encoding_DefaultJson);
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <exclude />


    public static partial class ObjectTypeIds
    {
        public static readonly NodeId BaseObjectType = new NodeId(Opc.Ua.ObjectTypes.BaseObjectType);

        public static readonly NodeId DataTypeEncodingType = new NodeId(Opc.Ua.ObjectTypes.DataTypeEncodingType);
    }
    #endregion

    #region ReferenceType Node Identifiers
    /// <exclude />


    public static partial class ReferenceTypeIds
    {
        public static readonly NodeId References = new NodeId(Opc.Ua.ReferenceTypes.References);

        public static readonly NodeId NonHierarchicalReferences = new NodeId(Opc.Ua.ReferenceTypes.NonHierarchicalReferences);

        public static readonly NodeId HierarchicalReferences = new NodeId(Opc.Ua.ReferenceTypes.HierarchicalReferences);

        public static readonly NodeId Organizes = new NodeId(Opc.Ua.ReferenceTypes.Organizes);

        public static readonly NodeId HasEventSource = new NodeId(Opc.Ua.ReferenceTypes.HasEventSource);

        public static readonly NodeId HasModellingRule = new NodeId(Opc.Ua.ReferenceTypes.HasModellingRule);

        public static readonly NodeId HasEncoding = new NodeId(Opc.Ua.ReferenceTypes.HasEncoding);

        public static readonly NodeId HasDescription = new NodeId(Opc.Ua.ReferenceTypes.HasDescription);

        public static readonly NodeId HasTypeDefinition = new NodeId(Opc.Ua.ReferenceTypes.HasTypeDefinition);

        public static readonly NodeId GeneratesEvent = new NodeId(Opc.Ua.ReferenceTypes.GeneratesEvent);

        public static readonly NodeId AlwaysGeneratesEvent = new NodeId(Opc.Ua.ReferenceTypes.AlwaysGeneratesEvent);

        public static readonly NodeId HasSubtype = new NodeId(Opc.Ua.ReferenceTypes.HasSubtype);

        public static readonly NodeId HasProperty = new NodeId(Opc.Ua.ReferenceTypes.HasProperty);

        public static readonly NodeId HasComponent = new NodeId(Opc.Ua.ReferenceTypes.HasComponent);

        public static readonly NodeId HasNotifier = new NodeId(Opc.Ua.ReferenceTypes.HasNotifier);

        public static readonly NodeId HasOrderedComponent = new NodeId(Opc.Ua.ReferenceTypes.HasOrderedComponent);

        public static readonly NodeId FromState = new NodeId(Opc.Ua.ReferenceTypes.FromState);

        public static readonly NodeId ToState = new NodeId(Opc.Ua.ReferenceTypes.ToState);

        public static readonly NodeId HasCause = new NodeId(Opc.Ua.ReferenceTypes.HasCause);

        public static readonly NodeId HasEffect = new NodeId(Opc.Ua.ReferenceTypes.HasEffect);

        public static readonly NodeId HasGuard = new NodeId(Opc.Ua.ReferenceTypes.HasGuard);

        public static readonly NodeId HasDictionaryEntry = new NodeId(Opc.Ua.ReferenceTypes.HasDictionaryEntry);

        public static readonly NodeId HasInterface = new NodeId(Opc.Ua.ReferenceTypes.HasInterface);

        public static readonly NodeId HasAddIn = new NodeId(Opc.Ua.ReferenceTypes.HasAddIn);

        public static readonly NodeId HasTrueSubState = new NodeId(Opc.Ua.ReferenceTypes.HasTrueSubState);

        public static readonly NodeId HasFalseSubState = new NodeId(Opc.Ua.ReferenceTypes.HasFalseSubState);

        public static readonly NodeId HasAlarmSuppressionGroup = new NodeId(Opc.Ua.ReferenceTypes.HasAlarmSuppressionGroup);

        public static readonly NodeId AlarmGroupMember = new NodeId(Opc.Ua.ReferenceTypes.AlarmGroupMember);

        public static readonly NodeId AlarmSuppressionGroupMember = new NodeId(Opc.Ua.ReferenceTypes.AlarmSuppressionGroupMember);

        public static readonly NodeId HasCondition = new NodeId(Opc.Ua.ReferenceTypes.HasCondition);
    }
    #endregion

    #region VariableType Node Identifiers
    /// <exclude />


    public static partial class VariableTypeIds
    {
        public static readonly NodeId BaseVariableType = new NodeId(Opc.Ua.VariableTypes.BaseVariableType);

        public static readonly NodeId BaseDataVariableType = new NodeId(Opc.Ua.VariableTypes.BaseDataVariableType);

        public static readonly NodeId PropertyType = new NodeId(Opc.Ua.VariableTypes.PropertyType);

        public static readonly NodeId DataTypeDictionaryType = new NodeId(Opc.Ua.VariableTypes.DataTypeDictionaryType);
    }
    #endregion

    #region BrowseName Declarations


    public static partial class BrowseNames
    {

        public const string AlarmGroupMember = "AlarmGroupMember";

        public const string AlarmSuppressionGroupMember = "AlarmSuppressionGroupMember";

        public const string AlwaysGeneratesEvent = "AlwaysGeneratesEvent";

        public const string BaseDataType = "BaseDataType";

        public const string BaseDataVariableType = "BaseDataVariableType";

        public const string BaseObjectType = "BaseObjectType";

        public const string Boolean = "Boolean";

        public const string Byte = "Byte";

        public const string ByteString = "ByteString";

        public const string DateTime = "DateTime";

        public const string DefaultBinary = "Default Binary";

        public const string DefaultInstanceBrowseName = "DefaultInstanceBrowseName";

        public const string DefaultJson = "Default JSON";

        public const string DefaultXml = "Default XML";

        public const string Double = "Double";

        public const string Enumeration = "Enumeration";

        public const string EnumStrings = "EnumStrings";

        public const string ExpandedNodeId = "ExpandedNodeId";

        public const string Float = "Float";

        public const string FromState = "FromState";

        public const string GeneratesEvent = "GeneratesEvent";

        public const string Guid = "Guid";

        public const string HasAddIn = "HasAddIn";

        public const string HasAlarmSuppressionGroup = "HasAlarmSuppressionGroup";

        public const string HasCause = "HasCause";

        public const string HasComponent = "HasComponent";

        public const string HasCondition = "HasCondition";

        public const string HasDescription = "HasDescription";

        public const string HasDictionaryEntry = "HasDictionaryEntry";

        public const string HasEffect = "HasEffect";

        public const string HasEncoding = "HasEncoding";

        public const string HasEventSource = "HasEventSource";

        public const string HasFalseSubState = "HasFalseSubState";

        public const string HasGuard = "HasGuard";

        public const string HasInterface = "HasInterface";

        public const string HasModellingRule = "HasModellingRule";

        public const string HasNotifier = "HasNotifier";

        public const string HasOrderedComponent = "HasOrderedComponent";

        public const string HasProperty = "HasProperty";

        public const string HasSubtype = "HasSubtype";

        public const string HasTrueSubState = "HasTrueSubState";

        public const string HasTypeDefinition = "HasTypeDefinition";

        public const string HistoryUpdateDetails = "HistoryUpdateDetails";

        public const string Index = "Index";

        public const string InputArguments = "InputArguments";

        public const string Int16 = "Int16";

        public const string Int32 = "Int32";

        public const string Int64 = "Int64";

        public const string Integer = "Integer";

        public const string LocaleId = "LocaleId";

        public const string LocalizedText = "LocalizedText";

        public const string NamespacePublicationDate = "NamespacePublicationDate";

        public const string NamespaceVersion = "NamespaceVersion";

        public const string NodeId = "NodeId";

        public const string Number = "Number";

        public const string Organizes = "Organizes";

        public const string OutputArguments = "OutputArguments";

        public const string PropertyType = "PropertyType";

        public const string QualifiedName = "QualifiedName";

        public const string SByte = "SByte";

        public const string StatusCode = "StatusCode";

        public const string String = "String";

        public const string Structure = "Structure";

        public const string ToState = "ToState";

        public const string UInt16 = "UInt16";

        public const string UInt32 = "UInt32";

        public const string UInt64 = "UInt64";

        public const string UInteger = "UInteger";

        public const string Union = "Union";

        public const string XmlElement = "XmlElement";
    }
    #endregion

    #region Namespace Declarations


    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    }
    #endregion
}

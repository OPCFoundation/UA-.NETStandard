/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;

namespace Opc.Ua.DI
{
    #region DataType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypes
    {
        /// <remarks />
        public const uint DeviceHealthEnumeration = 6244;

        /// <remarks />
        public const uint FetchResultDataType = 6522;

        /// <remarks />
        public const uint TransferResultErrorDataType = 15888;

        /// <remarks />
        public const uint TransferResultDataDataType = 15889;

        /// <remarks />
        public const uint ParameterResultDataType = 6525;

        /// <remarks />
        public const uint SoftwareVersionFileType = 331;

        /// <remarks />
        public const uint UpdateBehavior = 333;
    }
    #endregion

    #region Method Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Methods
    {
        /// <remarks />
        public const uint TopologyElementType_Lock_InitLock = 6166;

        /// <remarks />
        public const uint TopologyElementType_Lock_RenewLock = 6169;

        /// <remarks />
        public const uint TopologyElementType_Lock_ExitLock = 6171;

        /// <remarks />
        public const uint TopologyElementType_Lock_BreakLock = 6173;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Open = 36;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Close = 39;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Read = 63;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Write = 66;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_GetPosition = 68;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_SetPosition = 71;

        /// <remarks />
        public const uint ComponentType_Lock_InitLock = 6166;

        /// <remarks />
        public const uint ComponentType_Lock_RenewLock = 6169;

        /// <remarks />
        public const uint ComponentType_Lock_ExitLock = 6171;

        /// <remarks />
        public const uint ComponentType_Lock_BreakLock = 6173;

        /// <remarks />
        public const uint DeviceType_Lock_InitLock = 6166;

        /// <remarks />
        public const uint DeviceType_Lock_RenewLock = 6169;

        /// <remarks />
        public const uint DeviceType_Lock_ExitLock = 6171;

        /// <remarks />
        public const uint DeviceType_Lock_BreakLock = 6173;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_InitLock = 6166;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_RenewLock = 6169;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_ExitLock = 6171;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_BreakLock = 6173;

        /// <remarks />
        public const uint SoftwareType_Lock_InitLock = 6166;

        /// <remarks />
        public const uint SoftwareType_Lock_RenewLock = 6169;

        /// <remarks />
        public const uint SoftwareType_Lock_ExitLock = 6171;

        /// <remarks />
        public const uint SoftwareType_Lock_BreakLock = 6173;

        /// <remarks />
        public const uint BlockType_Lock_InitLock = 6166;

        /// <remarks />
        public const uint BlockType_Lock_RenewLock = 6169;

        /// <remarks />
        public const uint BlockType_Lock_ExitLock = 6171;

        /// <remarks />
        public const uint BlockType_Lock_BreakLock = 6173;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_InitLock = 6166;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_RenewLock = 6169;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_ExitLock = 6171;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_BreakLock = 6173;

        /// <remarks />
        public const uint NetworkType_Lock_InitLock = 6299;

        /// <remarks />
        public const uint NetworkType_Lock_RenewLock = 6302;

        /// <remarks />
        public const uint NetworkType_Lock_ExitLock = 6304;

        /// <remarks />
        public const uint NetworkType_Lock_BreakLock = 6306;

        /// <remarks />
        public const uint ConnectionPointType_Lock_InitLock = 6166;

        /// <remarks />
        public const uint ConnectionPointType_Lock_RenewLock = 6169;

        /// <remarks />
        public const uint ConnectionPointType_Lock_ExitLock = 6171;

        /// <remarks />
        public const uint ConnectionPointType_Lock_BreakLock = 6173;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_InitLock = 6299;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_RenewLock = 6302;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_ExitLock = 6304;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_BreakLock = 6306;

        /// <remarks />
        public const uint TransferServicesType_TransferToDevice = 6527;

        /// <remarks />
        public const uint TransferServicesType_TransferFromDevice = 6529;

        /// <remarks />
        public const uint TransferServicesType_FetchTransferResultData = 6531;

        /// <remarks />
        public const uint LockingServicesType_InitLock = 6393;

        /// <remarks />
        public const uint LockingServicesType_RenewLock = 6396;

        /// <remarks />
        public const uint LockingServicesType_ExitLock = 6398;

        /// <remarks />
        public const uint LockingServicesType_BreakLock = 6400;

        /// <remarks />
        public const uint SoftwareUpdateType_PrepareForUpdate_Prepare = 19;

        /// <remarks />
        public const uint SoftwareUpdateType_PrepareForUpdate_Abort = 20;

        /// <remarks />
        public const uint SoftwareUpdateType_Installation_Resume = 61;

        /// <remarks />
        public const uint SoftwareUpdateType_Confirmation_Confirm = 112;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters_GenerateFileForRead = 124;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters_GenerateFileForWrite = 127;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters_CloseAndCommit = 130;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer_GenerateFileForRead = 142;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer_GenerateFileForWrite = 145;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer_CloseAndCommit = 148;

        /// <remarks />
        public const uint DirectLoadingType_FileTransfer_GenerateFileForRead = 142;

        /// <remarks />
        public const uint DirectLoadingType_FileTransfer_GenerateFileForWrite = 145;

        /// <remarks />
        public const uint DirectLoadingType_FileTransfer_CloseAndCommit = 148;

        /// <remarks />
        public const uint CachedLoadingType_FileTransfer_GenerateFileForRead = 142;

        /// <remarks />
        public const uint CachedLoadingType_FileTransfer_GenerateFileForWrite = 145;

        /// <remarks />
        public const uint CachedLoadingType_FileTransfer_CloseAndCommit = 148;

        /// <remarks />
        public const uint CachedLoadingType_GetUpdateBehavior = 189;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_CreateDirectory = 195;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_CreateFile = 198;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_DeleteFileSystemObject = 201;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_MoveOrCopy = 203;

        /// <remarks />
        public const uint FileSystemLoadingType_GetUpdateBehavior = 206;

        /// <remarks />
        public const uint FileSystemLoadingType_ValidateFiles = 209;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_Prepare = 228;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_Abort = 229;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_Resume = 230;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallSoftwarePackage = 265;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallFiles = 268;

        /// <remarks />
        public const uint InstallationStateMachineType_Resume = 270;

        /// <remarks />
        public const uint ConfirmationStateMachineType_Confirm = 321;

        /// <remarks />
        public const uint LockingServicesType_InitLockMethodType = 0;

        /// <remarks />
        public const uint LockingServicesType_RenewLockMethodType = 0;

        /// <remarks />
        public const uint LockingServicesType_ExitLockMethodType = 0;

        /// <remarks />
        public const uint LockingServicesType_BreakLockMethodType = 0;

        /// <remarks />
        public const uint TransferServicesType_TransferToDeviceMethodType = 0;

        /// <remarks />
        public const uint TransferServicesType_TransferFromDeviceMethodType = 0;

        /// <remarks />
        public const uint TransferServicesType_FetchTransferResultDataMethodType = 0;

        /// <remarks />
        public const uint CachedLoadingType_GetUpdateBehaviorMethodType = 0;

        /// <remarks />
        public const uint FileSystemLoadingType_ValidateFilesMethodType = 0;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallSoftwarePackageMethodType = 0;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallFilesMethodType = 0;
    }
    #endregion

    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint OPCUADINamespaceMetadata = 15001;

        /// <remarks />
        public const uint DeviceSet = 5001;

        /// <remarks />
        public const uint DeviceFeatures = 15034;

        /// <remarks />
        public const uint NetworkSet = 6078;

        /// <remarks />
        public const uint DeviceTopology = 6094;

        /// <remarks />
        public const uint TopologyElementType_ParameterSet = 5002;

        /// <remarks />
        public const uint TopologyElementType_MethodSet = 5003;

        /// <remarks />
        public const uint TopologyElementType_GroupIdentifier = 6567;

        /// <remarks />
        public const uint TopologyElementType_Identification = 6014;

        /// <remarks />
        public const uint TopologyElementType_Lock = 6161;

        /// <remarks />
        public const uint IDeviceHealthType_DeviceHealthAlarms = 15053;

        /// <remarks />
        public const uint ISupportInfoType_DeviceTypeImage = 15055;

        /// <remarks />
        public const uint ISupportInfoType_Documentation = 15057;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles = 27;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId = 28;

        /// <remarks />
        public const uint ISupportInfoType_ProtocolSupport = 15059;

        /// <remarks />
        public const uint ISupportInfoType_ImageSet = 15061;

        /// <remarks />
        public const uint DeviceType_CPIdentifier = 6571;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_NetworkAddress = 6592;

        /// <remarks />
        public const uint DeviceType_DeviceHealthAlarms = 15105;

        /// <remarks />
        public const uint DeviceType_DeviceTypeImage = 6209;

        /// <remarks />
        public const uint DeviceType_Documentation = 6211;

        /// <remarks />
        public const uint DeviceType_ProtocolSupport = 6213;

        /// <remarks />
        public const uint DeviceType_ImageSet = 6215;

        /// <remarks />
        public const uint ConfigurableObjectType_SupportedTypes = 5004;

        /// <remarks />
        public const uint ConfigurableObjectType_ObjectIdentifier = 6026;

        /// <remarks />
        public const uint FunctionalGroupType_GroupIdentifier = 6027;

        /// <remarks />
        public const uint NetworkType_ProfileIdentifier = 6596;

        /// <remarks />
        public const uint NetworkType_CPIdentifier = 6248;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_NetworkAddress = 6292;

        /// <remarks />
        public const uint NetworkType_Lock = 6294;

        /// <remarks />
        public const uint ConnectionPointType_NetworkAddress = 6354;

        /// <remarks />
        public const uint ConnectionPointType_ProfileIdentifier = 6499;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier = 6599;

        /// <remarks />
        public const uint SoftwareUpdateType_Loading = 2;

        /// <remarks />
        public const uint SoftwareUpdateType_PrepareForUpdate = 4;

        /// <remarks />
        public const uint SoftwareUpdateType_Installation = 40;

        /// <remarks />
        public const uint SoftwareUpdateType_PowerCycle = 76;

        /// <remarks />
        public const uint SoftwareUpdateType_Confirmation = 98;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters = 122;

        /// <remarks />
        public const uint PackageLoadingType_CurrentVersion = 139;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer = 140;

        /// <remarks />
        public const uint CachedLoadingType_PendingVersion = 187;

        /// <remarks />
        public const uint CachedLoadingType_FallbackVersion = 188;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem = 194;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_Idle = 231;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_Preparing = 233;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_PreparedForUpdate = 235;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_Resuming = 237;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_IdleToPreparing = 239;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_PreparingToIdle = 241;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_PreparingToPreparedForUpdate = 243;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_PreparedForUpdateToResuming = 245;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_ResumingToIdle = 247;

        /// <remarks />
        public const uint InstallationStateMachineType_Idle = 271;

        /// <remarks />
        public const uint InstallationStateMachineType_Installing = 273;

        /// <remarks />
        public const uint InstallationStateMachineType_Error = 275;

        /// <remarks />
        public const uint InstallationStateMachineType_IdleToInstalling = 277;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallingToIdle = 279;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallingToError = 281;

        /// <remarks />
        public const uint InstallationStateMachineType_ErrorToIdle = 283;

        /// <remarks />
        public const uint PowerCycleStateMachineType_NotWaitingForPowerCycle = 299;

        /// <remarks />
        public const uint PowerCycleStateMachineType_WaitingForPowerCycle = 301;

        /// <remarks />
        public const uint PowerCycleStateMachineType_NotWaitingForPowerCycleToWaitingForPowerCycle = 303;

        /// <remarks />
        public const uint PowerCycleStateMachineType_WaitingForPowerCycleToNotWaitingForPowerCycle = 305;

        /// <remarks />
        public const uint ConfirmationStateMachineType_NotWaitingForConfirm = 323;

        /// <remarks />
        public const uint ConfirmationStateMachineType_WaitingForConfirm = 325;

        /// <remarks />
        public const uint ConfirmationStateMachineType_NotWaitingForConfirmToWaitingForConfirm = 327;

        /// <remarks />
        public const uint ConfirmationStateMachineType_WaitingForConfirmToNotWaitingForConfirm = 329;

        /// <remarks />
        public const uint FetchResultDataType_Encoding_DefaultBinary = 6551;

        /// <remarks />
        public const uint TransferResultErrorDataType_Encoding_DefaultBinary = 15891;

        /// <remarks />
        public const uint TransferResultDataDataType_Encoding_DefaultBinary = 15892;

        /// <remarks />
        public const uint ParameterResultDataType_Encoding_DefaultBinary = 6554;

        /// <remarks />
        public const uint FetchResultDataType_Encoding_DefaultXml = 6535;

        /// <remarks />
        public const uint TransferResultErrorDataType_Encoding_DefaultXml = 15900;

        /// <remarks />
        public const uint TransferResultDataDataType_Encoding_DefaultXml = 15901;

        /// <remarks />
        public const uint ParameterResultDataType_Encoding_DefaultXml = 6538;

        /// <remarks />
        public const uint FetchResultDataType_Encoding_DefaultJson = 15909;

        /// <remarks />
        public const uint TransferResultErrorDataType_Encoding_DefaultJson = 15910;

        /// <remarks />
        public const uint TransferResultDataDataType_Encoding_DefaultJson = 15911;

        /// <remarks />
        public const uint ParameterResultDataType_Encoding_DefaultJson = 15912;
    }
    #endregion

    #region ObjectType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <remarks />
        public const uint TopologyElementType = 1001;

        /// <remarks />
        public const uint IVendorNameplateType = 15035;

        /// <remarks />
        public const uint ITagNameplateType = 15048;

        /// <remarks />
        public const uint IDeviceHealthType = 15051;

        /// <remarks />
        public const uint ISupportInfoType = 15054;

        /// <remarks />
        public const uint ComponentType = 15063;

        /// <remarks />
        public const uint DeviceType = 1002;

        /// <remarks />
        public const uint SoftwareType = 15106;

        /// <remarks />
        public const uint BlockType = 1003;

        /// <remarks />
        public const uint DeviceHealthDiagnosticAlarmType = 15143;

        /// <remarks />
        public const uint FailureAlarmType = 15292;

        /// <remarks />
        public const uint CheckFunctionAlarmType = 15441;

        /// <remarks />
        public const uint OffSpecAlarmType = 15590;

        /// <remarks />
        public const uint MaintenanceRequiredAlarmType = 15739;

        /// <remarks />
        public const uint ConfigurableObjectType = 1004;

        /// <remarks />
        public const uint BaseLifetimeIndicationType = 473;

        /// <remarks />
        public const uint TimeIndicationType = 474;

        /// <remarks />
        public const uint NumberOfPartsIndicationType = 475;

        /// <remarks />
        public const uint NumberOfUsagesIndicationType = 476;

        /// <remarks />
        public const uint LengthIndicationType = 477;

        /// <remarks />
        public const uint DiameterIndicationType = 478;

        /// <remarks />
        public const uint SubstanceVolumeIndicationType = 479;

        /// <remarks />
        public const uint FunctionalGroupType = 1005;

        /// <remarks />
        public const uint ProtocolType = 1006;

        /// <remarks />
        public const uint IOperationCounterType = 480;

        /// <remarks />
        public const uint NetworkType = 6247;

        /// <remarks />
        public const uint ConnectionPointType = 6308;

        /// <remarks />
        public const uint TransferServicesType = 6526;

        /// <remarks />
        public const uint LockingServicesType = 6388;

        /// <remarks />
        public const uint SoftwareUpdateType = 1;

        /// <remarks />
        public const uint SoftwareLoadingType = 135;

        /// <remarks />
        public const uint PackageLoadingType = 137;

        /// <remarks />
        public const uint DirectLoadingType = 153;

        /// <remarks />
        public const uint CachedLoadingType = 171;

        /// <remarks />
        public const uint FileSystemLoadingType = 192;

        /// <remarks />
        public const uint SoftwareVersionType = 212;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType = 213;

        /// <remarks />
        public const uint InstallationStateMachineType = 249;

        /// <remarks />
        public const uint PowerCycleStateMachineType = 285;

        /// <remarks />
        public const uint ConfirmationStateMachineType = 307;
    }
    #endregion

    #region ReferenceType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ReferenceTypes
    {
        /// <remarks />
        public const uint ConnectsTo = 6030;

        /// <remarks />
        public const uint ConnectsToParent = 6467;

        /// <remarks />
        public const uint IsOnline = 6031;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint OPCUADINamespaceMetadata_NamespaceUri = 15002;

        /// <remarks />
        public const uint OPCUADINamespaceMetadata_NamespaceVersion = 15003;

        /// <remarks />
        public const uint OPCUADINamespaceMetadata_NamespacePublicationDate = 15004;

        /// <remarks />
        public const uint OPCUADINamespaceMetadata_IsNamespaceSubset = 15005;

        /// <remarks />
        public const uint OPCUADINamespaceMetadata_StaticNodeIdTypes = 15006;

        /// <remarks />
        public const uint OPCUADINamespaceMetadata_StaticNumericNodeIdRange = 15007;

        /// <remarks />
        public const uint OPCUADINamespaceMetadata_StaticStringNodeIdPattern = 15008;

        /// <remarks />
        public const uint TopologyElementType_ParameterSet_ParameterIdentifier = 6017;

        /// <remarks />
        public const uint TopologyElementType_Lock_Locked = 6468;

        /// <remarks />
        public const uint TopologyElementType_Lock_LockingClient = 6163;

        /// <remarks />
        public const uint TopologyElementType_Lock_LockingUser = 6164;

        /// <remarks />
        public const uint TopologyElementType_Lock_RemainingLockTime = 6165;

        /// <remarks />
        public const uint TopologyElementType_Lock_InitLock_InputArguments = 6167;

        /// <remarks />
        public const uint TopologyElementType_Lock_InitLock_OutputArguments = 6168;

        /// <remarks />
        public const uint TopologyElementType_Lock_RenewLock_OutputArguments = 6170;

        /// <remarks />
        public const uint TopologyElementType_Lock_ExitLock_OutputArguments = 6172;

        /// <remarks />
        public const uint TopologyElementType_Lock_BreakLock_OutputArguments = 6174;

        /// <remarks />
        public const uint IVendorNameplateType_Manufacturer = 15036;

        /// <remarks />
        public const uint IVendorNameplateType_ManufacturerUri = 15037;

        /// <remarks />
        public const uint IVendorNameplateType_Model = 15038;

        /// <remarks />
        public const uint IVendorNameplateType_HardwareRevision = 15039;

        /// <remarks />
        public const uint IVendorNameplateType_SoftwareRevision = 15040;

        /// <remarks />
        public const uint IVendorNameplateType_DeviceRevision = 15041;

        /// <remarks />
        public const uint IVendorNameplateType_ProductCode = 15042;

        /// <remarks />
        public const uint IVendorNameplateType_DeviceManual = 15043;

        /// <remarks />
        public const uint IVendorNameplateType_DeviceClass = 15044;

        /// <remarks />
        public const uint IVendorNameplateType_SerialNumber = 15045;

        /// <remarks />
        public const uint IVendorNameplateType_ProductInstanceUri = 15046;

        /// <remarks />
        public const uint IVendorNameplateType_RevisionCounter = 15047;

        /// <remarks />
        public const uint IVendorNameplateType_SoftwareReleaseDate = 23;

        /// <remarks />
        public const uint IVendorNameplateType_PatchIdentifiers = 24;

        /// <remarks />
        public const uint ITagNameplateType_AssetId = 15049;

        /// <remarks />
        public const uint ITagNameplateType_ComponentName = 15050;

        /// <remarks />
        public const uint IDeviceHealthType_DeviceHealth = 15052;

        /// <remarks />
        public const uint ISupportInfoType_DeviceTypeImage_ImageIdentifier = 15056;

        /// <remarks />
        public const uint ISupportInfoType_Documentation_DocumentIdentifier = 15058;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Size = 29;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Writable = 30;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_UserWritable = 31;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_OpenCount = 32;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Open_InputArguments = 37;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Open_OutputArguments = 38;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Close_InputArguments = 62;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Read_InputArguments = 64;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Read_OutputArguments = 65;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_Write_InputArguments = 67;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_GetPosition_InputArguments = 69;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_GetPosition_OutputArguments = 70;

        /// <remarks />
        public const uint ISupportInfoType_DocumentationFiles_DocumentFileId_SetPosition_InputArguments = 72;

        /// <remarks />
        public const uint ISupportInfoType_ProtocolSupport_ProtocolSupportIdentifier = 15060;

        /// <remarks />
        public const uint ISupportInfoType_ImageSet_ImageIdentifier = 15062;

        /// <remarks />
        public const uint ComponentType_Lock_Locked = 6468;

        /// <remarks />
        public const uint ComponentType_Lock_LockingClient = 6163;

        /// <remarks />
        public const uint ComponentType_Lock_LockingUser = 6164;

        /// <remarks />
        public const uint ComponentType_Lock_RemainingLockTime = 6165;

        /// <remarks />
        public const uint ComponentType_Lock_InitLock_InputArguments = 6167;

        /// <remarks />
        public const uint ComponentType_Lock_InitLock_OutputArguments = 6168;

        /// <remarks />
        public const uint ComponentType_Lock_RenewLock_OutputArguments = 6170;

        /// <remarks />
        public const uint ComponentType_Lock_ExitLock_OutputArguments = 6172;

        /// <remarks />
        public const uint ComponentType_Lock_BreakLock_OutputArguments = 6174;

        /// <remarks />
        public const uint ComponentType_Manufacturer = 15086;

        /// <remarks />
        public const uint ComponentType_ManufacturerUri = 15087;

        /// <remarks />
        public const uint ComponentType_Model = 15088;

        /// <remarks />
        public const uint ComponentType_HardwareRevision = 15089;

        /// <remarks />
        public const uint ComponentType_SoftwareRevision = 15090;

        /// <remarks />
        public const uint ComponentType_DeviceRevision = 15091;

        /// <remarks />
        public const uint ComponentType_ProductCode = 15092;

        /// <remarks />
        public const uint ComponentType_DeviceManual = 15093;

        /// <remarks />
        public const uint ComponentType_DeviceClass = 15094;

        /// <remarks />
        public const uint ComponentType_SerialNumber = 15095;

        /// <remarks />
        public const uint ComponentType_ProductInstanceUri = 15096;

        /// <remarks />
        public const uint ComponentType_RevisionCounter = 15097;

        /// <remarks />
        public const uint ComponentType_AssetId = 15098;

        /// <remarks />
        public const uint ComponentType_ComponentName = 15099;

        /// <remarks />
        public const uint DeviceType_Lock_Locked = 6468;

        /// <remarks />
        public const uint DeviceType_Lock_LockingClient = 6163;

        /// <remarks />
        public const uint DeviceType_Lock_LockingUser = 6164;

        /// <remarks />
        public const uint DeviceType_Lock_RemainingLockTime = 6165;

        /// <remarks />
        public const uint DeviceType_Lock_InitLock_InputArguments = 6167;

        /// <remarks />
        public const uint DeviceType_Lock_InitLock_OutputArguments = 6168;

        /// <remarks />
        public const uint DeviceType_Lock_RenewLock_OutputArguments = 6170;

        /// <remarks />
        public const uint DeviceType_Lock_ExitLock_OutputArguments = 6172;

        /// <remarks />
        public const uint DeviceType_Lock_BreakLock_OutputArguments = 6174;

        /// <remarks />
        public const uint DeviceType_Manufacturer = 6003;

        /// <remarks />
        public const uint DeviceType_ManufacturerUri = 15100;

        /// <remarks />
        public const uint DeviceType_Model = 6004;

        /// <remarks />
        public const uint DeviceType_HardwareRevision = 6008;

        /// <remarks />
        public const uint DeviceType_SoftwareRevision = 6007;

        /// <remarks />
        public const uint DeviceType_DeviceRevision = 6006;

        /// <remarks />
        public const uint DeviceType_ProductCode = 15101;

        /// <remarks />
        public const uint DeviceType_DeviceManual = 6005;

        /// <remarks />
        public const uint DeviceType_DeviceClass = 6470;

        /// <remarks />
        public const uint DeviceType_SerialNumber = 6001;

        /// <remarks />
        public const uint DeviceType_ProductInstanceUri = 15102;

        /// <remarks />
        public const uint DeviceType_RevisionCounter = 6002;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_Locked = 6468;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_LockingClient = 6163;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_LockingUser = 6164;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_RemainingLockTime = 6165;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_InitLock_InputArguments = 6167;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_InitLock_OutputArguments = 6168;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_RenewLock_OutputArguments = 6170;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_ExitLock_OutputArguments = 6172;

        /// <remarks />
        public const uint DeviceType_CPIdentifier_Lock_BreakLock_OutputArguments = 6174;

        /// <remarks />
        public const uint DeviceType_DeviceHealth = 6208;

        /// <remarks />
        public const uint DeviceType_DeviceTypeImage_ImageIdentifier = 6210;

        /// <remarks />
        public const uint DeviceType_Documentation_DocumentIdentifier = 6212;

        /// <remarks />
        public const uint DeviceType_ProtocolSupport_ProtocolSupportIdentifier = 6214;

        /// <remarks />
        public const uint DeviceType_ImageSet_ImageIdentifier = 6216;

        /// <remarks />
        public const uint SoftwareType_Lock_Locked = 6468;

        /// <remarks />
        public const uint SoftwareType_Lock_LockingClient = 6163;

        /// <remarks />
        public const uint SoftwareType_Lock_LockingUser = 6164;

        /// <remarks />
        public const uint SoftwareType_Lock_RemainingLockTime = 6165;

        /// <remarks />
        public const uint SoftwareType_Lock_InitLock_InputArguments = 6167;

        /// <remarks />
        public const uint SoftwareType_Lock_InitLock_OutputArguments = 6168;

        /// <remarks />
        public const uint SoftwareType_Lock_RenewLock_OutputArguments = 6170;

        /// <remarks />
        public const uint SoftwareType_Lock_ExitLock_OutputArguments = 6172;

        /// <remarks />
        public const uint SoftwareType_Lock_BreakLock_OutputArguments = 6174;

        /// <remarks />
        public const uint SoftwareType_Manufacturer = 15129;

        /// <remarks />
        public const uint SoftwareType_Model = 15131;

        /// <remarks />
        public const uint SoftwareType_SoftwareRevision = 15133;

        /// <remarks />
        public const uint BlockType_Lock_Locked = 6468;

        /// <remarks />
        public const uint BlockType_Lock_LockingClient = 6163;

        /// <remarks />
        public const uint BlockType_Lock_LockingUser = 6164;

        /// <remarks />
        public const uint BlockType_Lock_RemainingLockTime = 6165;

        /// <remarks />
        public const uint BlockType_Lock_InitLock_InputArguments = 6167;

        /// <remarks />
        public const uint BlockType_Lock_InitLock_OutputArguments = 6168;

        /// <remarks />
        public const uint BlockType_Lock_RenewLock_OutputArguments = 6170;

        /// <remarks />
        public const uint BlockType_Lock_ExitLock_OutputArguments = 6172;

        /// <remarks />
        public const uint BlockType_Lock_BreakLock_OutputArguments = 6174;

        /// <remarks />
        public const uint BlockType_RevisionCounter = 6009;

        /// <remarks />
        public const uint BlockType_ActualMode = 6010;

        /// <remarks />
        public const uint BlockType_PermittedMode = 6011;

        /// <remarks />
        public const uint BlockType_NormalMode = 6012;

        /// <remarks />
        public const uint BlockType_TargetMode = 6013;

        /// <remarks />
        public const uint LifetimeVariableType_StartValue = 469;

        /// <remarks />
        public const uint LifetimeVariableType_LimitValue = 470;

        /// <remarks />
        public const uint LifetimeVariableType_Indication = 471;

        /// <remarks />
        public const uint LifetimeVariableType_WarningValues = 472;

        /// <remarks />
        public const uint FunctionalGroupType_GroupIdentifier_UIElement = 6242;

        /// <remarks />
        public const uint FunctionalGroupType_UIElement = 6243;

        /// <remarks />
        public const uint DeviceHealthEnumeration_EnumStrings = 6450;

        /// <remarks />
        public const uint IOperationCounterType_PowerOnDuration = 481;

        /// <remarks />
        public const uint IOperationCounterType_OperationDuration = 482;

        /// <remarks />
        public const uint IOperationCounterType_OperationCycleCounter = 483;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_Locked = 6468;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_LockingClient = 6163;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_LockingUser = 6164;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_RemainingLockTime = 6165;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_InitLock_InputArguments = 6167;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_InitLock_OutputArguments = 6168;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_RenewLock_OutputArguments = 6170;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_ExitLock_OutputArguments = 6172;

        /// <remarks />
        public const uint NetworkType_CPIdentifier_Lock_BreakLock_OutputArguments = 6174;

        /// <remarks />
        public const uint NetworkType_Lock_Locked = 6497;

        /// <remarks />
        public const uint NetworkType_Lock_LockingClient = 6296;

        /// <remarks />
        public const uint NetworkType_Lock_LockingUser = 6297;

        /// <remarks />
        public const uint NetworkType_Lock_RemainingLockTime = 6298;

        /// <remarks />
        public const uint NetworkType_Lock_InitLock_InputArguments = 6300;

        /// <remarks />
        public const uint NetworkType_Lock_InitLock_OutputArguments = 6301;

        /// <remarks />
        public const uint NetworkType_Lock_RenewLock_OutputArguments = 6303;

        /// <remarks />
        public const uint NetworkType_Lock_ExitLock_OutputArguments = 6305;

        /// <remarks />
        public const uint NetworkType_Lock_BreakLock_OutputArguments = 6307;

        /// <remarks />
        public const uint ConnectionPointType_Lock_Locked = 6468;

        /// <remarks />
        public const uint ConnectionPointType_Lock_LockingClient = 6163;

        /// <remarks />
        public const uint ConnectionPointType_Lock_LockingUser = 6164;

        /// <remarks />
        public const uint ConnectionPointType_Lock_RemainingLockTime = 6165;

        /// <remarks />
        public const uint ConnectionPointType_Lock_InitLock_InputArguments = 6167;

        /// <remarks />
        public const uint ConnectionPointType_Lock_InitLock_OutputArguments = 6168;

        /// <remarks />
        public const uint ConnectionPointType_Lock_RenewLock_OutputArguments = 6170;

        /// <remarks />
        public const uint ConnectionPointType_Lock_ExitLock_OutputArguments = 6172;

        /// <remarks />
        public const uint ConnectionPointType_Lock_BreakLock_OutputArguments = 6174;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_Locked = 6497;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_LockingClient = 6296;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_LockingUser = 6297;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_RemainingLockTime = 6298;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_InitLock_InputArguments = 6300;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_InitLock_OutputArguments = 6301;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_RenewLock_OutputArguments = 6303;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_ExitLock_OutputArguments = 6305;

        /// <remarks />
        public const uint ConnectionPointType_NetworkIdentifier_Lock_BreakLock_OutputArguments = 6307;

        /// <remarks />
        public const uint TransferServicesType_TransferToDevice_OutputArguments = 6528;

        /// <remarks />
        public const uint TransferServicesType_TransferFromDevice_OutputArguments = 6530;

        /// <remarks />
        public const uint TransferServicesType_FetchTransferResultData_InputArguments = 6532;

        /// <remarks />
        public const uint TransferServicesType_FetchTransferResultData_OutputArguments = 6533;

        /// <remarks />
        public const uint MaxInactiveLockTime = 6387;

        /// <remarks />
        public const uint LockingServicesType_DefaultInstanceBrowseName = 15890;

        /// <remarks />
        public const uint LockingServicesType_Locked = 6534;

        /// <remarks />
        public const uint LockingServicesType_LockingClient = 6390;

        /// <remarks />
        public const uint LockingServicesType_LockingUser = 6391;

        /// <remarks />
        public const uint LockingServicesType_RemainingLockTime = 6392;

        /// <remarks />
        public const uint LockingServicesType_InitLock_InputArguments = 6394;

        /// <remarks />
        public const uint LockingServicesType_InitLock_OutputArguments = 6395;

        /// <remarks />
        public const uint LockingServicesType_RenewLock_OutputArguments = 6397;

        /// <remarks />
        public const uint LockingServicesType_ExitLock_OutputArguments = 6399;

        /// <remarks />
        public const uint LockingServicesType_BreakLock_OutputArguments = 6401;

        /// <remarks />
        public const uint SoftwareUpdateType_PrepareForUpdate_CurrentState = 5;

        /// <remarks />
        public const uint SoftwareUpdateType_PrepareForUpdate_CurrentState_Id = 6;

        /// <remarks />
        public const uint SoftwareUpdateType_Installation_CurrentState = 41;

        /// <remarks />
        public const uint SoftwareUpdateType_Installation_CurrentState_Id = 42;

        /// <remarks />
        public const uint SoftwareUpdateType_Installation_InstallSoftwarePackage_InputArguments = 266;

        /// <remarks />
        public const uint SoftwareUpdateType_Installation_InstallFiles_InputArguments = 269;

        /// <remarks />
        public const uint SoftwareUpdateType_PowerCycle_CurrentState = 77;

        /// <remarks />
        public const uint SoftwareUpdateType_PowerCycle_CurrentState_Id = 78;

        /// <remarks />
        public const uint SoftwareUpdateType_Confirmation_CurrentState = 99;

        /// <remarks />
        public const uint SoftwareUpdateType_Confirmation_CurrentState_Id = 100;

        /// <remarks />
        public const uint SoftwareUpdateType_Confirmation_ConfirmationTimeout = 113;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters_ClientProcessingTimeout = 123;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters_GenerateFileForRead_InputArguments = 125;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters_GenerateFileForRead_OutputArguments = 126;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters_GenerateFileForWrite_InputArguments = 128;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters_GenerateFileForWrite_OutputArguments = 129;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters_CloseAndCommit_InputArguments = 131;

        /// <remarks />
        public const uint SoftwareUpdateType_Parameters_CloseAndCommit_OutputArguments = 132;

        /// <remarks />
        public const uint SoftwareUpdateType_UpdateStatus = 133;

        /// <remarks />
        public const uint SoftwareUpdateType_VendorErrorCode = 402;

        /// <remarks />
        public const uint SoftwareUpdateType_DefaultInstanceBrowseName = 134;

        /// <remarks />
        public const uint SoftwareLoadingType_UpdateKey = 136;

        /// <remarks />
        public const uint PackageLoadingType_CurrentVersion_Manufacturer = 345;

        /// <remarks />
        public const uint PackageLoadingType_CurrentVersion_ManufacturerUri = 346;

        /// <remarks />
        public const uint PackageLoadingType_CurrentVersion_SoftwareRevision = 347;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer_ClientProcessingTimeout = 141;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer_GenerateFileForRead_InputArguments = 143;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer_GenerateFileForRead_OutputArguments = 144;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer_GenerateFileForWrite_InputArguments = 146;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer_GenerateFileForWrite_OutputArguments = 147;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer_CloseAndCommit_InputArguments = 149;

        /// <remarks />
        public const uint PackageLoadingType_FileTransfer_CloseAndCommit_OutputArguments = 150;

        /// <remarks />
        public const uint PackageLoadingType_ErrorMessage = 151;

        /// <remarks />
        public const uint PackageLoadingType_WriteBlockSize = 152;

        /// <remarks />
        public const uint DirectLoadingType_CurrentVersion_Manufacturer = 345;

        /// <remarks />
        public const uint DirectLoadingType_CurrentVersion_ManufacturerUri = 346;

        /// <remarks />
        public const uint DirectLoadingType_CurrentVersion_SoftwareRevision = 347;

        /// <remarks />
        public const uint DirectLoadingType_FileTransfer_ClientProcessingTimeout = 141;

        /// <remarks />
        public const uint DirectLoadingType_FileTransfer_GenerateFileForRead_InputArguments = 143;

        /// <remarks />
        public const uint DirectLoadingType_FileTransfer_GenerateFileForRead_OutputArguments = 144;

        /// <remarks />
        public const uint DirectLoadingType_FileTransfer_GenerateFileForWrite_InputArguments = 146;

        /// <remarks />
        public const uint DirectLoadingType_FileTransfer_GenerateFileForWrite_OutputArguments = 147;

        /// <remarks />
        public const uint DirectLoadingType_FileTransfer_CloseAndCommit_InputArguments = 149;

        /// <remarks />
        public const uint DirectLoadingType_FileTransfer_CloseAndCommit_OutputArguments = 150;

        /// <remarks />
        public const uint DirectLoadingType_UpdateBehavior = 169;

        /// <remarks />
        public const uint DirectLoadingType_WriteTimeout = 170;

        /// <remarks />
        public const uint CachedLoadingType_CurrentVersion_Manufacturer = 345;

        /// <remarks />
        public const uint CachedLoadingType_CurrentVersion_ManufacturerUri = 346;

        /// <remarks />
        public const uint CachedLoadingType_CurrentVersion_SoftwareRevision = 347;

        /// <remarks />
        public const uint CachedLoadingType_FileTransfer_ClientProcessingTimeout = 141;

        /// <remarks />
        public const uint CachedLoadingType_FileTransfer_GenerateFileForRead_InputArguments = 143;

        /// <remarks />
        public const uint CachedLoadingType_FileTransfer_GenerateFileForRead_OutputArguments = 144;

        /// <remarks />
        public const uint CachedLoadingType_FileTransfer_GenerateFileForWrite_InputArguments = 146;

        /// <remarks />
        public const uint CachedLoadingType_FileTransfer_GenerateFileForWrite_OutputArguments = 147;

        /// <remarks />
        public const uint CachedLoadingType_FileTransfer_CloseAndCommit_InputArguments = 149;

        /// <remarks />
        public const uint CachedLoadingType_FileTransfer_CloseAndCommit_OutputArguments = 150;

        /// <remarks />
        public const uint CachedLoadingType_PendingVersion_Manufacturer = 366;

        /// <remarks />
        public const uint CachedLoadingType_PendingVersion_ManufacturerUri = 367;

        /// <remarks />
        public const uint CachedLoadingType_PendingVersion_SoftwareRevision = 368;

        /// <remarks />
        public const uint CachedLoadingType_FallbackVersion_Manufacturer = 373;

        /// <remarks />
        public const uint CachedLoadingType_FallbackVersion_ManufacturerUri = 374;

        /// <remarks />
        public const uint CachedLoadingType_FallbackVersion_SoftwareRevision = 375;

        /// <remarks />
        public const uint CachedLoadingType_GetUpdateBehavior_InputArguments = 190;

        /// <remarks />
        public const uint CachedLoadingType_GetUpdateBehavior_OutputArguments = 191;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_CreateDirectory_InputArguments = 196;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_CreateDirectory_OutputArguments = 197;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_CreateFile_InputArguments = 199;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_CreateFile_OutputArguments = 200;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_DeleteFileSystemObject_InputArguments = 202;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_MoveOrCopy_InputArguments = 204;

        /// <remarks />
        public const uint FileSystemLoadingType_FileSystem_MoveOrCopy_OutputArguments = 205;

        /// <remarks />
        public const uint FileSystemLoadingType_GetUpdateBehavior_InputArguments = 207;

        /// <remarks />
        public const uint FileSystemLoadingType_GetUpdateBehavior_OutputArguments = 208;

        /// <remarks />
        public const uint FileSystemLoadingType_ValidateFiles_InputArguments = 210;

        /// <remarks />
        public const uint FileSystemLoadingType_ValidateFiles_OutputArguments = 211;

        /// <remarks />
        public const uint SoftwareVersionType_Manufacturer = 380;

        /// <remarks />
        public const uint SoftwareVersionType_ManufacturerUri = 381;

        /// <remarks />
        public const uint SoftwareVersionType_SoftwareRevision = 382;

        /// <remarks />
        public const uint SoftwareVersionType_PatchIdentifiers = 383;

        /// <remarks />
        public const uint SoftwareVersionType_ReleaseDate = 384;

        /// <remarks />
        public const uint SoftwareVersionType_ChangeLogReference = 385;

        /// <remarks />
        public const uint SoftwareVersionType_Hash = 386;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_PercentComplete = 227;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_Idle_StateNumber = 232;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_Preparing_StateNumber = 234;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_PreparedForUpdate_StateNumber = 236;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_Resuming_StateNumber = 238;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_IdleToPreparing_TransitionNumber = 240;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_PreparingToIdle_TransitionNumber = 242;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_PreparingToPreparedForUpdate_TransitionNumber = 244;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_PreparedForUpdateToResuming_TransitionNumber = 246;

        /// <remarks />
        public const uint PrepareForUpdateStateMachineType_ResumingToIdle_TransitionNumber = 248;

        /// <remarks />
        public const uint InstallationStateMachineType_PercentComplete = 263;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallationDelay = 264;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallSoftwarePackage_InputArguments = 266;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallFiles_InputArguments = 269;

        /// <remarks />
        public const uint InstallationStateMachineType_Idle_StateNumber = 272;

        /// <remarks />
        public const uint InstallationStateMachineType_Installing_StateNumber = 274;

        /// <remarks />
        public const uint InstallationStateMachineType_Error_StateNumber = 276;

        /// <remarks />
        public const uint InstallationStateMachineType_IdleToInstalling_TransitionNumber = 387;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallingToIdle_TransitionNumber = 280;

        /// <remarks />
        public const uint InstallationStateMachineType_InstallingToError_TransitionNumber = 282;

        /// <remarks />
        public const uint InstallationStateMachineType_ErrorToIdle_TransitionNumber = 284;

        /// <remarks />
        public const uint PowerCycleStateMachineType_NotWaitingForPowerCycle_StateNumber = 300;

        /// <remarks />
        public const uint PowerCycleStateMachineType_WaitingForPowerCycle_StateNumber = 302;

        /// <remarks />
        public const uint PowerCycleStateMachineType_NotWaitingForPowerCycleToWaitingForPowerCycle_TransitionNumber = 304;

        /// <remarks />
        public const uint PowerCycleStateMachineType_WaitingForPowerCycleToNotWaitingForPowerCycle_TransitionNumber = 306;

        /// <remarks />
        public const uint ConfirmationStateMachineType_ConfirmationTimeout = 322;

        /// <remarks />
        public const uint ConfirmationStateMachineType_NotWaitingForConfirm_StateNumber = 324;

        /// <remarks />
        public const uint ConfirmationStateMachineType_WaitingForConfirm_StateNumber = 326;

        /// <remarks />
        public const uint ConfirmationStateMachineType_NotWaitingForConfirmToWaitingForConfirm_TransitionNumber = 328;

        /// <remarks />
        public const uint ConfirmationStateMachineType_WaitingForConfirmToNotWaitingForConfirm_TransitionNumber = 330;

        /// <remarks />
        public const uint SoftwareVersionFileType_EnumStrings = 332;

        /// <remarks />
        public const uint UpdateBehavior_OptionSetValues = 388;

        /// <remarks />
        public const uint OpcUaDi_BinarySchema = 6435;

        /// <remarks />
        public const uint OpcUaDi_XmlSchema = 6423;
    }
    #endregion

    #region VariableType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableTypes
    {
        /// <remarks />
        public const uint LifetimeVariableType = 468;

        /// <remarks />
        public const uint UIElementType = 6246;
    }
    #endregion

    #region DataType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId DeviceHealthEnumeration = new ExpandedNodeId(Opc.Ua.DI.DataTypes.DeviceHealthEnumeration, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FetchResultDataType = new ExpandedNodeId(Opc.Ua.DI.DataTypes.FetchResultDataType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferResultErrorDataType = new ExpandedNodeId(Opc.Ua.DI.DataTypes.TransferResultErrorDataType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferResultDataDataType = new ExpandedNodeId(Opc.Ua.DI.DataTypes.TransferResultDataDataType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ParameterResultDataType = new ExpandedNodeId(Opc.Ua.DI.DataTypes.ParameterResultDataType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareVersionFileType = new ExpandedNodeId(Opc.Ua.DI.DataTypes.SoftwareVersionFileType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId UpdateBehavior = new ExpandedNodeId(Opc.Ua.DI.DataTypes.UpdateBehavior, Opc.Ua.DI.Namespaces.OpcUaDI);
    }
    #endregion

    #region Method Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class MethodIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.TopologyElementType_Lock_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.TopologyElementType_Lock_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.TopologyElementType_Lock_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.TopologyElementType_Lock_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Open = new ExpandedNodeId(Opc.Ua.DI.Methods.ISupportInfoType_DocumentationFiles_DocumentFileId_Open, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Close = new ExpandedNodeId(Opc.Ua.DI.Methods.ISupportInfoType_DocumentationFiles_DocumentFileId_Close, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Read = new ExpandedNodeId(Opc.Ua.DI.Methods.ISupportInfoType_DocumentationFiles_DocumentFileId_Read, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Write = new ExpandedNodeId(Opc.Ua.DI.Methods.ISupportInfoType_DocumentationFiles_DocumentFileId_Write, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_GetPosition = new ExpandedNodeId(Opc.Ua.DI.Methods.ISupportInfoType_DocumentationFiles_DocumentFileId_GetPosition, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_SetPosition = new ExpandedNodeId(Opc.Ua.DI.Methods.ISupportInfoType_DocumentationFiles_DocumentFileId_SetPosition, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ComponentType_Lock_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ComponentType_Lock_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ComponentType_Lock_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ComponentType_Lock_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.DeviceType_Lock_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.DeviceType_Lock_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.DeviceType_Lock_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.DeviceType_Lock_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.DeviceType_CPIdentifier_Lock_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.DeviceType_CPIdentifier_Lock_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.DeviceType_CPIdentifier_Lock_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.DeviceType_CPIdentifier_Lock_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareType_Lock_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareType_Lock_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareType_Lock_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareType_Lock_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.BlockType_Lock_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.BlockType_Lock_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.BlockType_Lock_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.BlockType_Lock_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.NetworkType_CPIdentifier_Lock_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.NetworkType_CPIdentifier_Lock_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.NetworkType_CPIdentifier_Lock_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.NetworkType_CPIdentifier_Lock_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.NetworkType_Lock_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.NetworkType_Lock_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.NetworkType_Lock_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.NetworkType_Lock_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ConnectionPointType_Lock_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ConnectionPointType_Lock_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ConnectionPointType_Lock_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ConnectionPointType_Lock_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ConnectionPointType_NetworkIdentifier_Lock_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ConnectionPointType_NetworkIdentifier_Lock_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ConnectionPointType_NetworkIdentifier_Lock_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.ConnectionPointType_NetworkIdentifier_Lock_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType_TransferToDevice = new ExpandedNodeId(Opc.Ua.DI.Methods.TransferServicesType_TransferToDevice, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType_TransferFromDevice = new ExpandedNodeId(Opc.Ua.DI.Methods.TransferServicesType_TransferFromDevice, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType_FetchTransferResultData = new ExpandedNodeId(Opc.Ua.DI.Methods.TransferServicesType_FetchTransferResultData, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_InitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.LockingServicesType_InitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_RenewLock = new ExpandedNodeId(Opc.Ua.DI.Methods.LockingServicesType_RenewLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_ExitLock = new ExpandedNodeId(Opc.Ua.DI.Methods.LockingServicesType_ExitLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_BreakLock = new ExpandedNodeId(Opc.Ua.DI.Methods.LockingServicesType_BreakLock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_PrepareForUpdate_Prepare = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareUpdateType_PrepareForUpdate_Prepare, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_PrepareForUpdate_Abort = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareUpdateType_PrepareForUpdate_Abort, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Installation_Resume = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareUpdateType_Installation_Resume, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Confirmation_Confirm = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareUpdateType_Confirmation_Confirm, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters_GenerateFileForRead = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareUpdateType_Parameters_GenerateFileForRead, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters_GenerateFileForWrite = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareUpdateType_Parameters_GenerateFileForWrite, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters_CloseAndCommit = new ExpandedNodeId(Opc.Ua.DI.Methods.SoftwareUpdateType_Parameters_CloseAndCommit, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer_GenerateFileForRead = new ExpandedNodeId(Opc.Ua.DI.Methods.PackageLoadingType_FileTransfer_GenerateFileForRead, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer_GenerateFileForWrite = new ExpandedNodeId(Opc.Ua.DI.Methods.PackageLoadingType_FileTransfer_GenerateFileForWrite, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer_CloseAndCommit = new ExpandedNodeId(Opc.Ua.DI.Methods.PackageLoadingType_FileTransfer_CloseAndCommit, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_FileTransfer_GenerateFileForRead = new ExpandedNodeId(Opc.Ua.DI.Methods.DirectLoadingType_FileTransfer_GenerateFileForRead, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_FileTransfer_GenerateFileForWrite = new ExpandedNodeId(Opc.Ua.DI.Methods.DirectLoadingType_FileTransfer_GenerateFileForWrite, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_FileTransfer_CloseAndCommit = new ExpandedNodeId(Opc.Ua.DI.Methods.DirectLoadingType_FileTransfer_CloseAndCommit, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FileTransfer_GenerateFileForRead = new ExpandedNodeId(Opc.Ua.DI.Methods.CachedLoadingType_FileTransfer_GenerateFileForRead, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FileTransfer_GenerateFileForWrite = new ExpandedNodeId(Opc.Ua.DI.Methods.CachedLoadingType_FileTransfer_GenerateFileForWrite, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FileTransfer_CloseAndCommit = new ExpandedNodeId(Opc.Ua.DI.Methods.CachedLoadingType_FileTransfer_CloseAndCommit, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_GetUpdateBehavior = new ExpandedNodeId(Opc.Ua.DI.Methods.CachedLoadingType_GetUpdateBehavior, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_CreateDirectory = new ExpandedNodeId(Opc.Ua.DI.Methods.FileSystemLoadingType_FileSystem_CreateDirectory, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_CreateFile = new ExpandedNodeId(Opc.Ua.DI.Methods.FileSystemLoadingType_FileSystem_CreateFile, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_DeleteFileSystemObject = new ExpandedNodeId(Opc.Ua.DI.Methods.FileSystemLoadingType_FileSystem_DeleteFileSystemObject, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_MoveOrCopy = new ExpandedNodeId(Opc.Ua.DI.Methods.FileSystemLoadingType_FileSystem_MoveOrCopy, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_GetUpdateBehavior = new ExpandedNodeId(Opc.Ua.DI.Methods.FileSystemLoadingType_GetUpdateBehavior, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_ValidateFiles = new ExpandedNodeId(Opc.Ua.DI.Methods.FileSystemLoadingType_ValidateFiles, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_Prepare = new ExpandedNodeId(Opc.Ua.DI.Methods.PrepareForUpdateStateMachineType_Prepare, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_Abort = new ExpandedNodeId(Opc.Ua.DI.Methods.PrepareForUpdateStateMachineType_Abort, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_Resume = new ExpandedNodeId(Opc.Ua.DI.Methods.PrepareForUpdateStateMachineType_Resume, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallSoftwarePackage = new ExpandedNodeId(Opc.Ua.DI.Methods.InstallationStateMachineType_InstallSoftwarePackage, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallFiles = new ExpandedNodeId(Opc.Ua.DI.Methods.InstallationStateMachineType_InstallFiles, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_Resume = new ExpandedNodeId(Opc.Ua.DI.Methods.InstallationStateMachineType_Resume, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType_Confirm = new ExpandedNodeId(Opc.Ua.DI.Methods.ConfirmationStateMachineType_Confirm, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_InitLockMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.LockingServicesType_InitLockMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_RenewLockMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.LockingServicesType_RenewLockMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_ExitLockMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.LockingServicesType_ExitLockMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_BreakLockMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.LockingServicesType_BreakLockMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType_TransferToDeviceMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.TransferServicesType_TransferToDeviceMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType_TransferFromDeviceMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.TransferServicesType_TransferFromDeviceMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType_FetchTransferResultDataMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.TransferServicesType_FetchTransferResultDataMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_GetUpdateBehaviorMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.CachedLoadingType_GetUpdateBehaviorMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_ValidateFilesMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.FileSystemLoadingType_ValidateFilesMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallSoftwarePackageMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.InstallationStateMachineType_InstallSoftwarePackageMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallFilesMethodType = new ExpandedNodeId(Opc.Ua.DI.Methods.InstallationStateMachineType_InstallFilesMethodType, Opc.Ua.DI.Namespaces.OpcUaDI);
    }
    #endregion

    #region Object Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId OPCUADINamespaceMetadata = new ExpandedNodeId(Opc.Ua.DI.Objects.OPCUADINamespaceMetadata, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceSet = new ExpandedNodeId(Opc.Ua.DI.Objects.DeviceSet, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceFeatures = new ExpandedNodeId(Opc.Ua.DI.Objects.DeviceFeatures, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkSet = new ExpandedNodeId(Opc.Ua.DI.Objects.NetworkSet, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceTopology = new ExpandedNodeId(Opc.Ua.DI.Objects.DeviceTopology, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_ParameterSet = new ExpandedNodeId(Opc.Ua.DI.Objects.TopologyElementType_ParameterSet, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_MethodSet = new ExpandedNodeId(Opc.Ua.DI.Objects.TopologyElementType_MethodSet, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_GroupIdentifier = new ExpandedNodeId(Opc.Ua.DI.Objects.TopologyElementType_GroupIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Identification = new ExpandedNodeId(Opc.Ua.DI.Objects.TopologyElementType_Identification, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock = new ExpandedNodeId(Opc.Ua.DI.Objects.TopologyElementType_Lock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IDeviceHealthType_DeviceHealthAlarms = new ExpandedNodeId(Opc.Ua.DI.Objects.IDeviceHealthType_DeviceHealthAlarms, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DeviceTypeImage = new ExpandedNodeId(Opc.Ua.DI.Objects.ISupportInfoType_DeviceTypeImage, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_Documentation = new ExpandedNodeId(Opc.Ua.DI.Objects.ISupportInfoType_Documentation, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles = new ExpandedNodeId(Opc.Ua.DI.Objects.ISupportInfoType_DocumentationFiles, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId = new ExpandedNodeId(Opc.Ua.DI.Objects.ISupportInfoType_DocumentationFiles_DocumentFileId, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_ProtocolSupport = new ExpandedNodeId(Opc.Ua.DI.Objects.ISupportInfoType_ProtocolSupport, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_ImageSet = new ExpandedNodeId(Opc.Ua.DI.Objects.ISupportInfoType_ImageSet, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier = new ExpandedNodeId(Opc.Ua.DI.Objects.DeviceType_CPIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_NetworkAddress = new ExpandedNodeId(Opc.Ua.DI.Objects.DeviceType_CPIdentifier_NetworkAddress, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_DeviceHealthAlarms = new ExpandedNodeId(Opc.Ua.DI.Objects.DeviceType_DeviceHealthAlarms, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_DeviceTypeImage = new ExpandedNodeId(Opc.Ua.DI.Objects.DeviceType_DeviceTypeImage, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Documentation = new ExpandedNodeId(Opc.Ua.DI.Objects.DeviceType_Documentation, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_ProtocolSupport = new ExpandedNodeId(Opc.Ua.DI.Objects.DeviceType_ProtocolSupport, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_ImageSet = new ExpandedNodeId(Opc.Ua.DI.Objects.DeviceType_ImageSet, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfigurableObjectType_SupportedTypes = new ExpandedNodeId(Opc.Ua.DI.Objects.ConfigurableObjectType_SupportedTypes, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfigurableObjectType_ObjectIdentifier = new ExpandedNodeId(Opc.Ua.DI.Objects.ConfigurableObjectType_ObjectIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FunctionalGroupType_GroupIdentifier = new ExpandedNodeId(Opc.Ua.DI.Objects.FunctionalGroupType_GroupIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_ProfileIdentifier = new ExpandedNodeId(Opc.Ua.DI.Objects.NetworkType_ProfileIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier = new ExpandedNodeId(Opc.Ua.DI.Objects.NetworkType_CPIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_NetworkAddress = new ExpandedNodeId(Opc.Ua.DI.Objects.NetworkType_CPIdentifier_NetworkAddress, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock = new ExpandedNodeId(Opc.Ua.DI.Objects.NetworkType_Lock, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkAddress = new ExpandedNodeId(Opc.Ua.DI.Objects.ConnectionPointType_NetworkAddress, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_ProfileIdentifier = new ExpandedNodeId(Opc.Ua.DI.Objects.ConnectionPointType_ProfileIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier = new ExpandedNodeId(Opc.Ua.DI.Objects.ConnectionPointType_NetworkIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Loading = new ExpandedNodeId(Opc.Ua.DI.Objects.SoftwareUpdateType_Loading, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_PrepareForUpdate = new ExpandedNodeId(Opc.Ua.DI.Objects.SoftwareUpdateType_PrepareForUpdate, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Installation = new ExpandedNodeId(Opc.Ua.DI.Objects.SoftwareUpdateType_Installation, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_PowerCycle = new ExpandedNodeId(Opc.Ua.DI.Objects.SoftwareUpdateType_PowerCycle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Confirmation = new ExpandedNodeId(Opc.Ua.DI.Objects.SoftwareUpdateType_Confirmation, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters = new ExpandedNodeId(Opc.Ua.DI.Objects.SoftwareUpdateType_Parameters, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_CurrentVersion = new ExpandedNodeId(Opc.Ua.DI.Objects.PackageLoadingType_CurrentVersion, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer = new ExpandedNodeId(Opc.Ua.DI.Objects.PackageLoadingType_FileTransfer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_PendingVersion = new ExpandedNodeId(Opc.Ua.DI.Objects.CachedLoadingType_PendingVersion, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FallbackVersion = new ExpandedNodeId(Opc.Ua.DI.Objects.CachedLoadingType_FallbackVersion, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem = new ExpandedNodeId(Opc.Ua.DI.Objects.FileSystemLoadingType_FileSystem, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_Idle = new ExpandedNodeId(Opc.Ua.DI.Objects.PrepareForUpdateStateMachineType_Idle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_Preparing = new ExpandedNodeId(Opc.Ua.DI.Objects.PrepareForUpdateStateMachineType_Preparing, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_PreparedForUpdate = new ExpandedNodeId(Opc.Ua.DI.Objects.PrepareForUpdateStateMachineType_PreparedForUpdate, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_Resuming = new ExpandedNodeId(Opc.Ua.DI.Objects.PrepareForUpdateStateMachineType_Resuming, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_IdleToPreparing = new ExpandedNodeId(Opc.Ua.DI.Objects.PrepareForUpdateStateMachineType_IdleToPreparing, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_PreparingToIdle = new ExpandedNodeId(Opc.Ua.DI.Objects.PrepareForUpdateStateMachineType_PreparingToIdle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_PreparingToPreparedForUpdate = new ExpandedNodeId(Opc.Ua.DI.Objects.PrepareForUpdateStateMachineType_PreparingToPreparedForUpdate, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_PreparedForUpdateToResuming = new ExpandedNodeId(Opc.Ua.DI.Objects.PrepareForUpdateStateMachineType_PreparedForUpdateToResuming, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_ResumingToIdle = new ExpandedNodeId(Opc.Ua.DI.Objects.PrepareForUpdateStateMachineType_ResumingToIdle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_Idle = new ExpandedNodeId(Opc.Ua.DI.Objects.InstallationStateMachineType_Idle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_Installing = new ExpandedNodeId(Opc.Ua.DI.Objects.InstallationStateMachineType_Installing, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_Error = new ExpandedNodeId(Opc.Ua.DI.Objects.InstallationStateMachineType_Error, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_IdleToInstalling = new ExpandedNodeId(Opc.Ua.DI.Objects.InstallationStateMachineType_IdleToInstalling, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallingToIdle = new ExpandedNodeId(Opc.Ua.DI.Objects.InstallationStateMachineType_InstallingToIdle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallingToError = new ExpandedNodeId(Opc.Ua.DI.Objects.InstallationStateMachineType_InstallingToError, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_ErrorToIdle = new ExpandedNodeId(Opc.Ua.DI.Objects.InstallationStateMachineType_ErrorToIdle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PowerCycleStateMachineType_NotWaitingForPowerCycle = new ExpandedNodeId(Opc.Ua.DI.Objects.PowerCycleStateMachineType_NotWaitingForPowerCycle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PowerCycleStateMachineType_WaitingForPowerCycle = new ExpandedNodeId(Opc.Ua.DI.Objects.PowerCycleStateMachineType_WaitingForPowerCycle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PowerCycleStateMachineType_NotWaitingForPowerCycleToWaitingForPowerCycle = new ExpandedNodeId(Opc.Ua.DI.Objects.PowerCycleStateMachineType_NotWaitingForPowerCycleToWaitingForPowerCycle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PowerCycleStateMachineType_WaitingForPowerCycleToNotWaitingForPowerCycle = new ExpandedNodeId(Opc.Ua.DI.Objects.PowerCycleStateMachineType_WaitingForPowerCycleToNotWaitingForPowerCycle, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType_NotWaitingForConfirm = new ExpandedNodeId(Opc.Ua.DI.Objects.ConfirmationStateMachineType_NotWaitingForConfirm, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType_WaitingForConfirm = new ExpandedNodeId(Opc.Ua.DI.Objects.ConfirmationStateMachineType_WaitingForConfirm, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType_NotWaitingForConfirmToWaitingForConfirm = new ExpandedNodeId(Opc.Ua.DI.Objects.ConfirmationStateMachineType_NotWaitingForConfirmToWaitingForConfirm, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType_WaitingForConfirmToNotWaitingForConfirm = new ExpandedNodeId(Opc.Ua.DI.Objects.ConfirmationStateMachineType_WaitingForConfirmToNotWaitingForConfirm, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FetchResultDataType_Encoding_DefaultBinary = new ExpandedNodeId(Opc.Ua.DI.Objects.FetchResultDataType_Encoding_DefaultBinary, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferResultErrorDataType_Encoding_DefaultBinary = new ExpandedNodeId(Opc.Ua.DI.Objects.TransferResultErrorDataType_Encoding_DefaultBinary, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferResultDataDataType_Encoding_DefaultBinary = new ExpandedNodeId(Opc.Ua.DI.Objects.TransferResultDataDataType_Encoding_DefaultBinary, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ParameterResultDataType_Encoding_DefaultBinary = new ExpandedNodeId(Opc.Ua.DI.Objects.ParameterResultDataType_Encoding_DefaultBinary, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FetchResultDataType_Encoding_DefaultXml = new ExpandedNodeId(Opc.Ua.DI.Objects.FetchResultDataType_Encoding_DefaultXml, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferResultErrorDataType_Encoding_DefaultXml = new ExpandedNodeId(Opc.Ua.DI.Objects.TransferResultErrorDataType_Encoding_DefaultXml, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferResultDataDataType_Encoding_DefaultXml = new ExpandedNodeId(Opc.Ua.DI.Objects.TransferResultDataDataType_Encoding_DefaultXml, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ParameterResultDataType_Encoding_DefaultXml = new ExpandedNodeId(Opc.Ua.DI.Objects.ParameterResultDataType_Encoding_DefaultXml, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FetchResultDataType_Encoding_DefaultJson = new ExpandedNodeId(Opc.Ua.DI.Objects.FetchResultDataType_Encoding_DefaultJson, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferResultErrorDataType_Encoding_DefaultJson = new ExpandedNodeId(Opc.Ua.DI.Objects.TransferResultErrorDataType_Encoding_DefaultJson, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferResultDataDataType_Encoding_DefaultJson = new ExpandedNodeId(Opc.Ua.DI.Objects.TransferResultDataDataType_Encoding_DefaultJson, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ParameterResultDataType_Encoding_DefaultJson = new ExpandedNodeId(Opc.Ua.DI.Objects.ParameterResultDataType_Encoding_DefaultJson, Opc.Ua.DI.Namespaces.OpcUaDI);
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.TopologyElementType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.IVendorNameplateType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ITagNameplateType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.ITagNameplateType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IDeviceHealthType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.IDeviceHealthType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.ISupportInfoType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.ComponentType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.DeviceType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.SoftwareType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.BlockType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceHealthDiagnosticAlarmType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.DeviceHealthDiagnosticAlarmType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FailureAlarmType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.FailureAlarmType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CheckFunctionAlarmType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.CheckFunctionAlarmType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId OffSpecAlarmType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.OffSpecAlarmType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId MaintenanceRequiredAlarmType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.MaintenanceRequiredAlarmType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfigurableObjectType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.ConfigurableObjectType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BaseLifetimeIndicationType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.BaseLifetimeIndicationType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TimeIndicationType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.TimeIndicationType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NumberOfPartsIndicationType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.NumberOfPartsIndicationType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NumberOfUsagesIndicationType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.NumberOfUsagesIndicationType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LengthIndicationType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.LengthIndicationType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DiameterIndicationType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.DiameterIndicationType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SubstanceVolumeIndicationType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.SubstanceVolumeIndicationType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FunctionalGroupType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.FunctionalGroupType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ProtocolType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.ProtocolType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IOperationCounterType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.IOperationCounterType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.NetworkType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.ConnectionPointType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.TransferServicesType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.LockingServicesType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.SoftwareUpdateType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareLoadingType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.SoftwareLoadingType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.PackageLoadingType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.DirectLoadingType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.CachedLoadingType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.FileSystemLoadingType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareVersionType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.SoftwareVersionType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.PrepareForUpdateStateMachineType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.InstallationStateMachineType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PowerCycleStateMachineType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.PowerCycleStateMachineType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType = new ExpandedNodeId(Opc.Ua.DI.ObjectTypes.ConfirmationStateMachineType, Opc.Ua.DI.Namespaces.OpcUaDI);
    }
    #endregion

    #region ReferenceType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ReferenceTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId ConnectsTo = new ExpandedNodeId(Opc.Ua.DI.ReferenceTypes.ConnectsTo, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectsToParent = new ExpandedNodeId(Opc.Ua.DI.ReferenceTypes.ConnectsToParent, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IsOnline = new ExpandedNodeId(Opc.Ua.DI.ReferenceTypes.IsOnline, Opc.Ua.DI.Namespaces.OpcUaDI);
    }
    #endregion

    #region Variable Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId OPCUADINamespaceMetadata_NamespaceUri = new ExpandedNodeId(Opc.Ua.DI.Variables.OPCUADINamespaceMetadata_NamespaceUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUADINamespaceMetadata_NamespaceVersion = new ExpandedNodeId(Opc.Ua.DI.Variables.OPCUADINamespaceMetadata_NamespaceVersion, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUADINamespaceMetadata_NamespacePublicationDate = new ExpandedNodeId(Opc.Ua.DI.Variables.OPCUADINamespaceMetadata_NamespacePublicationDate, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUADINamespaceMetadata_IsNamespaceSubset = new ExpandedNodeId(Opc.Ua.DI.Variables.OPCUADINamespaceMetadata_IsNamespaceSubset, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUADINamespaceMetadata_StaticNodeIdTypes = new ExpandedNodeId(Opc.Ua.DI.Variables.OPCUADINamespaceMetadata_StaticNodeIdTypes, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUADINamespaceMetadata_StaticNumericNodeIdRange = new ExpandedNodeId(Opc.Ua.DI.Variables.OPCUADINamespaceMetadata_StaticNumericNodeIdRange, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUADINamespaceMetadata_StaticStringNodeIdPattern = new ExpandedNodeId(Opc.Ua.DI.Variables.OPCUADINamespaceMetadata_StaticStringNodeIdPattern, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_ParameterSet_ParameterIdentifier = new ExpandedNodeId(Opc.Ua.DI.Variables.TopologyElementType_ParameterSet_ParameterIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.TopologyElementType_Lock_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.TopologyElementType_Lock_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.TopologyElementType_Lock_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.TopologyElementType_Lock_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.TopologyElementType_Lock_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.TopologyElementType_Lock_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.TopologyElementType_Lock_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.TopologyElementType_Lock_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TopologyElementType_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.TopologyElementType_Lock_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_Manufacturer = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_Manufacturer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_ManufacturerUri = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_ManufacturerUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_Model = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_Model, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_HardwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_HardwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_SoftwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_SoftwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_DeviceRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_DeviceRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_ProductCode = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_ProductCode, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_DeviceManual = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_DeviceManual, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_DeviceClass = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_DeviceClass, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_SerialNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_SerialNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_ProductInstanceUri = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_ProductInstanceUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_RevisionCounter = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_RevisionCounter, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_SoftwareReleaseDate = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_SoftwareReleaseDate, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IVendorNameplateType_PatchIdentifiers = new ExpandedNodeId(Opc.Ua.DI.Variables.IVendorNameplateType_PatchIdentifiers, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ITagNameplateType_AssetId = new ExpandedNodeId(Opc.Ua.DI.Variables.ITagNameplateType_AssetId, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ITagNameplateType_ComponentName = new ExpandedNodeId(Opc.Ua.DI.Variables.ITagNameplateType_ComponentName, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IDeviceHealthType_DeviceHealth = new ExpandedNodeId(Opc.Ua.DI.Variables.IDeviceHealthType_DeviceHealth, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DeviceTypeImage_ImageIdentifier = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DeviceTypeImage_ImageIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_Documentation_DocumentIdentifier = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_Documentation_DocumentIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Size = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_Size, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Writable = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_Writable, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_UserWritable = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_UserWritable, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_OpenCount = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_OpenCount, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Open_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_Open_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_Open_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Close_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_Close_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Read_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_Read_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_Read_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_Write_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_Write_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_GetPosition_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_GetPosition_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_DocumentationFiles_DocumentFileId_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_DocumentationFiles_DocumentFileId_SetPosition_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_ProtocolSupport_ProtocolSupportIdentifier = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_ProtocolSupport_ProtocolSupportIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ISupportInfoType_ImageSet_ImageIdentifier = new ExpandedNodeId(Opc.Ua.DI.Variables.ISupportInfoType_ImageSet_ImageIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Lock_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Lock_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Lock_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Lock_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Lock_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Lock_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Lock_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Lock_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Lock_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Manufacturer = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Manufacturer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_ManufacturerUri = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_ManufacturerUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_Model = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_Model, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_HardwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_HardwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_SoftwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_SoftwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_DeviceRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_DeviceRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_ProductCode = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_ProductCode, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_DeviceManual = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_DeviceManual, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_DeviceClass = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_DeviceClass, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_SerialNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_SerialNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_ProductInstanceUri = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_ProductInstanceUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_RevisionCounter = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_RevisionCounter, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_AssetId = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_AssetId, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ComponentType_ComponentName = new ExpandedNodeId(Opc.Ua.DI.Variables.ComponentType_ComponentName, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Lock_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Lock_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Lock_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Lock_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Lock_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Lock_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Lock_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Lock_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Lock_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Manufacturer = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Manufacturer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_ManufacturerUri = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_ManufacturerUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Model = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Model, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_HardwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_HardwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_SoftwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_SoftwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_DeviceRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_DeviceRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_ProductCode = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_ProductCode, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_DeviceManual = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_DeviceManual, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_DeviceClass = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_DeviceClass, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_SerialNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_SerialNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_ProductInstanceUri = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_ProductInstanceUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_RevisionCounter = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_RevisionCounter, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_CPIdentifier_Lock_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_CPIdentifier_Lock_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_CPIdentifier_Lock_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_CPIdentifier_Lock_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_CPIdentifier_Lock_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_CPIdentifier_Lock_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_CPIdentifier_Lock_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_CPIdentifier_Lock_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_CPIdentifier_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_CPIdentifier_Lock_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_DeviceHealth = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_DeviceHealth, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_DeviceTypeImage_ImageIdentifier = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_DeviceTypeImage_ImageIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_Documentation_DocumentIdentifier = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_Documentation_DocumentIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_ProtocolSupport_ProtocolSupportIdentifier = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_ProtocolSupport_ProtocolSupportIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceType_ImageSet_ImageIdentifier = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceType_ImageSet_ImageIdentifier, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Lock_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Lock_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Lock_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Lock_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Lock_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Lock_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Lock_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Lock_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Lock_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Manufacturer = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Manufacturer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_Model = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_Model, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareType_SoftwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareType_SoftwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_Lock_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_Lock_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_Lock_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_Lock_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_Lock_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_Lock_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_Lock_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_Lock_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_Lock_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_RevisionCounter = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_RevisionCounter, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_ActualMode = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_ActualMode, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_PermittedMode = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_PermittedMode, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_NormalMode = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_NormalMode, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId BlockType_TargetMode = new ExpandedNodeId(Opc.Ua.DI.Variables.BlockType_TargetMode, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LifetimeVariableType_StartValue = new ExpandedNodeId(Opc.Ua.DI.Variables.LifetimeVariableType_StartValue, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LifetimeVariableType_LimitValue = new ExpandedNodeId(Opc.Ua.DI.Variables.LifetimeVariableType_LimitValue, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LifetimeVariableType_Indication = new ExpandedNodeId(Opc.Ua.DI.Variables.LifetimeVariableType_Indication, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LifetimeVariableType_WarningValues = new ExpandedNodeId(Opc.Ua.DI.Variables.LifetimeVariableType_WarningValues, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FunctionalGroupType_GroupIdentifier_UIElement = new ExpandedNodeId(Opc.Ua.DI.Variables.FunctionalGroupType_GroupIdentifier_UIElement, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FunctionalGroupType_UIElement = new ExpandedNodeId(Opc.Ua.DI.Variables.FunctionalGroupType_UIElement, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DeviceHealthEnumeration_EnumStrings = new ExpandedNodeId(Opc.Ua.DI.Variables.DeviceHealthEnumeration_EnumStrings, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IOperationCounterType_PowerOnDuration = new ExpandedNodeId(Opc.Ua.DI.Variables.IOperationCounterType_PowerOnDuration, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IOperationCounterType_OperationDuration = new ExpandedNodeId(Opc.Ua.DI.Variables.IOperationCounterType_OperationDuration, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId IOperationCounterType_OperationCycleCounter = new ExpandedNodeId(Opc.Ua.DI.Variables.IOperationCounterType_OperationCycleCounter, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_CPIdentifier_Lock_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_CPIdentifier_Lock_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_CPIdentifier_Lock_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_CPIdentifier_Lock_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_CPIdentifier_Lock_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_CPIdentifier_Lock_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_CPIdentifier_Lock_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_CPIdentifier_Lock_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_CPIdentifier_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_CPIdentifier_Lock_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_Lock_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_Lock_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_Lock_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_Lock_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_Lock_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_Lock_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_Lock_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_Lock_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId NetworkType_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.NetworkType_Lock_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_Lock_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_Lock_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_Lock_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_Lock_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_Lock_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_Lock_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_Lock_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_Lock_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_Lock_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_NetworkIdentifier_Lock_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_NetworkIdentifier_Lock_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_NetworkIdentifier_Lock_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_NetworkIdentifier_Lock_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_NetworkIdentifier_Lock_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_NetworkIdentifier_Lock_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_NetworkIdentifier_Lock_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_NetworkIdentifier_Lock_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConnectionPointType_NetworkIdentifier_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.ConnectionPointType_NetworkIdentifier_Lock_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType_TransferToDevice_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.TransferServicesType_TransferToDevice_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType_TransferFromDevice_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.TransferServicesType_TransferFromDevice_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType_FetchTransferResultData_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.TransferServicesType_FetchTransferResultData_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId TransferServicesType_FetchTransferResultData_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.TransferServicesType_FetchTransferResultData_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId MaxInactiveLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.MaxInactiveLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_DefaultInstanceBrowseName = new ExpandedNodeId(Opc.Ua.DI.Variables.LockingServicesType_DefaultInstanceBrowseName, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_Locked = new ExpandedNodeId(Opc.Ua.DI.Variables.LockingServicesType_Locked, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_LockingClient = new ExpandedNodeId(Opc.Ua.DI.Variables.LockingServicesType_LockingClient, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_LockingUser = new ExpandedNodeId(Opc.Ua.DI.Variables.LockingServicesType_LockingUser, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_RemainingLockTime = new ExpandedNodeId(Opc.Ua.DI.Variables.LockingServicesType_RemainingLockTime, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.LockingServicesType_InitLock_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.LockingServicesType_InitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.LockingServicesType_RenewLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.LockingServicesType_ExitLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId LockingServicesType_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.LockingServicesType_BreakLock_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_PrepareForUpdate_CurrentState = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_PrepareForUpdate_CurrentState, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_PrepareForUpdate_CurrentState_Id = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_PrepareForUpdate_CurrentState_Id, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Installation_CurrentState = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Installation_CurrentState, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Installation_CurrentState_Id = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Installation_CurrentState_Id, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Installation_InstallSoftwarePackage_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Installation_InstallSoftwarePackage_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Installation_InstallFiles_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Installation_InstallFiles_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_PowerCycle_CurrentState = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_PowerCycle_CurrentState, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_PowerCycle_CurrentState_Id = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_PowerCycle_CurrentState_Id, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Confirmation_CurrentState = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Confirmation_CurrentState, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Confirmation_CurrentState_Id = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Confirmation_CurrentState_Id, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Confirmation_ConfirmationTimeout = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Confirmation_ConfirmationTimeout, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters_ClientProcessingTimeout = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Parameters_ClientProcessingTimeout, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters_GenerateFileForRead_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Parameters_GenerateFileForRead_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters_GenerateFileForRead_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Parameters_GenerateFileForRead_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters_GenerateFileForWrite_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Parameters_GenerateFileForWrite_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters_GenerateFileForWrite_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Parameters_GenerateFileForWrite_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters_CloseAndCommit_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Parameters_CloseAndCommit_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_Parameters_CloseAndCommit_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_Parameters_CloseAndCommit_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_UpdateStatus = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_UpdateStatus, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_VendorErrorCode = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_VendorErrorCode, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareUpdateType_DefaultInstanceBrowseName = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareUpdateType_DefaultInstanceBrowseName, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareLoadingType_UpdateKey = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareLoadingType_UpdateKey, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_CurrentVersion_Manufacturer = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_CurrentVersion_Manufacturer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_CurrentVersion_ManufacturerUri = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_CurrentVersion_ManufacturerUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_CurrentVersion_SoftwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_CurrentVersion_SoftwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer_ClientProcessingTimeout = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_FileTransfer_ClientProcessingTimeout, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer_GenerateFileForRead_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_FileTransfer_GenerateFileForRead_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer_GenerateFileForRead_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_FileTransfer_GenerateFileForRead_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer_GenerateFileForWrite_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_FileTransfer_GenerateFileForWrite_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer_GenerateFileForWrite_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_FileTransfer_GenerateFileForWrite_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer_CloseAndCommit_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_FileTransfer_CloseAndCommit_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_FileTransfer_CloseAndCommit_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_FileTransfer_CloseAndCommit_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_ErrorMessage = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_ErrorMessage, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PackageLoadingType_WriteBlockSize = new ExpandedNodeId(Opc.Ua.DI.Variables.PackageLoadingType_WriteBlockSize, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_CurrentVersion_Manufacturer = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_CurrentVersion_Manufacturer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_CurrentVersion_ManufacturerUri = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_CurrentVersion_ManufacturerUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_CurrentVersion_SoftwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_CurrentVersion_SoftwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_FileTransfer_ClientProcessingTimeout = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_FileTransfer_ClientProcessingTimeout, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_FileTransfer_GenerateFileForRead_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_FileTransfer_GenerateFileForRead_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_FileTransfer_GenerateFileForRead_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_FileTransfer_GenerateFileForRead_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_FileTransfer_GenerateFileForWrite_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_FileTransfer_GenerateFileForWrite_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_FileTransfer_GenerateFileForWrite_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_FileTransfer_GenerateFileForWrite_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_FileTransfer_CloseAndCommit_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_FileTransfer_CloseAndCommit_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_FileTransfer_CloseAndCommit_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_FileTransfer_CloseAndCommit_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_UpdateBehavior = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_UpdateBehavior, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId DirectLoadingType_WriteTimeout = new ExpandedNodeId(Opc.Ua.DI.Variables.DirectLoadingType_WriteTimeout, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_CurrentVersion_Manufacturer = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_CurrentVersion_Manufacturer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_CurrentVersion_ManufacturerUri = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_CurrentVersion_ManufacturerUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_CurrentVersion_SoftwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_CurrentVersion_SoftwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FileTransfer_ClientProcessingTimeout = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_FileTransfer_ClientProcessingTimeout, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FileTransfer_GenerateFileForRead_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_FileTransfer_GenerateFileForRead_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FileTransfer_GenerateFileForRead_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_FileTransfer_GenerateFileForRead_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FileTransfer_GenerateFileForWrite_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_FileTransfer_GenerateFileForWrite_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FileTransfer_GenerateFileForWrite_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_FileTransfer_GenerateFileForWrite_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FileTransfer_CloseAndCommit_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_FileTransfer_CloseAndCommit_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FileTransfer_CloseAndCommit_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_FileTransfer_CloseAndCommit_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_PendingVersion_Manufacturer = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_PendingVersion_Manufacturer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_PendingVersion_ManufacturerUri = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_PendingVersion_ManufacturerUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_PendingVersion_SoftwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_PendingVersion_SoftwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FallbackVersion_Manufacturer = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_FallbackVersion_Manufacturer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FallbackVersion_ManufacturerUri = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_FallbackVersion_ManufacturerUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_FallbackVersion_SoftwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_FallbackVersion_SoftwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_GetUpdateBehavior_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_GetUpdateBehavior_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId CachedLoadingType_GetUpdateBehavior_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.CachedLoadingType_GetUpdateBehavior_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_CreateDirectory_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_FileSystem_CreateDirectory_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_CreateDirectory_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_FileSystem_CreateDirectory_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_CreateFile_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_FileSystem_CreateFile_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_CreateFile_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_FileSystem_CreateFile_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_DeleteFileSystemObject_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_FileSystem_DeleteFileSystemObject_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_MoveOrCopy_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_FileSystem_MoveOrCopy_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_FileSystem_MoveOrCopy_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_FileSystem_MoveOrCopy_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_GetUpdateBehavior_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_GetUpdateBehavior_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_GetUpdateBehavior_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_GetUpdateBehavior_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_ValidateFiles_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_ValidateFiles_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId FileSystemLoadingType_ValidateFiles_OutputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.FileSystemLoadingType_ValidateFiles_OutputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareVersionType_Manufacturer = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareVersionType_Manufacturer, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareVersionType_ManufacturerUri = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareVersionType_ManufacturerUri, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareVersionType_SoftwareRevision = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareVersionType_SoftwareRevision, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareVersionType_PatchIdentifiers = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareVersionType_PatchIdentifiers, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareVersionType_ReleaseDate = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareVersionType_ReleaseDate, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareVersionType_ChangeLogReference = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareVersionType_ChangeLogReference, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareVersionType_Hash = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareVersionType_Hash, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_PercentComplete = new ExpandedNodeId(Opc.Ua.DI.Variables.PrepareForUpdateStateMachineType_PercentComplete, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_Idle_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PrepareForUpdateStateMachineType_Idle_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_Preparing_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PrepareForUpdateStateMachineType_Preparing_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_PreparedForUpdate_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PrepareForUpdateStateMachineType_PreparedForUpdate_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_Resuming_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PrepareForUpdateStateMachineType_Resuming_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_IdleToPreparing_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PrepareForUpdateStateMachineType_IdleToPreparing_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_PreparingToIdle_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PrepareForUpdateStateMachineType_PreparingToIdle_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_PreparingToPreparedForUpdate_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PrepareForUpdateStateMachineType_PreparingToPreparedForUpdate_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_PreparedForUpdateToResuming_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PrepareForUpdateStateMachineType_PreparedForUpdateToResuming_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PrepareForUpdateStateMachineType_ResumingToIdle_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PrepareForUpdateStateMachineType_ResumingToIdle_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_PercentComplete = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_PercentComplete, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallationDelay = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_InstallationDelay, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallSoftwarePackage_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_InstallSoftwarePackage_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallFiles_InputArguments = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_InstallFiles_InputArguments, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_Idle_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_Idle_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_Installing_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_Installing_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_Error_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_Error_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_IdleToInstalling_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_IdleToInstalling_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallingToIdle_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_InstallingToIdle_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_InstallingToError_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_InstallingToError_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId InstallationStateMachineType_ErrorToIdle_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.InstallationStateMachineType_ErrorToIdle_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PowerCycleStateMachineType_NotWaitingForPowerCycle_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PowerCycleStateMachineType_NotWaitingForPowerCycle_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PowerCycleStateMachineType_WaitingForPowerCycle_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PowerCycleStateMachineType_WaitingForPowerCycle_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PowerCycleStateMachineType_NotWaitingForPowerCycleToWaitingForPowerCycle_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PowerCycleStateMachineType_NotWaitingForPowerCycleToWaitingForPowerCycle_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId PowerCycleStateMachineType_WaitingForPowerCycleToNotWaitingForPowerCycle_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.PowerCycleStateMachineType_WaitingForPowerCycleToNotWaitingForPowerCycle_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType_ConfirmationTimeout = new ExpandedNodeId(Opc.Ua.DI.Variables.ConfirmationStateMachineType_ConfirmationTimeout, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType_NotWaitingForConfirm_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.ConfirmationStateMachineType_NotWaitingForConfirm_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType_WaitingForConfirm_StateNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.ConfirmationStateMachineType_WaitingForConfirm_StateNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType_NotWaitingForConfirmToWaitingForConfirm_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.ConfirmationStateMachineType_NotWaitingForConfirmToWaitingForConfirm_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId ConfirmationStateMachineType_WaitingForConfirmToNotWaitingForConfirm_TransitionNumber = new ExpandedNodeId(Opc.Ua.DI.Variables.ConfirmationStateMachineType_WaitingForConfirmToNotWaitingForConfirm_TransitionNumber, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId SoftwareVersionFileType_EnumStrings = new ExpandedNodeId(Opc.Ua.DI.Variables.SoftwareVersionFileType_EnumStrings, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId UpdateBehavior_OptionSetValues = new ExpandedNodeId(Opc.Ua.DI.Variables.UpdateBehavior_OptionSetValues, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId OpcUaDi_BinarySchema = new ExpandedNodeId(Opc.Ua.DI.Variables.OpcUaDi_BinarySchema, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId OpcUaDi_XmlSchema = new ExpandedNodeId(Opc.Ua.DI.Variables.OpcUaDi_XmlSchema, Opc.Ua.DI.Namespaces.OpcUaDI);
    }
    #endregion

    #region VariableType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId LifetimeVariableType = new ExpandedNodeId(Opc.Ua.DI.VariableTypes.LifetimeVariableType, Opc.Ua.DI.Namespaces.OpcUaDI);

        /// <remarks />
        public static readonly ExpandedNodeId UIElementType = new ExpandedNodeId(Opc.Ua.DI.VariableTypes.UIElementType, Opc.Ua.DI.Namespaces.OpcUaDI);
    }
    #endregion

    #region BrowseName Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class BrowseNames
    {
        /// <remarks />
        public const string Abort = "Abort";

        /// <remarks />
        public const string ActualMode = "ActualMode";

        /// <remarks />
        public const string AssetId = "AssetId";

        /// <remarks />
        public const string BaseLifetimeIndicationType = "BaseLifetimeIndicationType";

        /// <remarks />
        public const string BlockType = "BlockType";

        /// <remarks />
        public const string BreakLock = "BreakLock";

        /// <remarks />
        public const string BreakLockMethodType = "BreakLockMethodType";

        /// <remarks />
        public const string CachedLoadingType = "CachedLoadingType";

        /// <remarks />
        public const string ChangeLogReference = "ChangeLogReference";

        /// <remarks />
        public const string CheckFunctionAlarmType = "CheckFunctionAlarmType";

        /// <remarks />
        public const string ComponentName = "ComponentName";

        /// <remarks />
        public const string ComponentType = "ComponentType";

        /// <remarks />
        public const string ConfigurableObjectType = "ConfigurableObjectType";

        /// <remarks />
        public const string Confirm = "Confirm";

        /// <remarks />
        public const string Confirmation = "Confirmation";

        /// <remarks />
        public const string ConfirmationStateMachineType = "ConfirmationStateMachineType";

        /// <remarks />
        public const string ConfirmationTimeout = "ConfirmationTimeout";

        /// <remarks />
        public const string ConnectionPointType = "ConnectionPointType";

        /// <remarks />
        public const string ConnectsTo = "ConnectsTo";

        /// <remarks />
        public const string ConnectsToParent = "ConnectsToParent";

        /// <remarks />
        public const string CPIdentifier = "<CPIdentifier>";

        /// <remarks />
        public const string CurrentVersion = "CurrentVersion";

        /// <remarks />
        public const string DeviceClass = "DeviceClass";

        /// <remarks />
        public const string DeviceFeatures = "DeviceFeatures";

        /// <remarks />
        public const string DeviceHealth = "DeviceHealth";

        /// <remarks />
        public const string DeviceHealthAlarms = "DeviceHealthAlarms";

        /// <remarks />
        public const string DeviceHealthDiagnosticAlarmType = "DeviceHealthDiagnosticAlarmType";

        /// <remarks />
        public const string DeviceHealthEnumeration = "DeviceHealthEnumeration";

        /// <remarks />
        public const string DeviceManual = "DeviceManual";

        /// <remarks />
        public const string DeviceRevision = "DeviceRevision";

        /// <remarks />
        public const string DeviceSet = "DeviceSet";

        /// <remarks />
        public const string DeviceTopology = "DeviceTopology";

        /// <remarks />
        public const string DeviceType = "DeviceType";

        /// <remarks />
        public const string DeviceTypeImage = "DeviceTypeImage";

        /// <remarks />
        public const string DiameterIndicationType = "DiameterIndicationType";

        /// <remarks />
        public const string DirectLoadingType = "DirectLoadingType";

        /// <remarks />
        public const string Documentation = "Documentation";

        /// <remarks />
        public const string DocumentationFiles = "DocumentationFiles";

        /// <remarks />
        public const string Error = "Error";

        /// <remarks />
        public const string ErrorMessage = "ErrorMessage";

        /// <remarks />
        public const string ErrorToIdle = "ErrorToIdle";

        /// <remarks />
        public const string ExitLock = "ExitLock";

        /// <remarks />
        public const string ExitLockMethodType = "ExitLockMethodType";

        /// <remarks />
        public const string FailureAlarmType = "FailureAlarmType";

        /// <remarks />
        public const string FallbackVersion = "FallbackVersion";

        /// <remarks />
        public const string FetchResultDataType = "FetchResultDataType";

        /// <remarks />
        public const string FetchTransferResultData = "FetchTransferResultData";

        /// <remarks />
        public const string FetchTransferResultDataMethodType = "FetchTransferResultDataMethodType";

        /// <remarks />
        public const string FileSystemLoadingType = "FileSystemLoadingType";

        /// <remarks />
        public const string FileTransfer = "FileTransfer";

        /// <remarks />
        public const string FunctionalGroupType = "FunctionalGroupType";

        /// <remarks />
        public const string GetUpdateBehavior = "GetUpdateBehavior";

        /// <remarks />
        public const string GetUpdateBehaviorMethodType = "GetUpdateBehaviorMethodType";

        /// <remarks />
        public const string GroupIdentifier = "<GroupIdentifier>";

        /// <remarks />
        public const string HardwareRevision = "HardwareRevision";

        /// <remarks />
        public const string Hash = "Hash";

        /// <remarks />
        public const string Identification = "Identification";

        /// <remarks />
        public const string IDeviceHealthType = "IDeviceHealthType";

        /// <remarks />
        public const string Idle = "Idle";

        /// <remarks />
        public const string IdleToInstalling = "IdleToInstalling";

        /// <remarks />
        public const string IdleToPreparing = "IdleToPreparing";

        /// <remarks />
        public const string ImageSet = "ImageSet";

        /// <remarks />
        public const string Indication = "Indication";

        /// <remarks />
        public const string InitLock = "InitLock";

        /// <remarks />
        public const string InitLockMethodType = "InitLockMethodType";

        /// <remarks />
        public const string Installation = "Installation";

        /// <remarks />
        public const string InstallationDelay = "InstallationDelay";

        /// <remarks />
        public const string InstallationStateMachineType = "InstallationStateMachineType";

        /// <remarks />
        public const string InstallFiles = "InstallFiles";

        /// <remarks />
        public const string InstallFilesMethodType = "InstallFilesMethodType";

        /// <remarks />
        public const string Installing = "Installing";

        /// <remarks />
        public const string InstallingToError = "InstallingToError";

        /// <remarks />
        public const string InstallingToIdle = "InstallingToIdle";

        /// <remarks />
        public const string InstallSoftwarePackage = "InstallSoftwarePackage";

        /// <remarks />
        public const string InstallSoftwarePackageMethodType = "InstallSoftwarePackageMethodType";

        /// <remarks />
        public const string IOperationCounterType = "IOperationCounterType";

        /// <remarks />
        public const string IsOnline = "IsOnline";

        /// <remarks />
        public const string ISupportInfoType = "ISupportInfoType";

        /// <remarks />
        public const string ITagNameplateType = "ITagNameplateType";

        /// <remarks />
        public const string IVendorNameplateType = "IVendorNameplateType";

        /// <remarks />
        public const string LengthIndicationType = "LengthIndicationType";

        /// <remarks />
        public const string LifetimeVariableType = "LifetimeVariableType";

        /// <remarks />
        public const string LimitValue = "LimitValue";

        /// <remarks />
        public const string Loading = "Loading";

        /// <remarks />
        public const string Lock = "Lock";

        /// <remarks />
        public const string Locked = "Locked";

        /// <remarks />
        public const string LockingClient = "LockingClient";

        /// <remarks />
        public const string LockingServicesType = "LockingServicesType";

        /// <remarks />
        public const string LockingUser = "LockingUser";

        /// <remarks />
        public const string MaintenanceRequiredAlarmType = "MaintenanceRequiredAlarmType";

        /// <remarks />
        public const string Manufacturer = "Manufacturer";

        /// <remarks />
        public const string ManufacturerUri = "ManufacturerUri";

        /// <remarks />
        public const string MaxInactiveLockTime = "MaxInactiveLockTime";

        /// <remarks />
        public const string MethodSet = "MethodSet";

        /// <remarks />
        public const string Model = "Model";

        /// <remarks />
        public const string NetworkAddress = "NetworkAddress";

        /// <remarks />
        public const string NetworkIdentifier = "<NetworkIdentifier>";

        /// <remarks />
        public const string NetworkSet = "NetworkSet";

        /// <remarks />
        public const string NetworkType = "NetworkType";

        /// <remarks />
        public const string NormalMode = "NormalMode";

        /// <remarks />
        public const string NotWaitingForConfirm = "NotWaitingForConfirm";

        /// <remarks />
        public const string NotWaitingForConfirmToWaitingForConfirm = "NotWaitingForConfirmToWaitingForConfirm";

        /// <remarks />
        public const string NotWaitingForPowerCycle = "NotWaitingForPowerCycle";

        /// <remarks />
        public const string NotWaitingForPowerCycleToWaitingForPowerCycle = "NotWaitingForPowerCycleToWaitingForPowerCycle";

        /// <remarks />
        public const string NumberOfPartsIndicationType = "NumberOfPartsIndicationType";

        /// <remarks />
        public const string NumberOfUsagesIndicationType = "NumberOfUsagesIndicationType";

        /// <remarks />
        public const string ObjectIdentifier = "<ObjectIdentifier>";

        /// <remarks />
        public const string OffSpecAlarmType = "OffSpecAlarmType";

        /// <remarks />
        public const string OnlineAccess = "OnlineAccess";

        /// <remarks />
        public const string OpcUaDi_BinarySchema = "Opc.Ua.Di";

        /// <remarks />
        public const string OpcUaDi_XmlSchema = "Opc.Ua.Di";

        /// <remarks />
        public const string OPCUADINamespaceMetadata = "http://opcfoundation.org/UA/DI/";

        /// <remarks />
        public const string OperationCycleCounter = "OperationCycleCounter";

        /// <remarks />
        public const string OperationDuration = "OperationDuration";

        /// <remarks />
        public const string PackageLoadingType = "PackageLoadingType";

        /// <remarks />
        public const string ParameterResultDataType = "ParameterResultDataType";

        /// <remarks />
        public const string Parameters = "Parameters";

        /// <remarks />
        public const string ParameterSet = "ParameterSet";

        /// <remarks />
        public const string PatchIdentifiers = "PatchIdentifiers";

        /// <remarks />
        public const string PendingVersion = "PendingVersion";

        /// <remarks />
        public const string PercentComplete = "PercentComplete";

        /// <remarks />
        public const string PermittedMode = "PermittedMode";

        /// <remarks />
        public const string PowerCycle = "PowerCycle";

        /// <remarks />
        public const string PowerCycleStateMachineType = "PowerCycleStateMachineType";

        /// <remarks />
        public const string PowerOnDuration = "PowerOnDuration";

        /// <remarks />
        public const string Prepare = "Prepare";

        /// <remarks />
        public const string PreparedForUpdate = "PreparedForUpdate";

        /// <remarks />
        public const string PreparedForUpdateToResuming = "PreparedForUpdateToResuming";

        /// <remarks />
        public const string PrepareForUpdate = "PrepareForUpdate";

        /// <remarks />
        public const string PrepareForUpdateStateMachineType = "PrepareForUpdateStateMachineType";

        /// <remarks />
        public const string Preparing = "Preparing";

        /// <remarks />
        public const string PreparingToIdle = "PreparingToIdle";

        /// <remarks />
        public const string PreparingToPreparedForUpdate = "PreparingToPreparedForUpdate";

        /// <remarks />
        public const string ProductCode = "ProductCode";

        /// <remarks />
        public const string ProductInstanceUri = "ProductInstanceUri";

        /// <remarks />
        public const string ProfileIdentifier = "<ProfileIdentifier>";

        /// <remarks />
        public const string ProtocolSupport = "ProtocolSupport";

        /// <remarks />
        public const string ProtocolType = "ProtocolType";

        /// <remarks />
        public const string ReleaseDate = "ReleaseDate";

        /// <remarks />
        public const string RemainingLockTime = "RemainingLockTime";

        /// <remarks />
        public const string RenewLock = "RenewLock";

        /// <remarks />
        public const string RenewLockMethodType = "RenewLockMethodType";

        /// <remarks />
        public const string Resume = "Resume";

        /// <remarks />
        public const string Resuming = "Resuming";

        /// <remarks />
        public const string ResumingToIdle = "ResumingToIdle";

        /// <remarks />
        public const string RevisionCounter = "RevisionCounter";

        /// <remarks />
        public const string SerialNumber = "SerialNumber";

        /// <remarks />
        public const string SoftwareLoadingType = "SoftwareLoadingType";

        /// <remarks />
        public const string SoftwareReleaseDate = "SoftwareReleaseDate";

        /// <remarks />
        public const string SoftwareRevision = "SoftwareRevision";

        /// <remarks />
        public const string SoftwareType = "SoftwareType";

        /// <remarks />
        public const string SoftwareUpdate = "SoftwareUpdate";

        /// <remarks />
        public const string SoftwareUpdateType = "SoftwareUpdateType";

        /// <remarks />
        public const string SoftwareVersionFileType = "SoftwareVersionFileType";

        /// <remarks />
        public const string SoftwareVersionType = "SoftwareVersionType";

        /// <remarks />
        public const string StartValue = "StartValue";

        /// <remarks />
        public const string SubstanceVolumeIndicationType = "SubstanceVolumeIndicationType";

        /// <remarks />
        public const string SupportedTypes = "SupportedTypes";

        /// <remarks />
        public const string TargetMode = "TargetMode";

        /// <remarks />
        public const string TimeIndicationType = "TimeIndicationType";

        /// <remarks />
        public const string TopologyElementType = "TopologyElementType";

        /// <remarks />
        public const string TransferFromDevice = "TransferFromDevice";

        /// <remarks />
        public const string TransferFromDeviceMethodType = "TransferFromDeviceMethodType";

        /// <remarks />
        public const string TransferResultDataDataType = "TransferResultDataDataType";

        /// <remarks />
        public const string TransferResultErrorDataType = "TransferResultErrorDataType";

        /// <remarks />
        public const string TransferServicesType = "TransferServicesType";

        /// <remarks />
        public const string TransferToDevice = "TransferToDevice";

        /// <remarks />
        public const string TransferToDeviceMethodType = "TransferToDeviceMethodType";

        /// <remarks />
        public const string UIElement = "UIElement";

        /// <remarks />
        public const string UIElementType = "UIElementType";

        /// <remarks />
        public const string UpdateBehavior = "UpdateBehavior";

        /// <remarks />
        public const string UpdateKey = "UpdateKey";

        /// <remarks />
        public const string UpdateStatus = "UpdateStatus";

        /// <remarks />
        public const string ValidateFiles = "ValidateFiles";

        /// <remarks />
        public const string ValidateFilesMethodType = "ValidateFilesMethodType";

        /// <remarks />
        public const string VendorErrorCode = "VendorErrorCode";

        /// <remarks />
        public const string WaitingForConfirm = "WaitingForConfirm";

        /// <remarks />
        public const string WaitingForConfirmToNotWaitingForConfirm = "WaitingForConfirmToNotWaitingForConfirm";

        /// <remarks />
        public const string WaitingForPowerCycle = "WaitingForPowerCycle";

        /// <remarks />
        public const string WaitingForPowerCycleToNotWaitingForPowerCycle = "WaitingForPowerCycleToNotWaitingForPowerCycle";

        /// <remarks />
        public const string WarningValues = "WarningValues";

        /// <remarks />
        public const string WriteBlockSize = "WriteBlockSize";

        /// <remarks />
        public const string WriteTimeout = "WriteTimeout";
    }
    #endregion

    #region Namespace Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUaDI namespace (.NET code namespace is 'Opc.Ua.DI').
        /// </summary>
        public const string OpcUaDI = "http://opcfoundation.org/UA/DI/";

        /// <summary>
        /// The URI for the OpcUaDIXsd namespace (.NET code namespace is 'Opc.Ua.DI').
        /// </summary>
        public const string OpcUaDIXsd = "http://opcfoundation.org/UA/DI/Types.xsd";

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
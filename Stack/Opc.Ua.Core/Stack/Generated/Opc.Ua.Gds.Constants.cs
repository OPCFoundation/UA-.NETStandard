/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Gds
{
    #region DataType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypes
    {
        /// <remarks />
        public const uint ApplicationRecordDataType = 1;
    }
    #endregion

    #region Method Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Methods
    {
        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Open = 735;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Close = 738;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Read = 740;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Write = 743;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition = 745;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition = 748;

        /// <remarks />
        public const uint DirectoryType_FindApplications = 15;

        /// <remarks />
        public const uint DirectoryType_RegisterApplication = 18;

        /// <remarks />
        public const uint DirectoryType_UpdateApplication = 188;

        /// <remarks />
        public const uint DirectoryType_UnregisterApplication = 21;

        /// <remarks />
        public const uint DirectoryType_GetApplication = 210;

        /// <remarks />
        public const uint DirectoryType_QueryApplications = 868;

        /// <remarks />
        public const uint DirectoryType_QueryServers = 23;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open = 519;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close = 522;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read = 524;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write = 527;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition = 529;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition = 532;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks = 535;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable = 15041;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable = 15042;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment = 15043;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge = 15063;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve = 15110;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve = 15112;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve = 15113;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable = 15189;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable = 15190;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment = 15191;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge = 15211;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 15258;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve = 15260;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 15261;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open = 553;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close = 556;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read = 558;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write = 561;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition = 563;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition = 566;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks = 569;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable = 15337;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable = 15338;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment = 15339;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge = 15359;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve = 15406;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve = 15408;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve = 15409;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable = 15485;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable = 15486;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment = 15487;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge = 15507;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 15554;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve = 15556;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 15557;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open = 587;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close = 590;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read = 592;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write = 595;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition = 597;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition = 600;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks = 603;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable = 15633;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable = 15634;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment = 15635;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge = 15655;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve = 15702;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve = 15704;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve = 15705;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable = 15781;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable = 15782;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment = 15783;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge = 15803;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 15850;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve = 15852;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 15853;

        /// <remarks />
        public const uint CertificateDirectoryType_StartSigningRequest = 79;

        /// <remarks />
        public const uint CertificateDirectoryType_StartNewKeyPairRequest = 76;

        /// <remarks />
        public const uint CertificateDirectoryType_FinishRequest = 85;

        /// <remarks />
        public const uint CertificateDirectoryType_RevokeCertificate = 15003;

        /// <remarks />
        public const uint CertificateDirectoryType_GetCertificateGroups = 369;

        /// <remarks />
        public const uint CertificateDirectoryType_GetTrustList = 197;

        /// <remarks />
        public const uint CertificateDirectoryType_GetCertificateStatus = 222;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest = 168;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest = 196;

        /// <remarks />
        public const uint KeyCredentialManagement_ServiceName_Placeholder_StartRequest = 1012;

        /// <remarks />
        public const uint KeyCredentialManagement_ServiceName_Placeholder_FinishRequest = 1015;

        /// <remarks />
        public const uint KeyCredentialServiceType_StartRequest = 1023;

        /// <remarks />
        public const uint KeyCredentialServiceType_FinishRequest = 1026;

        /// <remarks />
        public const uint KeyCredentialServiceType_Revoke = 1029;

        /// <remarks />
        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription = 238;

        /// <remarks />
        public const uint AuthorizationServices_ServiceName_Placeholder_GetServiceDescription = 1001;

        /// <remarks />
        public const uint AuthorizationServiceType_GetServiceDescription = 1004;

        /// <remarks />
        public const uint AuthorizationServiceType_RequestAccessToken = 969;

        /// <remarks />
        public const uint Directory_FindApplications = 143;

        /// <remarks />
        public const uint Directory_RegisterApplication = 146;

        /// <remarks />
        public const uint Directory_UpdateApplication = 200;

        /// <remarks />
        public const uint Directory_UnregisterApplication = 149;

        /// <remarks />
        public const uint Directory_GetApplication = 216;

        /// <remarks />
        public const uint Directory_QueryApplications = 992;

        /// <remarks />
        public const uint Directory_QueryServers = 151;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open = 622;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close = 625;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read = 627;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write = 630;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition = 632;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition = 635;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks = 638;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate = 641;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate = 644;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate = 646;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable = 15946;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable = 15947;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment = 15948;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge = 15968;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve = 16015;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve = 16017;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve = 16018;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable = 16094;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable = 16095;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment = 16096;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge = 16116;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 16163;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve = 16165;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 16166;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open = 656;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close = 659;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read = 661;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write = 664;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition = 666;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition = 669;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks = 672;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate = 675;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate = 678;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate = 680;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable = 16242;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable = 16243;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment = 16244;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge = 16264;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve = 16311;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve = 16313;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve = 16314;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable = 16390;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable = 16391;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment = 16392;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge = 16412;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 16459;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve = 16461;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 16462;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open = 690;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close = 693;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read = 695;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write = 698;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition = 700;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition = 703;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks = 706;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate = 709;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate = 712;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate = 714;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable = 16538;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable = 16539;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment = 16540;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge = 16560;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve = 16607;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve = 16609;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve = 16610;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable = 16686;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable = 16687;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment = 16688;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge = 16708;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 16755;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve = 16757;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 16758;

        /// <remarks />
        public const uint Directory_StartSigningRequest = 157;

        /// <remarks />
        public const uint Directory_StartNewKeyPairRequest = 154;

        /// <remarks />
        public const uint Directory_FinishRequest = 163;

        /// <remarks />
        public const uint Directory_GetCertificateGroups = 508;

        /// <remarks />
        public const uint Directory_GetTrustList = 204;

        /// <remarks />
        public const uint Directory_GetCertificateStatus = 225;
    }
    #endregion

    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata = 721;

        /// <remarks />
        public const uint DirectoryType_Applications = 14;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups = 511;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup = 512;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList = 513;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList = 547;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList = 581;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder = 61;

        /// <remarks />
        public const uint KeyCredentialManagement = 1008;

        /// <remarks />
        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder = 234;

        /// <remarks />
        public const uint AuthorizationServices = 959;

        /// <remarks />
        public const uint Directory = 141;

        /// <remarks />
        public const uint Directory_Applications = 142;

        /// <remarks />
        public const uint Directory_CertificateGroups = 614;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup = 615;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList = 616;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup = 649;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList = 650;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup = 683;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList = 684;

        /// <remarks />
        public const uint ApplicationRecordDataType_Encoding_DefaultBinary = 134;

        /// <remarks />
        public const uint ApplicationRecordDataType_Encoding_DefaultXml = 127;

        /// <remarks />
        public const uint ApplicationRecordDataType_Encoding_DefaultJson = 8001;
    }
    #endregion

    #region ObjectType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <remarks />
        public const uint DirectoryType = 13;

        /// <remarks />
        public const uint ApplicationRegistrationChangedAuditEventType = 26;

        /// <remarks />
        public const uint CertificateDirectoryType = 63;

        /// <remarks />
        public const uint CertificateRequestedAuditEventType = 91;

        /// <remarks />
        public const uint CertificateDeliveredAuditEventType = 109;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType = 55;

        /// <remarks />
        public const uint KeyCredentialServiceType = 1020;

        /// <remarks />
        public const uint KeyCredentialRequestedAuditEventType = 1039;

        /// <remarks />
        public const uint KeyCredentialDeliveredAuditEventType = 1057;

        /// <remarks />
        public const uint KeyCredentialRevokedAuditEventType = 1075;

        /// <remarks />
        public const uint AuthorizationServicesFolderType = 233;

        /// <remarks />
        public const uint AuthorizationServiceType = 966;

        /// <remarks />
        public const uint AccessTokenIssuedAuditEventType = 975;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceUri = 722;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceVersion = 723;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespacePublicationDate = 724;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_IsNamespaceSubset = 725;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_StaticNodeIdTypes = 726;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_StaticNumericNodeIdRange = 727;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_StaticStringNodeIdPattern = 728;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Size = 730;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Writable = 731;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_UserWritable = 732;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_OpenCount = 733;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Open_InputArguments = 736;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Open_OutputArguments = 737;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Close_InputArguments = 739;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Read_InputArguments = 741;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Read_OutputArguments = 742;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Write_InputArguments = 744;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_InputArguments = 746;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_OutputArguments = 747;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition_InputArguments = 749;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_DefaultRolePermissions = 862;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_DefaultUserRolePermissions = 863;

        /// <remarks />
        public const uint OPCUAGDSNamespaceMetadata_DefaultAccessRestrictions = 864;

        /// <remarks />
        public const uint DirectoryType_FindApplications_InputArguments = 16;

        /// <remarks />
        public const uint DirectoryType_FindApplications_OutputArguments = 17;

        /// <remarks />
        public const uint DirectoryType_RegisterApplication_InputArguments = 19;

        /// <remarks />
        public const uint DirectoryType_RegisterApplication_OutputArguments = 20;

        /// <remarks />
        public const uint DirectoryType_UpdateApplication_InputArguments = 189;

        /// <remarks />
        public const uint DirectoryType_UnregisterApplication_InputArguments = 22;

        /// <remarks />
        public const uint DirectoryType_GetApplication_InputArguments = 211;

        /// <remarks />
        public const uint DirectoryType_GetApplication_OutputArguments = 212;

        /// <remarks />
        public const uint DirectoryType_QueryApplications_InputArguments = 869;

        /// <remarks />
        public const uint DirectoryType_QueryApplications_OutputArguments = 870;

        /// <remarks />
        public const uint DirectoryType_QueryServers_InputArguments = 24;

        /// <remarks />
        public const uint DirectoryType_QueryServers_OutputArguments = 25;

        /// <remarks />
        public const uint CertificateDirectoryType_FindApplications_InputArguments = 66;

        /// <remarks />
        public const uint CertificateDirectoryType_FindApplications_OutputArguments = 67;

        /// <remarks />
        public const uint CertificateDirectoryType_RegisterApplication_InputArguments = 69;

        /// <remarks />
        public const uint CertificateDirectoryType_RegisterApplication_OutputArguments = 70;

        /// <remarks />
        public const uint CertificateDirectoryType_UpdateApplication_InputArguments = 194;

        /// <remarks />
        public const uint CertificateDirectoryType_UnregisterApplication_InputArguments = 72;

        /// <remarks />
        public const uint CertificateDirectoryType_GetApplication_InputArguments = 214;

        /// <remarks />
        public const uint CertificateDirectoryType_GetApplication_OutputArguments = 215;

        /// <remarks />
        public const uint CertificateDirectoryType_QueryApplications_InputArguments = 872;

        /// <remarks />
        public const uint CertificateDirectoryType_QueryApplications_OutputArguments = 873;

        /// <remarks />
        public const uint CertificateDirectoryType_QueryServers_InputArguments = 74;

        /// <remarks />
        public const uint CertificateDirectoryType_QueryServers_OutputArguments = 75;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Size = 514;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Writable = 515;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable = 516;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount = 517;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments = 520;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments = 521;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments = 523;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments = 525;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments = 526;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments = 528;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments = 530;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments = 531;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments = 533;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime = 534;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments = 536;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments = 537;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments = 539;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments = 540;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments = 542;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments = 544;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateTypes = 545;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId = 15009;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType = 15010;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode = 15011;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName = 15012;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time = 15013;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime = 15014;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message = 15016;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity = 15017;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId = 15018;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName = 15019;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName = 15022;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId = 15023;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain = 15024;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState = 15025;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id = 15026;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality = 15034;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp = 15035;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity = 15036;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp = 15037;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment = 15038;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp = 15039;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId = 15040;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments = 15044;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState = 15045;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id = 15046;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id = 15055;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments = 15064;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments = 15066;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState = 15067;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id = 15068;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode = 15076;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id = 15078;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id = 15087;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState = 15096;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id = 15097;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id = 15102;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime = 15109;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 15111;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved = 15114;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id = 15122;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id = 15135;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState = 15151;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate = 15152;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType = 15154;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate = 15155;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId = 15157;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType = 15158;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode = 15159;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName = 15160;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time = 15161;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime = 15162;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message = 15164;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity = 15165;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId = 15166;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName = 15167;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName = 15170;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId = 15171;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain = 15172;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState = 15173;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id = 15174;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality = 15182;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp = 15183;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity = 15184;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 15185;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment = 15186;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp = 15187;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId = 15188;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments = 15192;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState = 15193;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id = 15194;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id = 15203;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments = 15212;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments = 15214;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState = 15215;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id = 15216;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode = 15224;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id = 15226;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id = 15235;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState = 15244;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 15245;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 15250;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 15257;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 15259;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved = 15262;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id = 15270;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id = 15283;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState = 15299;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId = 15300;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime = 15301;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency = 15302;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments = 60;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Size = 548;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Writable = 549;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable = 550;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount = 551;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments = 554;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments = 555;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments = 557;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments = 559;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments = 560;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments = 562;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments = 564;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments = 565;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments = 567;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime = 568;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments = 570;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments = 571;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments = 573;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments = 574;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments = 576;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments = 578;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateTypes = 579;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId = 15305;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType = 15306;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode = 15307;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName = 15308;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time = 15309;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime = 15310;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message = 15312;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity = 15313;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId = 15314;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName = 15315;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName = 15318;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId = 15319;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain = 15320;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState = 15321;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id = 15322;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality = 15330;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp = 15331;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity = 15332;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp = 15333;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment = 15334;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp = 15335;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId = 15336;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments = 15340;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState = 15341;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id = 15342;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id = 15351;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments = 15360;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments = 15362;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState = 15363;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id = 15364;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode = 15372;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id = 15374;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id = 15383;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState = 15392;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id = 15393;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id = 15398;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime = 15405;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 15407;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved = 15410;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id = 15418;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id = 15431;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState = 15447;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate = 15448;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType = 15450;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate = 15451;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId = 15453;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType = 15454;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode = 15455;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName = 15456;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time = 15457;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime = 15458;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message = 15460;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity = 15461;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId = 15462;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName = 15463;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName = 15466;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId = 15467;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain = 15468;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState = 15469;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id = 15470;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality = 15478;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp = 15479;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity = 15480;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 15481;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment = 15482;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp = 15483;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId = 15484;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments = 15488;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState = 15489;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id = 15490;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id = 15499;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments = 15508;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments = 15510;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState = 15511;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id = 15512;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode = 15520;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id = 15522;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id = 15531;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState = 15540;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 15541;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 15546;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 15553;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 15555;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved = 15558;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id = 15566;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id = 15579;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState = 15595;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId = 15596;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime = 15597;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency = 15598;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments = 82;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Size = 582;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable = 583;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable = 584;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount = 585;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments = 588;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments = 589;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments = 591;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments = 593;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments = 594;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments = 596;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments = 598;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments = 599;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments = 601;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime = 602;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments = 604;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments = 605;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments = 607;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments = 608;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments = 610;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments = 612;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateTypes = 613;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId = 15601;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType = 15602;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode = 15603;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName = 15604;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time = 15605;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime = 15606;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message = 15608;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity = 15609;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId = 15610;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName = 15611;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName = 15614;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId = 15615;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain = 15616;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState = 15617;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id = 15618;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality = 15626;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp = 15627;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity = 15628;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp = 15629;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment = 15630;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp = 15631;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId = 15632;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments = 15636;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState = 15637;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id = 15638;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id = 15647;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments = 15656;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments = 15658;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState = 15659;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id = 15660;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode = 15668;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id = 15670;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id = 15679;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState = 15688;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id = 15689;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id = 15694;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime = 15701;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 15703;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved = 15706;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id = 15714;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id = 15727;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState = 15743;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate = 15744;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType = 15746;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate = 15747;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId = 15749;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType = 15750;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode = 15751;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName = 15752;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time = 15753;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime = 15754;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message = 15756;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity = 15757;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId = 15758;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName = 15759;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName = 15762;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId = 15763;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain = 15764;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState = 15765;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id = 15766;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality = 15774;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp = 15775;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity = 15776;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 15777;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment = 15778;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp = 15779;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId = 15780;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments = 15784;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState = 15785;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id = 15786;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id = 15795;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments = 15804;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments = 15806;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState = 15807;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id = 15808;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode = 15816;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id = 15818;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id = 15827;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState = 15836;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 15837;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 15842;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 15849;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 15851;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved = 15854;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id = 15862;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id = 15875;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState = 15891;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId = 15892;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime = 15893;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency = 15894;

        /// <remarks />
        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments = 88;

        /// <remarks />
        public const uint CertificateDirectoryType_StartSigningRequest_InputArguments = 80;

        /// <remarks />
        public const uint CertificateDirectoryType_StartSigningRequest_OutputArguments = 81;

        /// <remarks />
        public const uint CertificateDirectoryType_StartNewKeyPairRequest_InputArguments = 77;

        /// <remarks />
        public const uint CertificateDirectoryType_StartNewKeyPairRequest_OutputArguments = 78;

        /// <remarks />
        public const uint CertificateDirectoryType_FinishRequest_InputArguments = 86;

        /// <remarks />
        public const uint CertificateDirectoryType_FinishRequest_OutputArguments = 87;

        /// <remarks />
        public const uint CertificateDirectoryType_RevokeCertificate_InputArguments = 15004;

        /// <remarks />
        public const uint CertificateDirectoryType_GetCertificateGroups_InputArguments = 370;

        /// <remarks />
        public const uint CertificateDirectoryType_GetCertificateGroups_OutputArguments = 371;

        /// <remarks />
        public const uint CertificateDirectoryType_GetTrustList_InputArguments = 198;

        /// <remarks />
        public const uint CertificateDirectoryType_GetTrustList_OutputArguments = 199;

        /// <remarks />
        public const uint CertificateDirectoryType_GetCertificateStatus_InputArguments = 223;

        /// <remarks />
        public const uint CertificateDirectoryType_GetCertificateStatus_OutputArguments = 224;

        /// <remarks />
        public const uint CertificateRequestedAuditEventType_CertificateGroup = 717;

        /// <remarks />
        public const uint CertificateRequestedAuditEventType_CertificateType = 718;

        /// <remarks />
        public const uint CertificateDeliveredAuditEventType_CertificateGroup = 719;

        /// <remarks />
        public const uint CertificateDeliveredAuditEventType_CertificateType = 720;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_ResourceUri = 83;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_ProfileUris = 162;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_InputArguments = 171;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_OutputArguments = 195;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_InputArguments = 202;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_OutputArguments = 203;

        /// <remarks />
        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_Revoke_InputArguments = 229;

        /// <remarks />
        public const uint KeyCredentialManagement_ServiceName_Placeholder_ResourceUri = 1010;

        /// <remarks />
        public const uint KeyCredentialManagement_ServiceName_Placeholder_ProfileUris = 1011;

        /// <remarks />
        public const uint KeyCredentialManagement_ServiceName_Placeholder_StartRequest_InputArguments = 1013;

        /// <remarks />
        public const uint KeyCredentialManagement_ServiceName_Placeholder_StartRequest_OutputArguments = 1014;

        /// <remarks />
        public const uint KeyCredentialManagement_ServiceName_Placeholder_FinishRequest_InputArguments = 1016;

        /// <remarks />
        public const uint KeyCredentialManagement_ServiceName_Placeholder_FinishRequest_OutputArguments = 1017;

        /// <remarks />
        public const uint KeyCredentialManagement_ServiceName_Placeholder_Revoke_InputArguments = 1019;

        /// <remarks />
        public const uint KeyCredentialServiceType_ResourceUri = 1021;

        /// <remarks />
        public const uint KeyCredentialServiceType_ProfileUris = 1022;

        /// <remarks />
        public const uint KeyCredentialServiceType_StartRequest_InputArguments = 1024;

        /// <remarks />
        public const uint KeyCredentialServiceType_StartRequest_OutputArguments = 1025;

        /// <remarks />
        public const uint KeyCredentialServiceType_FinishRequest_InputArguments = 1027;

        /// <remarks />
        public const uint KeyCredentialServiceType_FinishRequest_OutputArguments = 1028;

        /// <remarks />
        public const uint KeyCredentialServiceType_Revoke_InputArguments = 1030;

        /// <remarks />
        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceUri = 235;

        /// <remarks />
        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceCertificate = 236;

        /// <remarks />
        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription_OutputArguments = 239;

        /// <remarks />
        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_InputArguments = 241;

        /// <remarks />
        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_OutputArguments = 242;

        /// <remarks />
        public const uint AuthorizationServices_ServiceName_Placeholder_ServiceUri = 1000;

        /// <remarks />
        public const uint AuthorizationServices_ServiceName_Placeholder_ServiceCertificate = 962;

        /// <remarks />
        public const uint AuthorizationServices_ServiceName_Placeholder_GetServiceDescription_OutputArguments = 1002;

        /// <remarks />
        public const uint AuthorizationServices_ServiceName_Placeholder_RequestAccessToken_InputArguments = 964;

        /// <remarks />
        public const uint AuthorizationServices_ServiceName_Placeholder_RequestAccessToken_OutputArguments = 965;

        /// <remarks />
        public const uint AuthorizationServiceType_ServiceUri = 1003;

        /// <remarks />
        public const uint AuthorizationServiceType_ServiceCertificate = 968;

        /// <remarks />
        public const uint AuthorizationServiceType_UserTokenPolicies = 967;

        /// <remarks />
        public const uint AuthorizationServiceType_GetServiceDescription_OutputArguments = 1005;

        /// <remarks />
        public const uint AuthorizationServiceType_RequestAccessToken_InputArguments = 970;

        /// <remarks />
        public const uint AuthorizationServiceType_RequestAccessToken_OutputArguments = 971;

        /// <remarks />
        public const uint Directory_FindApplications_InputArguments = 144;

        /// <remarks />
        public const uint Directory_FindApplications_OutputArguments = 145;

        /// <remarks />
        public const uint Directory_RegisterApplication_InputArguments = 147;

        /// <remarks />
        public const uint Directory_RegisterApplication_OutputArguments = 148;

        /// <remarks />
        public const uint Directory_UpdateApplication_InputArguments = 201;

        /// <remarks />
        public const uint Directory_UnregisterApplication_InputArguments = 150;

        /// <remarks />
        public const uint Directory_GetApplication_InputArguments = 217;

        /// <remarks />
        public const uint Directory_GetApplication_OutputArguments = 218;

        /// <remarks />
        public const uint Directory_QueryApplications_InputArguments = 993;

        /// <remarks />
        public const uint Directory_QueryApplications_OutputArguments = 994;

        /// <remarks />
        public const uint Directory_QueryServers_InputArguments = 152;

        /// <remarks />
        public const uint Directory_QueryServers_OutputArguments = 153;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Size = 617;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Writable = 618;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable = 619;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount = 620;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments = 623;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments = 624;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments = 626;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments = 628;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments = 629;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments = 631;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments = 633;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments = 634;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments = 636;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime = 637;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments = 639;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments = 640;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments = 642;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments = 643;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments = 645;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments = 647;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateTypes = 648;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId = 15914;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType = 15915;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode = 15916;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName = 15917;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time = 15918;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime = 15919;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message = 15921;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity = 15922;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId = 15923;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName = 15924;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName = 15927;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId = 15928;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain = 15929;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState = 15930;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id = 15931;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality = 15939;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp = 15940;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity = 15941;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp = 15942;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment = 15943;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp = 15944;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId = 15945;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments = 15949;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState = 15950;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id = 15951;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id = 15960;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments = 15969;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments = 15971;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState = 15972;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id = 15973;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode = 15981;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id = 15983;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id = 15992;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState = 16001;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id = 16002;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id = 16007;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime = 16014;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 16016;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved = 16019;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id = 16027;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id = 16040;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState = 16056;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate = 16057;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType = 16059;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate = 16060;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId = 16062;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType = 16063;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode = 16064;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName = 16065;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time = 16066;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime = 16067;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message = 16069;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity = 16070;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId = 16071;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName = 16072;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName = 16075;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId = 16076;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain = 16077;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState = 16078;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id = 16079;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality = 16087;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp = 16088;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity = 16089;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 16090;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment = 16091;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp = 16092;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId = 16093;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments = 16097;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState = 16098;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id = 16099;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id = 16108;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments = 16117;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments = 16119;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState = 16120;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id = 16121;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode = 16129;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id = 16131;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id = 16140;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState = 16149;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 16150;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 16155;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 16162;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 16164;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved = 16167;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id = 16175;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id = 16188;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState = 16204;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId = 16205;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime = 16206;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency = 16207;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments = 167;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Size = 651;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Writable = 652;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable = 653;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount = 654;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments = 657;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments = 658;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments = 660;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments = 662;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments = 663;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments = 665;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments = 667;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments = 668;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments = 670;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime = 671;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments = 673;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments = 674;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments = 676;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments = 677;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments = 679;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments = 681;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateTypes = 682;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId = 16210;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType = 16211;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode = 16212;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName = 16213;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time = 16214;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime = 16215;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message = 16217;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity = 16218;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId = 16219;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName = 16220;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName = 16223;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId = 16224;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain = 16225;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState = 16226;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id = 16227;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality = 16235;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp = 16236;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity = 16237;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp = 16238;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment = 16239;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp = 16240;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId = 16241;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments = 16245;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState = 16246;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id = 16247;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id = 16256;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments = 16265;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments = 16267;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState = 16268;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id = 16269;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode = 16277;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id = 16279;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id = 16288;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState = 16297;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id = 16298;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id = 16303;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime = 16310;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 16312;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved = 16315;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id = 16323;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id = 16336;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState = 16352;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate = 16353;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType = 16355;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate = 16356;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId = 16358;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType = 16359;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode = 16360;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName = 16361;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time = 16362;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime = 16363;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message = 16365;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity = 16366;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId = 16367;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName = 16368;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName = 16371;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId = 16372;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain = 16373;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState = 16374;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id = 16375;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality = 16383;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp = 16384;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity = 16385;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 16386;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment = 16387;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp = 16388;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId = 16389;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments = 16393;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState = 16394;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id = 16395;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id = 16404;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments = 16413;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments = 16415;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState = 16416;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id = 16417;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode = 16425;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id = 16427;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id = 16436;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState = 16445;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 16446;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 16451;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 16458;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 16460;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved = 16463;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id = 16471;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id = 16484;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState = 16500;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId = 16501;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime = 16502;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency = 16503;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments = 170;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Size = 685;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable = 686;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable = 687;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount = 688;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments = 691;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments = 692;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments = 694;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments = 696;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments = 697;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments = 699;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments = 701;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments = 702;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments = 704;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime = 705;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments = 707;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments = 708;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments = 710;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments = 711;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments = 713;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments = 715;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateTypes = 716;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId = 16506;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType = 16507;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode = 16508;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName = 16509;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time = 16510;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime = 16511;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message = 16513;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity = 16514;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId = 16515;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName = 16516;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName = 16519;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId = 16520;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain = 16521;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState = 16522;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id = 16523;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality = 16531;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp = 16532;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity = 16533;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp = 16534;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment = 16535;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp = 16536;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId = 16537;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments = 16541;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState = 16542;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id = 16543;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id = 16552;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments = 16561;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments = 16563;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState = 16564;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id = 16565;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode = 16573;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id = 16575;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id = 16584;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState = 16593;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id = 16594;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id = 16599;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime = 16606;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 16608;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved = 16611;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id = 16619;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id = 16632;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState = 16648;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate = 16649;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType = 16651;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate = 16652;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId = 16654;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType = 16655;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode = 16656;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName = 16657;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time = 16658;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime = 16659;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message = 16661;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity = 16662;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId = 16663;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName = 16664;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName = 16667;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId = 16668;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain = 16669;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState = 16670;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id = 16671;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality = 16679;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp = 16680;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity = 16681;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 16682;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment = 16683;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp = 16684;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId = 16685;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments = 16689;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState = 16690;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id = 16691;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id = 16700;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments = 16709;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments = 16711;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState = 16712;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id = 16713;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode = 16721;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id = 16723;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id = 16732;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState = 16741;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 16742;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 16747;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 16754;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 16756;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved = 16759;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id = 16767;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id = 16780;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState = 16796;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId = 16797;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime = 16798;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency = 16799;

        /// <remarks />
        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments = 173;

        /// <remarks />
        public const uint Directory_StartSigningRequest_InputArguments = 158;

        /// <remarks />
        public const uint Directory_StartSigningRequest_OutputArguments = 159;

        /// <remarks />
        public const uint Directory_StartNewKeyPairRequest_InputArguments = 155;

        /// <remarks />
        public const uint Directory_StartNewKeyPairRequest_OutputArguments = 156;

        /// <remarks />
        public const uint Directory_FinishRequest_InputArguments = 164;

        /// <remarks />
        public const uint Directory_FinishRequest_OutputArguments = 165;

        /// <remarks />
        public const uint Directory_RevokeCertificate_InputArguments = 15006;

        /// <remarks />
        public const uint Directory_GetCertificateGroups_InputArguments = 509;

        /// <remarks />
        public const uint Directory_GetCertificateGroups_OutputArguments = 510;

        /// <remarks />
        public const uint Directory_GetTrustList_InputArguments = 205;

        /// <remarks />
        public const uint Directory_GetTrustList_OutputArguments = 206;

        /// <remarks />
        public const uint Directory_GetCertificateStatus_InputArguments = 226;

        /// <remarks />
        public const uint Directory_GetCertificateStatus_OutputArguments = 227;

        /// <remarks />
        public const uint OpcUaGds_BinarySchema = 135;

        /// <remarks />
        public const uint OpcUaGds_BinarySchema_NamespaceUri = 137;

        /// <remarks />
        public const uint OpcUaGds_BinarySchema_Deprecated = 8002;

        /// <remarks />
        public const uint OpcUaGds_BinarySchema_ApplicationRecordDataType = 138;

        /// <remarks />
        public const uint OpcUaGds_XmlSchema = 128;

        /// <remarks />
        public const uint OpcUaGds_XmlSchema_NamespaceUri = 130;

        /// <remarks />
        public const uint OpcUaGds_XmlSchema_Deprecated = 8004;

        /// <remarks />
        public const uint OpcUaGds_XmlSchema_ApplicationRecordDataType = 131;
    }
    #endregion

    #region DataType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId ApplicationRecordDataType = new ExpandedNodeId(Opc.Ua.Gds.DataTypes.ApplicationRecordDataType, Opc.Ua.Gds.Namespaces.OpcUaGds);
    }
    #endregion

    #region Method Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class MethodIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_FindApplications = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_FindApplications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_RegisterApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_RegisterApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_UpdateApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_UpdateApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_UnregisterApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_UnregisterApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_GetApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_GetApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_QueryApplications = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_QueryApplications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_QueryServers = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_QueryServers, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_StartSigningRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_StartSigningRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_StartNewKeyPairRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_StartNewKeyPairRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_FinishRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_FinishRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_RevokeCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_RevokeCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateGroups = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_GetCertificateGroups, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetTrustList = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_GetTrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateStatus = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_GetCertificateStatus, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagement_ServiceName_Placeholder_StartRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialManagement_ServiceName_Placeholder_StartRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagement_ServiceName_Placeholder_FinishRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialManagement_ServiceName_Placeholder_FinishRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType_StartRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialServiceType_StartRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType_FinishRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialServiceType_FinishRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType_Revoke = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialServiceType_Revoke, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription = new ExpandedNodeId(Opc.Ua.Gds.Methods.AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServices_ServiceName_Placeholder_GetServiceDescription = new ExpandedNodeId(Opc.Ua.Gds.Methods.AuthorizationServices_ServiceName_Placeholder_GetServiceDescription, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServiceType_GetServiceDescription = new ExpandedNodeId(Opc.Ua.Gds.Methods.AuthorizationServiceType_GetServiceDescription, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServiceType_RequestAccessToken = new ExpandedNodeId(Opc.Ua.Gds.Methods.AuthorizationServiceType_RequestAccessToken, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_FindApplications = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_FindApplications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_RegisterApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_RegisterApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_UpdateApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_UpdateApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_UnregisterApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_UnregisterApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_GetApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_QueryApplications = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_QueryApplications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_QueryServers = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_QueryServers, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_StartSigningRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_StartSigningRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_StartNewKeyPairRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_StartNewKeyPairRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_FinishRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_FinishRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetCertificateGroups = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_GetCertificateGroups, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetTrustList = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_GetTrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetCertificateStatus = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_GetCertificateStatus, Opc.Ua.Gds.Namespaces.OpcUaGds);
    }
    #endregion

    #region Object Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata = new ExpandedNodeId(Opc.Ua.Gds.Objects.OPCUAGDSNamespaceMetadata, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_Applications = new ExpandedNodeId(Opc.Ua.Gds.Objects.DirectoryType_Applications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups = new ExpandedNodeId(Opc.Ua.Gds.Objects.CertificateDirectoryType_CertificateGroups, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup = new ExpandedNodeId(Opc.Ua.Gds.Objects.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder = new ExpandedNodeId(Opc.Ua.Gds.Objects.KeyCredentialManagementFolderType_ServiceName_Placeholder, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagement = new ExpandedNodeId(Opc.Ua.Gds.Objects.KeyCredentialManagement, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder = new ExpandedNodeId(Opc.Ua.Gds.Objects.AuthorizationServicesFolderType_ServiceName_Placeholder, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServices = new ExpandedNodeId(Opc.Ua.Gds.Objects.AuthorizationServices, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_Applications = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_Applications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultApplicationGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultApplicationGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultHttpsGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultHttpsGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultUserTokenGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId ApplicationRecordDataType_Encoding_DefaultBinary = new ExpandedNodeId(Opc.Ua.Gds.Objects.ApplicationRecordDataType_Encoding_DefaultBinary, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId ApplicationRecordDataType_Encoding_DefaultXml = new ExpandedNodeId(Opc.Ua.Gds.Objects.ApplicationRecordDataType_Encoding_DefaultXml, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId ApplicationRecordDataType_Encoding_DefaultJson = new ExpandedNodeId(Opc.Ua.Gds.Objects.ApplicationRecordDataType_Encoding_DefaultJson, Opc.Ua.Gds.Namespaces.OpcUaGds);
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.DirectoryType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId ApplicationRegistrationChangedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.ApplicationRegistrationChangedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.CertificateDirectoryType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateRequestedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.CertificateRequestedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDeliveredAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.CertificateDeliveredAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.KeyCredentialManagementFolderType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.KeyCredentialServiceType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialRequestedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.KeyCredentialRequestedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialDeliveredAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.KeyCredentialDeliveredAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialRevokedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.KeyCredentialRevokedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServicesFolderType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.AuthorizationServicesFolderType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServiceType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.AuthorizationServiceType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AccessTokenIssuedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.AccessTokenIssuedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);
    }
    #endregion

    #region Variable Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceVersion = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceVersion, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespacePublicationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespacePublicationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_IsNamespaceSubset = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_IsNamespaceSubset, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_StaticNodeIdTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_StaticNodeIdTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_StaticNumericNodeIdRange = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_StaticNumericNodeIdRange, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_StaticStringNodeIdPattern = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_StaticStringNodeIdPattern, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_DefaultRolePermissions = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_DefaultRolePermissions, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_DefaultUserRolePermissions = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_DefaultUserRolePermissions, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_DefaultAccessRestrictions = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_DefaultAccessRestrictions, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_FindApplications_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_FindApplications_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_FindApplications_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_FindApplications_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_RegisterApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_RegisterApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_RegisterApplication_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_RegisterApplication_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_UpdateApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_UpdateApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_UnregisterApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_UnregisterApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_GetApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_GetApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_GetApplication_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_GetApplication_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_QueryApplications_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_QueryApplications_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_QueryApplications_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_QueryApplications_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_QueryServers_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_QueryServers_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId DirectoryType_QueryServers_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_QueryServers_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_FindApplications_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_FindApplications_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_FindApplications_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_FindApplications_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_RegisterApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_RegisterApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_RegisterApplication_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_RegisterApplication_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_UpdateApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_UpdateApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_UnregisterApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_UnregisterApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetApplication_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetApplication_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_QueryApplications_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_QueryApplications_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_QueryApplications_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_QueryApplications_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_QueryServers_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_QueryServers_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_QueryServers_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_QueryServers_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_StartSigningRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_StartSigningRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_StartSigningRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_StartSigningRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_StartNewKeyPairRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_StartNewKeyPairRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_StartNewKeyPairRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_StartNewKeyPairRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_FinishRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_FinishRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_FinishRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_FinishRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_RevokeCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_RevokeCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateGroups_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetCertificateGroups_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateGroups_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetCertificateGroups_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetTrustList_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetTrustList_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetTrustList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetTrustList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateStatus_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetCertificateStatus_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateStatus_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetCertificateStatus_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateRequestedAuditEventType_CertificateGroup = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateRequestedAuditEventType_CertificateGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateRequestedAuditEventType_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateRequestedAuditEventType_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDeliveredAuditEventType_CertificateGroup = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDeliveredAuditEventType_CertificateGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId CertificateDeliveredAuditEventType_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDeliveredAuditEventType_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_ResourceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_ResourceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_ProfileUris = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_ProfileUris, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_Revoke_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_Revoke_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagement_ServiceName_Placeholder_ResourceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagement_ServiceName_Placeholder_ResourceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagement_ServiceName_Placeholder_ProfileUris = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagement_ServiceName_Placeholder_ProfileUris, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagement_ServiceName_Placeholder_StartRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagement_ServiceName_Placeholder_StartRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagement_ServiceName_Placeholder_StartRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagement_ServiceName_Placeholder_StartRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagement_ServiceName_Placeholder_FinishRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagement_ServiceName_Placeholder_FinishRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagement_ServiceName_Placeholder_FinishRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagement_ServiceName_Placeholder_FinishRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialManagement_ServiceName_Placeholder_Revoke_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagement_ServiceName_Placeholder_Revoke_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType_ResourceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_ResourceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType_ProfileUris = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_ProfileUris, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType_StartRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_StartRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType_StartRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_StartRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType_FinishRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_FinishRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType_FinishRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_FinishRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId KeyCredentialServiceType_Revoke_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_Revoke_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceCertificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServices_ServiceName_Placeholder_ServiceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServices_ServiceName_Placeholder_ServiceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServices_ServiceName_Placeholder_ServiceCertificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServices_ServiceName_Placeholder_ServiceCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServices_ServiceName_Placeholder_GetServiceDescription_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServices_ServiceName_Placeholder_GetServiceDescription_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServices_ServiceName_Placeholder_RequestAccessToken_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServices_ServiceName_Placeholder_RequestAccessToken_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServices_ServiceName_Placeholder_RequestAccessToken_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServices_ServiceName_Placeholder_RequestAccessToken_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServiceType_ServiceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_ServiceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServiceType_ServiceCertificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_ServiceCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServiceType_UserTokenPolicies = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_UserTokenPolicies, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServiceType_GetServiceDescription_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_GetServiceDescription_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServiceType_RequestAccessToken_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_RequestAccessToken_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId AuthorizationServiceType_RequestAccessToken_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_RequestAccessToken_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_FindApplications_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_FindApplications_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_FindApplications_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_FindApplications_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_RegisterApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_RegisterApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_RegisterApplication_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_RegisterApplication_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_UpdateApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_UpdateApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_UnregisterApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_UnregisterApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetApplication_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetApplication_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_QueryApplications_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_QueryApplications_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_QueryApplications_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_QueryApplications_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_QueryServers_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_QueryServers_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_QueryServers_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_QueryServers_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_StartSigningRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_StartSigningRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_StartSigningRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_StartSigningRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_StartNewKeyPairRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_StartNewKeyPairRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_StartNewKeyPairRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_StartNewKeyPairRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_FinishRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_FinishRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_FinishRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_FinishRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_RevokeCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_RevokeCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetCertificateGroups_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetCertificateGroups_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetCertificateGroups_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetCertificateGroups_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetTrustList_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetTrustList_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetTrustList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetTrustList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetCertificateStatus_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetCertificateStatus_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId Directory_GetCertificateStatus_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetCertificateStatus_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OpcUaGds_BinarySchema = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_BinarySchema, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OpcUaGds_BinarySchema_NamespaceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_BinarySchema_NamespaceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OpcUaGds_BinarySchema_Deprecated = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_BinarySchema_Deprecated, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OpcUaGds_BinarySchema_ApplicationRecordDataType = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_BinarySchema_ApplicationRecordDataType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OpcUaGds_XmlSchema = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_XmlSchema, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OpcUaGds_XmlSchema_NamespaceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_XmlSchema_NamespaceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OpcUaGds_XmlSchema_Deprecated = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_XmlSchema_Deprecated, Opc.Ua.Gds.Namespaces.OpcUaGds);

        /// <remarks />
        public static readonly ExpandedNodeId OpcUaGds_XmlSchema_ApplicationRecordDataType = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_XmlSchema_ApplicationRecordDataType, Opc.Ua.Gds.Namespaces.OpcUaGds);
    }
    #endregion

    #region BrowseName Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class BrowseNames
    {
        /// <remarks />
        public const string AccessTokenIssuedAuditEventType = "AccessTokenIssuedAuditEventType";

        /// <remarks />
        public const string ApplicationRecordDataType = "ApplicationRecordDataType";

        /// <remarks />
        public const string ApplicationRegistrationChangedAuditEventType = "ApplicationRegistrationChangedAuditEventType";

        /// <remarks />
        public const string Applications = "Applications";

        /// <remarks />
        public const string AuthorizationServices = "AuthorizationServices";

        /// <remarks />
        public const string AuthorizationServicesFolderType = "AuthorizationServicesFolderType";

        /// <remarks />
        public const string AuthorizationServiceType = "AuthorizationServiceType";

        /// <remarks />
        public const string CertificateDeliveredAuditEventType = "CertificateDeliveredAuditEventType";

        /// <remarks />
        public const string CertificateDirectoryType = "CertificateDirectoryType";

        /// <remarks />
        public const string CertificateGroup = "CertificateGroup";

        /// <remarks />
        public const string CertificateGroups = "CertificateGroups";

        /// <remarks />
        public const string CertificateRequestedAuditEventType = "CertificateRequestedAuditEventType";

        /// <remarks />
        public const string CertificateType = "CertificateType";

        /// <remarks />
        public const string Directory = "Directory";

        /// <remarks />
        public const string DirectoryType = "DirectoryType";

        /// <remarks />
        public const string FindApplications = "FindApplications";

        /// <remarks />
        public const string FinishRequest = "FinishRequest";

        /// <remarks />
        public const string GetApplication = "GetApplication";

        /// <remarks />
        public const string GetCertificateGroups = "GetCertificateGroups";

        /// <remarks />
        public const string GetCertificateStatus = "GetCertificateStatus";

        /// <remarks />
        public const string GetServiceDescription = "GetServiceDescription";

        /// <remarks />
        public const string GetTrustList = "GetTrustList";

        /// <remarks />
        public const string KeyCredentialDeliveredAuditEventType = "KeyCredentialDeliveredAuditEventType";

        /// <remarks />
        public const string KeyCredentialManagement = "KeyCredentialManagement";

        /// <remarks />
        public const string KeyCredentialManagementFolderType = "KeyCredentialManagementFolderType";

        /// <remarks />
        public const string KeyCredentialRequestedAuditEventType = "KeyCredentialRequestedAuditEventType";

        /// <remarks />
        public const string KeyCredentialRevokedAuditEventType = "KeyCredentialRevokedAuditEventType";

        /// <remarks />
        public const string KeyCredentialServiceType = "KeyCredentialServiceType";

        /// <remarks />
        public const string OpcUaGds_BinarySchema = "Opc.Ua.Gds";

        /// <remarks />
        public const string OpcUaGds_XmlSchema = "Opc.Ua.Gds";

        /// <remarks />
        public const string OPCUAGDSNamespaceMetadata = "http://opcfoundation.org/UA/GDS/";

        /// <remarks />
        public const string ProfileUris = "ProfileUris";

        /// <remarks />
        public const string QueryApplications = "QueryApplications";

        /// <remarks />
        public const string QueryServers = "QueryServers";

        /// <remarks />
        public const string RegisterApplication = "RegisterApplication";

        /// <remarks />
        public const string RequestAccessToken = "RequestAccessToken";

        /// <remarks />
        public const string ResourceUri = "ResourceUri";

        /// <remarks />
        public const string Revoke = "Revoke";

        /// <remarks />
        public const string RevokeCertificate = "RevokeCertificate";

        /// <remarks />
        public const string ServiceCertificate = "ServiceCertificate";

        /// <remarks />
        public const string ServiceName_Placeholder = "<ServiceName>";

        /// <remarks />
        public const string ServiceUri = "ServiceUri";

        /// <remarks />
        public const string StartNewKeyPairRequest = "StartNewKeyPairRequest";

        /// <remarks />
        public const string StartRequest = "StartRequest";

        /// <remarks />
        public const string StartSigningRequest = "StartSigningRequest";

        /// <remarks />
        public const string UnregisterApplication = "UnregisterApplication";

        /// <remarks />
        public const string UpdateApplication = "UpdateApplication";

        /// <remarks />
        public const string UserTokenPolicies = "UserTokenPolicies";
    }
    #endregion

    #region Namespace Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUaGds namespace (.NET code namespace is 'Opc.Ua.Gds').
        /// </summary>
        public const string OpcUaGds = "http://opcfoundation.org/UA/GDS/";

        /// <summary>
        /// The URI for the OpcUaGdsXsd namespace (.NET code namespace is 'Opc.Ua.Gds').
        /// </summary>
        public const string OpcUaGdsXsd = "http://opcfoundation.org/UA/GDS/Types.xsd";

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

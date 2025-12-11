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
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

#pragma warning disable 1591

namespace Opc.Ua.Gds
{
    #region DataType Identifiers
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class DataTypes
    {
        public const uint ApplicationRecordDataType = 1;
    }
    #endregion

    #region Method Identifiers
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class Methods
    {
        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Open = 735;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Close = 738;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Read = 740;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Write = 743;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition = 745;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition = 748;

        public const uint WellKnownRole_DiscoveryAdmin_AddIdentity = 1668;

        public const uint WellKnownRole_DiscoveryAdmin_RemoveIdentity = 1670;

        public const uint WellKnownRole_DiscoveryAdmin_AddApplication = 1672;

        public const uint WellKnownRole_DiscoveryAdmin_RemoveApplication = 1674;

        public const uint WellKnownRole_DiscoveryAdmin_AddEndpoint = 1676;

        public const uint WellKnownRole_DiscoveryAdmin_RemoveEndpoint = 1678;

        public const uint WellKnownRole_CertificateAuthorityAdmin_AddIdentity = 1687;

        public const uint WellKnownRole_CertificateAuthorityAdmin_RemoveIdentity = 1689;

        public const uint WellKnownRole_CertificateAuthorityAdmin_AddApplication = 1691;

        public const uint WellKnownRole_CertificateAuthorityAdmin_RemoveApplication = 1693;

        public const uint WellKnownRole_CertificateAuthorityAdmin_AddEndpoint = 1695;

        public const uint WellKnownRole_CertificateAuthorityAdmin_RemoveEndpoint = 1697;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_AddIdentity = 1706;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_RemoveIdentity = 1708;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_AddApplication = 1710;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_RemoveApplication = 1712;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_AddEndpoint = 1714;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_RemoveEndpoint = 1716;

        public const uint WellKnownRole_KeyCredentialAdmin_AddIdentity = 1725;

        public const uint WellKnownRole_KeyCredentialAdmin_RemoveIdentity = 1727;

        public const uint WellKnownRole_KeyCredentialAdmin_AddApplication = 1729;

        public const uint WellKnownRole_KeyCredentialAdmin_RemoveApplication = 1731;

        public const uint WellKnownRole_KeyCredentialAdmin_AddEndpoint = 1733;

        public const uint WellKnownRole_KeyCredentialAdmin_RemoveEndpoint = 1735;

        public const uint WellKnownRole_AuthorizationServiceAdmin_AddIdentity = 1744;

        public const uint WellKnownRole_AuthorizationServiceAdmin_RemoveIdentity = 1746;

        public const uint WellKnownRole_AuthorizationServiceAdmin_AddApplication = 1748;

        public const uint WellKnownRole_AuthorizationServiceAdmin_RemoveApplication = 1750;

        public const uint WellKnownRole_AuthorizationServiceAdmin_AddEndpoint = 1752;

        public const uint WellKnownRole_AuthorizationServiceAdmin_RemoveEndpoint = 1754;

        public const uint DirectoryType_FindApplications = 15;

        public const uint DirectoryType_RegisterApplication = 18;

        public const uint DirectoryType_UpdateApplication = 188;

        public const uint DirectoryType_UnregisterApplication = 21;

        public const uint DirectoryType_GetApplication = 210;

        public const uint DirectoryType_QueryApplications = 868;

        public const uint DirectoryType_QueryServers = 23;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open = 519;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close = 522;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read = 524;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write = 527;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition = 529;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition = 532;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks = 535;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate = 538;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate = 541;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate = 543;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable = 15041;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable = 15042;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment = 15043;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge = 15063;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve = 15110;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve = 15112;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve = 15113;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable = 15189;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable = 15190;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment = 15191;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge = 15211;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 15258;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve = 15260;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 15261;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open = 553;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close = 556;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read = 558;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write = 561;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition = 563;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition = 566;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks = 569;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate = 572;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate = 575;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate = 577;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable = 15337;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable = 15338;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment = 15339;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge = 15359;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve = 15406;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve = 15408;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve = 15409;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable = 15485;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable = 15486;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment = 15487;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge = 15507;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 15554;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve = 15556;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 15557;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open = 587;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close = 590;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read = 592;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write = 595;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition = 597;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition = 600;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks = 603;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate = 606;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate = 609;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate = 611;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable = 15633;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable = 15634;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment = 15635;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge = 15655;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve = 15702;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve = 15704;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve = 15705;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable = 15781;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable = 15782;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment = 15783;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge = 15803;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 15850;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve = 15852;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 15853;

        public const uint CertificateDirectoryType_StartSigningRequest = 79;

        public const uint CertificateDirectoryType_StartNewKeyPairRequest = 76;

        public const uint CertificateDirectoryType_FinishRequest = 85;

        public const uint CertificateDirectoryType_RevokeCertificate = 15003;

        public const uint CertificateDirectoryType_GetCertificateGroups = 369;

        public const uint CertificateDirectoryType_GetCertificates = 89;

        public const uint CertificateDirectoryType_GetTrustList = 197;

        public const uint CertificateDirectoryType_GetCertificateStatus = 222;

        public const uint CertificateDirectoryType_CheckRevocationStatus = 126;

        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest = 168;

        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest = 196;

        public const uint KeyCredentialServiceType_StartRequest = 1023;

        public const uint KeyCredentialServiceType_FinishRequest = 1026;

        public const uint KeyCredentialServiceType_Revoke = 1029;

        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription = 238;

        public const uint AuthorizationServiceType_GetServiceDescription = 1004;

        public const uint AuthorizationServiceType_RequestAccessToken = 969;

        public const uint Directory_FindApplications = 143;

        public const uint Directory_RegisterApplication = 146;

        public const uint Directory_UpdateApplication = 200;

        public const uint Directory_UnregisterApplication = 149;

        public const uint Directory_GetApplication = 216;

        public const uint Directory_QueryApplications = 992;

        public const uint Directory_QueryServers = 151;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open = 622;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close = 625;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read = 627;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write = 630;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition = 632;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition = 635;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks = 638;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate = 641;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate = 644;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate = 646;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable = 15946;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable = 15947;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment = 15948;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge = 15968;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve = 16015;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve = 16017;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve = 16018;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable = 16094;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable = 16095;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment = 16096;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge = 16116;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 16163;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve = 16165;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 16166;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open = 656;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close = 659;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read = 661;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write = 664;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition = 666;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition = 669;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks = 672;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate = 675;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate = 678;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate = 680;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable = 16242;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable = 16243;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment = 16244;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge = 16264;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve = 16311;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve = 16313;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve = 16314;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable = 16390;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable = 16391;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment = 16392;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge = 16412;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 16459;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve = 16461;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 16462;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open = 690;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close = 693;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read = 695;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write = 698;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition = 700;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition = 703;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks = 706;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate = 709;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate = 712;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate = 714;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable = 16538;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable = 16539;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment = 16540;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge = 16560;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve = 16607;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve = 16609;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve = 16610;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable = 16686;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable = 16687;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment = 16688;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge = 16708;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve = 16755;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve = 16757;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = 16758;

        public const uint Directory_StartSigningRequest = 157;

        public const uint Directory_StartNewKeyPairRequest = 154;

        public const uint Directory_FinishRequest = 163;

        public const uint Directory_GetCertificateGroups = 508;

        public const uint Directory_GetTrustList = 204;

        public const uint Directory_GetCertificateStatus = 225;
    }
    #endregion

    #region Object Identifiers
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class Objects
    {
        public const uint OPCUAGDSNamespaceMetadata = 721;

        public const uint WellKnownRole_DiscoveryAdmin = 1661;

        public const uint WellKnownRole_CertificateAuthorityAdmin = 1680;

        public const uint WellKnownRole_RegistrationAuthorityAdmin = 1699;

        public const uint WellKnownRole_KeyCredentialAdmin = 1718;

        public const uint WellKnownRole_AuthorizationServiceAdmin = 1737;

        public const uint DirectoryType_Applications = 14;

        public const uint CertificateDirectoryType_CertificateGroups = 511;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup = 512;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList = 513;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList = 547;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList = 581;

        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder = 61;

        public const uint KeyCredentialManagement = 1008;

        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder = 234;

        public const uint AuthorizationServices = 959;

        public const uint Directory = 141;

        public const uint Directory_Applications = 142;

        public const uint Directory_CertificateGroups = 614;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup = 615;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList = 616;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup = 649;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList = 650;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup = 683;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList = 684;

        public const uint ApplicationRecordDataType_Encoding_DefaultBinary = 134;

        public const uint ApplicationRecordDataType_Encoding_DefaultXml = 127;

        public const uint ApplicationRecordDataType_Encoding_DefaultJson = 8001;
    }
    #endregion

    #region ObjectType Identifiers
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class ObjectTypes
    {
        public const uint DirectoryType = 13;

        public const uint ApplicationRegistrationChangedAuditEventType = 26;

        public const uint CertificateDirectoryType = 63;

        public const uint CertificateRequestedAuditEventType = 91;

        public const uint CertificateDeliveredAuditEventType = 109;

        public const uint CertificateRevokedAuditEventType = 27;

        public const uint KeyCredentialManagementFolderType = 55;

        public const uint KeyCredentialServiceType = 1020;

        public const uint KeyCredentialRequestedAuditEventType = 1039;

        public const uint KeyCredentialDeliveredAuditEventType = 1057;

        public const uint KeyCredentialRevokedAuditEventType = 1075;

        public const uint AuthorizationServicesFolderType = 233;

        public const uint AuthorizationServiceType = 966;

        public const uint AccessTokenIssuedAuditEventType = 975;
    }
    #endregion

    #region Variable Identifiers
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class Variables
    {
        public const uint OPCUAGDSNamespaceMetadata_NamespaceUri = 722;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceVersion = 723;

        public const uint OPCUAGDSNamespaceMetadata_NamespacePublicationDate = 724;

        public const uint OPCUAGDSNamespaceMetadata_IsNamespaceSubset = 725;

        public const uint OPCUAGDSNamespaceMetadata_StaticNodeIdTypes = 726;

        public const uint OPCUAGDSNamespaceMetadata_StaticNumericNodeIdRange = 727;

        public const uint OPCUAGDSNamespaceMetadata_StaticStringNodeIdPattern = 728;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Size = 730;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Writable = 731;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_UserWritable = 732;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_OpenCount = 733;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Open_InputArguments = 736;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Open_OutputArguments = 737;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Close_InputArguments = 739;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Read_InputArguments = 741;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Read_OutputArguments = 742;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_Write_InputArguments = 744;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_InputArguments = 746;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_OutputArguments = 747;

        public const uint OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition_InputArguments = 749;

        public const uint OPCUAGDSNamespaceMetadata_DefaultRolePermissions = 862;

        public const uint OPCUAGDSNamespaceMetadata_DefaultUserRolePermissions = 863;

        public const uint OPCUAGDSNamespaceMetadata_DefaultAccessRestrictions = 864;

        public const uint WellKnownRole_DiscoveryAdmin_Identities = 1662;

        public const uint WellKnownRole_DiscoveryAdmin_ApplicationsExclude = 1663;

        public const uint WellKnownRole_DiscoveryAdmin_Applications = 1664;

        public const uint WellKnownRole_DiscoveryAdmin_EndpointsExclude = 1665;

        public const uint WellKnownRole_DiscoveryAdmin_Endpoints = 1666;

        public const uint WellKnownRole_DiscoveryAdmin_AddIdentity_InputArguments = 1669;

        public const uint WellKnownRole_DiscoveryAdmin_RemoveIdentity_InputArguments = 1671;

        public const uint WellKnownRole_DiscoveryAdmin_AddApplication_InputArguments = 1673;

        public const uint WellKnownRole_DiscoveryAdmin_RemoveApplication_InputArguments = 1675;

        public const uint WellKnownRole_DiscoveryAdmin_AddEndpoint_InputArguments = 1677;

        public const uint WellKnownRole_DiscoveryAdmin_RemoveEndpoint_InputArguments = 1679;

        public const uint WellKnownRole_CertificateAuthorityAdmin_Identities = 1681;

        public const uint WellKnownRole_CertificateAuthorityAdmin_ApplicationsExclude = 1682;

        public const uint WellKnownRole_CertificateAuthorityAdmin_Applications = 1683;

        public const uint WellKnownRole_CertificateAuthorityAdmin_EndpointsExclude = 1684;

        public const uint WellKnownRole_CertificateAuthorityAdmin_Endpoints = 1685;

        public const uint WellKnownRole_CertificateAuthorityAdmin_AddIdentity_InputArguments = 1688;

        public const uint WellKnownRole_CertificateAuthorityAdmin_RemoveIdentity_InputArguments = 1690;

        public const uint WellKnownRole_CertificateAuthorityAdmin_AddApplication_InputArguments = 1692;

        public const uint WellKnownRole_CertificateAuthorityAdmin_RemoveApplication_InputArguments = 1694;

        public const uint WellKnownRole_CertificateAuthorityAdmin_AddEndpoint_InputArguments = 1696;

        public const uint WellKnownRole_CertificateAuthorityAdmin_RemoveEndpoint_InputArguments = 1698;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_Identities = 1700;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_ApplicationsExclude = 1701;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_Applications = 1702;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_EndpointsExclude = 1703;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_Endpoints = 1704;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_AddIdentity_InputArguments = 1707;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_RemoveIdentity_InputArguments = 1709;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_AddApplication_InputArguments = 1711;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_RemoveApplication_InputArguments = 1713;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_AddEndpoint_InputArguments = 1715;

        public const uint WellKnownRole_RegistrationAuthorityAdmin_RemoveEndpoint_InputArguments = 1717;

        public const uint WellKnownRole_KeyCredentialAdmin_Identities = 1719;

        public const uint WellKnownRole_KeyCredentialAdmin_ApplicationsExclude = 1720;

        public const uint WellKnownRole_KeyCredentialAdmin_Applications = 1721;

        public const uint WellKnownRole_KeyCredentialAdmin_EndpointsExclude = 1722;

        public const uint WellKnownRole_KeyCredentialAdmin_Endpoints = 1723;

        public const uint WellKnownRole_KeyCredentialAdmin_AddIdentity_InputArguments = 1726;

        public const uint WellKnownRole_KeyCredentialAdmin_RemoveIdentity_InputArguments = 1728;

        public const uint WellKnownRole_KeyCredentialAdmin_AddApplication_InputArguments = 1730;

        public const uint WellKnownRole_KeyCredentialAdmin_RemoveApplication_InputArguments = 1732;

        public const uint WellKnownRole_KeyCredentialAdmin_AddEndpoint_InputArguments = 1734;

        public const uint WellKnownRole_KeyCredentialAdmin_RemoveEndpoint_InputArguments = 1736;

        public const uint WellKnownRole_AuthorizationServiceAdmin_Identities = 1738;

        public const uint WellKnownRole_AuthorizationServiceAdmin_ApplicationsExclude = 1739;

        public const uint WellKnownRole_AuthorizationServiceAdmin_Applications = 1740;

        public const uint WellKnownRole_AuthorizationServiceAdmin_EndpointsExclude = 1741;

        public const uint WellKnownRole_AuthorizationServiceAdmin_Endpoints = 1742;

        public const uint WellKnownRole_AuthorizationServiceAdmin_AddIdentity_InputArguments = 1745;

        public const uint WellKnownRole_AuthorizationServiceAdmin_RemoveIdentity_InputArguments = 1747;

        public const uint WellKnownRole_AuthorizationServiceAdmin_AddApplication_InputArguments = 1749;

        public const uint WellKnownRole_AuthorizationServiceAdmin_RemoveApplication_InputArguments = 1751;

        public const uint WellKnownRole_AuthorizationServiceAdmin_AddEndpoint_InputArguments = 1753;

        public const uint WellKnownRole_AuthorizationServiceAdmin_RemoveEndpoint_InputArguments = 1755;

        public const uint DirectoryType_FindApplications_InputArguments = 16;

        public const uint DirectoryType_FindApplications_OutputArguments = 17;

        public const uint DirectoryType_RegisterApplication_InputArguments = 19;

        public const uint DirectoryType_RegisterApplication_OutputArguments = 20;

        public const uint DirectoryType_UpdateApplication_InputArguments = 189;

        public const uint DirectoryType_UnregisterApplication_InputArguments = 22;

        public const uint DirectoryType_GetApplication_InputArguments = 211;

        public const uint DirectoryType_GetApplication_OutputArguments = 212;

        public const uint DirectoryType_QueryApplications_InputArguments = 869;

        public const uint DirectoryType_QueryApplications_OutputArguments = 870;

        public const uint DirectoryType_QueryServers_InputArguments = 24;

        public const uint DirectoryType_QueryServers_OutputArguments = 25;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Size = 514;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Writable = 515;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable = 516;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount = 517;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments = 520;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments = 521;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments = 523;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments = 525;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments = 526;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments = 528;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments = 530;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments = 531;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments = 533;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime = 534;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments = 536;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments = 537;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments = 539;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments = 540;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments = 542;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments = 544;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateTypes = 545;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId = 15009;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType = 15010;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode = 15011;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName = 15012;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time = 15013;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime = 15014;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message = 15016;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity = 15017;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId = 15018;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName = 15019;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName = 15022;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId = 15023;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain = 15024;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState = 15025;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id = 15026;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality = 15034;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp = 15035;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity = 15036;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp = 15037;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment = 15038;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp = 15039;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId = 15040;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments = 15044;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState = 15045;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id = 15046;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id = 15055;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments = 15064;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments = 15066;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState = 15067;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id = 15068;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode = 15076;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id = 15078;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id = 15087;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState = 15096;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id = 15097;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id = 15102;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime = 15109;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 15111;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = 256;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = 258;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = 260;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved = 15114;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id = 15122;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id = 15135;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Suppress2_InputArguments = 262;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Unsuppress2_InputArguments = 264;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_RemoveFromService2_InputArguments = 266;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_PlaceInService2_InputArguments = 268;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Reset2_InputArguments = 270;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_GetGroupMemberships_OutputArguments = 272;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState = 15151;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate = 15152;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType = 15154;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate = 15155;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId = 15157;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType = 15158;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode = 15159;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName = 15160;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time = 15161;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime = 15162;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message = 15164;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity = 15165;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId = 15166;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName = 15167;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName = 15170;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId = 15171;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain = 15172;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState = 15173;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id = 15174;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality = 15182;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp = 15183;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity = 15184;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 15185;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment = 15186;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp = 15187;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId = 15188;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments = 15192;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState = 15193;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id = 15194;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id = 15203;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments = 15212;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments = 15214;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState = 15215;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id = 15216;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode = 15224;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id = 15226;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id = 15235;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState = 15244;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 15245;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 15250;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 15257;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 15259;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = 274;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = 276;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = 278;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved = 15262;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id = 15270;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id = 15283;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Suppress2_InputArguments = 280;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Unsuppress2_InputArguments = 282;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = 284;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_PlaceInService2_InputArguments = 286;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Reset2_InputArguments = 288;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = 290;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState = 15299;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId = 15300;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime = 15301;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency = 15302;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments = 60;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Size = 548;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Writable = 549;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable = 550;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount = 551;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments = 554;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments = 555;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments = 557;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments = 559;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments = 560;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments = 562;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments = 564;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments = 565;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments = 567;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime = 568;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments = 570;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments = 571;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments = 573;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments = 574;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments = 576;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments = 578;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateTypes = 579;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId = 15305;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType = 15306;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode = 15307;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName = 15308;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time = 15309;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime = 15310;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message = 15312;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity = 15313;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId = 15314;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName = 15315;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName = 15318;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId = 15319;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain = 15320;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState = 15321;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id = 15322;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality = 15330;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp = 15331;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity = 15332;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp = 15333;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment = 15334;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp = 15335;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId = 15336;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments = 15340;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState = 15341;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id = 15342;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id = 15351;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments = 15360;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments = 15362;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState = 15363;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id = 15364;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode = 15372;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id = 15374;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id = 15383;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState = 15392;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id = 15393;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id = 15398;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime = 15405;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 15407;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = 294;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = 296;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = 298;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved = 15410;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id = 15418;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id = 15431;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Suppress2_InputArguments = 300;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Unsuppress2_InputArguments = 302;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_RemoveFromService2_InputArguments = 304;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_PlaceInService2_InputArguments = 306;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Reset2_InputArguments = 308;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_GetGroupMemberships_OutputArguments = 310;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState = 15447;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate = 15448;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType = 15450;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate = 15451;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId = 15453;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType = 15454;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode = 15455;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName = 15456;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time = 15457;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime = 15458;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message = 15460;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity = 15461;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId = 15462;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName = 15463;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName = 15466;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId = 15467;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain = 15468;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState = 15469;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id = 15470;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality = 15478;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp = 15479;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity = 15480;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 15481;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment = 15482;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp = 15483;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId = 15484;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments = 15488;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState = 15489;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id = 15490;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id = 15499;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments = 15508;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments = 15510;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState = 15511;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id = 15512;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode = 15520;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id = 15522;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id = 15531;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState = 15540;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 15541;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 15546;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 15553;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 15555;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = 312;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = 314;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = 316;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved = 15558;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id = 15566;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id = 15579;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Suppress2_InputArguments = 318;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Unsuppress2_InputArguments = 320;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = 322;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_PlaceInService2_InputArguments = 324;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Reset2_InputArguments = 326;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = 328;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState = 15595;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId = 15596;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime = 15597;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency = 15598;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments = 82;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Size = 582;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable = 583;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable = 584;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount = 585;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments = 588;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments = 589;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments = 591;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments = 593;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments = 594;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments = 596;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments = 598;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments = 599;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments = 601;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime = 602;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments = 604;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments = 605;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments = 607;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments = 608;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments = 610;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments = 612;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateTypes = 613;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId = 15601;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType = 15602;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode = 15603;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName = 15604;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time = 15605;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime = 15606;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message = 15608;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity = 15609;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId = 15610;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName = 15611;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName = 15614;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId = 15615;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain = 15616;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState = 15617;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id = 15618;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality = 15626;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp = 15627;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity = 15628;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp = 15629;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment = 15630;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp = 15631;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId = 15632;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments = 15636;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState = 15637;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id = 15638;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id = 15647;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments = 15656;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments = 15658;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState = 15659;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id = 15660;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode = 15668;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id = 15670;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id = 15679;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState = 15688;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id = 15689;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id = 15694;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime = 15701;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 15703;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = 332;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = 334;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = 336;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved = 15706;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id = 15714;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id = 15727;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Suppress2_InputArguments = 338;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Unsuppress2_InputArguments = 340;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_RemoveFromService2_InputArguments = 342;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_PlaceInService2_InputArguments = 344;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Reset2_InputArguments = 346;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_GetGroupMemberships_OutputArguments = 348;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState = 15743;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate = 15744;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType = 15746;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate = 15747;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId = 15749;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType = 15750;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode = 15751;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName = 15752;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time = 15753;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime = 15754;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message = 15756;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity = 15757;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId = 15758;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName = 15759;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName = 15762;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId = 15763;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain = 15764;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState = 15765;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id = 15766;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality = 15774;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp = 15775;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity = 15776;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 15777;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment = 15778;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp = 15779;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId = 15780;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments = 15784;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState = 15785;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id = 15786;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id = 15795;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments = 15804;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments = 15806;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState = 15807;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id = 15808;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode = 15816;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id = 15818;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id = 15827;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState = 15836;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 15837;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 15842;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 15849;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 15851;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = 350;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = 352;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = 354;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved = 15854;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id = 15862;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id = 15875;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Suppress2_InputArguments = 356;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Unsuppress2_InputArguments = 358;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = 360;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_PlaceInService2_InputArguments = 362;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Reset2_InputArguments = 364;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = 366;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState = 15891;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId = 15892;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime = 15893;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency = 15894;

        public const uint CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments = 88;

        public const uint CertificateDirectoryType_StartSigningRequest_InputArguments = 80;

        public const uint CertificateDirectoryType_StartSigningRequest_OutputArguments = 81;

        public const uint CertificateDirectoryType_StartNewKeyPairRequest_InputArguments = 77;

        public const uint CertificateDirectoryType_StartNewKeyPairRequest_OutputArguments = 78;

        public const uint CertificateDirectoryType_FinishRequest_InputArguments = 86;

        public const uint CertificateDirectoryType_FinishRequest_OutputArguments = 87;

        public const uint CertificateDirectoryType_RevokeCertificate_InputArguments = 15004;

        public const uint CertificateDirectoryType_GetCertificateGroups_InputArguments = 370;

        public const uint CertificateDirectoryType_GetCertificateGroups_OutputArguments = 371;

        public const uint CertificateDirectoryType_GetCertificates_InputArguments = 90;

        public const uint CertificateDirectoryType_GetCertificates_OutputArguments = 108;

        public const uint CertificateDirectoryType_GetTrustList_InputArguments = 198;

        public const uint CertificateDirectoryType_GetTrustList_OutputArguments = 199;

        public const uint CertificateDirectoryType_GetCertificateStatus_InputArguments = 223;

        public const uint CertificateDirectoryType_GetCertificateStatus_OutputArguments = 224;

        public const uint CertificateDirectoryType_CheckRevocationStatus_InputArguments = 160;

        public const uint CertificateDirectoryType_CheckRevocationStatus_OutputArguments = 161;

        public const uint CertificateRequestedAuditEventType_CertificateGroup = 717;

        public const uint CertificateRequestedAuditEventType_CertificateType = 718;

        public const uint CertificateDeliveredAuditEventType_CertificateGroup = 719;

        public const uint CertificateDeliveredAuditEventType_CertificateType = 720;

        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_ResourceUri = 83;

        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_ProfileUris = 162;

        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_InputArguments = 171;

        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_OutputArguments = 195;

        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_InputArguments = 202;

        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_OutputArguments = 203;

        public const uint KeyCredentialManagementFolderType_ServiceName_Placeholder_Revoke_InputArguments = 229;

        public const uint KeyCredentialServiceType_ResourceUri = 1021;

        public const uint KeyCredentialServiceType_ProfileUris = 1022;

        public const uint KeyCredentialServiceType_SecurityPolicyUris = 495;

        public const uint KeyCredentialServiceType_StartRequest_InputArguments = 1024;

        public const uint KeyCredentialServiceType_StartRequest_OutputArguments = 1025;

        public const uint KeyCredentialServiceType_FinishRequest_InputArguments = 1027;

        public const uint KeyCredentialServiceType_FinishRequest_OutputArguments = 1028;

        public const uint KeyCredentialServiceType_Revoke_InputArguments = 1030;

        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceUri = 235;

        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceCertificate = 236;

        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription_OutputArguments = 239;

        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_InputArguments = 241;

        public const uint AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_OutputArguments = 242;

        public const uint AuthorizationServiceType_ServiceUri = 1003;

        public const uint AuthorizationServiceType_ServiceCertificate = 968;

        public const uint AuthorizationServiceType_UserTokenPolicies = 967;

        public const uint AuthorizationServiceType_GetServiceDescription_OutputArguments = 1005;

        public const uint AuthorizationServiceType_RequestAccessToken_InputArguments = 970;

        public const uint AuthorizationServiceType_RequestAccessToken_OutputArguments = 971;

        public const uint Directory_FindApplications_InputArguments = 144;

        public const uint Directory_FindApplications_OutputArguments = 145;

        public const uint Directory_RegisterApplication_InputArguments = 147;

        public const uint Directory_RegisterApplication_OutputArguments = 148;

        public const uint Directory_UpdateApplication_InputArguments = 201;

        public const uint Directory_UnregisterApplication_InputArguments = 150;

        public const uint Directory_GetApplication_InputArguments = 217;

        public const uint Directory_GetApplication_OutputArguments = 218;

        public const uint Directory_QueryApplications_InputArguments = 993;

        public const uint Directory_QueryApplications_OutputArguments = 994;

        public const uint Directory_QueryServers_InputArguments = 152;

        public const uint Directory_QueryServers_OutputArguments = 153;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Size = 617;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Writable = 618;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable = 619;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount = 620;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments = 623;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments = 624;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments = 626;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments = 628;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments = 629;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments = 631;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments = 633;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments = 634;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments = 636;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime = 637;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments = 639;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments = 640;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments = 642;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments = 643;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments = 645;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments = 647;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateTypes = 648;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId = 15914;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType = 15915;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode = 15916;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName = 15917;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time = 15918;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime = 15919;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message = 15921;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity = 15922;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId = 15923;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName = 15924;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName = 15927;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId = 15928;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain = 15929;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState = 15930;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id = 15931;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality = 15939;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp = 15940;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity = 15941;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp = 15942;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment = 15943;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp = 15944;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId = 15945;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments = 15949;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState = 15950;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id = 15951;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id = 15960;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments = 15969;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments = 15971;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState = 15972;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id = 15973;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode = 15981;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id = 15983;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id = 15992;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState = 16001;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id = 16002;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id = 16007;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime = 16014;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 16016;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = 373;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = 375;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = 377;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved = 16019;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id = 16027;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id = 16040;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Suppress2_InputArguments = 379;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Unsuppress2_InputArguments = 381;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_RemoveFromService2_InputArguments = 383;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_PlaceInService2_InputArguments = 385;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Reset2_InputArguments = 387;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_GetGroupMemberships_OutputArguments = 389;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState = 16056;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate = 16057;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType = 16059;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate = 16060;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId = 16062;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType = 16063;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode = 16064;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName = 16065;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time = 16066;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime = 16067;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message = 16069;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity = 16070;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId = 16071;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName = 16072;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName = 16075;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId = 16076;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain = 16077;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState = 16078;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id = 16079;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality = 16087;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp = 16088;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity = 16089;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 16090;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment = 16091;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp = 16092;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId = 16093;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments = 16097;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState = 16098;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id = 16099;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id = 16108;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments = 16117;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments = 16119;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState = 16120;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id = 16121;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode = 16129;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id = 16131;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id = 16140;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState = 16149;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 16150;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 16155;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 16162;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 16164;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = 391;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = 393;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = 395;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved = 16167;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id = 16175;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id = 16188;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Suppress2_InputArguments = 397;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Unsuppress2_InputArguments = 399;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = 401;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_PlaceInService2_InputArguments = 403;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Reset2_InputArguments = 405;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = 407;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState = 16204;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId = 16205;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime = 16206;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency = 16207;

        public const uint Directory_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments = 167;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Size = 651;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Writable = 652;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable = 653;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount = 654;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments = 657;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments = 658;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments = 660;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments = 662;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments = 663;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments = 665;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments = 667;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments = 668;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments = 670;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime = 671;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments = 673;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments = 674;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments = 676;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments = 677;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments = 679;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments = 681;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateTypes = 682;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId = 16210;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType = 16211;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode = 16212;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName = 16213;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time = 16214;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime = 16215;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message = 16217;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity = 16218;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId = 16219;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName = 16220;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName = 16223;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId = 16224;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain = 16225;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState = 16226;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id = 16227;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality = 16235;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp = 16236;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity = 16237;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp = 16238;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment = 16239;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp = 16240;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId = 16241;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments = 16245;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState = 16246;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id = 16247;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id = 16256;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments = 16265;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments = 16267;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState = 16268;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id = 16269;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode = 16277;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id = 16279;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id = 16288;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState = 16297;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id = 16298;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id = 16303;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime = 16310;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 16312;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = 411;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = 413;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = 415;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved = 16315;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id = 16323;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id = 16336;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Suppress2_InputArguments = 417;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Unsuppress2_InputArguments = 419;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_RemoveFromService2_InputArguments = 421;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_PlaceInService2_InputArguments = 423;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Reset2_InputArguments = 425;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_GetGroupMemberships_OutputArguments = 427;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState = 16352;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate = 16353;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType = 16355;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate = 16356;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId = 16358;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType = 16359;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode = 16360;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName = 16361;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time = 16362;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime = 16363;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message = 16365;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity = 16366;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId = 16367;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName = 16368;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName = 16371;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId = 16372;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain = 16373;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState = 16374;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id = 16375;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality = 16383;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp = 16384;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity = 16385;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 16386;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment = 16387;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp = 16388;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId = 16389;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments = 16393;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState = 16394;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id = 16395;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id = 16404;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments = 16413;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments = 16415;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState = 16416;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id = 16417;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode = 16425;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id = 16427;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id = 16436;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState = 16445;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 16446;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 16451;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 16458;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 16460;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = 429;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = 431;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = 433;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved = 16463;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id = 16471;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id = 16484;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Suppress2_InputArguments = 435;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Unsuppress2_InputArguments = 437;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = 439;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_PlaceInService2_InputArguments = 441;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Reset2_InputArguments = 443;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = 445;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState = 16500;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId = 16501;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime = 16502;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency = 16503;

        public const uint Directory_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments = 170;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Size = 685;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable = 686;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable = 687;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount = 688;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments = 691;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments = 692;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments = 694;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments = 696;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments = 697;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments = 699;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments = 701;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments = 702;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments = 704;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime = 705;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments = 707;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments = 708;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments = 710;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments = 711;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments = 713;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments = 715;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateTypes = 716;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId = 16506;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType = 16507;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode = 16508;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName = 16509;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time = 16510;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime = 16511;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message = 16513;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity = 16514;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId = 16515;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName = 16516;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName = 16519;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId = 16520;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain = 16521;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState = 16522;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id = 16523;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality = 16531;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp = 16532;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity = 16533;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp = 16534;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment = 16535;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp = 16536;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId = 16537;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments = 16541;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState = 16542;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id = 16543;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id = 16552;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments = 16561;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments = 16563;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState = 16564;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id = 16565;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode = 16573;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id = 16575;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id = 16584;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState = 16593;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id = 16594;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id = 16599;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime = 16606;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = 16608;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = 449;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = 451;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = 453;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved = 16611;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id = 16619;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id = 16632;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Suppress2_InputArguments = 455;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Unsuppress2_InputArguments = 457;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_RemoveFromService2_InputArguments = 459;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_PlaceInService2_InputArguments = 461;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Reset2_InputArguments = 463;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_GetGroupMemberships_OutputArguments = 465;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState = 16648;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate = 16649;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType = 16651;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate = 16652;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId = 16654;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType = 16655;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode = 16656;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName = 16657;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time = 16658;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime = 16659;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message = 16661;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity = 16662;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId = 16663;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName = 16664;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName = 16667;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId = 16668;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain = 16669;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState = 16670;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id = 16671;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality = 16679;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp = 16680;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity = 16681;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = 16682;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment = 16683;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp = 16684;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId = 16685;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments = 16689;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState = 16690;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id = 16691;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id = 16700;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments = 16709;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments = 16711;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState = 16712;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id = 16713;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode = 16721;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id = 16723;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id = 16732;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState = 16741;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = 16742;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = 16747;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = 16754;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = 16756;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = 467;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = 469;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = 471;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved = 16759;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id = 16767;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id = 16780;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Suppress2_InputArguments = 473;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Unsuppress2_InputArguments = 475;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = 477;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_PlaceInService2_InputArguments = 479;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Reset2_InputArguments = 481;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = 483;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState = 16796;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId = 16797;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime = 16798;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency = 16799;

        public const uint Directory_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments = 173;

        public const uint Directory_StartSigningRequest_InputArguments = 158;

        public const uint Directory_StartSigningRequest_OutputArguments = 159;

        public const uint Directory_StartNewKeyPairRequest_InputArguments = 155;

        public const uint Directory_StartNewKeyPairRequest_OutputArguments = 156;

        public const uint Directory_FinishRequest_InputArguments = 164;

        public const uint Directory_FinishRequest_OutputArguments = 165;

        public const uint Directory_RevokeCertificate_InputArguments = 15006;

        public const uint Directory_GetCertificateGroups_InputArguments = 509;

        public const uint Directory_GetCertificateGroups_OutputArguments = 510;

        public const uint Directory_GetCertificates_InputArguments = 175;

        public const uint Directory_GetCertificates_OutputArguments = 176;

        public const uint Directory_GetTrustList_InputArguments = 205;

        public const uint Directory_GetTrustList_OutputArguments = 206;

        public const uint Directory_GetCertificateStatus_InputArguments = 226;

        public const uint Directory_GetCertificateStatus_OutputArguments = 227;

        public const uint Directory_CheckRevocationStatus_InputArguments = 178;

        public const uint Directory_CheckRevocationStatus_OutputArguments = 179;

        public const uint OpcUaGds_BinarySchema = 135;

        public const uint OpcUaGds_BinarySchema_NamespaceUri = 137;

        public const uint OpcUaGds_BinarySchema_Deprecated = 8002;

        public const uint OpcUaGds_BinarySchema_ApplicationRecordDataType = 138;

        public const uint OpcUaGds_XmlSchema = 128;

        public const uint OpcUaGds_XmlSchema_NamespaceUri = 130;

        public const uint OpcUaGds_XmlSchema_Deprecated = 8004;

        public const uint OpcUaGds_XmlSchema_ApplicationRecordDataType = 131;
    }
    #endregion

    #region DataType Node Identifiers
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class DataTypeIds
    {
        public static readonly ExpandedNodeId ApplicationRecordDataType = new ExpandedNodeId(Opc.Ua.Gds.DataTypes.ApplicationRecordDataType, Opc.Ua.Gds.Namespaces.OpcUaGds);
    }
    #endregion

    #region Method Node Identifiers
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class MethodIds
    {
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_AddIdentity = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_DiscoveryAdmin_AddIdentity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_RemoveIdentity = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_DiscoveryAdmin_RemoveIdentity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_AddApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_DiscoveryAdmin_AddApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_RemoveApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_DiscoveryAdmin_RemoveApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_AddEndpoint = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_DiscoveryAdmin_AddEndpoint, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_RemoveEndpoint = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_DiscoveryAdmin_RemoveEndpoint, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_AddIdentity = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_CertificateAuthorityAdmin_AddIdentity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_RemoveIdentity = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_CertificateAuthorityAdmin_RemoveIdentity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_AddApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_CertificateAuthorityAdmin_AddApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_RemoveApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_CertificateAuthorityAdmin_RemoveApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_AddEndpoint = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_CertificateAuthorityAdmin_AddEndpoint, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_RemoveEndpoint = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_CertificateAuthorityAdmin_RemoveEndpoint, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_AddIdentity = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_RegistrationAuthorityAdmin_AddIdentity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_RemoveIdentity = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_RegistrationAuthorityAdmin_RemoveIdentity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_AddApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_RegistrationAuthorityAdmin_AddApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_RemoveApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_RegistrationAuthorityAdmin_RemoveApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_AddEndpoint = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_RegistrationAuthorityAdmin_AddEndpoint, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_RemoveEndpoint = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_RegistrationAuthorityAdmin_RemoveEndpoint, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_AddIdentity = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_KeyCredentialAdmin_AddIdentity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_RemoveIdentity = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_KeyCredentialAdmin_RemoveIdentity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_AddApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_KeyCredentialAdmin_AddApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_RemoveApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_KeyCredentialAdmin_RemoveApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_AddEndpoint = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_KeyCredentialAdmin_AddEndpoint, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_RemoveEndpoint = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_KeyCredentialAdmin_RemoveEndpoint, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_AddIdentity = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_AuthorizationServiceAdmin_AddIdentity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_RemoveIdentity = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_AuthorizationServiceAdmin_RemoveIdentity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_AddApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_AuthorizationServiceAdmin_AddApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_RemoveApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_AuthorizationServiceAdmin_RemoveApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_AddEndpoint = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_AuthorizationServiceAdmin_AddEndpoint, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_RemoveEndpoint = new ExpandedNodeId(Opc.Ua.Gds.Methods.WellKnownRole_AuthorizationServiceAdmin_RemoveEndpoint, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_FindApplications = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_FindApplications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_RegisterApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_RegisterApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_UpdateApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_UpdateApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_UnregisterApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_UnregisterApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_GetApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_GetApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_QueryApplications = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_QueryApplications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_QueryServers = new ExpandedNodeId(Opc.Ua.Gds.Methods.DirectoryType_QueryServers, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_StartSigningRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_StartSigningRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_StartNewKeyPairRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_StartNewKeyPairRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_FinishRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_FinishRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_RevokeCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_RevokeCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateGroups = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_GetCertificateGroups, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificates = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_GetCertificates, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetTrustList = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_GetTrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateStatus = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_GetCertificateStatus, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CheckRevocationStatus = new ExpandedNodeId(Opc.Ua.Gds.Methods.CertificateDirectoryType_CheckRevocationStatus, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_StartRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialServiceType_StartRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_FinishRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialServiceType_FinishRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_Revoke = new ExpandedNodeId(Opc.Ua.Gds.Methods.KeyCredentialServiceType_Revoke, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription = new ExpandedNodeId(Opc.Ua.Gds.Methods.AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServiceType_GetServiceDescription = new ExpandedNodeId(Opc.Ua.Gds.Methods.AuthorizationServiceType_GetServiceDescription, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServiceType_RequestAccessToken = new ExpandedNodeId(Opc.Ua.Gds.Methods.AuthorizationServiceType_RequestAccessToken, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_FindApplications = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_FindApplications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_RegisterApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_RegisterApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_UpdateApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_UpdateApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_UnregisterApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_UnregisterApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetApplication = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_GetApplication, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_QueryApplications = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_QueryApplications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_QueryServers = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_QueryServers, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Disable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Enable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_StartSigningRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_StartSigningRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_StartNewKeyPairRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_StartNewKeyPairRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_FinishRequest = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_FinishRequest, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetCertificateGroups = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_GetCertificateGroups, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetTrustList = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_GetTrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetCertificateStatus = new ExpandedNodeId(Opc.Ua.Gds.Methods.Directory_GetCertificateStatus, Opc.Ua.Gds.Namespaces.OpcUaGds);
    }
    #endregion

    #region Object Node Identifiers
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class ObjectIds
    {
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata = new ExpandedNodeId(Opc.Ua.Gds.Objects.OPCUAGDSNamespaceMetadata, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin = new ExpandedNodeId(Opc.Ua.Gds.Objects.WellKnownRole_DiscoveryAdmin, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin = new ExpandedNodeId(Opc.Ua.Gds.Objects.WellKnownRole_CertificateAuthorityAdmin, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin = new ExpandedNodeId(Opc.Ua.Gds.Objects.WellKnownRole_RegistrationAuthorityAdmin, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin = new ExpandedNodeId(Opc.Ua.Gds.Objects.WellKnownRole_KeyCredentialAdmin, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin = new ExpandedNodeId(Opc.Ua.Gds.Objects.WellKnownRole_AuthorizationServiceAdmin, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_Applications = new ExpandedNodeId(Opc.Ua.Gds.Objects.DirectoryType_Applications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups = new ExpandedNodeId(Opc.Ua.Gds.Objects.CertificateDirectoryType_CertificateGroups, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup = new ExpandedNodeId(Opc.Ua.Gds.Objects.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder = new ExpandedNodeId(Opc.Ua.Gds.Objects.KeyCredentialManagementFolderType_ServiceName_Placeholder, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagement = new ExpandedNodeId(Opc.Ua.Gds.Objects.KeyCredentialManagement, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder = new ExpandedNodeId(Opc.Ua.Gds.Objects.AuthorizationServicesFolderType_ServiceName_Placeholder, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServices = new ExpandedNodeId(Opc.Ua.Gds.Objects.AuthorizationServices, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_Applications = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_Applications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultApplicationGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultApplicationGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultHttpsGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultHttpsGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultUserTokenGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList = new ExpandedNodeId(Opc.Ua.Gds.Objects.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId ApplicationRecordDataType_Encoding_DefaultBinary = new ExpandedNodeId(Opc.Ua.Gds.Objects.ApplicationRecordDataType_Encoding_DefaultBinary, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId ApplicationRecordDataType_Encoding_DefaultXml = new ExpandedNodeId(Opc.Ua.Gds.Objects.ApplicationRecordDataType_Encoding_DefaultXml, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId ApplicationRecordDataType_Encoding_DefaultJson = new ExpandedNodeId(Opc.Ua.Gds.Objects.ApplicationRecordDataType_Encoding_DefaultJson, Opc.Ua.Gds.Namespaces.OpcUaGds);
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class ObjectTypeIds
    {
        public static readonly ExpandedNodeId DirectoryType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.DirectoryType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId ApplicationRegistrationChangedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.ApplicationRegistrationChangedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.CertificateDirectoryType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateRequestedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.CertificateRequestedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDeliveredAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.CertificateDeliveredAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateRevokedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.CertificateRevokedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.KeyCredentialManagementFolderType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.KeyCredentialServiceType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialRequestedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.KeyCredentialRequestedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialDeliveredAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.KeyCredentialDeliveredAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialRevokedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.KeyCredentialRevokedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServicesFolderType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.AuthorizationServicesFolderType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServiceType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.AuthorizationServiceType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AccessTokenIssuedAuditEventType = new ExpandedNodeId(Opc.Ua.Gds.ObjectTypes.AccessTokenIssuedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds);
    }
    #endregion

    #region Variable Node Identifiers
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class VariableIds
    {
        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceVersion = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceVersion, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespacePublicationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespacePublicationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_IsNamespaceSubset = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_IsNamespaceSubset, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_StaticNodeIdTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_StaticNodeIdTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_StaticNumericNodeIdRange = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_StaticNumericNodeIdRange, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_StaticStringNodeIdPattern = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_StaticStringNodeIdPattern, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_NamespaceFile_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_DefaultRolePermissions = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_DefaultRolePermissions, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_DefaultUserRolePermissions = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_DefaultUserRolePermissions, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OPCUAGDSNamespaceMetadata_DefaultAccessRestrictions = new ExpandedNodeId(Opc.Ua.Gds.Variables.OPCUAGDSNamespaceMetadata_DefaultAccessRestrictions, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_Identities = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_Identities, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_ApplicationsExclude = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_ApplicationsExclude, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_Applications = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_Applications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_EndpointsExclude = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_EndpointsExclude, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_Endpoints = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_Endpoints, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_AddIdentity_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_AddIdentity_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_RemoveIdentity_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_RemoveIdentity_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_AddApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_AddApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_RemoveApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_RemoveApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_AddEndpoint_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_AddEndpoint_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_DiscoveryAdmin_RemoveEndpoint_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_DiscoveryAdmin_RemoveEndpoint_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_Identities = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_Identities, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_ApplicationsExclude = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_ApplicationsExclude, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_Applications = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_Applications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_EndpointsExclude = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_EndpointsExclude, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_Endpoints = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_Endpoints, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_AddIdentity_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_AddIdentity_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_RemoveIdentity_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_RemoveIdentity_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_AddApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_AddApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_RemoveApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_RemoveApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_AddEndpoint_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_AddEndpoint_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_CertificateAuthorityAdmin_RemoveEndpoint_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_CertificateAuthorityAdmin_RemoveEndpoint_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_Identities = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_Identities, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_ApplicationsExclude = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_ApplicationsExclude, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_Applications = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_Applications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_EndpointsExclude = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_EndpointsExclude, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_Endpoints = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_Endpoints, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_AddIdentity_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_AddIdentity_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_RemoveIdentity_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_RemoveIdentity_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_AddApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_AddApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_RemoveApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_RemoveApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_AddEndpoint_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_AddEndpoint_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_RegistrationAuthorityAdmin_RemoveEndpoint_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_RegistrationAuthorityAdmin_RemoveEndpoint_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_Identities = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_Identities, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_ApplicationsExclude = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_ApplicationsExclude, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_Applications = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_Applications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_EndpointsExclude = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_EndpointsExclude, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_Endpoints = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_Endpoints, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_AddIdentity_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_AddIdentity_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_RemoveIdentity_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_RemoveIdentity_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_AddApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_AddApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_RemoveApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_RemoveApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_AddEndpoint_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_AddEndpoint_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_KeyCredentialAdmin_RemoveEndpoint_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_KeyCredentialAdmin_RemoveEndpoint_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_Identities = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_Identities, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_ApplicationsExclude = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_ApplicationsExclude, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_Applications = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_Applications, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_EndpointsExclude = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_EndpointsExclude, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_Endpoints = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_Endpoints, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_AddIdentity_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_AddIdentity_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_RemoveIdentity_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_RemoveIdentity_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_AddApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_AddApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_RemoveApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_RemoveApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_AddEndpoint_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_AddEndpoint_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId WellKnownRole_AuthorizationServiceAdmin_RemoveEndpoint_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.WellKnownRole_AuthorizationServiceAdmin_RemoveEndpoint_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_FindApplications_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_FindApplications_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_FindApplications_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_FindApplications_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_RegisterApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_RegisterApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_RegisterApplication_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_RegisterApplication_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_UpdateApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_UpdateApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_UnregisterApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_UnregisterApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_GetApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_GetApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_GetApplication_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_GetApplication_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_QueryApplications_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_QueryApplications_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_QueryApplications_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_QueryApplications_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_QueryServers_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_QueryServers_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId DirectoryType_QueryServers_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.DirectoryType_QueryServers_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_StartSigningRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_StartSigningRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_StartSigningRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_StartSigningRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_StartNewKeyPairRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_StartNewKeyPairRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_StartNewKeyPairRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_StartNewKeyPairRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_FinishRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_FinishRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_FinishRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_FinishRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_RevokeCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_RevokeCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateGroups_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetCertificateGroups_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateGroups_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetCertificateGroups_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificates_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetCertificates_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificates_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetCertificates_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetTrustList_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetTrustList_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetTrustList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetTrustList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateStatus_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetCertificateStatus_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_GetCertificateStatus_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_GetCertificateStatus_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CheckRevocationStatus_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CheckRevocationStatus_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDirectoryType_CheckRevocationStatus_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDirectoryType_CheckRevocationStatus_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateRequestedAuditEventType_CertificateGroup = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateRequestedAuditEventType_CertificateGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateRequestedAuditEventType_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateRequestedAuditEventType_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDeliveredAuditEventType_CertificateGroup = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDeliveredAuditEventType_CertificateGroup, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId CertificateDeliveredAuditEventType_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.CertificateDeliveredAuditEventType_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_ResourceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_ResourceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_ProfileUris = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_ProfileUris, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_StartRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_FinishRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialManagementFolderType_ServiceName_Placeholder_Revoke_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialManagementFolderType_ServiceName_Placeholder_Revoke_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_ResourceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_ResourceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_ProfileUris = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_ProfileUris, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_SecurityPolicyUris = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_SecurityPolicyUris, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_StartRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_StartRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_StartRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_StartRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_FinishRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_FinishRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_FinishRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_FinishRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId KeyCredentialServiceType_Revoke_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.KeyCredentialServiceType_Revoke_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceCertificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServicesFolderType_ServiceName_Placeholder_ServiceCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServicesFolderType_ServiceName_Placeholder_GetServiceDescription_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServicesFolderType_ServiceName_Placeholder_RequestAccessToken_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServiceType_ServiceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_ServiceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServiceType_ServiceCertificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_ServiceCertificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServiceType_UserTokenPolicies = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_UserTokenPolicies, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServiceType_GetServiceDescription_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_GetServiceDescription_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServiceType_RequestAccessToken_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_RequestAccessToken_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId AuthorizationServiceType_RequestAccessToken_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.AuthorizationServiceType_RequestAccessToken_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_FindApplications_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_FindApplications_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_FindApplications_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_FindApplications_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_RegisterApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_RegisterApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_RegisterApplication_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_RegisterApplication_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_UpdateApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_UpdateApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_UnregisterApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_UnregisterApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetApplication_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetApplication_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetApplication_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetApplication_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_QueryApplications_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_QueryApplications_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_QueryApplications_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_QueryApplications_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_QueryServers_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_QueryServers_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_QueryServers_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_QueryServers_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultApplicationGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultHttpsGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Size = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Size, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Writable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_UserWritable, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenCount, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Open_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Close_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Read_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_Write_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_GetPosition_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_SetPosition_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_OpenWithMasks_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_CloseAndUpdate_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_AddCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList_RemoveCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateTypes = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateTypes, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_ExpirationDate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_CertificateType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_CertificateExpired_Certificate, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EventType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SourceName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Time, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ReceiveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Message, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Severity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionClassName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConditionName, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_BranchId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Retain, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_EnabledState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Quality_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastSeverity_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Comment_SourceTimestamp, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ClientUserId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AddComment_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_AckedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ConfirmedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Acknowledge_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Confirm_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ActiveState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_InputNode, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_OutOfServiceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_CurrentState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_LastTransition_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_UnshelveTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_TimedShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_Unshelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_ShelvingState_OneShotShelve2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SuppressedOrShelved, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_SilenceState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LatchedState_Id, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Suppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Suppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Unsuppress2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Unsuppress2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_RemoveFromService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_RemoveFromService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_PlaceInService2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_PlaceInService2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Reset2_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_Reset2_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_GetGroupMemberships_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_NormalState, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_TrustListId, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_LastUpdateTime, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_TrustListOutOfDate_UpdateFrequency, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CertificateGroups_DefaultUserTokenGroup_GetRejectedList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_StartSigningRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_StartSigningRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_StartSigningRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_StartSigningRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_StartNewKeyPairRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_StartNewKeyPairRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_StartNewKeyPairRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_StartNewKeyPairRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_FinishRequest_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_FinishRequest_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_FinishRequest_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_FinishRequest_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_RevokeCertificate_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_RevokeCertificate_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetCertificateGroups_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetCertificateGroups_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetCertificateGroups_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetCertificateGroups_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetCertificates_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetCertificates_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetCertificates_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetCertificates_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetTrustList_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetTrustList_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetTrustList_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetTrustList_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetCertificateStatus_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetCertificateStatus_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_GetCertificateStatus_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_GetCertificateStatus_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CheckRevocationStatus_InputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CheckRevocationStatus_InputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId Directory_CheckRevocationStatus_OutputArguments = new ExpandedNodeId(Opc.Ua.Gds.Variables.Directory_CheckRevocationStatus_OutputArguments, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OpcUaGds_BinarySchema = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_BinarySchema, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OpcUaGds_BinarySchema_NamespaceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_BinarySchema_NamespaceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OpcUaGds_BinarySchema_Deprecated = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_BinarySchema_Deprecated, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OpcUaGds_BinarySchema_ApplicationRecordDataType = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_BinarySchema_ApplicationRecordDataType, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OpcUaGds_XmlSchema = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_XmlSchema, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OpcUaGds_XmlSchema_NamespaceUri = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_XmlSchema_NamespaceUri, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OpcUaGds_XmlSchema_Deprecated = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_XmlSchema_Deprecated, Opc.Ua.Gds.Namespaces.OpcUaGds);

        public static readonly ExpandedNodeId OpcUaGds_XmlSchema_ApplicationRecordDataType = new ExpandedNodeId(Opc.Ua.Gds.Variables.OpcUaGds_XmlSchema_ApplicationRecordDataType, Opc.Ua.Gds.Namespaces.OpcUaGds);
    }
    #endregion

    #region BrowseName Declarations
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public static partial class BrowseNames
    {
        public const string AccessTokenIssuedAuditEventType = "AccessTokenIssuedAuditEventType";

        public const string ApplicationRecordDataType = "ApplicationRecordDataType";

        public const string ApplicationRegistrationChangedAuditEventType = "ApplicationRegistrationChangedAuditEventType";

        public const string Applications = "Applications";

        public const string AuthorizationServices = "AuthorizationServices";

        public const string AuthorizationServicesFolderType = "AuthorizationServicesFolderType";

        public const string AuthorizationServiceType = "AuthorizationServiceType";

        public const string CertificateDeliveredAuditEventType = "CertificateDeliveredAuditEventType";

        public const string CertificateDirectoryType = "CertificateDirectoryType";

        public const string CertificateGroup = "CertificateGroup";

        public const string CertificateGroups = "CertificateGroups";

        public const string CertificateRequestedAuditEventType = "CertificateRequestedAuditEventType";

        public const string CertificateRevokedAuditEventType = "CertificateRevokedAuditEventType";

        public const string CertificateType = "CertificateType";

        public const string CheckRevocationStatus = "CheckRevocationStatus";

        public const string Directory = "Directory";

        public const string DirectoryType = "DirectoryType";

        public const string FindApplications = "FindApplications";

        public const string FinishRequest = "FinishRequest";

        public const string GetApplication = "GetApplication";

        public const string GetCertificateGroups = "GetCertificateGroups";

        public const string GetCertificates = "GetCertificates";

        public const string GetCertificateStatus = "GetCertificateStatus";

        public const string GetServiceDescription = "GetServiceDescription";

        public const string GetTrustList = "GetTrustList";

        public const string KeyCredentialDeliveredAuditEventType = "KeyCredentialDeliveredAuditEventType";

        public const string KeyCredentialManagement = "KeyCredentialManagement";

        public const string KeyCredentialManagementFolderType = "KeyCredentialManagementFolderType";

        public const string KeyCredentialRequestedAuditEventType = "KeyCredentialRequestedAuditEventType";

        public const string KeyCredentialRevokedAuditEventType = "KeyCredentialRevokedAuditEventType";

        public const string KeyCredentialServiceType = "KeyCredentialServiceType";

        public const string ModelVersion = "ModelVersion";

        public const string OpcUaGds_BinarySchema = "Opc.Ua.Gds";

        public const string OpcUaGds_XmlSchema = "Opc.Ua.Gds";

        public const string OPCUAGDSNamespaceMetadata = "http://opcfoundation.org/UA/GDS/";

        public const string ProfileUris = "ProfileUris";

        public const string QueryApplications = "QueryApplications";

        public const string QueryServers = "QueryServers";

        public const string RegisterApplication = "RegisterApplication";

        public const string RequestAccessToken = "RequestAccessToken";

        public const string ResourceUri = "ResourceUri";

        public const string Revoke = "Revoke";

        public const string RevokeCertificate = "RevokeCertificate";

        public const string SecurityPolicyUris = "SecurityPolicyUris";

        public const string ServiceCertificate = "ServiceCertificate";

        public const string ServiceName_Placeholder = "<ServiceName>";

        public const string ServiceUri = "ServiceUri";

        public const string StartNewKeyPairRequest = "StartNewKeyPairRequest";

        public const string StartRequest = "StartRequest";

        public const string StartSigningRequest = "StartSigningRequest";

        public const string UnregisterApplication = "UnregisterApplication";

        public const string UpdateApplication = "UpdateApplication";

        public const string UserTokenPolicies = "UserTokenPolicies";

        public const string WellKnownRole_AuthorizationServiceAdmin = "AuthorizationServiceAdmin";

        public const string WellKnownRole_CertificateAuthorityAdmin = "CertificateAuthorityAdmin";

        public const string WellKnownRole_DiscoveryAdmin = "DiscoveryAdmin";

        public const string WellKnownRole_KeyCredentialAdmin = "KeyCredentialAdmin";

        public const string WellKnownRole_RegistrationAuthorityAdmin = "RegistrationAuthorityAdmin";
    }
    #endregion

    #region Namespace Declarations
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
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

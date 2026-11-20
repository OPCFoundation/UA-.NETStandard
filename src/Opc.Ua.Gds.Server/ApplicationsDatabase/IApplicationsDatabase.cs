/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Gds.Server.Database
{
    /// <summary>
    /// An abstract interface to the application database
    /// </summary>
    public interface IApplicationsDatabase
    {
        void Initialize();
        ushort NamespaceIndex { get; set; }
        NodeId RegisterApplication(ApplicationRecordDataType application);
        NodeId UpdateApplication(ApplicationRecordDataType application);
        void UnregisterApplication(NodeId applicationId);
        ApplicationRecordDataType? GetApplication(NodeId applicationId);
        ApplicationRecordDataType[]? FindApplications(string applicationUri);

        ServerOnNetwork[]? QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            ArrayOf<string> serverCapabilities,
            out DateTimeUtc lastCounterResetTime);

        bool SetApplicationCertificate(
            NodeId applicationId,
            string certificateTypeId,
            ByteString certificate);

        bool GetApplicationCertificate(
            NodeId applicationId,
            string certificateTypeId,
            out ByteString certificate);

        bool SetApplicationTrustLists(
            NodeId applicationId,
            string certificateTypeId,
            string trustListId);

        bool GetApplicationTrustLists(
            NodeId applicationId,
            string certificateTypeId,
            out string? trustListId);

        ApplicationDescription[]? QueryApplications(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            ArrayOf<string> serverCapabilities,
            out DateTimeUtc lastCounterResetTime,
            out uint nextRecordId);
    }
}

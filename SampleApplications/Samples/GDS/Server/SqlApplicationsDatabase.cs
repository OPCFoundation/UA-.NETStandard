/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace Opc.Ua.Gds.Server.Database.Sql
{
    public class SqlApplicationsDatabase : ApplicationsDatabaseBase, ICertificateRequest
    {
        #region IApplicationsDatabase Members
        public override void Initialize()
        {
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                Assembly assembly = typeof(SqlApplicationsDatabase).GetTypeInfo().Assembly;
                StreamReader istrm = new StreamReader(assembly.GetManifestResourceStream("Opc.Ua.Gds.Server.DB.gdsdb.edmx.sql"));
                string tables = istrm.ReadToEnd();
                entities.Database.Initialize(true);
                entities.Database.CreateIfNotExists();
                var parts = tables.Split(new string[] { "GO" }, System.StringSplitOptions.None);
                foreach (var part in parts) { entities.Database.ExecuteSqlCommand(part); }
                entities.SaveChanges();
            }
        }

        public override NodeId RegisterApplication(
            ApplicationRecordDataType application
            )
        {
            NodeId appNodeId = base.RegisterApplication(application);
            if (NodeId.IsNull(appNodeId))
            {
                appNodeId = new NodeId(Guid.NewGuid(), NamespaceIndex);
            }
            Guid applicationId = GetNodeIdGuid(appNodeId);
            string capabilities = base.ServerCapabilities(application);

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                Application record = null;

                if (applicationId != Guid.Empty)
                {
                    var results = from ii in entities.Applications
                                  where ii.ApplicationId == applicationId
                                  select ii;

                    record = results.SingleOrDefault();

                    if (record != null)
                    {
                        var endpoints = from ii in entities.ServerEndpoints
                                        where ii.ApplicationId == record.ID
                                        select ii;

                        foreach (var endpoint in endpoints)
                        {
                            entities.ServerEndpoints.Remove(endpoint);
                        }

                        var names = from ii in entities.ApplicationNames
                                    where ii.ApplicationId == record.ID
                                    select ii;

                        foreach (var name in names)
                        {
                            entities.ApplicationNames.Remove(name);
                        }

                        entities.SaveChanges();
                    }
                }

                bool isNew = false;

                if (record == null)
                {
                    applicationId = Guid.NewGuid();
                    record = new Application() { ApplicationId = applicationId };
                    isNew = true;
                }

                record.ApplicationUri = application.ApplicationUri;
                record.ApplicationName = application.ApplicationNames[0].Text;
                record.ApplicationType = (int)application.ApplicationType;
                record.ProductUri = application.ProductUri;
                record.ServerCapabilities = capabilities;

                if (isNew)
                {
                    entities.Applications.Add(record);
                }

                entities.SaveChanges();

                if (application.DiscoveryUrls != null)
                {
                    foreach (var discoveryUrl in application.DiscoveryUrls)
                    {
                        entities.ServerEndpoints.Add(new ServerEndpoint() { ApplicationId = record.ID, DiscoveryUrl = discoveryUrl });
                    }
                }

                if (application.ApplicationNames != null && application.ApplicationNames.Count >= 1)
                {
                    foreach (var applicationName in application.ApplicationNames)
                    {
                        entities.ApplicationNames.Add(new ApplicationName() { ApplicationId = record.ID, Locale = applicationName.Locale, Text = applicationName.Text });
                    }
                }

                entities.SaveChanges();
                m_lastCounterResetTime = DateTime.UtcNow;
                return new NodeId(applicationId, NamespaceIndex); ;
            }
        }

        public override void UnregisterApplication(NodeId applicationId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            List<byte[]> certificates = new List<byte[]>();

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var result = (from ii in entities.Applications
                              where ii.ApplicationId == id
                              select ii).SingleOrDefault();

                if (result == null)
                {
                    throw new ArgumentException("A record with the specified application id does not exist.", nameof(applicationId));
                }

                foreach (var entry in new List<CertificateRequest>(result.CertificateRequests))
                {
                    entities.CertificateRequests.Remove(entry);
                }

                foreach (var entry in new List<ApplicationName>(result.ApplicationNames))
                {
                    entities.ApplicationNames.Remove(entry);
                }

                foreach (var entry in new List<ServerEndpoint>(result.ServerEndpoints))
                {
                    entities.ServerEndpoints.Remove(entry);
                }

                entities.Applications.Remove(result);
                entities.SaveChanges();
                m_lastCounterResetTime = DateTime.UtcNow;
            }
        }

        public override ApplicationRecordDataType GetApplication(
            NodeId applicationId
            )
        {
            Guid id = GetNodeIdGuid(applicationId);
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.Applications
                              where x.ApplicationId == id
                              select x;

                var result = results.SingleOrDefault();

                if (result == null)
                {
                    return null;
                }

                LocalizedTextCollection names = new LocalizedTextCollection();
                if (result.ApplicationNames != null)
                {
                    foreach (var entry in new List<ApplicationName>(result.ApplicationNames))
                    {
                        names.Add(new LocalizedText(entry.Locale, entry.Text));
                    }
                }
                else
                {
                    names.Add(new LocalizedText(result.ApplicationName));
                }

                StringCollection discoveryUrls = null;

                if (result.ServerEndpoints != null)
                {
                    discoveryUrls = new StringCollection();

                    foreach (var endpoint in result.ServerEndpoints)
                    {
                        discoveryUrls.Add(endpoint.DiscoveryUrl);
                    }
                }

                string[] capabilities = null;

                if (result.ServerCapabilities != null &&
                    result.ServerCapabilities.Length > 0)
                {
                    capabilities = result.ServerCapabilities.Split(',');
                }

                return new ApplicationRecordDataType()
                {
                    ApplicationId = new NodeId(result.ApplicationId, NamespaceIndex),
                    ApplicationUri = result.ApplicationUri,
                    ApplicationType = (ApplicationType)result.ApplicationType,
                    ApplicationNames = names,
                    ProductUri = result.ProductUri,
                    DiscoveryUrls = discoveryUrls,
                    ServerCapabilities = capabilities
                };
            }
        }

        public override ApplicationRecordDataType[] FindApplications(
            string applicationUri
            )
        {
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.Applications
                              where x.ApplicationUri == applicationUri
                              select x;

                List<ApplicationRecordDataType> records = new List<ApplicationRecordDataType>();

                foreach (var result in results)
                {
                    LocalizedText[] names = null;

                    if (result.ApplicationName != null)
                    {
                        names = new LocalizedText[] { result.ApplicationName };
                    }

                    StringCollection discoveryUrls = null;

                    if (result.ServerEndpoints != null)
                    {
                        discoveryUrls = new StringCollection();

                        foreach (var endpoint in result.ServerEndpoints)
                        {
                            discoveryUrls.Add(endpoint.DiscoveryUrl);
                        }
                    }

                    string[] capabilities = null;

                    if (result.ServerCapabilities != null)
                    {
                        capabilities = result.ServerCapabilities.Split(',');
                    }

                    records.Add(new ApplicationRecordDataType()
                    {
                        ApplicationId = new NodeId(result.ApplicationId, NamespaceIndex),
                        ApplicationUri = result.ApplicationUri,
                        ApplicationType = (ApplicationType)result.ApplicationType,
                        ApplicationNames = new LocalizedTextCollection(names),
                        ProductUri = result.ProductUri,
                        DiscoveryUrls = discoveryUrls,
                        ServerCapabilities = capabilities
                    });
                }

                return records.ToArray();
            }
        }

        public override ApplicationDescription[] QueryApplications(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            string[] serverCapabilities,
            out DateTime lastCounterResetTime,
            out uint nextRecordId)
        {
            lastCounterResetTime = DateTime.MinValue;
            nextRecordId = 0;
            var records = new List<ApplicationDescription>();

            lastCounterResetTime = m_lastCounterResetTime;

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.Applications
                              where ((int)startingRecordId == 0 || (int)startingRecordId <= x.ID)
                              orderby x.ID
                              select x;

                int lastID = 0;

                foreach (var result in results)
                {

                    if (!String.IsNullOrEmpty(applicationName))
                    {
                        if (!Match(result.ApplicationName, applicationName))
                        {
                            continue;
                        }
                    }

                    if (!String.IsNullOrEmpty(applicationUri))
                    {
                        if (!Match(result.ApplicationUri, applicationUri))
                        {
                            continue;
                        }
                    }

                    if (!String.IsNullOrEmpty(productUri))
                    {
                        if (!Match(result.ProductUri, productUri))
                        {
                            continue;
                        }
                    }

                    string[] capabilities = null;
                    if (!String.IsNullOrEmpty(result.ServerCapabilities))
                    {
                        capabilities = result.ServerCapabilities.Split(',');
                    }

                    if (serverCapabilities != null && serverCapabilities.Length > 0)
                    {
                        bool match = true;

                        for (int ii = 0; ii < serverCapabilities.Length; ii++)
                        {
                            if (capabilities == null || !capabilities.Contains(serverCapabilities[ii]))
                            {
                                match = false;
                                break;
                            }
                        }

                        if (!match)
                        {
                            continue;
                        }
                    }

                    // type filter, 0 and 3 returns all
                    // filter for servers
                    if (applicationType == 1 &&
                        result.ApplicationType == (int)ApplicationType.Client)
                    {
                        continue;
                    }
                    else // filter for clients
                    if (applicationType == 2 &&
                        result.ApplicationType != (int)ApplicationType.Client &&
                        result.ApplicationType != (int)ApplicationType.ClientAndServer)
                    {
                        continue;
                    }

                    var discoveryUrls = new StringCollection();
                    if (result.ServerEndpoints != null)
                    {
                        discoveryUrls = new StringCollection();

                        foreach (var endpoint in result.ServerEndpoints)
                        {
                            discoveryUrls.Add(endpoint.DiscoveryUrl);
                        }
                    }

                    if (lastID == 0)
                    {
                        lastID = result.ID;
                    }
                    else
                    {
                        if (maxRecordsToReturn != 0 &&
                            records.Count >= maxRecordsToReturn)
                        {
                            break;
                        }

                        lastID = result.ID;
                    }

                    records.Add(new ApplicationDescription()
                    {
                        ApplicationUri = result.ApplicationUri,
                        ProductUri = result.ProductUri,
                        ApplicationName = result.ApplicationName,
                        ApplicationType = (ApplicationType)result.ApplicationType,
                        GatewayServerUri = null,
                        DiscoveryProfileUri = null,
                        DiscoveryUrls = discoveryUrls
                    });
                    nextRecordId = (uint)lastID + 1;

                }
                return records.ToArray();
            }
        }

        public override ServerOnNetwork[] QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            string[] serverCapabilities,
            out DateTime lastCounterResetTime)
        {
            lastCounterResetTime = m_lastCounterResetTime;

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.ServerEndpoints
                              join y in entities.Applications on x.ApplicationId equals y.ID
                              where ((int)startingRecordId == 0 || (int)startingRecordId <= x.ID)
                              orderby x.ID
                              select new
                              {
                                  x.ID,
                                  y.ApplicationName,
                                  y.ApplicationUri,
                                  y.ProductUri,
                                  x.DiscoveryUrl,
                                  y.ServerCapabilities
                              };

                List<ServerOnNetwork> records = new List<ServerOnNetwork>();
                int lastID = 0;

                foreach (var result in results)
                {

                    if (!String.IsNullOrEmpty(applicationName))
                    {
                        if (!Match(result.ApplicationName, applicationName))
                        {
                            continue;
                        }
                    }

                    if (!String.IsNullOrEmpty(applicationUri))
                    {
                        if (!Match(result.ApplicationUri, applicationUri))
                        {
                            continue;
                        }
                    }

                    if (!String.IsNullOrEmpty(productUri))
                    {
                        if (!Match(result.ProductUri, productUri))
                        {
                            continue;
                        }
                    }

                    string[] capabilities = null;
                    if (!String.IsNullOrEmpty(result.ServerCapabilities))
                    {
                        capabilities = result.ServerCapabilities.Split(',');
                    }

                    if (serverCapabilities != null && serverCapabilities.Length > 0)
                    {
                        bool match = true;

                        for (int ii = 0; ii < serverCapabilities.Length; ii++)
                        {
                            if (capabilities == null || !capabilities.Contains(serverCapabilities[ii]))
                            {
                                match = false;
                                break;
                            }
                        }

                        if (!match)
                        {
                            continue;
                        }
                    }

                    if (lastID == 0)
                    {
                        lastID = result.ID;
                    }
                    else
                    {
                        if (maxRecordsToReturn != 0 &&
                            lastID != result.ID &&
                            records.Count >= maxRecordsToReturn)
                        {
                            break;
                        }

                        lastID = result.ID;
                    }

                    records.Add(new ServerOnNetwork()
                    {
                        RecordId = (uint)result.ID,
                        ServerName = result.ApplicationName,
                        DiscoveryUrl = result.DiscoveryUrl,
                        ServerCapabilities = capabilities
                    });


                }

                return records.ToArray();
            }
        }

        public override bool SetApplicationCertificate(
            NodeId applicationId,
            string certificateTypeId,
            byte[] certificate)
        {
            Guid id = GetNodeIdGuid(applicationId);

            if (certificateTypeId.Equals(nameof(Ua.ObjectTypeIds.UserCredentialCertificateType), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.Applications
                              where x.ApplicationId == id
                              select x;

                var result = results.SingleOrDefault();

                if (result == null)
                {
                    return false;
                }

                if (certificateTypeId.Equals(nameof(Ua.ObjectTypeIds.HttpsCertificateType), StringComparison.OrdinalIgnoreCase))
                {
                    result.HttpsCertificate = certificate;
                }
                else
                {
                    result.Certificate = certificate;
                }

                entities.SaveChanges();
            }

            return true;
        }

        public override bool GetApplicationCertificate(
            NodeId applicationId,
            string certificateTypeId,
            out byte[] certificate)
        {
            certificate = null;

            Guid id = GetNodeIdGuid(applicationId);

            List<byte[]> certificates = new List<byte[]>();

            if (certificateTypeId.Equals(nameof(Ua.ObjectTypeIds.UserCredentialCertificateType), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var result = (from ii in entities.Applications
                              where ii.ApplicationId == id
                              select ii).SingleOrDefault();

                if (result == null)
                {
                    throw new ArgumentException("A record with the specified application id does not exist.", nameof(applicationId));
                }

                if (certificateTypeId.Equals(nameof(Ua.ObjectTypeIds.HttpsCertificateType), StringComparison.OrdinalIgnoreCase))
                {
                    certificate = result.HttpsCertificate;
                }
                else
                {
                    certificate = result.Certificate;
                }
            }

            return certificate != null;
        }


        public override bool SetApplicationTrustLists(
            NodeId applicationId,
            string trustListId,
            string httpsTrustListId
            )
        {
            Guid id = GetNodeIdGuid(applicationId);
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var result = (from x in entities.Applications where x.ApplicationId == id select x).SingleOrDefault();

                if (result == null)
                {
                    return false;
                }

                result.TrustListId = null;
                result.HttpsTrustListId = null;

                if (trustListId != null)
                {
                    var result2 = (from x in entities.CertificateStores where x.Path == trustListId select x).SingleOrDefault();

                    if (result2 != null)
                    {
                        result.TrustListId = result2.ID;
                    }
                }

                if (httpsTrustListId != null)
                {
                    var result2 = (from x in entities.CertificateStores where x.Path == httpsTrustListId select x).SingleOrDefault();

                    if (result2 != null)
                    {
                        result.HttpsTrustListId = result2.ID;
                    }
                }

                entities.SaveChanges();
            }

            return true;
        }

        #endregion
        #region ICertificateRequest
        public NodeId StartSigningRequest(
            NodeId applicationId,
            string certificateGroupId,
            string certificateTypeId,
            byte[] certificateRequest,
            string authorityId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var application = (from x in entities.Applications where x.ApplicationId == id select x).SingleOrDefault();

                if (application == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                var request = (from x in application.CertificateRequests where x.AuthorityId == authorityId select x).SingleOrDefault();

                bool isNew = false;

                if (request == null)
                {
                    request = new CertificateRequest() { RequestId = Guid.NewGuid(), AuthorityId = authorityId };
                    isNew = true;
                }

                request.State = (int)CertificateRequestState.New;
                request.CertificateGroupId = certificateGroupId;
                request.CertificateTypeId = certificateTypeId;
                request.SubjectName = null;
                request.DomainNames = null;
                request.PrivateKeyFormat = null;
                request.PrivateKeyPassword = null;
                request.CertificateSigningRequest = certificateRequest;

                if (isNew)
                {
                    application.CertificateRequests.Add(request);
                }

                entities.SaveChanges();

                return new NodeId(request.RequestId, NamespaceIndex);
            }
        }

        public NodeId StartNewKeyPairRequest(
            NodeId applicationId,
            string certificateGroupId,
            string certificateTypeId,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            string privateKeyPassword,
            string authorityId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var application = (from x in entities.Applications where x.ApplicationId == id select x).SingleOrDefault();

                if (application == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                var request = (from x in application.CertificateRequests where x.AuthorityId == authorityId select x).SingleOrDefault();

                bool isNew = false;

                if (request == null)
                {
                    request = new CertificateRequest() { RequestId = Guid.NewGuid(), AuthorityId = authorityId };
                    isNew = true;
                }

                request.State = (int)CertificateRequestState.New;
                request.CertificateGroupId = certificateGroupId;
                request.CertificateTypeId = certificateTypeId;
                request.SubjectName = subjectName;
                request.DomainNames = JsonConvert.SerializeObject(domainNames);
                request.PrivateKeyFormat = privateKeyFormat;
                request.PrivateKeyPassword = privateKeyPassword;
                request.CertificateSigningRequest = null;

                if (isNew)
                {
                    application.CertificateRequests.Add(request);
                }

                entities.SaveChanges();

                return new NodeId(request.RequestId, NamespaceIndex);
            }
        }

        public void ApproveRequest(
            NodeId requestId,
            bool isRejected
            )
        {
            Guid id = GetNodeIdGuid(requestId);
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var request = (from x in entities.CertificateRequests where x.RequestId == id select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                if (isRejected)
                {
                    request.State = (int)CertificateRequestState.Rejected;
                    // erase information which is ot required anymore
                    request.CertificateSigningRequest = null;
                    request.PrivateKeyPassword = null;
                }
                else
                {
                    request.State = (int)CertificateRequestState.Approved;
                }

                entities.SaveChanges();
            }
        }

        public void AcceptRequest(
            NodeId requestId,
            byte[] certificate)
        {
            Guid id = GetNodeIdGuid(requestId);
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var request = (from x in entities.CertificateRequests where x.RequestId == id select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                request.State = (int)CertificateRequestState.Accepted;

                // erase information which is ot required anymore
                request.CertificateSigningRequest = null;
                request.PrivateKeyPassword = null;

                entities.SaveChanges();
            }
        }


        public CertificateRequestState FinishRequest(
            NodeId applicationId,
            NodeId requestId,
            out string certificateGroupId,
            out string certificateTypeId,
            out byte[] signedCertificate,
            out byte[] privateKey)
        {
            certificateGroupId = null;
            certificateTypeId = null;
            signedCertificate = null;
            privateKey = null;
            Guid reqId = GetNodeIdGuid(requestId);
            Guid appId = GetNodeIdGuid(applicationId);

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var request = (from x in entities.CertificateRequests where x.RequestId == reqId select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                switch (request.State)
                {
                    case (int)CertificateRequestState.New:
                        return CertificateRequestState.New;
                    case (int)CertificateRequestState.Rejected:
                        return CertificateRequestState.Rejected;
                    case (int)CertificateRequestState.Accepted:
                        return CertificateRequestState.Accepted;
                    case (int)CertificateRequestState.Approved:
                        break;
                    default:
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                certificateGroupId = request.CertificateGroupId;
                certificateTypeId = request.CertificateTypeId;

                entities.SaveChanges();
                return CertificateRequestState.Approved;
            }
        }

        public CertificateRequestState ReadRequest(
            NodeId applicationId,
            NodeId requestId,
            out string certificateGroupId,
            out string certificateTypeId,
            out byte[] certificateRequest,
            out string subjectName,
            out string[] domainNames,
            out string privateKeyFormat,
            out string privateKeyPassword)
        {
            certificateGroupId = null;
            certificateTypeId = null;
            certificateRequest = null;
            subjectName = null;
            domainNames = null;
            privateKeyFormat = null;
            privateKeyPassword = null;
            Guid reqId = GetNodeIdGuid(requestId);
            Guid appId = GetNodeIdGuid(applicationId);

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var request = (from x in entities.CertificateRequests where x.RequestId == reqId select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                switch (request.State)
                {
                    case (int)CertificateRequestState.New:
                        return CertificateRequestState.New;
                    case (int)CertificateRequestState.Rejected:
                        return CertificateRequestState.Rejected;
                    case (int)CertificateRequestState.Accepted:
                        return CertificateRequestState.Accepted;
                    case (int)CertificateRequestState.Approved:
                        break;
                    default:
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                certificateGroupId = request.CertificateGroupId;
                certificateTypeId = request.CertificateTypeId;
                certificateRequest = request.CertificateSigningRequest;
                subjectName = request.SubjectName;
                domainNames = request.DomainNames != null ? JsonConvert.DeserializeObject<string[]>(request.DomainNames) : null;
                privateKeyFormat = request.PrivateKeyFormat;
                privateKeyPassword = request.PrivateKeyPassword;

                entities.SaveChanges();
                return CertificateRequestState.Approved;
            }
        }

        #endregion
        #region Private Fileds
        private DateTime m_lastCounterResetTime = DateTime.MinValue;
        #endregion
    }
}

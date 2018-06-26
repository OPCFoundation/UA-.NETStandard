/* ========================================================================
 * Copyright (c) 2005-2017 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.Gds.Server.Database.Linq
{
    [Serializable]
    class ApplicationName
    {
        public Guid ApplicationId { get; set; }
        public string Locale { get; set; }
        public string Text { get; set; }
    }
    [Serializable]
    class Application
    {
        public uint ID { get; set; }
        public Guid ApplicationId { get; set; }
        public string ApplicationUri { get; set; }
        public string ApplicationName { get; set; }
        public int ApplicationType { get; set; }
        public string ProductUri { get; set; }
        public string ServerCapabilities { get; set; }
        public byte[] Certificate { get; set; }
        public byte[] HttpsCertificate { get; set; }
        public Guid? TrustListId { get; set; }
        public Guid? HttpsTrustListId { get; set; }
    }
    [Serializable]
    class CertificateRequest
    {
        public Guid RequestId { get; set; }
        public Guid ApplicationId { get; set; }
        public int State { get; set; }
        public NodeId CertificateGroupId { get; set; }
        public NodeId CertificateTypeId { get; set; }
        public byte[] CertificateSigningRequest { get; set; }
        public string SubjectName { get; set; }
        public string[] DomainNames { get; set; }
        public string PrivateKeyFormat { get; set; }
        public string PrivateKeyPassword { get; set; }
        public string AuthorityId { get; set; }
        public byte[] Certificate { get; set; }
    }
    [Serializable]
    class CertificateStore
    {
        CertificateStore()
        {
            TrustListId = Guid.NewGuid();
        }
        public string Path { get; set; }
        public string AuthorityId { get; set; }
        public Guid TrustListId { get; private set; }
    }
    [Serializable]
    class ServerEndpoint
    {
        public Guid ApplicationId { get; set; }
        public string DiscoveryUrl { get; set; }
    }
    [Serializable]
    public class LinqApplicationsDatabase : ApplicationsDatabaseBase, ICertificateRequest
    {
        #region IApplicationsDatabase 
        public override void Initialize()
        {
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

            lock (Lock)
            {
                Application record = null;

                if (applicationId != Guid.Empty)
                {
                    var results = from ii in Applications
                                  where ii.ApplicationId == applicationId
                                  select ii;

                    record = results.SingleOrDefault();

                    if (record != null)
                    {
                        var endpoints = (from ii in ServerEndpoints
                                         where ii.ApplicationId == record.ApplicationId
                                         select ii).ToList<ServerEndpoint>();

                        foreach (var endpoint in endpoints)
                        {
                            ServerEndpoints.Remove(endpoint);
                        }

                        var names = (from ii in ApplicationNames
                                     where ii.ApplicationId == record.ApplicationId
                                     select ii).ToList<ApplicationName>();

                        foreach (var name in names)
                        {
                            ApplicationNames.Remove(name);
                        }

                        SaveChanges();
                    }
                }

                bool isNew = false;

                if (record == null)
                {
                    applicationId = Guid.NewGuid();
                    record = new Application()
                    {
                        ApplicationId = applicationId,
                        ID = 0
                    };
                    isNew = true;
                }

                record.ApplicationUri = application.ApplicationUri;
                record.ApplicationName = application.ApplicationNames[0].Text;
                record.ApplicationType = (int)application.ApplicationType;
                record.ProductUri = application.ProductUri;
                record.ServerCapabilities = capabilities;

                if (isNew)
                {
                    Applications.Add(record);
                }

                SaveChanges();

                if (application.DiscoveryUrls != null)
                {
                    foreach (var discoveryUrl in application.DiscoveryUrls)
                    {
                        ServerEndpoints.Add(
                            new ServerEndpoint()
                            {
                                ApplicationId = record.ApplicationId,
                                DiscoveryUrl = discoveryUrl
                            });
                    }
                }

                if (application.ApplicationNames != null && application.ApplicationNames.Count > 0)
                {
                    foreach (var applicationName in application.ApplicationNames)
                    {
                        ApplicationNames.Add(new ApplicationName()
                        {
                            ApplicationId = record.ApplicationId,
                            Locale = applicationName.Locale,
                            Text = applicationName.Text
                        });
                    }
                }

                SaveChanges();

                return new NodeId(applicationId, NamespaceIndex);
            }
        }


        public override void UnregisterApplication(
            NodeId applicationId,
            out byte[] certificate,
            out byte[] httpsCertificate)
        {
            certificate = null;
            httpsCertificate = null;

            Guid id = GetNodeIdGuid(applicationId);

            List<byte[]> certificates = new List<byte[]>();

            lock (Lock)
            {
                var application = (from ii in Applications
                                   where ii.ApplicationId == id
                                   select ii).SingleOrDefault();

                if (application == null)
                {
                    throw new ArgumentException("A record with the specified application id does not exist.", nameof(applicationId));
                }

                certificate = application.Certificate;
                httpsCertificate = application.HttpsCertificate;

                var certificateRequests =
                    from ii in CertificateRequests
                    where ii.ApplicationId == id
                    select ii;

                foreach (var entry in new List<CertificateRequest>(certificateRequests))
                {
                    CertificateRequests.Remove(entry);
                }

                var applicationNames =
                    from ii in ApplicationNames
                    where ii.ApplicationId == id
                    select ii;

                foreach (var entry in new List<ApplicationName>(applicationNames))
                {
                    ApplicationNames.Remove(entry);
                }

                var serverEndpoints =
                    from ii in ServerEndpoints
                    where ii.ApplicationId == id
                    select ii;

                foreach (var entry in new List<ServerEndpoint>(serverEndpoints))
                {
                    ServerEndpoints.Remove(entry);
                }

                Applications.Remove(application);
                SaveChanges();
            }

        }

        public override ApplicationRecordDataType GetApplication(
            NodeId applicationId
            )
        {
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                var results = from x in Applications
                              where x.ApplicationId == id
                              select x;

                var result = results.SingleOrDefault();

                if (result == null)
                {
                    return null;
                }

                var applicationNames =
                     from ii in ApplicationNames
                     where ii.ApplicationId == id
                     select ii;

                var names = new List<LocalizedText>();
                foreach (var applicationName in applicationNames)
                {
                    names.Add(new LocalizedText(applicationName.Locale, applicationName.Text));
                }

                StringCollection discoveryUrls = null;

                var endpoints = from ii in ServerEndpoints
                                where ii.ApplicationId == result.ApplicationId
                                select ii;

                if (endpoints != null)
                {
                    discoveryUrls = new StringCollection();

                    foreach (var endpoint in endpoints)
                    {
                        discoveryUrls.Add(endpoint.DiscoveryUrl);
                    }
                }

                var capabilities = new StringCollection();
                if (!String.IsNullOrWhiteSpace(result.ServerCapabilities))
                {
                    capabilities.AddRange(result.ServerCapabilities.Split(','));
                }

                return new ApplicationRecordDataType()
                {
                    ApplicationId = new NodeId(result.ApplicationId, NamespaceIndex),
                    ApplicationUri = result.ApplicationUri,
                    ApplicationType = (ApplicationType)result.ApplicationType,
                    ApplicationNames = new LocalizedTextCollection(names),
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
            lock (Lock)
            {
                var results = from x in Applications
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

                    var endpoints = from ii in ServerEndpoints
                                    where ii.ApplicationId == result.ApplicationId
                                    select ii;

                    if (endpoints != null)
                    {
                        discoveryUrls = new StringCollection();

                        foreach (var endpoint in endpoints)
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

        public override ServerOnNetwork[] QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            string[] serverCapabilities,
            out DateTime lastCounterResetTime)
        {
            lock (Lock)
            {
                lastCounterResetTime = queryCounterResetTime;

                var results = from x in ServerEndpoints
                              join y in Applications on x.ApplicationId equals y.ApplicationId
                              where y.ID >= startingRecordId
                              orderby y.ID
                              select new
                              {
                                  y.ID,
                                  y.ApplicationName,
                                  y.ApplicationUri,
                                  y.ProductUri,
                                  x.DiscoveryUrl,
                                  y.ServerCapabilities
                              };

                List<ServerOnNetwork> records = new List<ServerOnNetwork>();
                uint lastID = 0;

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

                    if (lastID != 0)
                    {
                        if (maxRecordsToReturn != 0 &&
                            lastID != result.ID &&
                            records.Count >= maxRecordsToReturn)
                        {
                            break;
                        }
                    }
                    lastID = result.ID;

                    records.Add(new ServerOnNetwork()
                    {
                        RecordId = result.ID,
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
            byte[] certificate,
            bool isHttpsCertificate
            )
        {
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                var results = from x in Applications
                              where x.ApplicationId == id
                              select x;

                var result = results.SingleOrDefault();

                if (result == null)
                {
                    return false;
                }

                if (isHttpsCertificate)
                {
                    result.HttpsCertificate = certificate;
                }
                else
                {
                    result.Certificate = certificate;
                }

                SaveChanges();
            }

            return true;
        }

        public override bool SetApplicationTrustLists(
            NodeId applicationId,
            NodeId trustListId,
            NodeId httpsTrustListId
            )
        {
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                var result = (from x in Applications where x.ApplicationId == id select x).SingleOrDefault();

                if (result == null)
                {
                    return false;
                }

                result.TrustListId = null;
                result.HttpsTrustListId = null;

                if (trustListId != null)
                {
                    string storePath = trustListId.ToString();

                    var result2 = (from x in CertificateStores where x.Path == storePath select x).SingleOrDefault();

                    if (result2 != null)
                    {
                        result.TrustListId = result2.TrustListId;
                    }
                }

                if (httpsTrustListId != null)
                {
                    string storePath = httpsTrustListId.ToString();

                    var result2 = (from x in CertificateStores where x.Path == storePath select x).SingleOrDefault();

                    if (result2 != null)
                    {
                        result.HttpsTrustListId = result2.TrustListId;
                    }
                }

                SaveChanges();
            }

            return true;
        }
        #endregion
        #region ICertificateRequest
        public NodeId CreateSigningRequest(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificateRequest,
            string authorityId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                var application = (from x in Applications where x.ApplicationId == id select x).SingleOrDefault();

                if (application == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                var request = (from x in CertificateRequests where x.AuthorityId == authorityId && x.ApplicationId == id select x).SingleOrDefault();

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
                request.ApplicationId = id;

                if (isNew)
                {
                    CertificateRequests.Add(request);
                }

                SaveChanges();

                return new NodeId(request.RequestId, NamespaceIndex);
            }

        }

        public NodeId CreateNewKeyPairRequest(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            string privateKeyPassword,
            string authorityId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                var application = (from x in Applications where x.ApplicationId == id select x).SingleOrDefault();

                if (application == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                var request = (from x in CertificateRequests where x.AuthorityId == authorityId && x.ApplicationId == id select x).SingleOrDefault();

                bool isNew = false;

                if (request == null)
                {
                    request = new CertificateRequest()
                    {
                        RequestId = Guid.NewGuid(),
                        AuthorityId = authorityId
                    };
                    isNew = true;
                }

                request.State = (int)CertificateRequestState.New;
                request.CertificateGroupId = certificateGroupId;
                request.CertificateTypeId = certificateTypeId;
                request.SubjectName = subjectName;
                request.DomainNames = domainNames;
                request.PrivateKeyFormat = privateKeyFormat;
                request.PrivateKeyPassword = privateKeyPassword;
                request.CertificateSigningRequest = null;
                request.ApplicationId = id;

                if (isNew)
                {
                    CertificateRequests.Add(request);
                }

                SaveChanges();

                return new NodeId(request.RequestId, NamespaceIndex);
            }

        }

        public void ApproveCertificateRequest(
            NodeId requestId,
            bool isRejected
            )
        {
            Guid id = GetNodeIdGuid(requestId);

            lock (Lock)
            {
                var request = (from x in CertificateRequests where x.RequestId == id select x).SingleOrDefault();

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

                SaveChanges();
            }

        }

        public void AcceptCertificateRequest(
            NodeId requestId,
            byte[] signedCertificate)
        {
            Guid id = GetNodeIdGuid(requestId);

            lock (Lock)
            {
                var request = (from x in CertificateRequests where x.RequestId == id select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                request.State = (int)CertificateRequestState.Accepted;

                // save certificate for audit trail
                request.Certificate = signedCertificate;

                // erase information which is ot required anymore
                request.CertificateSigningRequest = null;
                request.PrivateKeyPassword = null;

                SaveChanges();
            }

        }

        public CertificateRequestState CompleteCertificateRequest(
            NodeId applicationId,
            NodeId requestId,
            out NodeId certificateGroupId,
            out NodeId certificateTypeId,
            out byte[] signedCertificate,
            out byte[] privateKey
            )
        {
            certificateGroupId = null;
            certificateTypeId = null;
            signedCertificate = null;
            privateKey = null;
            Guid reqId = GetNodeIdGuid(requestId);
            Guid appId = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                var request = (from x in CertificateRequests where x.RequestId == reqId select x).SingleOrDefault();

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

                return CertificateRequestState.Approved;
            }
        }

        public CertificateRequestState ReadRequest(
            NodeId applicationId,
            NodeId requestId,
            out NodeId certificateGroupId,
            out NodeId certificateTypeId,
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

            lock (Lock)
            {
                var request = (from x in CertificateRequests where x.RequestId == reqId select x).SingleOrDefault();

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
                domainNames = request.DomainNames;
                privateKeyFormat = request.PrivateKeyFormat;
                privateKeyPassword = request.PrivateKeyPassword;

                return CertificateRequestState.Approved;
            }
        }
        #endregion
        #region Public Members
        public virtual void Save()
        {
        }
        #endregion
        #region Private Members
        private void SaveChanges()
        {
            lock (Lock)
            {
                queryCounterResetTime = DateTime.UtcNow;
                // assign IDs to new apps
                var queryNewApps = from x in Applications
                                   where x.ID == 0
                                   select x;
                if (Applications.Count > 0)
                {
                    uint appMax = Applications.Max(a => a.ID);
                    foreach (var application in queryNewApps)
                    {
                        appMax++;
                        application.ID = appMax;
                    }
                }
                Save();
            }
        }
        #endregion
        #region Internal Members
        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            Lock = new object();
            queryCounterResetTime = DateTime.UtcNow;
        }
        #endregion
        #region Internal Fields
        [NonSerialized]
        internal object Lock = new object();
        [NonSerialized]
        internal DateTime queryCounterResetTime = DateTime.UtcNow;
        [JsonProperty]
        internal ICollection<Application> Applications = new HashSet<Application>();
        [JsonProperty]
        internal ICollection<ApplicationName> ApplicationNames = new List<ApplicationName>();
        [JsonProperty]
        internal ICollection<ServerEndpoint> ServerEndpoints = new List<ServerEndpoint>();
        [JsonProperty]
        internal ICollection<CertificateRequest> CertificateRequests = new HashSet<CertificateRequest>();
        [JsonProperty]
        internal ICollection<CertificateStore> CertificateStores = new HashSet<CertificateStore>();
        #endregion
    }
}

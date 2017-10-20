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

namespace Opc.Ua.Gds.Server.Database
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
        public byte[] Certificate { get; set; }
        public byte[] PrivateKey { get; set; }
        public string AuthorityId { get; set; }
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
    public class LinqApplicationsDatabase : ApplicationsDatabaseBase
    {
        #region IApplicationsDatabase Members
        public override void Initialize()
        {
        }

        public override NodeId RegisterApplication(
            ApplicationRecordDataType application
            )
        {
            NodeId appNodeId = base.RegisterApplication(application);
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
                        var endpoints = from ii in ServerEndpoints
                                        where ii.ApplicationId == record.ApplicationId
                                        select ii;

                        foreach (var endpoint in endpoints)
                        {
                            ServerEndpoints.Remove(endpoint);
                        }

                        var names = from ii in ApplicationNames
                                    where ii.ApplicationId == record.ApplicationId
                                    select ii;

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
                    Applications.Add(record);
                }

                SaveChanges();

                if (application.DiscoveryUrls != null)
                {
                    foreach (var discoveryUrl in application.DiscoveryUrls)
                    {
                        ServerEndpoints.Add(new ServerEndpoint() { ApplicationId = record.ApplicationId, DiscoveryUrl = discoveryUrl });
                    }
                }

                if (application.ApplicationNames != null && application.ApplicationNames.Count > 1)
                {
                    foreach (var applicationName in application.ApplicationNames)
                    {
                        ApplicationNames.Add(new ApplicationName() { ApplicationId = record.ApplicationId, Locale = applicationName.Locale, Text = applicationName.Text });
                    }
                }

                SaveChanges();

                return new NodeId(applicationId, NamespaceIndex);
            }
        }

        public override NodeId CreateCertificateRequest(
            NodeId applicationId,
            byte[] certificate,
            byte[] privateKey,
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
                request.Certificate = certificate;
                request.PrivateKey = privateKey;
                request.ApplicationId = id;

                if (isNew)
                {
                    CertificateRequests.Add(request);
                }

                SaveChanges();

                return new NodeId(request.RequestId, NamespaceIndex);
            }

        }

        public override void ApproveCertificateRequest(
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

                request.State = (int)((isRejected) ? CertificateRequestState.Rejected : CertificateRequestState.Approved);
                SaveChanges();
            }

        }

        public override bool CompleteCertificateRequest(
            NodeId applicationId,
            NodeId requestId,
            out byte[] certificate,
            out byte[] privateKey)
        {
            certificate = null;
            privateKey = null;
            Guid reqId = GetNodeIdGuid(requestId);
            Guid appId = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                var request = (from x in CertificateRequests where x.RequestId == reqId select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                if (request.State == (int)CertificateRequestState.New)
                {
                    return false;
                }

                if (request.State == (int)CertificateRequestState.Rejected)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "The certificate request has been rejected by the administrator.");
                }

                certificate = request.Certificate;
                privateKey = request.PrivateKey;

                if (request.State == (int)CertificateRequestState.Approved)
                {
                    var application = (
                        from ii in Applications
                        where ii.ApplicationId == request.ApplicationId
                        select ii).SingleOrDefault();

                    if (application == null)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                    }

                    if (request.AuthorityId != "https")
                    {
                        application.Certificate = certificate;
                    }
                    else
                    {
                        application.HttpsCertificate = certificate;
                    }

                    request.State = (int)CertificateRequestState.Accepted;
                }

                SaveChanges();
                return true;
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

                foreach (var entry in certificateRequests)
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

                    if (result.ServerCapabilities != null)
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

                    records.Add(new ServerOnNetwork()
                    {
                        RecordId = result.ID,
                        ServerName = result.ApplicationName,
                        DiscoveryUrl = result.DiscoveryUrl,
                        ServerCapabilities = capabilities
                    });

                    if (records.Count >= maxRecordsToReturn)
                    {
                        break;
                    }
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
                uint queryCounter = 0;
                foreach (var application in Applications)
                {
                    application.ID = queryCounter++;
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
        #region Private Fields
        [NonSerialized] object Lock = new object();
        [NonSerialized] DateTime queryCounterResetTime = DateTime.UtcNow;
        ICollection<Application> Applications = new HashSet<Application>();
        ICollection<ApplicationName> ApplicationNames = new HashSet<ApplicationName>();
        ICollection<ServerEndpoint> ServerEndpoints = new HashSet<ServerEndpoint>();
        ICollection<CertificateRequest> CertificateRequests = new HashSet<CertificateRequest>();
        ICollection<CertificateStore> CertificateStores = new HashSet<CertificateStore>();
        #endregion
    }
}

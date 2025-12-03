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
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.Gds.Server.Database.Linq
{
    [Serializable]
    internal class ApplicationName
    {
        public Guid ApplicationId { get; set; }
        public string Locale { get; set; }
        public string Text { get; set; }
    }

    [Serializable]
    internal class Application
    {
        public Application()
        {
            Certificate = [];
            TrustListId = [];
        }

        public uint ID { get; set; }
        public Guid ApplicationId { get; set; }
        public string ApplicationUri { get; set; }
        public string ApplicationName { get; set; }
        public int ApplicationType { get; set; }
        public string ProductUri { get; set; }
        public string ServerCapabilities { get; set; }
        public Dictionary<string, byte[]> Certificate { get; }
        public Dictionary<string, string> TrustListId { get; }
    }

    [Serializable]
    internal class CertificateRequest
    {
        public Guid RequestId { get; set; }
        public Guid ApplicationId { get; set; }
        public int State { get; set; }
        public string CertificateGroupId { get; set; }
        public string CertificateTypeId { get; set; }
        public byte[] CertificateSigningRequest { get; set; }
        public string SubjectName { get; set; }
        public string[] DomainNames { get; set; }
        public string PrivateKeyFormat { get; set; }
        public char[] PrivateKeyPassword { get; set; }
        public string AuthorityId { get; set; }
        public byte[] Certificate { get; set; }
    }

    [Serializable]
    internal sealed class CertificateStore
    {
        public CertificateStore()
        {
            TrustListId = Guid.NewGuid();
        }

        public string Path { get; set; }
        public string AuthorityId { get; set; }
        public Guid TrustListId { get; }
    }

    [Serializable]
    internal class ServerEndpoint
    {
        public Guid ApplicationId { get; set; }
        public string DiscoveryUrl { get; set; }
    }

    [Serializable]
    public class LinqApplicationsDatabase : ApplicationsDatabaseBase, ICertificateRequest
    {
        public override void Initialize()
        {
        }

        public override NodeId RegisterApplication(ApplicationRecordDataType application)
        {
            NodeId appNodeId = base.RegisterApplication(application);
            if (NodeId.IsNull(appNodeId))
            {
                appNodeId = new NodeId(Guid.NewGuid(), NamespaceIndex);
            }
            Guid applicationId = GetNodeIdGuid(appNodeId);
            string capabilities = ServerCapabilities(application);

            lock (Lock)
            {
                Application record = null;

                if (applicationId != Guid.Empty)
                {
                    IEnumerable<Application> results =
                        from ii in Applications
                        where ii.ApplicationId == applicationId
                        select ii;

                    record = results.SingleOrDefault();

                    if (record != null)
                    {
                        var endpoints = (
                            from ii in ServerEndpoints
                            where ii.ApplicationId == record.ApplicationId
                            select ii
                        ).ToList();

                        foreach (ServerEndpoint endpoint in endpoints)
                        {
                            ServerEndpoints.Remove(endpoint);
                        }

                        var names = (
                            from ii in ApplicationNames
                            where ii.ApplicationId == record.ApplicationId
                            select ii
                        ).ToList();

                        foreach (ApplicationName name in names)
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
                    record = new Application { ApplicationId = applicationId, ID = 0 };
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
                    foreach (string discoveryUrl in application.DiscoveryUrls)
                    {
                        ServerEndpoints.Add(
                            new ServerEndpoint
                            {
                                ApplicationId = record.ApplicationId,
                                DiscoveryUrl = discoveryUrl
                            });
                    }
                }

                if (application.ApplicationNames != null && application.ApplicationNames.Count > 0)
                {
                    foreach (LocalizedText applicationName in application.ApplicationNames)
                    {
                        ApplicationNames.Add(
                            new ApplicationName
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

        public override void UnregisterApplication(NodeId applicationId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            var certificates = new List<byte[]>();

            lock (Lock)
            {
                Application application =
                    (from ii in Applications where ii.ApplicationId == id select ii)
                        .SingleOrDefault()
                    ?? throw new ArgumentException(
                        "A record with the specified application id does not exist.",
                        nameof(applicationId));

                IEnumerable<CertificateRequest> certificateRequests =
                    from ii in CertificateRequests
                    where ii.ApplicationId == id
                    select ii;

                foreach (CertificateRequest entry in new List<CertificateRequest>(
                    certificateRequests))
                {
                    CertificateRequests.Remove(entry);
                }

                IEnumerable<ApplicationName> applicationNames =
                    from ii in ApplicationNames
                    where ii.ApplicationId == id
                    select ii;

                foreach (ApplicationName entry in new List<ApplicationName>(applicationNames))
                {
                    ApplicationNames.Remove(entry);
                }

                IEnumerable<ServerEndpoint> serverEndpoints =
                    from ii in ServerEndpoints
                    where ii.ApplicationId == id
                    select ii;

                foreach (ServerEndpoint entry in new List<ServerEndpoint>(serverEndpoints))
                {
                    ServerEndpoints.Remove(entry);
                }

                Applications.Remove(application);
                SaveChanges();
            }
        }

        public override ApplicationRecordDataType GetApplication(NodeId applicationId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                IEnumerable<Application> results = from x in Applications
                                                   where x
                    .ApplicationId == id
                                                   select x;

                Application result = results.SingleOrDefault();

                if (result == null)
                {
                    return null;
                }

                IEnumerable<ApplicationName> applicationNames =
                    from ii in ApplicationNames
                    where ii.ApplicationId == id
                    select ii;

                var names = new List<LocalizedText>();
                foreach (ApplicationName applicationName in applicationNames)
                {
                    names.Add(new LocalizedText(applicationName.Locale, applicationName.Text));
                }

                StringCollection discoveryUrls = null;

                IEnumerable<ServerEndpoint> endpoints =
                    from ii in ServerEndpoints
                    where ii.ApplicationId == result.ApplicationId
                    select ii;

                if (endpoints != null)
                {
                    discoveryUrls = [];

                    foreach (ServerEndpoint endpoint in endpoints)
                    {
                        discoveryUrls.Add(endpoint.DiscoveryUrl);
                    }
                }

                var capabilities = new StringCollection();
                if (!string.IsNullOrWhiteSpace(result.ServerCapabilities))
                {
                    capabilities.AddRange(result.ServerCapabilities.Split(','));
                }

                return new ApplicationRecordDataType
                {
                    ApplicationId = new NodeId(result.ApplicationId, NamespaceIndex),
                    ApplicationUri = result.ApplicationUri,
                    ApplicationType = (ApplicationType)result.ApplicationType,
                    ApplicationNames = [.. names],
                    ProductUri = result.ProductUri,
                    DiscoveryUrls = discoveryUrls,
                    ServerCapabilities = capabilities
                };
            }
        }

        public override ApplicationRecordDataType[] FindApplications(string applicationUri)
        {
            lock (Lock)
            {
                IEnumerable<Application> results =
                    from x in Applications
                    where x.ApplicationUri == applicationUri
                    select x;

                var records = new List<ApplicationRecordDataType>();

                foreach (Application result in results)
                {
                    LocalizedText[] names = null;

                    if (result.ApplicationName != null)
                    {
                        names = [result.ApplicationName];
                    }

                    StringCollection discoveryUrls = null;

                    IEnumerable<ServerEndpoint> endpoints =
                        from ii in ServerEndpoints
                        where ii.ApplicationId == result.ApplicationId
                        select ii;

                    if (endpoints != null)
                    {
                        discoveryUrls = [];

                        foreach (ServerEndpoint endpoint in endpoints)
                        {
                            discoveryUrls.Add(endpoint.DiscoveryUrl);
                        }
                    }

                    string[] capabilities = null;

                    if (result.ServerCapabilities != null)
                    {
                        capabilities = result.ServerCapabilities.Split(',');
                    }

                    records.Add(
                        new ApplicationRecordDataType
                        {
                            ApplicationId = new NodeId(result.ApplicationId, NamespaceIndex),
                            ApplicationUri = result.ApplicationUri,
                            ApplicationType = (ApplicationType)result.ApplicationType,
                            ApplicationNames = [.. names],
                            ProductUri = result.ProductUri,
                            DiscoveryUrls = discoveryUrls,
                            ServerCapabilities = capabilities
                        });
                }

                return [.. records];
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

            lock (Lock)
            {
                IEnumerable<Application> results =
                    from x in Applications
                    where (int)startingRecordId == 0 || (int)startingRecordId <= x.ID
                    select x;

                lastCounterResetTime = QueryCounterResetTime;
                uint lastID = 0;

                foreach (Application result in results)
                {
                    if (!string.IsNullOrEmpty(applicationName) &&
                        !Match(result.ApplicationName, applicationName))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(applicationUri) &&
                        !Match(result.ApplicationUri, applicationUri))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(productUri) && !Match(result.ProductUri, productUri))
                    {
                        continue;
                    }

                    string[] capabilities = null;
                    if (!string.IsNullOrEmpty(result.ServerCapabilities))
                    {
                        capabilities = result.ServerCapabilities.Split(',');
                    }

                    if (serverCapabilities != null && serverCapabilities.Length > 0)
                    {
                        bool match = true;

                        for (int ii = 0; ii < serverCapabilities.Length; ii++)
                        {
                            if (capabilities == null ||
                                !capabilities.Contains(serverCapabilities[ii]))
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

                    IEnumerable<ServerEndpoint> endpoints =
                        from ii in ServerEndpoints
                        where ii.ApplicationId == result.ApplicationId
                        select ii;

                    var discoveryUrls = new StringCollection();
                    if (endpoints != null)
                    {
                        foreach (ServerEndpoint endpoint in endpoints)
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
                        if (maxRecordsToReturn != 0 && records.Count >= maxRecordsToReturn)
                        {
                            break;
                        }

                        lastID = result.ID;
                    }

                    records.Add(
                        new ApplicationDescription
                        {
                            ApplicationUri = result.ApplicationUri,
                            ProductUri = result.ProductUri,
                            ApplicationName = result.ApplicationName,
                            ApplicationType = (ApplicationType)result.ApplicationType,
                            GatewayServerUri = null,
                            DiscoveryProfileUri = null,
                            DiscoveryUrls = discoveryUrls
                        });
                    nextRecordId = lastID + 1;
                }
                return [.. records];
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
                lastCounterResetTime = QueryCounterResetTime;

                var results =
                    from x in ServerEndpoints
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

                var records = new List<ServerOnNetwork>();
                uint lastID = 0;

                foreach (var result in results)
                {
                    if (!string.IsNullOrEmpty(applicationName) &&
                        !Match(result.ApplicationName, applicationName))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(applicationUri) &&
                        !Match(result.ApplicationUri, applicationUri))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(productUri) && !Match(result.ProductUri, productUri))
                    {
                        continue;
                    }

                    string[] capabilities = null;
                    if (!string.IsNullOrEmpty(result.ServerCapabilities))
                    {
                        capabilities = result.ServerCapabilities.Split(',');
                    }

                    if (serverCapabilities != null && serverCapabilities.Length > 0)
                    {
                        bool match = true;

                        for (int ii = 0; ii < serverCapabilities.Length; ii++)
                        {
                            if (capabilities == null ||
                                !capabilities.Contains(serverCapabilities[ii]))
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

                    if (lastID != 0 &&
                        maxRecordsToReturn != 0 &&
                        lastID != result.ID &&
                        records.Count >= maxRecordsToReturn)
                    {
                        break;
                    }
                    lastID = result.ID;

                    records.Add(
                        new ServerOnNetwork
                        {
                            RecordId = result.ID,
                            ServerName = result.ApplicationName,
                            DiscoveryUrl = result.DiscoveryUrl,
                            ServerCapabilities = capabilities
                        });
                }

                return [.. records];
            }
        }

        public override bool SetApplicationCertificate(
            NodeId applicationId,
            string certificateTypeId,
            byte[] certificate)
        {
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                IEnumerable<Application> results = from x in Applications
                                                   where x
                    .ApplicationId == id
                                                   select x;

                Application result = results.SingleOrDefault();

                if (result == null)
                {
                    return false;
                }

                result.Certificate[certificateTypeId] = certificate;

                SaveChanges();
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

            lock (Lock)
            {
                Application application =
                    (from ii in Applications where ii.ApplicationId == id select ii)
                        .SingleOrDefault()
                    ?? throw new ArgumentException(
                        "A record with the specified application id does not exist.",
                        nameof(applicationId));

                if (!application.Certificate.TryGetValue(certificateTypeId, out certificate))
                {
                    return false;
                }
            }
            return true;
        }

        public override bool SetApplicationTrustLists(
            NodeId applicationId,
            string certificateTypeId,
            string trustListId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                Application result = (from x in Applications where x.ApplicationId == id select x)
                    .SingleOrDefault();
                if (result == null)
                {
                    return false;
                }

                if (trustListId != null)
                {
                    result.TrustListId[certificateTypeId] = trustListId;
                }
                SaveChanges();
            }

            return true;
        }

        public override bool GetApplicationTrustLists(
            NodeId applicationId,
            string certificateTypeId,
            out string trustListId)
        {
            trustListId = null;
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                Application result = (from x in Applications where x.ApplicationId == id select x)
                    .SingleOrDefault();

                if (result == null)
                {
                    return false;
                }
                return result.TrustListId.TryGetValue(certificateTypeId, out trustListId);
            }
        }

        public NodeId StartSigningRequest(
            NodeId applicationId,
            string certificateGroupId,
            string certificateTypeId,
            byte[] certificateRequest,
            string authorityId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                Application application =
                    (from x in Applications where x.ApplicationId == id select x).SingleOrDefault()
                    ?? throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);

                CertificateRequest request = (
                    from x in CertificateRequests
                    where x.AuthorityId == authorityId && x.ApplicationId == id
                    select x
                ).SingleOrDefault();

                bool isNew = false;

                if (request == null)
                {
                    request = new CertificateRequest
                    {
                        RequestId = Guid.NewGuid(),
                        AuthorityId = authorityId
                    };
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

        public NodeId StartNewKeyPairRequest(
            NodeId applicationId,
            string certificateGroupId,
            string certificateTypeId,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            ReadOnlySpan<char> privateKeyPassword,
            string authorityId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            lock (Lock)
            {
                Application application =
                    (from x in Applications where x.ApplicationId == id select x).SingleOrDefault()
                    ?? throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);

                CertificateRequest request = (
                    from x in CertificateRequests
                    where x.AuthorityId == authorityId && x.ApplicationId == id
                    select x
                ).SingleOrDefault();

                bool isNew = false;

                if (request == null)
                {
                    request = new CertificateRequest
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
                request.PrivateKeyPassword = privateKeyPassword.ToArray();
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

        public void ApproveRequest(NodeId requestId, bool isRejected)
        {
            Guid id = GetNodeIdGuid(requestId);

            lock (Lock)
            {
                CertificateRequest request =
                    (from x in CertificateRequests where x.RequestId == id select x)
                        .SingleOrDefault()
                    ?? throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);

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

        public void AcceptRequest(NodeId requestId, byte[] certificate)
        {
            Guid id = GetNodeIdGuid(requestId);

            lock (Lock)
            {
                CertificateRequest request =
                    (from x in CertificateRequests where x.RequestId == id select x)
                        .SingleOrDefault()
                    ?? throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);

                request.State = (int)CertificateRequestState.Accepted;

                // save certificate for audit trail
                request.Certificate = certificate;

                // erase information which is ot required anymore
                request.CertificateSigningRequest = null;
                request.PrivateKeyPassword = null;

                SaveChanges();
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

            lock (Lock)
            {
                CertificateRequest request =
                    (from x in CertificateRequests where x.RequestId == reqId select x)
                        .SingleOrDefault()
                    ?? throw new ServiceResultException(StatusCodes.BadInvalidArgument);

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
            out string certificateGroupId,
            out string certificateTypeId,
            out byte[] certificateRequest,
            out string subjectName,
            out string[] domainNames,
            out string privateKeyFormat,
            out ReadOnlySpan<char> privateKeyPassword)
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
                CertificateRequest request =
                    (from x in CertificateRequests where x.RequestId == reqId select x)
                        .SingleOrDefault()
                    ?? throw new ServiceResultException(StatusCodes.BadInvalidArgument);

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

        public virtual void Save()
        {
        }

        private void SaveChanges()
        {
            lock (Lock)
            {
                QueryCounterResetTime = DateTime.UtcNow;
                // assign IDs to new apps
                IEnumerable<Application> queryNewApps = from x in Applications
                                                        where x
                    .ID == 0
                                                        select x;
                if (Applications.Count > 0)
                {
                    uint appMax = Applications.Max(a => a.ID);
                    foreach (Application application in queryNewApps)
                    {
                        appMax++;
                        application.ID = appMax;
                    }
                }
                Save();
            }
        }

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            Lock = new object();
            QueryCounterResetTime = DateTime.UtcNow;
        }

        [NonSerialized]
        internal object Lock = new();

        [NonSerialized]
        internal DateTime QueryCounterResetTime = DateTime.UtcNow;

        [JsonProperty]
        internal ICollection<Application> Applications = new HashSet<Application>();

        [JsonProperty]
        internal ICollection<ApplicationName> ApplicationNames = [];

        [JsonProperty]
        internal ICollection<ServerEndpoint> ServerEndpoints = [];

        [JsonProperty]
        internal ICollection<CertificateRequest> CertificateRequests
            = new HashSet<CertificateRequest>();

        [JsonProperty]
        internal ICollection<CertificateStore> CertificateStores = new HashSet<CertificateStore>();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Opc.Ua.Gds;

namespace Opc.Ua.GdsServer
{
    public class ApplicationsDatabase
    {
        public ApplicationsDatabase()
        {
        }

        public ushort NamespaceIndex { get; set; }

        public NodeId RegisterApplication(ApplicationRecordDataType application)
        {
            if (application == null)
            {
                throw new ArgumentNullException("application");
            }

            if (application.ApplicationUri == null)
            {
                throw new ArgumentNullException("ApplicationUri");
            }

            if (!Uri.IsWellFormedUriString(application.ApplicationUri, UriKind.Absolute))
            {
                throw new ArgumentException(application.ApplicationUri + " is not a valid URI.", "ApplicationUri");
            }

            if (application.ApplicationType < ApplicationType.Server || application.ApplicationType > ApplicationType.DiscoveryServer)
            {
                throw new ArgumentException(application.ApplicationType.ToString() + " is not a valid ApplicationType.", "ApplicationType");
            }

            if (application.ApplicationNames == null || application.ApplicationNames.Count == 0 || LocalizedText.IsNullOrEmpty(application.ApplicationNames[0]))
            {
                throw new ArgumentException("At least one ApplicationName must be provided.", "ApplicationNames");
            }

            if (String.IsNullOrEmpty(application.ProductUri))
            {
                throw new ArgumentException("A ProductUri must be provided.", "ProductUri");
            }

            if (!Uri.IsWellFormedUriString(application.ProductUri, UriKind.Absolute))
            {
                throw new ArgumentException(application.ProductUri + " is not a valid URI.", "ProductUri");
            }

            if (application.DiscoveryUrls != null)
            {
                foreach (var discoveryUrl in application.DiscoveryUrls)
                {
                    if (String.IsNullOrEmpty(discoveryUrl))
                    {
                        continue;
                    }

                    if (!Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        throw new ArgumentException(discoveryUrl + " is not a valid URL.", "DiscoveryUrls");
                    }
                }
            }

            if (application.ApplicationType != ApplicationType.Client)
            {
                if (application.DiscoveryUrls == null || application.DiscoveryUrls.Count == 0)
                {
                    throw new ArgumentException("At least one DiscoveryUrl must be provided.", "DiscoveryUrls");
                }
            }
            else
            {
                if (application.DiscoveryUrls != null && application.DiscoveryUrls.Count > 0)
                {
                    throw new ArgumentException("DiscoveryUrls must not be specified for clients.", "DiscoveryUrls");
                }
            }

            StringBuilder capabilities = new StringBuilder();

            if (application.ServerCapabilities != null)
            {
                foreach (var capability in application.ServerCapabilities)
                {
                    if (String.IsNullOrEmpty(capability))
                    {
                        continue;
                    }

                    if (capabilities.Length > 0)
                    {
                        capabilities.Append(',');
                    }

                    capabilities.Append(capability);
                }
            }

            if (application.ApplicationType != ApplicationType.Client)
            {
                if (application.ServerCapabilities == null || application.ServerCapabilities.Count == 0)
                {
                    throw new ArgumentException("At least one Server Capability must be provided.", "ServerCapabilities");
                }
            }

            Guid applicationId = Guid.Empty;

            if (!NodeId.IsNull(application.ApplicationId))
            {
                if (application.ApplicationId.IdType != IdType.Guid)
                {
                    throw new ArgumentException("The ApplicationId to does refer to a existing record.", "ApplicationId");
                }

                applicationId = (Guid)application.ApplicationId.Identifier;
            }

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
                    record = new Application() { ApplicationId = Guid.NewGuid() };
                    isNew = true;
                }

                record.ApplicationUri = application.ApplicationUri;
                record.ApplicationName = application.ApplicationNames[0].Text;
                record.ApplicationType = (int)application.ApplicationType;
                record.ProductUri = application.ProductUri;
                record.ServerCapabilities = capabilities.ToString();

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

                if (application.ApplicationNames != null && application.ApplicationNames.Count > 1)
                {
                    foreach (var applicationName in application.ApplicationNames)
                    {
                        entities.ApplicationNames.Add(new ApplicationName() { ApplicationId = record.ID, Locale = applicationName.Locale, Text = applicationName.Text });
                    }
                }

                entities.SaveChanges();

                return new NodeId(record.ApplicationId, NamespaceIndex);
            }
        }

        private enum CertificateRequestState
        {
            New,
            Approved,
            Rejected,
            Accepted
        }

        public NodeId CreateCertificateRequest(
            NodeId applicationId,
            byte[] certificate,
            byte[] privateKey,
            string authorityId)
        {
            if (NodeId.IsNull(applicationId))
            {
                throw new ArgumentNullException("applicationId");
            }

            Guid? id = applicationId.Identifier as Guid?;

            if (id == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
            }
            
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
                request.Certificate = certificate;
                request.PrivateKey = privateKey;

                if (isNew)
                {
                    application.CertificateRequests.Add(request);
                }

                entities.SaveChanges();

                return new NodeId(request.RequestId, NamespaceIndex);
            }
        }

        public void ApproveCertificateRequest(NodeId requestId, bool isRejected)
        {
            if (NodeId.IsNull(requestId))
            {
                throw new ArgumentNullException("requestId");
            }

            Guid? id = requestId.Identifier as Guid?;

            if (id == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
            }

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var request = (from x in entities.CertificateRequests where x.RequestId == id select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                request.State = (int)((isRejected)?CertificateRequestState.Rejected:CertificateRequestState.Approved);
                entities.SaveChanges();
            }
        }

        public bool CompleteCertificateRequest(
            NodeId applicationId,
            NodeId requestId,
            out byte[] certificate, 
            out byte[] privateKey)
        {
            certificate = null;
            privateKey = null;

            if (NodeId.IsNull(requestId))
            {
                throw new ArgumentNullException("requestId");
            }

            Guid? id = requestId.Identifier as Guid?;

            if (id == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
            }

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var request = (from x in entities.CertificateRequests where x.RequestId == id select x).SingleOrDefault();

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
                    if (request.AuthorityId != "https")
                    {
                        request.Application.Certificate = certificate;
                    }
                    else
                    {
                        request.Application.HttpsCertificate = certificate;
                    }

                    request.State = (int)CertificateRequestState.Accepted;
                }

                entities.SaveChanges();
                return true;
            }
        }

        public void UnregisterApplication(
            NodeId applicationId,
            out byte[] certificate,
            out byte[] httpsCertificate)
        {
            if (NodeId.IsNull(applicationId))
            {
                throw new ArgumentNullException("applicationId");
            }

            Guid? id = applicationId.Identifier as Guid?;

            if (id == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }

            List<byte[]> certificates = new List<byte[]>();

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var result = (from ii in entities.Applications
                              where ii.ApplicationId == id.Value
                              select ii).SingleOrDefault();

                if (result == null)
                {
                    throw new ArgumentException("A record with the specified application id does not exist.", "applicationId");
                }

                certificate = result.Certificate;
                httpsCertificate = result.HttpsCertificate;

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
            }
        }

        public ApplicationRecordDataType GetApplication(NodeId applicationId)
        {
            if (NodeId.IsNull(applicationId))
            {
                return null;
            }

            if (applicationId.IdType != IdType.Guid || NamespaceIndex != applicationId.NamespaceIndex)
            {
                return null;
            }

            Guid id = (Guid)applicationId.Identifier;

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

        public ApplicationRecordDataType[] FindApplications(string applicationUri)
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

        public ServerOnNetwork[] QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            string[] serverCapabilities,
            out DateTime lastCounterResetTime)
        {
            lastCounterResetTime = DateTime.MinValue;

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.ServerEndpoints
                              join y in entities.Applications on x.ApplicationId equals y.ID
                              where ((int)startingRecordId == 0 || (int)startingRecordId < x.ID)
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
                        RecordId = (uint)result.ID,
                        ServerName = result.ApplicationName,
                        DiscoveryUrl = result.DiscoveryUrl,
                        ServerCapabilities = capabilities
                    });
                }

                return records.ToArray();
            }
        }

        public bool SetApplicationCertificate(NodeId applicationId, byte[] certificate, bool isHttpsCertificate)
        {
            if (NodeId.IsNull(applicationId))
            {
                throw new ArgumentNullException("applicationId");
            }

            if (applicationId.IdType != IdType.Guid || NamespaceIndex != applicationId.NamespaceIndex)
            {
                throw new ArgumentException("The application id is not recognized.", "applicationId");
            }

            Guid id = (Guid)applicationId.Identifier;

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

                if (isHttpsCertificate)
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

        public bool SetApplicationTrustLists(NodeId applicationId, NodeId trustListId, NodeId httpsTrustListId)
        {
            if (NodeId.IsNull(applicationId))
            {
                throw new ArgumentNullException("applicationId");
            }

            if (applicationId.IdType != IdType.Guid || NamespaceIndex != applicationId.NamespaceIndex)
            {
                throw new ArgumentException("The application id is not recognized.", "applicationId");
            }

            Guid id = (Guid)applicationId.Identifier;

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
                    string storePath = trustListId.ToString();

                    var result2 = (from x in entities.CertificateStores where x.Path == storePath select x).SingleOrDefault();

                    if (result2 != null)
                    {
                        result.TrustListId = result2.ID;
                    }
                }

                if (httpsTrustListId != null)
                {
                    string storePath = httpsTrustListId.ToString();

                    var result2 = (from x in entities.CertificateStores where x.Path == storePath select x).SingleOrDefault();

                    if (result2 != null)
                    {
                        result.HttpsTrustListId = result2.ID;
                    }
                }

                entities.SaveChanges();
            }

            return true;
        }

        /// <summary>
        /// Returns true if the target string matches the UA pattern string. 
        /// The pattern string may include UA wildcards %_\[]!
        /// </summary>
        /// <param name="target">String to check for a pattern match.</param>
        /// <param name="pattern">Pattern to match with the target string.</param>
        /// <returns>true if the target string matches the pattern, otherwise false.</returns>
        public static bool Match(string target, string pattern)
        {
            if (String.IsNullOrEmpty(target))
            {
                return false;
            }

            if (String.IsNullOrEmpty(pattern))
            {
                return true;
            }

            var tokens = Parse(pattern);

            int targetIndex = 0;

            for (int ii = 0; ii < tokens.Count; ii++)
            {
                targetIndex = Match(target, targetIndex, tokens, ref ii);

                if (targetIndex < 0)
                {
                    return false;
                }
            }

            if (targetIndex < target.Length)
            {
                return false;
            }

            return true;
        }

        private static List<string> Parse(string pattern)
        {
            List<string> tokens = new List<string>();

            int ii = 0;
            var buffer = new System.Text.StringBuilder();

            while (ii < pattern.Length)
            {
                char ch = pattern[ii];

                if (ch == '\\')
                {
                    ii++;

                    if (ii >= pattern.Length)
                    {
                        break;
                    }

                    buffer.Append(pattern[ii]);
                    ii++;
                    continue;
                }

                if (ch == '_')
                {
                    if (buffer.Length > 0)
                    {
                        tokens.Add(buffer.ToString());
                        buffer.Length = 0;
                    }

                    tokens.Add("_");
                    ii++;
                    continue;
                }

                if (ch == '%')
                {
                    if (buffer.Length > 0)
                    {
                        tokens.Add(buffer.ToString());
                        buffer.Length = 0;
                    }

                    tokens.Add("%");
                    ii++;

                    while (ii < pattern.Length && pattern[ii] == '%')
                    {
                        ii++;
                    }

                    continue;
                }

                if (ch == '[')
                {
                    if (buffer.Length > 0)
                    {
                        tokens.Add(buffer.ToString());
                        buffer.Length = 0;
                    }

                    buffer.Append(ch);
                    ii++;

                    int start = 0;
                    int end = 0;
                    while (ii < pattern.Length && pattern[ii] != ']')
                    {
                        if (pattern[ii] == '-' && ii > 0 && ii < pattern.Length - 1)
                        {
                            start = Convert.ToInt32(pattern[ii - 1]) + 1;
                            end = Convert.ToInt32(pattern[ii + 1]);

                            while (start < end)
                            {
                                buffer.Append(Convert.ToChar(start));
                                start++;
                            }

                            buffer.Append(Convert.ToChar(end));
                            ii += 2;
                            continue;
                        }

                        buffer.Append(pattern[ii]);
                        ii++;
                    }

                    buffer.Append("]");
                    tokens.Add(buffer.ToString());
                    buffer.Length = 0;

                    ii++;
                    continue;
                }

                buffer.Append(ch);
                ii++;
            }

            if (buffer.Length > 0)
            {
                tokens.Add(buffer.ToString());
                buffer.Length = 0;
            }

            return tokens;
        }

        private static int SkipToNext(string target, int targetIndex, IList<string> tokens, ref int tokenIndex)
        {
            if (targetIndex >= target.Length - 1)
            {
                return targetIndex + 1;
            }

            if (tokenIndex >= tokens.Count - 1)
            {
                return target.Length + 1;
            }


            if (!tokens[tokenIndex + 1].StartsWith("[^"))
            {
                int nextTokenIndex = tokenIndex + 1;

                // skip over unmatched chars.
                while (targetIndex < target.Length && Match(target, targetIndex, tokens, ref nextTokenIndex) < 0)
                {
                    targetIndex++;
                    nextTokenIndex = tokenIndex + 1;
                }

                nextTokenIndex = tokenIndex + 1;

                // skip over duplicate matches.
                while (targetIndex < target.Length && Match(target, targetIndex, tokens, ref nextTokenIndex) >= 0)
                {
                    targetIndex++;
                    nextTokenIndex = tokenIndex + 1;
                }

                // return last match.
                if (targetIndex <= target.Length)
                {
                    return targetIndex - 1;
                }
            }
            else
            {
                int start = targetIndex;
                int nextTokenIndex = tokenIndex + 1;

                // skip over matches.
                while (targetIndex < target.Length && Match(target, targetIndex, tokens, ref nextTokenIndex) >= 0)
                {
                    targetIndex++;
                    nextTokenIndex = tokenIndex + 1;
                }

                // no match in string.
                if (targetIndex < target.Length)
                {
                    return -1;
                }

                // try the next token.
                if (tokenIndex >= tokens.Count - 2)
                {
                    return target.Length + 1;
                }

                tokenIndex++;

                return SkipToNext(target, start, tokens, ref tokenIndex);
            }

            return -1;
        }

        private static int Match(string target, int targetIndex, IList<string> tokens, ref int tokenIndex)
        {
            if (tokens == null || tokenIndex < 0 || tokenIndex >= tokens.Count)
            {
                return -1;
            }

            if (target == null || targetIndex < 0 || targetIndex >= target.Length)
            {
                if (tokens[tokenIndex] == "%" && tokenIndex == tokens.Count - 1)
                {
                    return targetIndex;
                }

                return -1;
            }

            string token = tokens[tokenIndex];

            if (token == "_")
            {
                if (targetIndex >= target.Length)
                {
                    return -1;
                }

                return targetIndex + 1;
            }

            if (token == "%")
            {
                return SkipToNext(target, targetIndex, tokens, ref tokenIndex);
            }

            if (token.StartsWith("["))
            {
                bool inverse = false;
                bool match = false;

                for (int ii = 1; ii < token.Length - 1; ii++)
                {
                    if (token[ii] == '^')
                    {
                        inverse = true;
                        continue;
                    }

                    if (!inverse && target[targetIndex] == token[ii])
                    {
                        return targetIndex + 1;
                    }

                    if (inverse && target[targetIndex] == token[ii])
                    {
                        match = true;
                    }
                }

                if (inverse && !match)
                {
                    return targetIndex + 1;
                }

                return -1;
            }

            if (target.Substring(targetIndex).StartsWith(token))
            {
                return targetIndex + token.Length;
            }

            return -1;
        }
    }
}

/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Federation
{
    public sealed partial class HttpFederationProvider
    {
        /// <summary>
        /// Explicitly preloads complete Group metadata and its independent provider observation.
        /// Both HTTPS routes must be authorized in the server-owned binding. No native target is invented.
        /// </summary>
        public async ValueTask<FederationGroupSnapshot> PreloadGroupAsync(
            string metadataLocator,
            string observationLocator,
            RegistryRecordMapper mapper,
            FederationGroupSnapshot? previous = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataUri = new Uri(metadataLocator, UriKind.Absolute);
            var observationUri = new Uri(observationLocator, UriKind.Absolute);
            if (metadataUri.Scheme != Uri.UriSchemeHttps || observationUri.Scheme != Uri.UriSchemeHttps ||
                metadataUri.Authority != observationUri.Authority || metadataUri.UserInfo.Length != 0 ||
                observationUri.UserInfo.Length != 0 || metadataUri.Fragment.Length != 0 ||
                observationUri.Fragment.Length != 0 || metadataLocator == observationLocator ||
                !m_binding.Authorizes(metadataLocator) || !m_binding.Authorizes(observationLocator))
            {
                throw new ArgumentException("Independent Group routes must be authorized HTTPS addresses on one authority.");
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(m_timeout);
            RegistryObjectValueDataType observation = Object(await GetAsync(observationUri, deadline.Token)
                .ConfigureAwait(false));
            string origin = String(observation, "OriginUri");
            string xid = String(observation, "Xid");
            string collection = String(observation, "CollectionName");
            string groupId = String(observation, "GroupId");
            uint epoch = UInt32(observation, "Epoch");
            if (observation.Members.Count != 5 || xid != "/" + collection + "/" + groupId)
            {
                throw new ArgumentException("Independent Group observation fields or ownership disagree.");
            }
            RegistryObjectValueDataType metadata = Object(await GetAsync(metadataUri, deadline.Token)
                .ConfigureAwait(false));
            if (UInt32(metadata, "epoch") != epoch)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The raw Group metadata does not match the independently observed revision.");
            }
            if (collection == "endpoints")
            {
                EndpointRegistryRules.ValidateEndpoint(metadata);
            }
            else if (collection == "messagegroups")
            {
                EndpointRegistryRules.ValidateMessageGroup(metadata, groupId);
            }
            else
            {
                throw new ArgumentException("The Group collection is not an Endpoint Registry domain.");
            }
            string idField = collection == "endpoints" ? "endpointid" : "messagegroupid";
            foreach (RegistryMemberDataType member in metadata.Members)
            {
                if (member.Name == idField && member.Value is RegistryStringValueDataType identity &&
                    identity.Value != groupId || member.Name == "xid" &&
                    member.Value is RegistryStringValueDataType path && path.Value != xid)
                {
                    throw new ArgumentException("The raw Group identity contradicts the independent observation.");
                }
            }
            RegistryRecordDataType record = mapper.Project(metadata,
                collection == "endpoints" ? nameof(EndpointDataType) : nameof(MessageGroupDataType));
            return new FederationGroupSnapshot(new RegistryEntityReferenceDataType
            {
                OriginUri = origin,
                Xid = xid,
                Role = "Group",
                Locator = metadataLocator
            }, m_binding, null, null, record, epoch, previous);
        }
    }
}

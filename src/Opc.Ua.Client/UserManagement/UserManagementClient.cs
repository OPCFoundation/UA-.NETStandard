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
using System.Threading;
using System.Threading.Tasks;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Client.UserManagement
{
    /// <summary>
    /// Async client over the OPC UA Part 18 §5.2 user-management methods.
    /// Wraps an <see cref="ISession"/> and dispatches against the standard
    /// <c>ServerConfiguration.UserManagement</c> object (NodeId
    /// <c>i=24290</c>) via the source-generated
    /// <see cref="UserManagementTypeClient"/> proxy.
    /// </summary>
    public sealed class UserManagementClient : IUserManagementClient
    {
        /// <summary>
        /// Creates a new client rooted at the server's standard
        /// <c>ServerConfiguration.UserManagement</c> object.
        /// </summary>
        public UserManagementClient(ISession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            Session = session;
            ObjectId = new NodeId(Objects.UserManagement);
            Proxy = new UserManagementTypeClient(
                session,
                ObjectId,
                session.MessageContext.Telemetry);
        }

        /// <summary>The session used for all service calls.</summary>
        public ISession Session { get; }

        /// <summary>
        /// NodeId of the standard <c>UserManagement</c> object
        /// (typically <c>i=24290</c>).
        /// </summary>
        public NodeId ObjectId { get; }

        /// <summary>
        /// The underlying source-generated proxy. Exposed so advanced
        /// callers can reach the raw method wrappers when the
        /// high-level API does not suffice.
        /// </summary>
        public UserManagementTypeClient Proxy { get; }

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<UserManagementUser>> ListUsersAsync(
            CancellationToken cancellationToken = default)
        {
            // The Users property is a mandatory child of UserManagementType
            // (browse name 'Users'); resolve it once and read the value.
            NodeId usersId = await ResolveChildAsync(
                ObjectId,
                BrowseNames.Users,
                cancellationToken).ConfigureAwait(false);

            var ids = new[]
            {
                new ReadValueId
                {
                    NodeId = usersId,
                    AttributeId = Attributes.Value
                }
            };
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                ArrayOf.Wrapped(ids),
                cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse<ReadValueId, DataValue>(response.Results, ids);

            DataValue dv = response.Results[0];
            if (StatusCode.IsBad(dv.StatusCode))
            {
                throw new ServiceResultException(dv.StatusCode,
                    $"Read of UserManagement.Users failed: {dv.StatusCode}");
            }

            var users = new List<UserManagementUser>();
            if (dv.WrappedValue.TryGetStructure(out ArrayOf<UserManagementDataType> typed))
            {
                foreach (UserManagementDataType raw in typed)
                {
                    if (raw == null)
                    {
                        continue;
                    }
                    users.Add(new UserManagementUser(
                        raw.UserName ?? string.Empty,
                        (UserConfigurationMask)raw.UserConfiguration,
                        string.IsNullOrEmpty(raw.Description) ? null : raw.Description));
                }
            }
            return users;
        }

        /// <inheritdoc/>
        public async ValueTask AddUserAsync(
            string userName,
            string password,
            UserConfigurationMask userConfiguration = 0,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName is required.", nameof(userName));
            }
            if (password == null)
            {
                throw new ArgumentNullException(nameof(password));
            }
            await Proxy.AddUserAsync(
                userName,
                password,
                (uint)userConfiguration,
                description ?? string.Empty,
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask ModifyUserAsync(
            string userName,
            string? newPassword = null,
            UserConfigurationMask? userConfiguration = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName is required.", nameof(userName));
            }
            bool modifyPassword = newPassword != null;
            bool modifyConfig = userConfiguration.HasValue;
            bool modifyDescription = description != null;
            await Proxy.ModifyUserAsync(
                userName,
                modifyPassword,
                newPassword ?? string.Empty,
                modifyConfig,
                (uint)(userConfiguration ?? 0),
                modifyDescription,
                description ?? string.Empty,
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveUserAsync(
            string userName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName is required.", nameof(userName));
            }
            await Proxy.RemoveUserAsync(userName, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask ChangePasswordAsync(
            string oldPassword,
            string newPassword,
            CancellationToken cancellationToken = default)
        {
            if (oldPassword == null)
            {
                throw new ArgumentNullException(nameof(oldPassword));
            }
            if (newPassword == null)
            {
                throw new ArgumentNullException(nameof(newPassword));
            }
            await Proxy.ChangePasswordAsync(
                oldPassword,
                newPassword,
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<Range> ReadPasswordLengthAsync(
            CancellationToken cancellationToken = default)
        {
            Variant value = await ReadPropertyAsync(
                BrowseNames.PasswordLength, cancellationToken).ConfigureAwait(false);
#pragma warning disable CS8600 // Variant.TryGetStructure returns null on miss; we check the bool.
            if (value.TryGetStructure(out Range range) && range != null)
#pragma warning restore CS8600
            {
                return range;
            }
            return new Range();
        }

        /// <inheritdoc/>
        public async ValueTask<PasswordOptionsMask> ReadPasswordOptionsAsync(
            CancellationToken cancellationToken = default)
        {
            Variant value = await ReadPropertyAsync(
                BrowseNames.PasswordOptions, cancellationToken).ConfigureAwait(false);
            if (value.TryGetValue(out uint raw))
            {
                return (PasswordOptionsMask)raw;
            }
            return 0;
        }

        /// <inheritdoc/>
        public async ValueTask<LocalizedText?> ReadPasswordRestrictionsAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                Variant value = await ReadPropertyAsync(
                    BrowseNames.PasswordRestrictions, cancellationToken).ConfigureAwait(false);
                if (value.TryGetValue(out LocalizedText lt) && !lt.IsNull)
                {
                    return lt;
                }
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNotFound)
            {
                // PasswordRestrictions is Optional per Part 18 §5.2.2.
                return null;
            }
            return null;
        }

        // ----- helpers -----

        private async ValueTask<NodeId> ResolveChildAsync(
            NodeId parentId,
            string browseName,
            CancellationToken cancellationToken)
        {
            var browsePaths = new[]
            {
                new BrowsePath
                {
                    StartingNode = parentId,
                    RelativePath = new RelativePath(new QualifiedName(browseName))
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    ArrayOf.Wrapped(browsePaths),
                    cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse<BrowsePath, BrowsePathResult>(response.Results, browsePaths);

            BrowsePathResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode,
                    $"Cannot resolve {browseName} on {parentId}: {result.StatusCode}");
            }
            if (result.Targets.Count == 0 || result.Targets[0].TargetId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound,
                    $"Child '{browseName}' not found on {parentId}.");
            }
            return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);
        }

        private async ValueTask<Variant> ReadPropertyAsync(
            string browseName,
            CancellationToken cancellationToken)
        {
            NodeId propertyId = await ResolveChildAsync(
                ObjectId, browseName, cancellationToken).ConfigureAwait(false);
            var ids = new[]
            {
                new ReadValueId
                {
                    NodeId = propertyId,
                    AttributeId = Attributes.Value
                }
            };
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                ArrayOf.Wrapped(ids),
                cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse<ReadValueId, DataValue>(response.Results, ids);

            DataValue dv = response.Results[0];
            if (StatusCode.IsBad(dv.StatusCode))
            {
                throw new ServiceResultException(dv.StatusCode,
                    $"Read of {browseName} failed: {dv.StatusCode}");
            }
            return dv.WrappedValue;
        }
    }
}

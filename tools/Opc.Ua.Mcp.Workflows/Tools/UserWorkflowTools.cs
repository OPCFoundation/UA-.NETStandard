/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Client.UserManagement;
using Descriptions = Opc.Ua.Mcp.WorkflowParameterDescriptions;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Part 18 user workflows with secret references instead of plaintext tool parameters.
    /// </summary>
    [McpServerToolType]
    public sealed class UserWorkflowTools
    {
        /// <summary>
        /// Creates tools using the host-selected secret gateway.
        /// </summary>
        public UserWorkflowTools(
            OpcUaSessionManager sessions, WorkflowOptions options, McpSecretReferences secrets,
            IWorkflowSessionFactory? scopes = null)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            m_scopes = scopes ?? new WorkflowSessionFactory();
            options.Validate();
        }

        /// <summary>
        /// Reads the password policy without treating optional absence as a default policy.
        /// </summary>
        [McpServerTool(Name = "users_policy", ReadOnly = true)]
        [Description("Read UserManagement password length/options/restrictions. Requires host IdentityAdministration " +
            "and an encrypted, caller-granted session. Returns quality/optional absence, never passwords. " +
            "Example: sessionName='plant'. Server permissions remain authoritative.")]
        public Task<CallToolResult> PolicyAsync(
            [Description(Descriptions.SessionName)] string? sessionName = null, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                    m_sessions, m_scopes, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                var client = new UserManagementClient(session);
                return await WorkflowJson.PropertiesAsync(session, client.ObjectId,
                    [
                        new QualifiedName(BrowseNames.PasswordLength), new QualifiedName(BrowseNames.PasswordOptions),
                        new QualifiedName(BrowseNames.PasswordRestrictions)
                    ], token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads a server-side index range rather than materializing the whole Users array.
        /// </summary>
        [McpServerTool(Name = "users_list", ReadOnly = true)]
        [Description("Read a bounded Users property range. Returns public names/configuration/descriptions, quality " +
            "and live nextOffset; no password. Example: maxResults=25. Servers lacking index-range support return " +
            "their actual status; no unbounded fallback. Requires IdentityAdministration and an encrypted session.")]
        public Task<CallToolResult> ListAsync(
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.Offset)] int offset = 0,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults, offset);
                IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                    m_sessions, m_scopes, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                var client = new UserManagementClient(session);
                NodeId node = await WorkflowJson.ChildAsync(
                    session, client.ObjectId, BrowseNames.Users, token).ConfigureAwait(false);
                ArrayOf<ReadValueId> requests =
                [
                    new ReadValueId
                    {
                        NodeId = node, AttributeId = Attributes.Value,
                        IndexRange = string.Create(CultureInfo.InvariantCulture, $"{offset}:{offset + maxResults}")
                    }
                ];
                ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Both, requests, token)
                    .ConfigureAwait(false);
                ClientBase.ValidateResponse(read.Results, requests);
                DataValue value = read.Results[0];
                var items = new JsonArray();
                bool more = false;
                if (!StatusCode.IsBad(value.StatusCode))
                {
                    if (value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> raw) && raw.Count > maxResults + 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The server did not honor the bounded Users index range.");
                    }
                    if (!value.WrappedValue.TryGetStructure(out ArrayOf<UserManagementDataType> users))
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError,
                            "The Users property did not contain the standard user records.");
                    }
                    if (users.Count > maxResults + 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The server did not honor the bounded Users index range.");
                    }
                    more = users.Count > maxResults;
                    for (int index = 0; index < Math.Min(users.Count, maxResults); index++)
                    {
                        items.Add(WorkflowJson.Encode(users[index], session.MessageContext));
                    }
                }
                return new JsonObject
                {
                    ["error"] = StatusCode.IsBad(value.StatusCode) &&
                        value.StatusCode != StatusCodes.BadIndexRangeNoData,
                    ["statusCode"] = value.StatusCode.ToString(),
                    ["statusCodeValue"] = value.StatusCode.Code,
                    ["sourceTimestamp"] = value.SourceTimestamp.ToDateTime()
                        .ToString("O", CultureInfo.InvariantCulture),
                    ["serverTimestamp"] = value.ServerTimestamp.ToDateTime()
                        .ToString("O", CultureInfo.InvariantCulture),
                    ["items"] = items,
                    ["offset"] = offset,
                    ["nextOffset"] = more ? offset + items.Count : null,
                    ["hasMore"] = more,
                    ["consistency"] = "live"
                };
            }, ct);
        }

        /// <summary>
        /// Adds one user with an authorized password reference.
        /// </summary>
        [McpServerTool(Name = "users_add", Destructive = true)]
        [Description("Add one user using a host-authorized secret reference, never a password argument. Requires " +
            "IdentityAdministration and SignAndEncrypt; server SecurityAdmin is authoritative. " +
            "Example: userName='alice', passwordReference='alias:initial-password'. Returns status only.")]
        public Task<CallToolResult> AddAsync(
            [Description(Descriptions.UserName)] string userName,
            [Description(Descriptions.PasswordReference)] string passwordReference,
            [Description(Descriptions.UserConfiguration)] uint userConfiguration = 0,
            [Description(Descriptions.UserDescription)] string? description = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SecretOperationAsync(async token =>
            {
                ValidateUser(userName, description);
                IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                    m_sessions, m_scopes, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                var client = new UserManagementClient(info.Session);
                using ISecret password = await PasswordAsync(passwordReference, info, client.ObjectId, token)
                    .ConfigureAwait(false);
                CallMethodResult call = await client.Proxy.CallWithResultAsync(MethodIds.UserManagementType_AddUser,
                    [
                        Variant.From(userName), Variant.From(Password(password)), Variant.From(userConfiguration),
                        Variant.From(description ?? string.Empty)
                    ], token).ConfigureAwait(false);
                return Result(info.Session, client.ObjectId, call);
            }, ct);
        }

        /// <summary>
        /// Modifies only supplied fields, distinguishing omission from explicit clearing.
        /// </summary>
        [McpServerTool(Name = "users_modify", Destructive = true)]
        [Description("Modify one user. Omit passwordReference/configuration/description to leave it unchanged; " +
            "description='' explicitly clears it and configuration=0 is an explicit write. No fake atomic CAS. " +
            "Example: userName='alice', description='Maintenance'. Returns actual status; sessions may close.")]
        public Task<CallToolResult> ModifyAsync(
            [Description(Descriptions.UserName)] string userName,
            [Description(Descriptions.NewPasswordReference)] string? passwordReference = null,
            [Description(Descriptions.UserConfiguration)] uint? userConfiguration = null,
            [Description(Descriptions.UserDescription)] string? description = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SecretOperationAsync(async token =>
            {
                ValidateUser(userName, description);
                if (passwordReference is null && userConfiguration is null && description is null)
                {
                    throw new ArgumentException("At least one explicit user field must be supplied.");
                }
                IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                    m_sessions, m_scopes, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                var client = new UserManagementClient(info.Session);
                using ISecret? password = passwordReference is null ? null :
                    await PasswordAsync(passwordReference, info, client.ObjectId, token).ConfigureAwait(false);
                CallMethodResult call = await client.Proxy.CallWithResultAsync(MethodIds.UserManagementType_ModifyUser,
                    [
                        Variant.From(userName), Variant.From(password is not null),
                        Variant.From(password is null ? string.Empty : Password(password)),
                        Variant.From(userConfiguration.HasValue), Variant.From(userConfiguration ?? 0),
                        Variant.From(description is not null), Variant.From(description ?? string.Empty)
                    ], token).ConfigureAwait(false);
                return Result(info.Session, client.ObjectId, call);
            }, ct);
        }

        /// <summary>
        /// Removes one selected user without overriding reserved or self-removal restrictions.
        /// </summary>
        [McpServerTool(Name = "users_remove", Destructive = true)]
        [Description("Remove one user; reserved/self-lockout refusals remain server decisions. Requires explicit " +
            "identity administration. Example: userName='alice'. Returns actual status, with no automatic retry.")]
        public Task<CallToolResult> RemoveAsync(
            [Description(Descriptions.UserName)] string userName,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SecretOperationAsync(async token =>
            {
                ValidateUser(userName, null);
                IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                    m_sessions, m_scopes, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                var client = new UserManagementClient(info.Session);
                CallMethodResult call = await client.Proxy.CallWithResultAsync(MethodIds.UserManagementType_RemoveUser,
                    [Variant.From(userName)], token).ConfigureAwait(false);
                return Result(info.Session, client.ObjectId, call);
            }, ct);
        }

        /// <summary>
        /// Changes the currently authenticated user's password, without accepting a claimed user name.
        /// </summary>
        [McpServerTool(Name = "users_change_password", Destructive = true)]
        [Description("Change only the UA session's authenticated user's password using old/new secret references. " +
            "The caller cannot supply another user's name. Example: oldPasswordReference='alias:current', " +
            "newPasswordReference='alias:replacement'. Returns committed status even if the session then closes.")]
        public Task<CallToolResult> ChangePasswordAsync(
            [Description(Descriptions.OldPasswordReference)] string oldPasswordReference,
            [Description(Descriptions.ReplacementPasswordReference)] string newPasswordReference,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SecretOperationAsync(async token =>
            {
                IWorkflowSessionScope scope = await WorkflowJson.IdentityScopeAsync(
                    m_sessions, m_scopes, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                var client = new UserManagementClient(info.Session);
                using ISecret previous = await PasswordAsync(oldPasswordReference, info, client.ObjectId, token)
                    .ConfigureAwait(false);
                using ISecret replacement = await PasswordAsync(newPasswordReference, info, client.ObjectId, token)
                    .ConfigureAwait(false);
                CallMethodResult call = await client.Proxy.CallWithResultAsync(
                    MethodIds.UserManagementType_ChangePassword,
                    [Variant.From(Password(previous)), Variant.From(Password(replacement))], token)
                    .ConfigureAwait(false);
                return Result(info.Session, client.ObjectId, call);
            }, ct);
        }

        /// <summary>
        /// Resolves the exact session/server/model resource binding of an authorized alias.
        /// </summary>
        private ValueTask<ISecret> PasswordAsync(
            string reference, OpcUaSessionManager.SessionInfo info, NodeId node, CancellationToken ct)
        {
            return m_secrets.ResolveAsync(reference, McpSecretReferences.GetResourceBinding(info, node),
                McpCapability.IdentityAdministration, ct);
        }

        /// <summary>
        /// Decodes only bounded, valid UTF-8; error messages never contain the secret.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private string Password(ISecret secret)
        {
            if (secret.Bytes.Length > m_options.MaxInlineBytes)
            {
                throw new ArgumentException("The password exceeds the host's secret-size ceiling.");
            }
            try
            {
                return s_utf8.GetString(secret.Bytes);
            }
            catch (DecoderFallbackException)
            {
                throw new ArgumentException("The referenced password is not valid UTF-8.");
            }
        }

        /// <summary>
        /// Omits unexpected output arguments because even a malformed server must not reflect private material.
        /// </summary>
        private static JsonObject Result(ISession session, NodeId node, CallMethodResult call)
        {
            JsonObject result = WorkflowJson.Method(session, node, call, includeOutputs: false);
            result["sessionMayClose"] = !StatusCode.IsBad(call.StatusCode);
            return result;
        }

        /// <summary>
        /// Checks only public fields before touching secrets.
        /// </summary>
        private static void ValidateUser(string userName, string? description)
        {
            WorkflowJson.Text(userName, 256);
            if (description is not null)
            {
                WorkflowJson.Text(description, 4096, empty: true);
            }
        }

        /// <summary>
        /// Removes provider and server messages on secret-bearing paths while retaining exact service codes.
        /// </summary>
        private static Task<CallToolResult> SecretOperationAsync(
            Func<CancellationToken, ValueTask<JsonNode?>> operation, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                try
                {
                    return await operation(token).ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    throw new ServiceResultException(exception.StatusCode,
                        "The user operation was refused or failed; no secret material is included.");
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                    System.IO.IOException or UnauthorizedAccessException or FormatException)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "The user operation failed input, secret-reference or provider validation.");
                }
            }, ct);
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly WorkflowOptions m_options;
        private readonly McpSecretReferences m_secrets;
        private readonly IWorkflowSessionFactory m_scopes;
        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}

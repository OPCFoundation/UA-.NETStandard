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
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Identity criteria types as defined in OPC UA Part 3.
    /// </summary>
    public enum IdentityCriteriaType
    {
        /// <summary>Match by user name.</summary>
        UserName = 1,

        /// <summary>Match by certificate thumbprint.</summary>
        Thumbprint = 2,

        /// <summary>Match by role.</summary>
        Role = 3,

        /// <summary>Match by group identifier.</summary>
        GroupId = 4,

        /// <summary>Match anonymous users.</summary>
        Anonymous = 5,

        /// <summary>Match any authenticated user.</summary>
        AuthenticatedUser = 6,

        /// <summary>Match by application URI.</summary>
        Application = 7
    }

    /// <summary>
    /// Holds per-role identity, application, and endpoint configuration.
    /// </summary>
    public sealed class RoleConfiguration
    {
        /// <summary>
        /// Gets the list of identity mappings for this role.
        /// Each tuple contains (CriteriaType, Criteria).
        /// </summary>
        public List<(int CriteriaType, string Criteria)> Identities { get; } = new();

        /// <summary>
        /// Gets the list of application URIs associated with this role.
        /// </summary>
        public List<string> Applications { get; } = new();

        /// <summary>
        /// Gets or sets a value indicating whether the application list
        /// is an exclusion list.
        /// </summary>
        public bool ApplicationsExclude { get; set; }

        /// <summary>
        /// Gets the list of endpoint URLs associated with this role.
        /// </summary>
        public List<string> Endpoints { get; } = new();

        /// <summary>
        /// Gets or sets a value indicating whether the endpoint list
        /// is an exclusion list.
        /// </summary>
        public bool EndpointsExclude { get; set; }
    }

    /// <summary>
    /// Manages OPC UA role management method call handling for
    /// well-known roles. The node creation is handled by
    /// <see cref="RoleManagementNodeManager"/>; this class only
    /// contains the business logic for method calls and property
    /// updates.
    /// </summary>
    public sealed class RoleManagementHandler : IDisposable
    {
        private readonly IServerInternal _server;
        private readonly ILogger _logger;
        private readonly Dictionary<NodeId, RoleConfiguration> _roleConfigs;
        private Dictionary<NodeId, NodeId> _methodToRole;
        private Dictionary<NodeId, Dictionary<string, BaseVariableState>> _properties;
        private readonly SemaphoreSlim _lock;
        private bool _disposed;

        internal static readonly NodeId[] WellKnownRoles =
        [
            ObjectIds.WellKnownRole_Anonymous,
            ObjectIds.WellKnownRole_AuthenticatedUser,
            ObjectIds.WellKnownRole_Observer,
            ObjectIds.WellKnownRole_Operator,
            ObjectIds.WellKnownRole_Engineer,
            ObjectIds.WellKnownRole_Supervisor,
            ObjectIds.WellKnownRole_ConfigureAdmin,
            ObjectIds.WellKnownRole_SecurityAdmin,
        ];

        /// <summary>
        /// Initializes a new instance of the <see cref="RoleManagementHandler"/> class.
        /// </summary>
        /// <param name="server">The server internal interface.</param>
        /// <param name="telemetry">The telemetry context for logging.</param>
        public RoleManagementHandler(
            IServerInternal server,
            ITelemetryContext telemetry)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _logger = telemetry.CreateLogger<RoleManagementHandler>();
            _roleConfigs = new Dictionary<NodeId, RoleConfiguration>();
            _methodToRole = new Dictionary<NodeId, NodeId>();
            _properties = new Dictionary<NodeId, Dictionary<string, BaseVariableState>>();
            _lock = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// Gets the role configuration for the specified role node identifier.
        /// Creates a new configuration if one does not already exist.
        /// </summary>
        /// <param name="roleId">The node identifier of the role.</param>
        /// <returns>The role configuration.</returns>
        public RoleConfiguration GetRoleConfiguration(NodeId roleId)
        {
            _lock.Wait();
            try
            {
                if (!_roleConfigs.TryGetValue(roleId, out var config))
                {
                    config = new RoleConfiguration();
                    _roleConfigs[roleId] = config;
                }

                return config;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Called by <see cref="RoleManagementNodeManager"/> after it
        /// creates the address space nodes. Receives the property and
        /// method-to-role maps so this handler can update property
        /// values and resolve method calls.
        /// </summary>
        /// <param name="properties">
        /// Per-role dictionary of property browse-name to variable state.
        /// </param>
        /// <param name="methodToRole">
        /// Map from method NodeId to the owning role NodeId.
        /// </param>
        public void Initialize(
            Dictionary<NodeId, Dictionary<string, BaseVariableState>> properties,
            Dictionary<NodeId, NodeId> methodToRole)
        {
            _properties = properties
                ?? throw new ArgumentNullException(nameof(properties));
            _methodToRole = methodToRole
                ?? throw new ArgumentNullException(nameof(methodToRole));

            InitializeDefaultConfigurations();

            _logger.LogInformation(
                Utils.TraceMasks.StartStop,
                "Role management handler initialized with {Count} roles " +
                "and {MethodCount} method handlers.",
                _roleConfigs.Count,
                _methodToRole.Count);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_disposed)
            {
                _lock.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// Handles a role management method call. This is the
        /// <see cref="GenericMethodCalledEventHandler2"/> callback
        /// registered by the node manager.
        /// </summary>
        public ServiceResult OnRoleMethodCalled(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            NodeId roleId = ResolveRoleId(method.NodeId, objectId);
            if (roleId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadMethodInvalid);
            }

            var accessResult = RequireSecurityAdmin(context);
            if (StatusCode.IsBad(accessResult.StatusCode))
            {
                return accessResult;
            }

            string methodName = method.BrowseName.Name;

            return methodName switch
            {
                BrowseNames.AddIdentity =>
                    HandleAddIdentity(roleId, inputArguments),
                BrowseNames.RemoveIdentity =>
                    HandleRemoveIdentity(roleId, inputArguments),
                BrowseNames.AddApplication =>
                    HandleAddApplication(roleId, inputArguments),
                BrowseNames.RemoveApplication =>
                    HandleRemoveApplication(roleId, inputArguments),
                BrowseNames.AddEndpoint =>
                    HandleAddEndpoint(roleId, inputArguments),
                BrowseNames.RemoveEndpoint =>
                    HandleRemoveEndpoint(roleId, inputArguments),
                _ => new ServiceResult(StatusCodes.BadMethodInvalid),
            };
        }

        private void InitializeDefaultConfigurations()
        {
            foreach (var roleId in WellKnownRoles)
            {
                _roleConfigs[roleId] = new RoleConfiguration();
            }

            _roleConfigs[ObjectIds.WellKnownRole_Anonymous].Identities.Add(
                ((int)IdentityCriteriaType.Anonymous, "Anonymous"));

            _roleConfigs[ObjectIds.WellKnownRole_AuthenticatedUser].Identities.Add(
                ((int)IdentityCriteriaType.AuthenticatedUser, "AuthenticatedUser"));
        }

        private NodeId ResolveRoleId(NodeId methodId, NodeId objectId)
        {
            if (_methodToRole.TryGetValue(methodId, out var roleId))
            {
                return roleId;
            }

            foreach (var knownRoleId in WellKnownRoles)
            {
                if (knownRoleId == objectId)
                {
                    return objectId;
                }
            }

            return NodeId.Null;
        }

        private static ServiceResult RequireSecurityAdmin(ISystemContext context)
        {
            if (context is ISessionSystemContext sessionContext)
            {
                var identity = sessionContext.UserIdentity;
                if (identity?.GrantedRoleIds.Contains(
                    ObjectIds.WellKnownRole_SecurityAdmin) == true)
                {
                    return ServiceResult.Good;
                }
            }

            return new ServiceResult(
                StatusCodes.BadUserAccessDenied,
                new LocalizedText(
                    "SecurityAdmin role is required for role management."));
        }

        #region Identity Methods
        private ServiceResult HandleAddIdentity(
            NodeId roleId,
            ArrayOf<Variant> inputArguments)
        {
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(StatusCodes.BadArgumentsMissing);
            }

            if (!TryExtractIdentityRule(
                inputArguments[0],
                out int criteriaType,
                out string criteria))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            if (string.IsNullOrEmpty(criteria))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            _lock.Wait();
            try
            {
                var config = GetRoleConfigurationUnsafe(roleId);
                if (config.Identities.Any(
                    i => i.CriteriaType == criteriaType &&
                         i.Criteria == criteria))
                {
                    return ServiceResult.Good;
                }

                config.Identities.Add((criteriaType, criteria));
                UpdateIdentitiesProperty(roleId, config);
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation(
                "AddIdentity: type={CriteriaType} criteria={Criteria} " +
                "role={RoleId}.",
                criteriaType,
                criteria,
                roleId);

            return ServiceResult.Good;
        }

        private ServiceResult HandleRemoveIdentity(
            NodeId roleId,
            ArrayOf<Variant> inputArguments)
        {
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(StatusCodes.BadArgumentsMissing);
            }

            if (!TryExtractIdentityRule(
                inputArguments[0],
                out int criteriaType,
                out string criteria))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            _lock.Wait();
            try
            {
                var config = GetRoleConfigurationUnsafe(roleId);
                int removed = config.Identities.RemoveAll(
                    i => i.CriteriaType == criteriaType &&
                         i.Criteria == criteria);

                if (removed == 0)
                {
                    return new ServiceResult(StatusCodes.BadNoMatch);
                }

                UpdateIdentitiesProperty(roleId, config);
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation(
                "RemoveIdentity: type={CriteriaType} criteria={Criteria} " +
                "role={RoleId}.",
                criteriaType,
                criteria,
                roleId);

            return ServiceResult.Good;
        }
        #endregion

        #region Application Methods
        private ServiceResult HandleAddApplication(
            NodeId roleId,
            ArrayOf<Variant> inputArguments)
        {
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(StatusCodes.BadArgumentsMissing);
            }

            if (!inputArguments[0].TryGetValue(out string applicationUri) ||
                string.IsNullOrEmpty(applicationUri))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            _lock.Wait();
            try
            {
                var config = GetRoleConfigurationUnsafe(roleId);
                if (!config.Applications.Contains(applicationUri))
                {
                    config.Applications.Add(applicationUri);
                    UpdateApplicationsProperty(roleId, config);
                }
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation(
                "AddApplication: uri={ApplicationUri} role={RoleId}.",
                applicationUri,
                roleId);

            return ServiceResult.Good;
        }

        private ServiceResult HandleRemoveApplication(
            NodeId roleId,
            ArrayOf<Variant> inputArguments)
        {
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(StatusCodes.BadArgumentsMissing);
            }

            if (!inputArguments[0].TryGetValue(out string applicationUri) ||
                string.IsNullOrEmpty(applicationUri))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            _lock.Wait();
            try
            {
                var config = GetRoleConfigurationUnsafe(roleId);
                if (!config.Applications.Remove(applicationUri))
                {
                    return new ServiceResult(StatusCodes.BadNoMatch);
                }

                UpdateApplicationsProperty(roleId, config);
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation(
                "RemoveApplication: uri={ApplicationUri} role={RoleId}.",
                applicationUri,
                roleId);

            return ServiceResult.Good;
        }
        #endregion

        #region Endpoint Methods
        private ServiceResult HandleAddEndpoint(
            NodeId roleId,
            ArrayOf<Variant> inputArguments)
        {
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(StatusCodes.BadArgumentsMissing);
            }

            string endpointUrl = ExtractEndpointUrl(inputArguments[0]);
            if (string.IsNullOrEmpty(endpointUrl))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            _lock.Wait();
            try
            {
                var config = GetRoleConfigurationUnsafe(roleId);
                if (!config.Endpoints.Contains(endpointUrl))
                {
                    config.Endpoints.Add(endpointUrl);
                    UpdateEndpointsProperty(roleId, config);
                }
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation(
                "AddEndpoint: url={EndpointUrl} role={RoleId}.",
                endpointUrl,
                roleId);

            return ServiceResult.Good;
        }

        private ServiceResult HandleRemoveEndpoint(
            NodeId roleId,
            ArrayOf<Variant> inputArguments)
        {
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(StatusCodes.BadArgumentsMissing);
            }

            string endpointUrl = ExtractEndpointUrl(inputArguments[0]);
            if (string.IsNullOrEmpty(endpointUrl))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            _lock.Wait();
            try
            {
                var config = GetRoleConfigurationUnsafe(roleId);
                if (!config.Endpoints.Remove(endpointUrl))
                {
                    return new ServiceResult(StatusCodes.BadNoMatch);
                }

                UpdateEndpointsProperty(roleId, config);
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation(
                "RemoveEndpoint: url={EndpointUrl} role={RoleId}.",
                endpointUrl,
                roleId);

            return ServiceResult.Good;
        }
        #endregion

        #region Decoding Helpers
        private bool TryExtractIdentityRule(
            Variant variant,
            out int criteriaType,
            out string criteria)
        {
            criteriaType = 0;
            criteria = null;

            if (variant.TryGetValue(out ExtensionObject extensionObject))
            {
                return TryDecodeFromExtensionObject(
                    extensionObject, out criteriaType, out criteria);
            }

            return false;
        }

        private bool TryDecodeFromExtensionObject(
            ExtensionObject extensionObject,
            out int criteriaType,
            out string criteria)
        {
            criteriaType = 0;
            criteria = null;

            if (extensionObject.TryGetValue(out IEncodeable encodeable))
            {
                return TryDecodeFromEncodeable(
                    encodeable, out criteriaType, out criteria);
            }

            if (extensionObject.TryGetAsBinary(out ByteString binary))
            {
                return TryDecodeFromBinary(
                    binary.ToArray(), out criteriaType, out criteria);
            }

            return false;
        }

        private static bool TryDecodeFromEncodeable(
            IEncodeable encodeable,
            out int criteriaType,
            out string criteria)
        {
            criteriaType = 0;
            criteria = null;

            try
            {
                var type = encodeable.GetType();
                var ctProp = type.GetProperty("CriteriaType");
                var cProp = type.GetProperty("Criteria");

                if (ctProp != null && cProp != null)
                {
                    criteriaType = Convert.ToInt32(ctProp.GetValue(encodeable));
                    criteria = cProp.GetValue(encodeable) as string;
                    return criteria != null;
                }
            }
            catch (Exception ex) when (
                ex is FormatException or
                OverflowException or
                InvalidCastException or
                System.Reflection.TargetInvocationException)
            {
                // Fall through to return false.
            }

            return false;
        }

        private bool TryDecodeFromBinary(
            byte[] body,
            out int criteriaType,
            out string criteria)
        {
            criteriaType = 0;
            criteria = null;

            try
            {
                using var decoder = new BinaryDecoder(
                    body, _server.MessageContext);
                criteriaType = decoder.ReadInt32("CriteriaType");
                criteria = decoder.ReadString("Criteria");
                return true;
            }
            catch (Exception ex) when (
                ex is ServiceResultException or
                FormatException or
                InvalidOperationException)
            {
                return false;
            }
        }

        private static string ExtractEndpointUrl(Variant variant)
        {
            if (variant.TryGetValue(out string url))
            {
                return url;
            }

            if (variant.TryGetValue(out ExtensionObject ext) &&
                ext.TryGetValue(out IEncodeable encodeable))
            {
                try
                {
                    var urlProp = encodeable.GetType()
                        .GetProperty("EndpointUrl");
                    return urlProp?.GetValue(encodeable) as string;
                }
                catch (Exception ex) when (
                    ex is InvalidCastException or
                    System.Reflection.TargetInvocationException)
                {
                    return null;
                }
            }

            return null;
        }
        #endregion

        #region Property Updates
        private void UpdateIdentitiesProperty(
            NodeId roleId,
            RoleConfiguration config)
        {
            if (!_properties.TryGetValue(roleId, out var props) ||
                !props.TryGetValue("Identities", out var node))
            {
                return;
            }

            var identityStrings = config.Identities
                .Select(i =>
                    $"{(IdentityCriteriaType)i.CriteriaType}:{i.Criteria}")
                .ToArray();

            node.Value = new Variant(
                (ArrayOf<string>)identityStrings);
            node.ClearChangeMasks(_server.DefaultSystemContext, true);
        }

        private void UpdateApplicationsProperty(
            NodeId roleId,
            RoleConfiguration config)
        {
            if (!_properties.TryGetValue(roleId, out var props))
            {
                return;
            }

            if (props.TryGetValue("Applications", out var appNode))
            {
                appNode.Value = new Variant(
                    (ArrayOf<string>)config.Applications.ToArray());
                appNode.ClearChangeMasks(
                    _server.DefaultSystemContext, true);
            }

            if (props.TryGetValue("ApplicationsExclude", out var exclNode))
            {
                exclNode.Value = new Variant(config.ApplicationsExclude);
                exclNode.ClearChangeMasks(
                    _server.DefaultSystemContext, true);
            }
        }

        private void UpdateEndpointsProperty(
            NodeId roleId,
            RoleConfiguration config)
        {
            if (!_properties.TryGetValue(roleId, out var props))
            {
                return;
            }

            if (props.TryGetValue("Endpoints", out var epNode))
            {
                epNode.Value = new Variant(
                    (ArrayOf<string>)config.Endpoints.ToArray());
                epNode.ClearChangeMasks(
                    _server.DefaultSystemContext, true);
            }

            if (props.TryGetValue("EndpointsExclude", out var exclNode))
            {
                exclNode.Value = new Variant(config.EndpointsExclude);
                exclNode.ClearChangeMasks(
                    _server.DefaultSystemContext, true);
            }
        }
        #endregion

        /// <summary>
        /// Gets the role configuration without acquiring the lock.
        /// Caller must hold <see cref="_lock"/>.
        /// </summary>
        private RoleConfiguration GetRoleConfigurationUnsafe(NodeId roleId)
        {
            if (!_roleConfigs.TryGetValue(roleId, out var config))
            {
                config = new RoleConfiguration();
                _roleConfigs[roleId] = config;
            }

            return config;
        }
    }

    /// <summary>
    /// A <see cref="CustomNodeManager2"/> that registers role management
    /// method and property nodes in namespace 0 using external references
    /// so that the MasterNodeManager correctly routes Browse and Call
    /// requests.
    /// </summary>
    public sealed class RoleManagementNodeManager : CustomNodeManager2
    {
        private readonly RoleManagementHandler _handler;
        private readonly Dictionary<NodeId, NodeId> _methodToRole;
        private readonly Dictionary<NodeId, Dictionary<string, BaseVariableState>> _properties;

        private static readonly (string Name, uint Offset)[] s_methodDefs =
        [
            (BrowseNames.AddIdentity, 100u),
            (BrowseNames.RemoveIdentity, 101u),
            (BrowseNames.AddApplication, 102u),
            (BrowseNames.RemoveApplication, 103u),
            (BrowseNames.AddEndpoint, 104u),
            (BrowseNames.RemoveEndpoint, 105u),
        ];

        private static readonly (string Name, uint Offset, NodeId DataType, int ValueRank, Variant Default)[] s_propertyDefs =
        [
            ("Identities", 200u, DataTypeIds.Structure, ValueRanks.OneDimension, new Variant(Array.Empty<string>())),
            ("Applications", 201u, DataTypeIds.String, ValueRanks.OneDimension, new Variant(Array.Empty<string>())),
            ("ApplicationsExclude", 202u, DataTypeIds.Boolean, ValueRanks.Scalar, new Variant(false)),
            ("Endpoints", 203u, DataTypeIds.String, ValueRanks.OneDimension, new Variant(Array.Empty<string>())),
            ("EndpointsExclude", 204u, DataTypeIds.Boolean, ValueRanks.Scalar, new Variant(false)),
        ];

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="RoleManagementNodeManager"/> class.
        /// </summary>
        /// <param name="server">The server internal interface.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="handler">
        /// The handler that owns the business logic for role method calls.
        /// </param>
        public RoleManagementNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            RoleManagementHandler handler)
            : base(server, configuration,
                   new string[] { Opc.Ua.Namespaces.OpcUa })
        {
            _handler = handler
                ?? throw new ArgumentNullException(nameof(handler));
            _methodToRole = new Dictionary<NodeId, NodeId>();
            _properties = new Dictionary<NodeId, Dictionary<string, BaseVariableState>>();
        }

        /// <summary>
        /// Creates the address space by adding method and property nodes
        /// for each well-known role and registering external references
        /// back to the role objects owned by the CoreNodeManager.
        /// </summary>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            foreach (var roleId in RoleManagementHandler.WellKnownRoles)
            {
                if (!roleId.TryGetValue(out uint baseId))
                {
                    continue;
                }

                var roleProps = new Dictionary<string, BaseVariableState>();

                foreach (var (name, offset) in s_methodDefs)
                {
                    var methodId = new NodeId(baseId + offset);
                    var method = new MethodState(null)
                    {
                        NodeId = methodId,
                        BrowseName = new QualifiedName(name),
                        DisplayName = LocalizedText.From(name),
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        Executable = true,
                        UserExecutable = true,
                        OnCallMethod2 =
                            new GenericMethodCalledEventHandler2(
                                _handler.OnRoleMethodCalled)
                    };

                    CreateInputArguments(method, name, baseId + offset);

                    AddExternalReference(
                        roleId,
                        ReferenceTypeIds.HasComponent,
                        false,
                        methodId,
                        externalReferences);

                    AddPredefinedNode(SystemContext, method);
                    _methodToRole[methodId] = roleId;
                }

                foreach (var (name, offset, dataType, valueRank, defaultVal) in s_propertyDefs)
                {
                    var propId = new NodeId(baseId + offset);
                    var prop = new PropertyState(null)
                    {
                        NodeId = propId,
                        BrowseName = new QualifiedName(name),
                        DisplayName = LocalizedText.From(name),
                        DataType = dataType,
                        ValueRank = valueRank,
                        Value = defaultVal,
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        TypeDefinitionId = VariableTypeIds.PropertyType,
                    };

                    AddExternalReference(
                        roleId,
                        ReferenceTypeIds.HasProperty,
                        false,
                        propId,
                        externalReferences);

                    AddPredefinedNode(SystemContext, prop);
                    roleProps[name] = prop;
                }

                _properties[roleId] = roleProps;
            }

            _handler.Initialize(_properties, _methodToRole);
        }

        private static void CreateInputArguments(
            MethodState method,
            string methodName,
            uint methodNumericId)
        {
            string argName;
            NodeId argType;

            if (methodName.Contains("Identity"))
            {
                argName = "Rule";
                argType = DataTypeIds.Structure;
            }
            else if (methodName.Contains("Application"))
            {
                argName = "ApplicationUri";
                argType = DataTypeIds.String;
            }
            else if (methodName.Contains("Endpoint"))
            {
                argName = "Endpoint";
                argType = DataTypeIds.String;
            }
            else
            {
                return;
            }

            method.InputArguments =
                new PropertyState<ArrayOf<Argument>>
                    .Implementation<StructureBuilder<Argument>>(method)
                {
                    NodeId = new NodeId(methodNumericId + 1000u),
                    BrowseName = QualifiedName.From(
                        BrowseNames.InputArguments),
                    DisplayName = LocalizedText.From(
                        BrowseNames.InputArguments),
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    DataType = DataTypeIds.Argument,
                    ValueRank = ValueRanks.OneDimension,
                };

            method.InputArguments.Value = new Argument[]
            {
                new Argument
                {
                    Name = argName,
                    Description = LocalizedText.From(argName),
                    DataType = argType,
                    ValueRank = ValueRanks.Scalar
                }
            }.ToArrayOf();
        }
    }
}

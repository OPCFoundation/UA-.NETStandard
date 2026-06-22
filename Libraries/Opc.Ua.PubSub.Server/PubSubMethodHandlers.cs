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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Hosts the synchronous method-handler delegates the
    /// <see cref="PubSubNodeManager"/> attaches to the standard
    /// <c>PublishSubscribe</c> Method nodes (Part 14 §9.1.3,
    /// §9.1.10 and §8.3.1).
    /// </summary>
    /// <remarks>
    /// Implements the configuration-mutation entry-points
    /// via the mutable <see cref="IPubSubApplication"/> surface.
    /// All entry-points adhere to the legacy synchronous
    /// <c>GenericMethodCalledEventHandler</c> contract; every async
    /// call is forwarded via <c>.AsTask().GetAwaiter().GetResult()</c>
    /// — the single sanctioned sync-over-async bridge.
    /// </remarks>
    internal sealed class PubSubMethodHandlers
    {
        private const string DefaultSecurityPolicyUri =
            "http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes256-CTR";

        private readonly IPubSubApplication m_application;
        private readonly IPubSubKeyServiceServer? m_keyService;
        private readonly PubSubServerOptions m_options;
        private readonly SksMethodHandler? m_sks;
        private readonly PushSecurityKeyProvider[] m_pushProviders;
        private readonly ILogger m_logger;
        private readonly Dictionary<NodeId, string> m_securityGroupNodeIds = new();
        private readonly System.Threading.Lock m_gate = new();
        private uint m_nextSecurityGroupHandle;

        /// <summary>
        /// Creates a new <see cref="PubSubMethodHandlers"/>.
        /// </summary>
        /// <param name="application">Runtime application.</param>
        /// <param name="keyService">
        /// SKS server, or <see langword="null"/> when the host is
        /// not acting as an SKS.
        /// </param>
        /// <param name="options">PubSub server options.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="pushProviders">Optional SetSecurityKeys push providers.</param>
        public PubSubMethodHandlers(
            IPubSubApplication application,
            IPubSubKeyServiceServer? keyService,
            PubSubServerOptions options,
            ITelemetryContext telemetry,
            IEnumerable<PushSecurityKeyProvider>? pushProviders = null)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_application = application;
            m_keyService = keyService;
            m_options = options;
            m_sks = keyService is null ? null : new SksMethodHandler(keyService, telemetry);
            m_pushProviders = pushProviders?.ToArray() ?? Array.Empty<PushSecurityKeyProvider>();
            m_logger = telemetry.CreateLogger<PubSubMethodHandlers>();
        }

        /// <summary>
        /// Implements Part 14 §9.1.10.2 <c>Status.Enable</c>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="inputArguments">Input arguments (none).</param>
        /// <param name="outputArguments">Output arguments (none).</param>
        public ServiceResult OnEnable(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = inputArguments;
            _ = outputArguments;
            try
            {
                m_application.StartAsync().AsTask().GetAwaiter().GetResult();
                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "PublishSubscribe Enable failed.");
                return new ServiceResult(StatusCodes.BadInvalidState, new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.10.3 <c>Status.Disable</c>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="inputArguments">Input arguments (none).</param>
        /// <param name="outputArguments">Output arguments (none).</param>
        public ServiceResult OnDisable(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = inputArguments;
            _ = outputArguments;
            try
            {
                m_application.StopAsync().AsTask().GetAwaiter().GetResult();
                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "PublishSubscribe Disable failed.");
                return new ServiceResult(StatusCodes.BadInvalidState, new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.3.4 <c>AddConnection</c>.
        /// Delegates to
        /// <see cref="IPubSubApplication.AddConnectionAsync"/>.
        /// </summary>
        public ServiceResult OnAddConnection(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddConnection expects 1 input argument."));
            }
            if (!inputArguments[0].TryGetValue(out ExtensionObject ext))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddConnection argument 0 is not an ExtensionObject."));
            }
            if (!ext.TryGetValue(out PubSubConnectionDataType? cfg) || cfg is null)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddConnection argument 0 body is not a PubSubConnectionDataType."));
            }
            try
            {
                NodeId id = m_application.AddConnectionAsync(cfg)
                    .AsTask().GetAwaiter().GetResult();
                outputArguments.Add(Variant.From(id));
                return ServiceResult.Good;
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "AddConnection failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.3.5 <c>RemoveConnection</c>.
        /// Delegates to
        /// <see cref="IPubSubApplication.RemoveConnectionAsync"/>.
        /// </summary>
        public ServiceResult OnRemoveConnection(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveConnection expects 1 input argument."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId connectionId)
                || connectionId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveConnection argument 0 is not a valid NodeId."));
            }
            try
            {
                m_application.RemoveConnectionAsync(connectionId)
                    .AsTask().GetAwaiter().GetResult();
                return ServiceResult.Good;
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (ArgumentException aex)
            {
                return new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    new LocalizedText(aex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "RemoveConnection failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.6 <c>SetConfiguration</c>.
        /// </summary>
        public ServiceResult OnSetConfiguration(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("SetConfiguration expects 1 input argument."));
            }
            if (!inputArguments[0].TryGetValue(out ExtensionObject ext))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("SetConfiguration argument 0 is not an ExtensionObject."));
            }
            if (!ext.TryGetValue(out PubSubConfigurationDataType? cfg) || cfg is null)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "SetConfiguration argument 0 body is not a PubSubConfigurationDataType."));
            }
            try
            {
                ArrayOf<StatusCode> results = m_application
                    .ReplaceConfigurationAsync(cfg)
                    .AsTask().GetAwaiter().GetResult();
                outputArguments.Add(Variant.From([.. results]));
                return ServiceResult.Good;
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "SetConfiguration failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.6 <c>GetConfiguration</c>.
        /// </summary>
        public ServiceResult OnGetConfiguration(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = inputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            try
            {
                PubSubConfigurationDataType config = m_application.GetConfiguration();
                outputArguments.Add(Variant.From(new ExtensionObject(config)));
                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "GetConfiguration failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.4.5 <c>AddPublishedDataItems</c>.
        /// </summary>
        public ServiceResult OnAddPublishedDataItems(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 4)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedDataItems expects 4 input arguments."));
            }
            if (!inputArguments[0].TryGetValue(out string? name) || string.IsNullOrEmpty(name))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedDataItems argument 0 (Name) is missing or empty."));
            }
            string[] aliases = TryGetStringArray(inputArguments[1]);
            if (!TryGetEncodeableArray(inputArguments[3], context, out PublishedVariableDataType[] variables))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedDataItems argument 3 is not a PublishedVariableDataType array."));
            }
            PublishedDataSetDataType dataSet = CreatePublishedDataItemsDataSet(name, aliases, variables, null);
            return AddPublishedDataSet(dataSet, variables.Length, outputArguments, includeConfigurationVersion: true);
        }

        /// <summary>
        /// Implements Part 14 §9.1.4.5 <c>AddPublishedEvents</c>.
        /// </summary>
        public ServiceResult OnAddPublishedEvents(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 6)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedEvents expects 6 input arguments."));
            }
            if (!inputArguments[0].TryGetValue(out string? name) || string.IsNullOrEmpty(name))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedEvents argument 0 (Name) is missing or empty."));
            }
            if (!inputArguments[1].TryGetValue(out NodeId eventNotifier) || eventNotifier.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedEvents argument 1 is not a valid NodeId."));
            }
            string[] aliases = TryGetStringArray(inputArguments[2]);
            if (!TryGetEncodeableArray(inputArguments[4], context, out SimpleAttributeOperand[] selectedFields))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedEvents argument 4 is not a SimpleAttributeOperand array."));
            }
            ContentFilter filter = inputArguments[5].TryGetValue(out ExtensionObject filterObject) &&
                filterObject.TryGetValue(out ContentFilter? decodedFilter) &&
                decodedFilter is not null
                ? decodedFilter
                : new ContentFilter();
            PublishedDataSetDataType dataSet = CreatePublishedEventsDataSet(
                name,
                eventNotifier,
                aliases,
                selectedFields,
                filter,
                null);
            return AddPublishedDataSet(dataSet, selectedFields.Length, outputArguments, includeConfigurationVersion: true);
        }

        /// <summary>
        /// Implements Part 14 §9.1.4.5 <c>AddPublishedDataItemsTemplate</c>.
        /// </summary>
        public ServiceResult OnAddPublishedDataItemsTemplate(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 3)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedDataItemsTemplate expects 3 input arguments."));
            }
            if (!inputArguments[0].TryGetValue(out string? name) || string.IsNullOrEmpty(name))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedDataItemsTemplate argument 0 (Name) is missing or empty."));
            }
            if (!inputArguments[1].TryGetValue(out ExtensionObject metaDataObject) ||
                !metaDataObject.TryGetValue(out DataSetMetaDataType? metaData) ||
                metaData is null)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedDataItemsTemplate argument 1 is not DataSetMetaDataType."));
            }
            if (!TryGetEncodeableArray(inputArguments[2], context, out PublishedVariableDataType[] variables))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddPublishedDataItemsTemplate argument 2 is not a PublishedVariableDataType array."));
            }
            PublishedDataSetDataType dataSet = CreatePublishedDataItemsDataSet(
                name,
                [],
                variables,
                metaData);
            return AddPublishedDataSet(dataSet, variables.Length, outputArguments, includeConfigurationVersion: false);
        }

        /// <summary>
        /// Implements Part 14 §9.1.6 <c>RemovePublishedDataSet</c>.
        /// </summary>
        public ServiceResult OnRemovePublishedDataSet(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemovePublishedDataSet expects 1 input argument."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId dataSetId)
                || dataSetId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "RemovePublishedDataSet argument 0 is not a valid NodeId."));
            }
            try
            {
                m_application.RemovePublishedDataSetAsync(dataSetId)
                    .AsTask().GetAwaiter().GetResult();
                return ServiceResult.Good;
            }
            catch (ArgumentException aex)
            {
                return new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    new LocalizedText(aex.Message));
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "RemovePublishedDataSet failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.5 <c>AddDataSetFolder</c>.
        /// The server NodeManager materializes the returned folder NodeId
        /// because folders are address-space objects, not configuration
        /// records.
        /// </summary>
        public ServiceResult OnAddDataSetFolder(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddDataSetFolder expects 1 input argument."));
            }
            if (!inputArguments[0].TryGetValue(out string folderName)
                || string.IsNullOrEmpty(folderName))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddDataSetFolder argument 0 (FolderName) is missing or empty."));
            }
            outputArguments.Add(Variant.From(
                new NodeId($"pubsub:folder:{folderName}", 0)));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Implements Part 14 §9.1.5 <c>RemoveDataSetFolder</c>.
        /// The server NodeManager owns address-space removal for folder nodes.
        /// </summary>
        public ServiceResult OnRemoveDataSetFolder(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveDataSetFolder expects 1 input argument."));
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Implements Part 14 §9.1.6 <c>AddWriterGroup</c>.
        /// </summary>
        public ServiceResult OnAddWriterGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 2)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddWriterGroup expects 2 input arguments."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId connectionId)
                || connectionId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddWriterGroup argument 0 (ConnectionId) is not a valid NodeId."));
            }
            if (!inputArguments[1].TryGetValue(out ExtensionObject ext))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddWriterGroup argument 1 is not an ExtensionObject."));
            }
            if (!ext.TryGetValue(out WriterGroupDataType? cfg) || cfg is null)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddWriterGroup argument 1 body is not a WriterGroupDataType."));
            }
            try
            {
                NodeId id = m_application.AddWriterGroupAsync(connectionId, cfg)
                    .AsTask().GetAwaiter().GetResult();
                outputArguments.Add(Variant.From(id));
                return ServiceResult.Good;
            }
            catch (ArgumentException aex)
            {
                return new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    new LocalizedText(aex.Message));
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "AddWriterGroup failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.6 <c>AddReaderGroup</c>.
        /// </summary>
        public ServiceResult OnAddReaderGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 2)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddReaderGroup expects 2 input arguments."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId connectionId)
                || connectionId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddReaderGroup argument 0 (ConnectionId) is not a valid NodeId."));
            }
            if (!inputArguments[1].TryGetValue(out ExtensionObject ext))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddReaderGroup argument 1 is not an ExtensionObject."));
            }
            if (!ext.TryGetValue(out ReaderGroupDataType? cfg) || cfg is null)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddReaderGroup argument 1 body is not a ReaderGroupDataType."));
            }
            try
            {
                NodeId id = m_application.AddReaderGroupAsync(connectionId, cfg)
                    .AsTask().GetAwaiter().GetResult();
                outputArguments.Add(Variant.From(id));
                return ServiceResult.Good;
            }
            catch (ArgumentException aex)
            {
                return new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    new LocalizedText(aex.Message));
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "AddReaderGroup failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.6 <c>RemoveGroup</c>.
        /// </summary>
        public ServiceResult OnRemoveGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveGroup expects 1 input argument."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId groupId)
                || groupId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "RemoveGroup argument 0 is not a valid NodeId."));
            }
            try
            {
                m_application.RemoveGroupAsync(groupId)
                    .AsTask().GetAwaiter().GetResult();
                return ServiceResult.Good;
            }
            catch (ArgumentException aex)
            {
                return new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    new LocalizedText(aex.Message));
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "RemoveGroup failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.7 <c>AddDataSetWriter</c>.
        /// </summary>
        public ServiceResult OnAddDataSetWriter(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 2)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddDataSetWriter expects 2 input arguments."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId writerGroupId)
                || writerGroupId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddDataSetWriter argument 0 (WriterGroupId) is not a valid NodeId."));
            }
            if (!inputArguments[1].TryGetValue(out ExtensionObject ext))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddDataSetWriter argument 1 is not an ExtensionObject."));
            }
            if (!ext.TryGetValue(out DataSetWriterDataType? cfg) || cfg is null)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddDataSetWriter argument 1 body is not a DataSetWriterDataType."));
            }
            try
            {
                NodeId id = m_application.AddDataSetWriterAsync(writerGroupId, cfg)
                    .AsTask().GetAwaiter().GetResult();
                outputArguments.Add(Variant.From(id));
                return ServiceResult.Good;
            }
            catch (ArgumentException aex)
            {
                return new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    new LocalizedText(aex.Message));
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "AddDataSetWriter failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.7 <c>RemoveDataSetWriter</c>.
        /// </summary>
        public ServiceResult OnRemoveDataSetWriter(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveDataSetWriter expects 1 input argument."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId writerId)
                || writerId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "RemoveDataSetWriter argument 0 is not a valid NodeId."));
            }
            try
            {
                m_application.RemoveDataSetWriterAsync(writerId)
                    .AsTask().GetAwaiter().GetResult();
                return ServiceResult.Good;
            }
            catch (ArgumentException aex)
            {
                return new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    new LocalizedText(aex.Message));
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "RemoveDataSetWriter failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.8 <c>AddDataSetReader</c>.
        /// </summary>
        public ServiceResult OnAddDataSetReader(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 2)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddDataSetReader expects 2 input arguments."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId readerGroupId)
                || readerGroupId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddDataSetReader argument 0 (ReaderGroupId) is not a valid NodeId."));
            }
            if (!inputArguments[1].TryGetValue(out ExtensionObject ext))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddDataSetReader argument 1 is not an ExtensionObject."));
            }
            if (!ext.TryGetValue(out DataSetReaderDataType? cfg) || cfg is null)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "AddDataSetReader argument 1 body is not a DataSetReaderDataType."));
            }
            try
            {
                NodeId id = m_application.AddDataSetReaderAsync(readerGroupId, cfg)
                    .AsTask().GetAwaiter().GetResult();
                outputArguments.Add(Variant.From(id));
                return ServiceResult.Good;
            }
            catch (ArgumentException aex)
            {
                return new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    new LocalizedText(aex.Message));
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "AddDataSetReader failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.8 <c>RemoveDataSetReader</c>.
        /// </summary>
        public ServiceResult OnRemoveDataSetReader(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveDataSetReader expects 1 input argument."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId readerId)
                || readerId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText(
                        "RemoveDataSetReader argument 0 is not a valid NodeId."));
            }
            try
            {
                m_application.RemoveDataSetReaderAsync(readerId)
                    .AsTask().GetAwaiter().GetResult();
                return ServiceResult.Good;
            }
            catch (ArgumentException aex)
            {
                return new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    new LocalizedText(aex.Message));
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(
                    StatusCodes.BadConfigurationError,
                    new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "RemoveDataSetReader failed.");
                return new ServiceResult(
                    StatusCodes.BadInvalidState,
                    new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.4.3 <c>AddVariables</c>.
        /// </summary>
        public ServiceResult OnAddVariables(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (!TryGetPublishedDataSetName(method, out string dataSetName))
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }
            if (inputArguments.Count < 4)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddVariables expects 4 input arguments."));
            }
            string[] aliases = TryGetStringArray(inputArguments[1]);
            if (!TryGetEncodeableArray(inputArguments[3], context, out PublishedVariableDataType[] variables))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddVariables argument 3 is not a PublishedVariableDataType array."));
            }

            return MutatePublishedDataItems(
                dataSetName,
                (dataSet, items) =>
                {
                    List<PublishedVariableDataType> published = ClonePublishedVariables(items);
                    published.AddRange(variables);
                    items.PublishedData = [.. published];
                    AppendMetaDataFields(dataSet, aliases, variables.Length);
                    return variables.Length;
                },
                outputArguments);
        }

        /// <summary>
        /// Implements Part 14 §9.1.4.3 <c>RemoveVariables</c>.
        /// </summary>
        public ServiceResult OnRemoveVariables(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (!TryGetPublishedDataSetName(method, out string dataSetName))
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }
            if (inputArguments.Count < 2)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveVariables expects 2 input arguments."));
            }
            if (!TryGetUInt32Array(inputArguments[1], out uint[] variablesToRemove))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveVariables argument 1 is not a UInt32 array."));
            }

            return MutatePublishedDataItems(
                dataSetName,
                (dataSet, items) =>
                {
                    List<PublishedVariableDataType> published = ClonePublishedVariables(items);
                    List<FieldMetaData> fields = CloneMetaDataFields(dataSet.DataSetMetaData);
                    Array.Sort(variablesToRemove);
                    int removed = 0;
                    for (int i = variablesToRemove.Length - 1; i >= 0; i--)
                    {
                        int index = checked((int)variablesToRemove[i]);
                        if (index < 0 || index >= published.Count)
                        {
                            continue;
                        }
                        published.RemoveAt(index);
                        if (index < fields.Count)
                        {
                            fields.RemoveAt(index);
                        }
                        removed++;
                    }
                    items.PublishedData = [.. published];
                    dataSet.DataSetMetaData ??= new DataSetMetaDataType();
                    dataSet.DataSetMetaData.Fields = [.. fields];
                    return removed;
                },
                outputArguments);
        }

        private ServiceResult AddPublishedDataSet(
            PublishedDataSetDataType dataSet,
            int resultCount,
            List<Variant> outputArguments,
            bool includeConfigurationVersion)
        {
            try
            {
                NodeId dataSetId = m_application.AddPublishedDataSetAsync(dataSet)
                    .AsTask().GetAwaiter().GetResult();
                PublishedDataSetDataType? added = FindPublishedDataSet(dataSet.Name ?? string.Empty);
                outputArguments.Add(Variant.From(dataSetId));
                if (includeConfigurationVersion)
                {
                    outputArguments.Add(Variant.From(new ExtensionObject(
                        added?.DataSetMetaData?.ConfigurationVersion ?? new ConfigurationVersionDataType())));
                }
                outputArguments.Add(Variant.From(CreateGoodResults(resultCount)));
                return ServiceResult.Good;
            }
            catch (PubSubConfigurationException vex)
            {
                return new ServiceResult(StatusCodes.BadConfigurationError, new LocalizedText(vex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "AddPublishedDataSet failed.");
                return new ServiceResult(StatusCodes.BadInvalidState, new LocalizedText(ex.Message));
            }
        }

        private ServiceResult MutatePublishedDataItems(
            string dataSetName,
            Func<PublishedDataSetDataType, PublishedDataItemsDataType, int> mutator,
            List<Variant> outputArguments)
        {
            try
            {
                PubSubConfigurationDataType configuration = m_application.GetConfiguration();
                PubSubConfigurationDataType clone = (PubSubConfigurationDataType)configuration.Clone();
                if (clone.PublishedDataSets.IsNull)
                {
                    return new ServiceResult(StatusCodes.BadNodeIdUnknown);
                }
                int index = FindIndexByName(clone.PublishedDataSets, dataSetName);
                if (index < 0)
                {
                    return new ServiceResult(StatusCodes.BadNodeIdUnknown);
                }
                PublishedDataSetDataType dataSet = clone.PublishedDataSets[index];
                if (dataSet.DataSetSource.IsNull ||
                    !dataSet.DataSetSource.TryGetValue(out PublishedDataItemsDataType? items) ||
                    items is null)
                {
                    return new ServiceResult(
                        StatusCodes.BadInvalidState,
                        new LocalizedText("The PublishedDataSet is not a PublishedDataItemsType instance."));
                }

                int resultCount = mutator(dataSet, items);
                dataSet.DataSetSource = new ExtensionObject(items);
                ArrayOf<StatusCode> replaceResults = m_application.ReplaceConfigurationAsync(clone)
                    .AsTask().GetAwaiter().GetResult();
                _ = replaceResults;
                PublishedDataSetDataType? updated = FindPublishedDataSet(dataSetName);
                outputArguments.Add(Variant.From(new ExtensionObject(
                    updated?.DataSetMetaData?.ConfigurationVersion ?? new ConfigurationVersionDataType())));
                outputArguments.Add(Variant.From(CreateGoodResults(resultCount)));
                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "PublishedDataItems mutation failed.");
                return new ServiceResult(StatusCodes.BadInvalidState, new LocalizedText(ex.Message));
            }
        }

        private static PublishedDataSetDataType CreatePublishedDataItemsDataSet(
            string name,
            string[] aliases,
            PublishedVariableDataType[] variables,
            DataSetMetaDataType? templateMetaData)
        {
            DataSetMetaDataType metaData = templateMetaData is null
                ? CreateMetaData(name, aliases, variables.Length)
                : (DataSetMetaDataType)templateMetaData.Clone();
            if (templateMetaData is not null)
            {
                metaData.Fields = [.. CloneMetaDataFields(templateMetaData)];
            }
            if (metaData.Fields.IsNull || metaData.Fields.Count == 0)
            {
                metaData.Fields = CreateFields(aliases, variables.Length);
            }
            return new PublishedDataSetDataType
            {
                Name = name,
                DataSetMetaData = metaData,
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [.. variables]
                })
            };
        }

        private static PublishedDataSetDataType CreatePublishedEventsDataSet(
            string name,
            NodeId eventNotifier,
            string[] aliases,
            SimpleAttributeOperand[] selectedFields,
            ContentFilter filter,
            DataSetMetaDataType? templateMetaData)
        {
            DataSetMetaDataType metaData = templateMetaData is null
                ? CreateMetaData(name, aliases, selectedFields.Length)
                : (DataSetMetaDataType)templateMetaData.Clone();
            if (templateMetaData is not null)
            {
                metaData.Fields = [.. CloneMetaDataFields(templateMetaData)];
            }
            if (metaData.Fields.IsNull || metaData.Fields.Count == 0)
            {
                metaData.Fields = CreateFields(aliases, selectedFields.Length);
            }
            return new PublishedDataSetDataType
            {
                Name = name,
                DataSetMetaData = metaData,
                DataSetSource = new ExtensionObject(new PublishedEventsDataType
                {
                    EventNotifier = eventNotifier,
                    SelectedFields = [.. selectedFields],
                    Filter = filter
                })
            };
        }

        private static DataSetMetaDataType CreateMetaData(
            string name,
            string[] aliases,
            int fieldCount)
        {
            return new DataSetMetaDataType
            {
                Name = name,
                Fields = CreateFields(aliases, fieldCount)
            };
        }

        private static ArrayOf<FieldMetaData> CreateFields(string[] aliases, int fieldCount)
        {
            var fields = new FieldMetaData[fieldCount];
            for (int i = 0; i < fields.Length; i++)
            {
                string fieldName = i < aliases.Length && !string.IsNullOrEmpty(aliases[i])
                    ? aliases[i]
                    : $"Field{i + 1}";
                fields[i] = new FieldMetaData
                {
                    Name = fieldName,
                    DataType = DataTypeIds.BaseDataType,
                    ValueRank = ValueRanks.Scalar,
                    Properties = []
                };
            }
            return [.. fields];
        }

        private static void AppendMetaDataFields(
            PublishedDataSetDataType dataSet,
            string[] aliases,
            int fieldCount)
        {
            dataSet.DataSetMetaData ??= new DataSetMetaDataType();
            List<FieldMetaData> fields = CloneMetaDataFields(dataSet.DataSetMetaData);
            ArrayOf<FieldMetaData> newFields = CreateFields(aliases, fieldCount);
            for (int i = 0; i < newFields.Count; i++)
            {
                fields.Add(newFields[i]);
            }
            dataSet.DataSetMetaData.Fields = [.. fields];
        }

        private PublishedDataSetDataType? FindPublishedDataSet(string name)
        {
            PubSubConfigurationDataType configuration = m_application.GetConfiguration();
            if (configuration.PublishedDataSets.IsNull)
            {
                return null;
            }
            int index = FindIndexByName(configuration.PublishedDataSets, name);
            return index < 0 ? null : configuration.PublishedDataSets[index];
        }

        private static int FindIndexByName(
            ArrayOf<PublishedDataSetDataType> dataSets,
            string name)
        {
            for (int i = 0; i < dataSets.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(dataSets[i].Name, name))
                {
                    return i;
                }
            }
            return -1;
        }

        private static List<PublishedVariableDataType> ClonePublishedVariables(
            PublishedDataItemsDataType items)
        {
            var published = new List<PublishedVariableDataType>();
            if (items.PublishedData.IsNull)
            {
                return published;
            }
            foreach (PublishedVariableDataType item in items.PublishedData)
            {
                published.Add((PublishedVariableDataType)item.Clone());
            }
            return published;
        }

        private static List<FieldMetaData> CloneMetaDataFields(DataSetMetaDataType? metaData)
        {
            var fields = new List<FieldMetaData>();
            if (metaData is null || metaData.Fields.IsNull)
            {
                return fields;
            }
            foreach (FieldMetaData field in metaData.Fields)
            {
                fields.Add((FieldMetaData)field.Clone());
            }
            return fields;
        }

        private static StatusCode[] CreateGoodResults(int count)
        {
            var results = new StatusCode[count];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = StatusCodes.Good;
            }
            return results;
        }

        private static string[] TryGetStringArray(Variant value)
        {
            if (!value.TryGetValue(out ArrayOf<string> values) || values.IsNull)
            {
                return [];
            }
            var result = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                result[i] = values[i];
            }
            return result;
        }

        private static bool TryGetUInt32Array(Variant value, out uint[] result)
        {
            result = [];
            if (!value.TryGetValue(out ArrayOf<uint> values) || values.IsNull)
            {
                return false;
            }
            result = new uint[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                result[i] = values[i];
            }
            return true;
        }

        private static bool TryGetEncodeableArray<T>(
            Variant value,
            ISystemContext context,
            out T[] result)
            where T : class, IEncodeable
        {
            result = [];
            IServiceMessageContext? messageContext = context as IServiceMessageContext
                ?? AmbientMessageContext.CurrentContext;
            if (!value.TryGetValue(out ArrayOf<T> values, messageContext) || values.IsNull)
            {
                return false;
            }
            result = new T[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                result[i] = values[i];
            }
            return true;
        }

        private static bool TryGetPublishedDataSetName(MethodState method, out string dataSetName)
        {
            dataSetName = string.Empty;
            string nodeId;
            if (method?.Parent is BaseObjectState parent)
            {
                nodeId = parent.NodeId.IdentifierAsString;
            }
            else if (method?.NodeId is not null)
            {
                nodeId = method.NodeId.IdentifierAsString;
            }
            else
            {
                return false;
            }
            const string prefix = "pubsub:published-data-set:";
            if (!nodeId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
            dataSetName = nodeId[prefix.Length..];
            int separator = dataSetName.IndexOf(':', StringComparison.Ordinal);
            if (separator >= 0)
            {
                dataSetName = dataSetName[..separator];
            }
            return dataSetName.Length > 0;
        }

        /// <summary>
        /// Implements Part 14 §8.3.4 <c>AddSecurityGroup</c>.
        /// Delegates to
        /// <see cref="IPubSubKeyServiceServer.AddSecurityGroupAsync"/>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="inputArguments">Input arguments.</param>
        /// <param name="outputArguments">Output arguments.</param>
        public ServiceResult OnAddSecurityGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (m_keyService is null)
            {
                return new ServiceResult(StatusCodes.BadServiceUnsupported);
            }
            if (inputArguments.Count < 5)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText($"AddSecurityGroup expects 5 input arguments; got {inputArguments.Count}."));
            }
            if (!inputArguments[0].TryGetValue(out string? name) || string.IsNullOrEmpty(name))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddSecurityGroup argument 0 (SecurityGroupName) is missing or empty."));
            }
            if (!inputArguments[1].TryGetValue(out double keyLifetimeMs) || keyLifetimeMs <= 0)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddSecurityGroup argument 1 (KeyLifetime) must be a positive Duration."));
            }
            if (!inputArguments[2].TryGetValue(out string? policyUri) || string.IsNullOrEmpty(policyUri))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddSecurityGroup argument 2 (SecurityPolicyUri) is missing or empty."));
            }
            if (!inputArguments[3].TryGetValue(out uint maxFuture))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddSecurityGroup argument 3 (MaxFutureKeyCount) is not a UInt32."));
            }
            if (!inputArguments[4].TryGetValue(out uint maxPast))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddSecurityGroup argument 4 (MaxPastKeyCount) is not a UInt32."));
            }

            var group = new SksSecurityGroup(
                securityGroupId: name,
                securityPolicyUri: policyUri,
                keyLifetime: TimeSpan.FromMilliseconds(keyLifetimeMs),
                maxFutureKeyCount: (int)Math.Min(maxFuture, int.MaxValue),
                maxPastKeyCount: (int)Math.Min(maxPast, int.MaxValue),
                keys: Array.Empty<PubSubSecurityKey>(),
                rolePermissions: TryReadRolePermissions(inputArguments, 5),
                authorizedCallerIdentities: TryReadAuthorizedCallers(inputArguments, 6));

            try
            {
                m_keyService
                    .AddSecurityGroupAsync(group)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OpcUaSksException ex)
            {
                m_logger.LogDebug(ex, "AddSecurityGroup {Name} rejected with {Status}.", name, ex.Status);
                return new ServiceResult(ex.Status, new LocalizedText(ex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "AddSecurityGroup {Name} threw unexpectedly.", name);
                return new ServiceResult(StatusCodes.BadInternalError, new LocalizedText(ex.Message));
            }

            NodeId groupNodeId = AllocateSecurityGroupNodeId(name);
            outputArguments.Add(Variant.From(name));
            outputArguments.Add(Variant.From(groupNodeId));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Implements Part 14 §8.3.5 <c>RemoveSecurityGroup</c>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="inputArguments">Input arguments.</param>
        /// <param name="outputArguments">Output arguments (none).</param>
        public ServiceResult OnRemoveSecurityGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (m_keyService is null)
            {
                return new ServiceResult(StatusCodes.BadServiceUnsupported);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveSecurityGroup expects 1 input argument."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId groupNodeId) || groupNodeId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveSecurityGroup argument 0 (SecurityGroupNodeId) is missing or not a NodeId."));
            }
            string? id = LookupSecurityGroupId(groupNodeId);
            if (id is null)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }
            try
            {
                m_keyService
                    .RemoveSecurityGroupAsync(id)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OpcUaSksException ex)
            {
                m_logger.LogDebug(ex, "RemoveSecurityGroup {Id} rejected with {Status}.", id, ex.Status);
                return new ServiceResult(ex.Status, new LocalizedText(ex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "RemoveSecurityGroup {Id} threw unexpectedly.", id);
                return new ServiceResult(StatusCodes.BadInternalError, new LocalizedText(ex.Message));
            }
            lock (m_gate)
            {
                m_securityGroupNodeIds.Remove(groupNodeId);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Implements Part 14 §8.3.2 <c>GetSecurityKeys</c>.
        /// Delegates to <see cref="SksMethodHandler.HandleGetSecurityKeys"/>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="objectId">Object the method is called on.</param>
        /// <param name="inputArguments">Input arguments.</param>
        /// <param name="outputArguments">Output arguments.</param>
        public ServiceResult OnGetSecurityKeys(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = method;
            if (m_sks is null)
            {
                return new ServiceResult(StatusCodes.BadServiceUnsupported);
            }
            return m_sks.HandleGetSecurityKeys(context, objectId, inputArguments.ToList(), outputArguments);
        }

        /// <summary>
        /// Implements Part 14 §8.3.3 <c>GetSecurityGroup</c>.
        /// </summary>
        public ServiceResult OnGetSecurityGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (m_keyService is null)
            {
                return new ServiceResult(StatusCodes.BadServiceUnsupported);
            }
            if (inputArguments.Count < 1 ||
                !inputArguments[0].TryGetValue(out string? securityGroupId) ||
                string.IsNullOrEmpty(securityGroupId))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            SksSecurityGroup? group = m_keyService
                .GetSecurityGroupAsync(securityGroupId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            if (group is null)
            {
                return new ServiceResult(StatusCodes.BadNoMatch);
            }

            outputArguments.Add(Variant.From(GetOrAllocateSecurityGroupNodeId(securityGroupId)));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Implements Part 14 §9.1.3.3 <c>SetSecurityKeys</c>.
        /// </summary>
        public ServiceResult OnSetSecurityKeys(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = method;
            _ = outputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (!IsSecurityKeyPushAuthorized(context))
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (inputArguments.Count < 7)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            if (!inputArguments[0].TryGetValue(out string? securityGroupId) || string.IsNullOrEmpty(securityGroupId) ||
                !inputArguments[1].TryGetValue(out string? policyUri) || string.IsNullOrEmpty(policyUri) ||
                !inputArguments[2].TryGetValue(out uint currentTokenId) ||
                !inputArguments[3].TryGetValue(out ByteString currentKey) ||
                !inputArguments[4].TryGetValue(out ArrayOf<ByteString> futureKeys) ||
                !inputArguments[5].TryGetValue(out double timeToNextKeyMs) ||
                !inputArguments[6].TryGetValue(out double keyLifetimeMs))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            PushSecurityKeyProvider? provider = FindPushProvider(securityGroupId);
            if (provider is null)
            {
                return new ServiceResult(StatusCodes.BadNotFound);
            }

            try
            {
                provider.SetSecurityKeysAsync(
                    policyUri,
                    currentTokenId,
                    currentKey,
                    futureKeys,
                    TimeSpan.FromMilliseconds(timeToNextKeyMs),
                    TimeSpan.FromMilliseconds(keyLifetimeMs))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                return ServiceResult.Good;
            }
            catch (OpcUaSksException ex)
            {
                return new ServiceResult(ex.Status, new LocalizedText(ex.Message));
            }
        }

        private static bool IsSecurityKeyPushAuthorized(ISystemContext context)
        {
            if (StringComparer.Ordinal.Equals(context.UserId, "sks"))
            {
                return true;
            }

            if (context is not ISessionOperationContext sessionContext)
            {
                return false;
            }

            ArrayOf<NodeId> grantedRoleIds = sessionContext.UserIdentity?.GrantedRoleIds ?? [];
            return grantedRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin);
        }

        /// <summary>
        /// Implements Part 14 §8.4.2 <c>InvalidateKeys</c>.
        /// </summary>
        public ServiceResult OnInvalidateKeys(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = inputArguments;
            _ = outputArguments;
            return RotateOrInvalidateKeys(objectId, invalidate: true);
        }

        /// <summary>
        /// Implements Part 14 §8.4.3 <c>ForceKeyRotation</c>.
        /// </summary>
        public ServiceResult OnForceKeyRotation(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = inputArguments;
            _ = outputArguments;
            return RotateOrInvalidateKeys(objectId, invalidate: false);
        }

        /// <summary>
        /// Returns the NodeId previously allocated for the
        /// SecurityGroup identified by <paramref name="securityGroupId"/>,
        /// or <see langword="null"/> when the id is unknown to this
        /// handler.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        public NodeId? TryGetSecurityGroupNodeId(string securityGroupId)
        {
            if (string.IsNullOrEmpty(securityGroupId))
            {
                return null;
            }
            lock (m_gate)
            {
                foreach (KeyValuePair<NodeId, string> kvp in m_securityGroupNodeIds)
                {
                    if (string.Equals(kvp.Value, securityGroupId, StringComparison.Ordinal))
                    {
                        return kvp.Key;
                    }
                }
                return null;
            }
        }

        private ServiceResult RotateOrInvalidateKeys(NodeId groupNodeId, bool invalidate)
        {
            if (m_keyService is null)
            {
                return new ServiceResult(StatusCodes.BadServiceUnsupported);
            }
            string? id = LookupSecurityGroupId(groupNodeId);
            if (id is null)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }
            try
            {
                ValueTask task = invalidate
                    ? m_keyService.InvalidateKeysAsync(id)
                    : m_keyService.ForceKeyRotationAsync(id);
                task.AsTask().GetAwaiter().GetResult();
                return ServiceResult.Good;
            }
            catch (OpcUaSksException ex)
            {
                return new ServiceResult(ex.Status, new LocalizedText(ex.Message));
            }
        }

        private PushSecurityKeyProvider? FindPushProvider(string securityGroupId)
        {
            for (int i = 0; i < m_pushProviders.Length; i++)
            {
                if (string.Equals(m_pushProviders[i].SecurityGroupId, securityGroupId, StringComparison.Ordinal))
                {
                    return m_pushProviders[i];
                }
            }
            return null;
        }

        private NodeId GetOrAllocateSecurityGroupNodeId(string securityGroupId)
        {
            NodeId? existing = TryGetSecurityGroupNodeId(securityGroupId);
            return existing ?? AllocateSecurityGroupNodeId(securityGroupId);
        }

        private static ArrayOf<RolePermissionType> TryReadRolePermissions(
            ArrayOf<Variant> inputArguments,
            int index)
        {
            if (inputArguments.Count <= index)
            {
                return [];
            }
            if (!inputArguments[index].TryGetValue(out ArrayOf<ExtensionObject> rolePermissionsArray))
            {
                return [];
            }

            var rolePermissions = new List<RolePermissionType>(rolePermissionsArray.Count);
            for (int i = 0; i < rolePermissionsArray.Count; i++)
            {
                if (rolePermissionsArray[i].TryGetValue(out RolePermissionType? rolePermission) &&
                    rolePermission is not null)
                {
                    rolePermissions.Add(rolePermission);
                }
            }
            return [.. rolePermissions];
        }

        private static ArrayOf<string> TryReadAuthorizedCallers(ArrayOf<Variant> inputArguments, int index)
        {
            if (inputArguments.Count <= index)
            {
                return [];
            }
            return inputArguments[index].TryGetValue(out ArrayOf<string> callers) ? callers : [];
        }

        private string? LookupSecurityGroupId(NodeId groupNodeId)
        {
            lock (m_gate)
            {
                if (m_securityGroupNodeIds.TryGetValue(groupNodeId, out string? id))
                {
                    return id;
                }
            }
            if (groupNodeId.IdType == IdType.String &&
                groupNodeId.TryGetValue(out string identifier) &&
                !string.IsNullOrEmpty(identifier))
            {
                return identifier;
            }
            return null;
        }

        private NodeId AllocateSecurityGroupNodeId(string securityGroupId)
        {
            uint handle;
            lock (m_gate)
            {
                handle = ++m_nextSecurityGroupHandle;
            }
            var nodeId = new NodeId($"SecurityGroups/{securityGroupId}/{handle}", 0);
            lock (m_gate)
            {
                m_securityGroupNodeIds[nodeId] = securityGroupId;
            }
            return nodeId;
        }

        /// <summary>
        /// Returns the default SecurityPolicyUri for the SKS host.
        /// </summary>
        public string DefaultPolicyUri => m_options.DefaultSecurityPolicyUri ?? DefaultSecurityPolicyUri;
    }
}

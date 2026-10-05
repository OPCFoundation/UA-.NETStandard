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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.Server;

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// Keeps the <c>2:AssetId</c> of an asset writable and persistent
    /// (OPC 10000-110 §7, "AMB Configurable Asset Identification").
    /// </summary>
    /// <remarks>
    /// <para>
    /// The binding sits in front of whatever write handlers the property
    /// already had: it validates the value, runs the earlier handlers,
    /// persists it and only then reports success. A value that cannot be
    /// stored is rejected rather than accepted unpersisted.
    /// </para>
    /// <para>
    /// The writes of one asset are serialized: persisting, the cached value
    /// and the move in <c>AssetsByAssetId</c> happen together, so a later
    /// write cannot overtake an earlier one and the three never disagree.
    /// The binding sets the cached value itself; the framework writes the
    /// same value again once the handler returns.
    /// </para>
    /// <para>
    /// Lengths are counted in Unicode characters, as Table 54 states them.
    /// </para>
    /// </remarks>
    internal sealed class ConfigurableAssetId : IDisposable
    {
        private ConfigurableAssetId(
            BaseVariableState variable,
            string productInstanceUri,
            IAssetConfigurationStore store,
            int maxLength,
            ILogger logger)
        {
            Variable = variable;
            m_productInstanceUri = productInstanceUri;
            m_store = store;
            m_maxLength = maxLength;
            m_logger = logger;
            m_previousAsync = variable.OnSimpleWriteValueAsync;
            m_previousWriteAsync = variable.OnWriteValueAsync;
            m_previousSimple = variable.OnSimpleWriteValue;
            m_previous = variable.OnWriteValue;
        }

        /// <summary>
        /// Gets the bound property.
        /// </summary>
        public BaseVariableState Variable { get; }

        /// <summary>
        /// Gets or sets what runs after a client wrote a new value and it was
        /// persisted, before the write is acknowledged.
        /// </summary>
        public Func<string?, CancellationToken, ValueTask>? OnChangedAsync { get; set; }

        /// <summary>
        /// Finds or creates the <c>AssetId</c> of an asset, restores the
        /// persisted value and makes it writable.
        /// </summary>
        /// <param name="owner">The node manager that owns the asset.</param>
        /// <param name="context">The context of <paramref name="owner"/>.</param>
        /// <param name="asset">The asset object.</param>
        /// <param name="diNamespaceIndex">The index of the DI namespace.</param>
        /// <param name="productInstanceUri">The key the value is persisted under.</param>
        /// <param name="defaultAssetId">The value used while nothing is persisted.</param>
        /// <param name="store">The store of the configured values.</param>
        /// <param name="maxLength">The maximum number of characters.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadConfigurationError"/> when the asset has
        /// no <c>ProductInstanceUri</c> to persist under, or when
        /// <paramref name="defaultAssetId"/> is longer than
        /// <paramref name="maxLength"/>.
        /// </exception>
        public static async ValueTask<ConfigurableAssetId> BindAsync(
            IAsyncNodeManager owner,
            ISystemContext context,
            BaseObjectState asset,
            ushort diNamespaceIndex,
            string productInstanceUri,
            string? defaultAssetId,
            IAssetConfigurationStore store,
            int maxLength,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(productInstanceUri))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Asset '{0}' has no ProductInstanceUri, which a configurable AssetId is persisted under.",
                    asset.BrowseName);
            }
            if (defaultAssetId != null && UnicodeText.Length(defaultAssetId) > maxLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The default AssetId of '{0}' is longer than {1} characters.",
                    asset.BrowseName,
                    maxLength);
            }

            BaseVariableState variable =
                AssetIdentification.FindProperty(context, asset, diNamespaceIndex, AssetIdentification.AssetId) ??
                Create(owner, context, asset, diNamespaceIndex);

            string? stored = await store
                .GetValueAsync(productInstanceUri, AssetConfigurationNames.AssetId, cancellationToken)
                .ConfigureAwait(false);
            string? current = AssetIdentification.ReadString(variable);
            string? initial = stored ?? (string.IsNullOrEmpty(current) ? defaultAssetId : current);
            if (!string.Equals(initial, current, StringComparison.Ordinal))
            {
                variable.WrappedValue = Variant.From(initial!);
                variable.Timestamp = DateTimeUtc.Now;
                variable.ClearChangeMasks(context, false);
            }

            // Both access levels have to say so: a client checks
            // UserAccessLevel, and the server enforces on AccessLevel.
            variable.AccessLevel |= AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel |= AccessLevels.CurrentReadOrWrite;

            var binding = new ConfigurableAssetId(
                variable,
                productInstanceUri,
                store,
                maxLength,
                logger);
            variable.OnSimpleWriteValueAsync = binding.OnWriteAsync;
            variable.OnWriteValueAsync = null;
            return binding;
        }

        /// <summary>
        /// Puts the write handlers back that were there before the binding.
        /// </summary>
        public void Release()
        {
            Variable.OnSimpleWriteValueAsync = m_previousAsync;
            Variable.OnWriteValueAsync = m_previousWriteAsync;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        private async ValueTask<AttributeWriteResult> OnWriteAsync(
            ISystemContext context,
            NodeState node,
            Variant value,
            CancellationToken cancellationToken)
        {
            string? assetId;
            if (value.IsNull)
            {
                assetId = null;
            }
            else if (!value.TryGetValue(out string text))
            {
                return new AttributeWriteResult(StatusCodes.BadTypeMismatch);
            }
            else
            {
                assetId = text;
            }

            if (assetId != null && UnicodeText.Length(assetId) > m_maxLength)
            {
                return new AttributeWriteResult(ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "The AssetId must not be longer than {0} characters.",
                    m_maxLength));
            }

            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ServiceResult previous = await RunPreviousHandlersAsync(context, node, value, cancellationToken)
                    .ConfigureAwait(false);
                if (ServiceResult.IsBad(previous))
                {
                    return new AttributeWriteResult(previous);
                }

                try
                {
                    await m_store
                        .SetValueAsync(
                            m_productInstanceUri,
                            AssetConfigurationNames.AssetId,
                            assetId,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.AssetIdNotPersisted(ex, m_productInstanceUri);
                    return new AttributeWriteResult(ServiceResult.Create(
                        ex,
                        StatusCodes.BadResourceUnavailable,
                        "The AssetId could not be persisted."));
                }

                Variable.WrappedValue = value;
                Variable.Timestamp = DateTimeUtc.Now;
                Variable.ClearChangeMasks(context, false);
                m_logger.AssetIdChanged(m_productInstanceUri, assetId);

                Func<string?, CancellationToken, ValueTask>? changed = OnChangedAsync;
                if (changed != null)
                {
                    try
                    {
                        await changed(assetId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // The value is stored; what follows from it must not
                        // turn an accepted write into a failed one.
                        m_logger.AssetIdFollowUpFailed(ex, m_productInstanceUri);
                    }
                }
                return new AttributeWriteResult(ServiceResult.Good);
            }
            finally
            {
                m_gate.Release();
            }
        }

        private async ValueTask<ServiceResult> RunPreviousHandlersAsync(
            ISystemContext context,
            NodeState node,
            Variant value,
            CancellationToken cancellationToken)
        {
            if (m_previousAsync != null)
            {
                AttributeWriteResult result = await m_previousAsync(context, node, value, cancellationToken)
                    .ConfigureAwait(false);
                return result.Result ?? ServiceResult.Good;
            }
            if (m_previousWriteAsync != null)
            {
                AttributeWriteResult result = await m_previousWriteAsync(
                    context,
                    node,
                    NumericRange.Null,
                    value,
                    cancellationToken).ConfigureAwait(false);
                return result.Result ?? ServiceResult.Good;
            }

            // A synchronous handler is skipped by the asynchronous write path
            // once an asynchronous one is set, so it is run from here.
            Variant copy = value;
            if (m_previousSimple != null)
            {
                return m_previousSimple(context, node, ref copy) ?? ServiceResult.Good;
            }
            if (m_previous != null)
            {
                StatusCode statusCode = StatusCodes.Good;
                DateTimeUtc timestamp = DateTimeUtc.Now;
                return m_previous(
                    context,
                    node,
                    NumericRange.Null,
                    QualifiedName.Null,
                    ref copy,
                    ref statusCode,
                    ref timestamp) ??
                    ServiceResult.Good;
            }
            return ServiceResult.Good;
        }

        private static BaseVariableState Create(
            IAsyncNodeManager owner,
            ISystemContext context,
            BaseObjectState asset,
            ushort diNamespaceIndex)
        {
            var browseName = new QualifiedName(AssetIdentification.AssetId, diNamespaceIndex);
            BaseVariableState variable;
            if (asset.CreateChild(context, browseName) is BaseVariableState declared)
            {
                // The type declares AssetId, so it implements
                // ITagNameplateType already and the slot is the property.
                // The generated slot comes without reference type, type
                // definition and data type.
                variable = declared;
                Structure.AssetProperties.Shape(variable, Ua.DataTypeIds.String);
            }
            else
            {
                PropertyState property = asset.AddProperty<string, VariantBuilder>(
                    AssetIdentification.AssetId,
                    Ua.DataTypeIds.String,
                    ValueRanks.Scalar);
                property.BrowseName = browseName;
                property.Description = new LocalizedText(
                    "To be used by end users to store a unique identification in the context of their " +
                    "overall application.");
                variable = property;

                // OPC 10000-110 §7 publishes AssetId through ITagNameplateType;
                // a type that does not implement it gets it on the instance.
                asset.AddReference(
                    Ua.ReferenceTypeIds.HasInterface,
                    false,
                    ExpandedNodeId.ToNodeId(Opc.Ua.Di.ObjectTypeIds.ITagNameplateType, context.NamespaceUris));
            }

            if (variable.NodeId.IsNull)
            {
                variable.NodeId = owner.New(context, variable);
            }
            owner.AddNode(variable);
            return variable;
        }

        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly string m_productInstanceUri;
        private readonly IAssetConfigurationStore m_store;
        private readonly int m_maxLength;
        private readonly ILogger m_logger;
        private readonly NodeValueSimpleWriteEventHandlerAsync? m_previousAsync;
        private readonly NodeValueWriteEventHandlerAsync? m_previousWriteAsync;
        private readonly NodeValueSimpleEventHandler? m_previousSimple;
        private readonly NodeValueEventHandler? m_previous;
    }

    internal static partial class ConfigurableAssetIdLog
    {
        [LoggerMessage(
            EventId = AmbServerEventIds.ConfigurableAssetId + 0,
            Level = LogLevel.Information,
            Message = "AssetId of asset {ProductInstanceUri} changed to '{AssetId}'.")]
        public static partial void AssetIdChanged(this ILogger logger, string productInstanceUri, string? assetId);

        [LoggerMessage(
            EventId = AmbServerEventIds.ConfigurableAssetId + 1,
            Level = LogLevel.Error,
            Message = "AssetId of asset {ProductInstanceUri} could not be persisted; the write is rejected.")]
        public static partial void AssetIdNotPersisted(
            this ILogger logger,
            Exception exception,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.ConfigurableAssetId + 2,
            Level = LogLevel.Warning,
            Message = "AssetId of asset {ProductInstanceUri} was stored, but updating the discovery failed.")]
        public static partial void AssetIdFollowUpFailed(
            this ILogger logger,
            Exception exception,
            string productInstanceUri);
    }
}

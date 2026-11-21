// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Describes a server-side PublishedActionMethod binding for a DataSetWriter.
    /// </summary>
    public sealed class PubSubActionMethodRegistration
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubActionMethodRegistration"/>.
        /// </summary>
        /// <param name="dataSetWriterId">DataSetWriterId that owns the action metadata.</param>
        /// <param name="publishedAction">PublishedActionMethod metadata to bind.</param>
        /// <param name="connectionName">Optional PubSub connection name used for routing.</param>
        /// <param name="serviceIdentity">
        /// Optional identity the bound Methods execute under (SA-ACT-02). When
        /// <see langword="null"/> the Methods run as an explicit <em>Anonymous</em>
        /// identity and node <c>RolePermissions</c> for the Anonymous role apply.
        /// </param>
        public PubSubActionMethodRegistration(
            ushort dataSetWriterId,
            PublishedActionMethodDataType publishedAction,
            string connectionName = "",
            IUserIdentity? serviceIdentity = null)
        {
            if (publishedAction is null)
            {
                throw new ArgumentNullException(nameof(publishedAction));
            }

            DataSetWriterId = dataSetWriterId;
            PublishedAction = publishedAction;
            ConnectionName = connectionName ?? string.Empty;
            ServiceIdentity = serviceIdentity;
        }

        /// <summary>
        /// DataSetWriterId that owns the PublishedAction metadata.
        /// </summary>
        public ushort DataSetWriterId { get; }

        /// <summary>
        /// Optional connection name used by PubSub runtime routing.
        /// </summary>
        public string ConnectionName { get; }

        /// <summary>
        /// PublishedActionMethod metadata whose targets are bound to server methods.
        /// </summary>
        public PublishedActionMethodDataType PublishedAction { get; }

        /// <summary>
        /// Optional identity the bound Methods execute under (SA-ACT-02). When
        /// <see langword="null"/> the Methods run as an explicit <em>Anonymous</em>
        /// identity.
        /// </summary>
        public IUserIdentity? ServiceIdentity { get; }
    }
}

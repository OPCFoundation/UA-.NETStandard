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

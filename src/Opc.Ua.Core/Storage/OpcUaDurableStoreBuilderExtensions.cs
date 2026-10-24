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
using Opc.Ua;
using Opc.Ua.Redundancy;
using Opc.Ua.Storage;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the local durable shared-store implementation without enabling any protocol feature.
    /// </summary>
    public static class OpcUaDurableStoreBuilderExtensions
    {
        /// <summary>
        /// Selects a bounded local persistent store. Register a secret-store-backed
        /// <see cref="IRecordProtector"/> first and verify durability during asynchronous startup.
        /// This does not supply restart-safe timing or distributed visibility.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddFileSharedKeyValueStore(
            this IOpcUaBuilder builder,
            string directory,
            string logicalStoreId,
            int maximumSnapshotBytes)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddSingleton(services => new FileSharedKeyValueStore(
                directory, logicalStoreId, services.GetRequiredService<IRecordProtector>(), maximumSnapshotBytes));
            builder.Services.AddSingleton<ISharedKeyValueStore>(
                services => services.GetRequiredService<FileSharedKeyValueStore>());
            builder.Services.AddSingleton<ISharedKeyValueStoreDurability>(
                services => services.GetRequiredService<FileSharedKeyValueStore>());
            builder.Services.AddSingleton<ISharedKeyValueStoreConsistency>(
                services => services.GetRequiredService<FileSharedKeyValueStore>());
            builder.Services.AddSingleton<ISharedKeyValueStoreCapacity>(
                services => services.GetRequiredService<FileSharedKeyValueStore>());
            return builder;
        }
    }
}

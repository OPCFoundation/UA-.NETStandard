// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Reader interface
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IReader<T> : IAsyncDisposable
    {
        /// <summary>
        /// Read all notifications
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        IAsyncEnumerable<T> ReadAsync(CancellationToken ct = default);
    }
}

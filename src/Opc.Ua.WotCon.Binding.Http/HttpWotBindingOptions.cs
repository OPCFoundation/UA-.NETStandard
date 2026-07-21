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
using System.Net.Http;

namespace Opc.Ua.WotCon.Binding.Http
{
    /// <summary>
    /// Options for the HTTP WoT binding executor. The client factory is injectable
    /// so callers can supply a pooled, mutually-authenticated or test
    /// <see cref="HttpClient"/>; when none is supplied the executor owns a private
    /// client. Default headers are applied to every request in addition to any
    /// credential the provider resolves.
    /// </summary>
    public sealed class HttpWotBindingOptions
    {
        /// <summary>
        /// Gets or sets the factory that supplies the <see cref="HttpClient"/>. When
        /// <c>null</c> the executor creates and owns a private client per channel.
        /// A supplied client is treated as caller-owned and is never disposed by
        /// the executor.
        /// </summary>
        public Func<HttpClient>? ClientFactory { get; set; }

        /// <summary>Gets or sets default headers applied to every request.</summary>
        public IReadOnlyDictionary<string, string>? DefaultHeaders { get; set; }

        /// <summary>Gets or sets the poll interval used for observe / event operations.</summary>
        public TimeSpan ObserveInterval { get; set; } = TimeSpan.FromSeconds(1);
    }
}

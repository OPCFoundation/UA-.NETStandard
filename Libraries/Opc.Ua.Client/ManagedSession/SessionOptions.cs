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

namespace Opc.Ua.Client.Sessions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Session options
    /// </summary>
    public record class SessionOptions
    {
        /// <summary>
        /// The name of the session. The name is displayed
        /// on the server to help administrators identify the
        /// client. If not name is set a default name is chosen.
        /// </summary>
        public string? SessionName { get; init; }

        /// <summary>
        /// Desired Session timeout after which the session
        /// is garbage collected on the server without any
        /// activity. This setting can be revised by the
        /// server and the actual timeout is exposed by the
        /// <see cref="ISession"/>.
        /// </summary>
        public TimeSpan? SessionTimeout { get; init; }

        /// <summary>
        /// Preferred locales to use on this session. The default
        /// locales used are en-Us.
        /// </summary>
        public IReadOnlyList<string>? PreferredLocales { get; init; }

        /// <summary>
        /// Gets or Sets how frequently the server is pinged to
        /// see if communication is still working. This interval
        /// controls how much time elaspes before a communication
        /// error is detected. The session will send read request
        /// when the keep alive interval expires. The keep alive
        /// timer is reset any time a successful response is
        /// returned (from any service, including publish and the
        /// keep alive read operation)
        /// </summary>
        public TimeSpan? KeepAliveInterval { get; init; }

        /// <summary>
        /// Check domain of the certificate against the endpoint
        /// of the server during session creation.
        /// </summary>
        public bool CheckDomain { get; init; }

        /// <summary>
        /// Enable complex type preloading when session is created.
        /// The complex type system will otherwise be lazily loaded.
        /// </summary>
        public bool EnableComplexTypePreloading { get; init; }

        /// <summary>
        /// Disable the use of DataType Dictionaries to create the
        /// complex type definition.
        /// </summary>
        public bool DisableDataTypeDictionary { get; init; }
    }
}

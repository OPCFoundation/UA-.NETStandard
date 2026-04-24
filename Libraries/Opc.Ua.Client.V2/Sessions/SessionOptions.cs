// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

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

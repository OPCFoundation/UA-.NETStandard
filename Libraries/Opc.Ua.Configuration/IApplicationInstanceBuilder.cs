/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading.Tasks;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// A fluent API to build the application instance configuration.
    /// </summary>
    public interface IApplicationInstanceBuilder :
        IApplicationInstanceBuilderTypes,
        IApplicationInstanceBuilderServerSelected,
        IApplicationInstanceBuilderClientSelected,
        IApplicationInstanceBuilderSecurity,
        IApplicationInstanceBuilderServerPolicies,
        IApplicationInstanceBuilderCreate
    {
    };

    /// <summary>
    /// The server configuration.
    /// </summary>
    public interface IApplicationInstanceBuilderTypes :
        IApplicationInstanceBuilderServer,
        IApplicationInstanceBuilderClient
    {
    }

    /// <summary>
    /// The server configuration.
    /// </summary>
    public interface IApplicationInstanceBuilderServerSelected :
        IApplicationInstanceBuilderServerPolicies,
        IApplicationInstanceBuilderClient,
        IApplicationInstanceBuilderSecurity
    {
    }

    /// <summary>
    /// The server configuration.
    /// </summary>
    public interface IApplicationInstanceBuilderClientSelected :
        IApplicationInstanceBuilderSecurity
    {
    }

    /// <summary>
    /// The server configuration.
    /// </summary>
    public interface IApplicationInstanceBuilderServer
    {
        /// <summary>
        /// Configure instance to be used for UA server.
        /// </summary>
        IApplicationInstanceBuilderServerSelected AsServer(
            string[] baseAddresses,
            string[] alternateBaseAddresses = null);
    }

    /// <summary>
    /// The server configuration.
    /// </summary>
    public interface IApplicationInstanceBuilderClient
    {
        /// <summary>
        /// Configure instance to be used for UA client.
        /// </summary>
        IApplicationInstanceBuilderClientSelected AsClient();
    }

    /// <summary>
    /// 
    /// </summary>
    public interface IApplicationInstanceBuilderSecurity
    {
        /// <summary>
        /// Add the security configuration.
        /// </summary>
        /// <remarks>
        /// The pki root path default to the directory store
        /// location as defined in <see cref="CertificateStoreIdentifier.DefaultPKIRoot"/>
        /// A <see cref="CertificateStoreType"/> defaults to the corresponding default store location.
        /// </remarks>
        /// <param name="subjectName">Application certificate subject name as distinguished name. A DC=localhost entry is converted to the hostname. The common name CN= is mandatory.</param>
        /// <param name="pkiRoot">The path to the pki root.</param>
        /// <param name="appRoot">The path to the app cert store, if different than the pki root.</param>
        /// <param name="rejectedRoot">The path to the rejected certificate store.</param>
        IApplicationInstanceBuilderCreate AddSecurityConfiguration(
            string subjectName,
            string pkiRoot = null,
            string appRoot = null,
            string rejectedRoot = null
            );
    }

    /// <summary>
    /// 
    /// </summary>
    public interface IApplicationInstanceBuilderServerPolicies
    {
        /// <summary>
        /// Add the unsecure security policy type none to server configuration.
        /// </summary>
        IApplicationInstanceBuilderServerSelected AddUnsecurePolicyNone();

        /// <summary>
        /// Add the sign security policies to the server configuration.
        /// </summary>
        IApplicationInstanceBuilderServerSelected AddSignPolicies(bool deprecated = false);

        /// <summary>
        /// Add the sign and encrypt security policies to the server configuration.
        /// </summary>
        IApplicationInstanceBuilderServerSelected AddSignAndEncryptPolicies(bool deprecated = false);

        /// <summary>
        /// Add user token policy to server config.
        /// </summary>
        /// <param name="userTokenType">The user token type to add.</param>
        /// <param name="clean">replace policies before add.</param>
        IApplicationInstanceBuilderServerSelected AddUserTokenPolicy(UserTokenType userTokenType, bool clean = false);
    }

    /// <summary>
    /// Create and validate the application configuration.
    /// </summary>
    public interface IApplicationInstanceBuilderCreate
    {
        /// <summary>
        /// The application configuration.
        /// </summary>
        ApplicationConfiguration ApplicationConfiguration { get; }

        /// <summary>
        /// Creates and updates the application configuration.
        /// </summary>
        Task<ApplicationConfiguration> Create();
    }
}


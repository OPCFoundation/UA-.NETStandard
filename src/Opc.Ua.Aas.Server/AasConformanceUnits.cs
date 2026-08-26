/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
 *
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

namespace Opc.Ua.Aas.Server
{
    /// <summary>
    /// The conformance units of clause 10.
    /// </summary>
    /// <remarks>
    /// A Server publishes the units it actually enables in
    /// <c>Server/ServerCapabilities/ConformanceUnits</c>, so a Client can
    /// answer the conformance question without probing the AddressSpace.
    /// Clause 10 names the units but assigns no server profile URIs, so
    /// <c>ServerProfileArray</c> carries no AAS entry.
    /// </remarks>
    public static class AasConformanceUnits
    {
        /// <summary>
        /// Shells, submodels and concept descriptions as typed nodes.
        /// </summary>
        public const string Metamodel = "AAS-Metamodel";

        /// <summary>
        /// The submodel element types.
        /// </summary>
        public const string SubmodelElements = "AAS-SubmodelElements";

        /// <summary>
        /// The xsd type assignment of clauses 6.1.2 and 6.3.1.
        /// </summary>
        public const string ValueFidelity = "AAS-ValueFidelity";

        /// <summary>
        /// Materialization per clause 6.1.6.
        /// </summary>
        public const string InstanceMaterialization = "AAS-InstanceMaterialization";

        /// <summary>
        /// Both directions of clause 6.4.
        /// </summary>
        public const string LosslessRoundTrip = "AAS-LosslessRoundTrip";

        /// <summary>
        /// The registry root, groups and submodel documents.
        /// </summary>
        public const string Registry = "AAS-Registry";

        /// <summary>
        /// Source identities and derived identifiers per clause 6.5.3.
        /// </summary>
        public const string RegistryIdentity = "AAS-RegistryIdentity";

        /// <summary>
        /// Versions as the lifecycle record, clause 6.5.4.
        /// </summary>
        public const string RegistryVersioning = "AAS-RegistryVersioning";

        /// <summary>
        /// LookupShellsByAssetLink and GetSubmodel.
        /// </summary>
        public const string Discovery = "AAS-Discovery";

        /// <summary>
        /// AASOperationType.Invoke, clause 6.2.5.
        /// </summary>
        public const string OperationInvoke = "AAS-OperationInvoke";

        /// <summary>
        /// External references and the identity rule of clause 6.5.6.
        /// </summary>
        public const string Federation = "AAS-Federation";

        /// <summary>
        /// DisclosureTier and Authorization, clause 6.5.7.
        /// </summary>
        public const string DisclosureTiers = "AAS-DisclosureTiers";

        /// <summary>
        /// Generational materialization from stored documents, clause 6.5.9.
        /// </summary>
        public const string UpdateableRegistry = "AAS-UpdateableRegistry";

        /// <summary>
        /// The materialized environment served as filtered AAS and AASX
        /// documents, clause 6.5.10.
        /// </summary>
        public const string EnvironmentExport = "AAS-EnvironmentExport";

        /// <summary>
        /// Package stores and package resources.
        /// </summary>
        public const string Packages = "AAS-Packages";

        /// <summary>
        /// The package integrity requirements of clause 6.5.4.
        /// </summary>
        public const string PackageIntegrity = "AAS-PackageIntegrity";
    }
}

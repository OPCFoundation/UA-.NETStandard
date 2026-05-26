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

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Well-known DI <c>FunctionalGroupType</c> instance names defined by
    /// OPC 10000-100 §5.6. These names appear as <c>BrowseName</c>s on
    /// <c>DeviceType</c> (and subtypes) and aggregate references to the
    /// device's parameters/measurements by topic.
    /// </summary>
    /// <remarks>
    /// Use these constants with
    /// <see cref="IDeviceBuilder{TDevice}.WithFunctionalGroup(QualifiedName, System.Action{IFunctionalGroupBuilder})"/>
    /// to ensure spec-compliant browse names. The typed convenience
    /// methods on <see cref="IDeviceBuilder{TDevice}"/> (e.g.
    /// <see cref="IDeviceBuilder{TDevice}.WithConfigurationGroup"/>) use
    /// these constants internally.
    /// </remarks>
    public static class WellKnownFunctionalGroups
    {
        /// <summary>Spec-defined name for the Identification group.</summary>
        public const string Identification = "Identification";

        /// <summary>Spec-defined name for the Configuration group.</summary>
        public const string Configuration = "Configuration";

        /// <summary>Spec-defined name for the Maintenance group.</summary>
        public const string Maintenance = "Maintenance";

        /// <summary>Spec-defined name for the Diagnostics group.</summary>
        public const string Diagnostics = "Diagnostics";

        /// <summary>Spec-defined name for the Status group.</summary>
        public const string Status = "Status";

        /// <summary>Spec-defined name for the Operational group.</summary>
        public const string Operational = "Operational";

        /// <summary>Spec-defined name for the Statistics group.</summary>
        public const string Statistics = "Statistics";

        /// <summary>Spec-defined name for the OperationCounters group.</summary>
        public const string OperationCounters = "OperationCounters";
    }
}

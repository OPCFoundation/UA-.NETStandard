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
using Opc.Ua.PackML;

namespace Opc.Ua.Scales.Server
{
    /// <summary>
    /// Options for the OPC 40200 node manager.
    /// </summary>
    public sealed class ScalesServerOptions
    {
        /// <summary>
        /// Gets or sets whether every top-level scale and scale system is also
        /// organized into the OPC 40001-1 <c>Machines</c> folder. Defaults to
        /// <see langword="true"/>.
        /// </summary>
        /// <remarks>
        /// Every OPC 40200 scale facet requires the Machinery Machine
        /// Identification Server Facet, which contains the "Machinery Find
        /// Machines" conformance unit. A scale that is only reachable from the
        /// <c>DeviceSet</c> does not satisfy it.
        /// </remarks>
        public bool OrganizeIntoMachinesFolder { get; set; } = true;

        /// <summary>
        /// Gets or sets whether method input arguments carrying engineering
        /// units must be SI units (the "Scales InputArgument_SI_units"
        /// conformance unit of the International System of Units facet).
        /// Defaults to <see langword="false"/>.
        /// </summary>
        public bool RequireSiUnits { get; set; }

        /// <summary>
        /// Gets or sets the state a scale's optional PackML state machine is
        /// initialised in. Defaults to <see cref="PackMLInitialState.Idle"/>:
        /// a scale is usually ready to weigh when the server starts.
        /// </summary>
        public PackMLInitialState PackMLInitialState { get; set; } = PackMLInitialState.Idle;

        /// <summary>
        /// Gets or sets the semi-automatic zero-setting range, as a fraction of
        /// the maximum capacity, within which <c>SetZero</c> is accepted and
        /// <c>InsideZero</c> is reported. Defaults to 0.04 (4 %, the
        /// OIML R 76-1 limit for semi-automatic zero-setting).
        /// </summary>
        public double ZeroSettingRange { get; set; } = 0.04;

        /// <summary>
        /// Gets or sets extra namespace URIs the node manager registers, for a
        /// subclass that composes further models into the same address space.
        /// </summary>
        public string[] AdditionalNamespaceUris { get; set; } = [];

        internal ScalesServerOptions Validate()
        {
            AdditionalNamespaceUris ??= [];
            foreach (string uri in AdditionalNamespaceUris)
            {
                if (string.IsNullOrEmpty(uri))
                {
                    throw new ArgumentException(
                        "An additional namespace URI must not be null or empty.",
                        nameof(AdditionalNamespaceUris));
                }
            }
            if (double.IsNaN(ZeroSettingRange) || ZeroSettingRange < 0 || ZeroSettingRange > 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ZeroSettingRange),
                    ZeroSettingRange,
                    "The zero-setting range is a fraction of the capacity between 0 and 1.");
            }
            return this;
        }
    }
}

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

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Common identification data exposed by Robotics components and the
    /// underlying OPC UA DI model.
    /// </summary>
    public sealed record RoboticsComponentIdentification
    {
        /// <summary>
        /// The component instance NodeId.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The component BrowseName.
        /// </summary>
        public QualifiedName BrowseName { get; init; } = QualifiedName.Null;

        /// <summary>
        /// The user-facing component name.
        /// </summary>
        public LocalizedText ComponentName { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The DI asset identifier.
        /// </summary>
        public string? AssetId { get; init; }

        /// <summary>
        /// The manufacturer name.
        /// </summary>
        public LocalizedText Manufacturer { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The manufacturer model name.
        /// </summary>
        public LocalizedText Model { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The manufacturer product code.
        /// </summary>
        public string? ProductCode { get; init; }

        /// <summary>
        /// The manufacturer serial number.
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// A URI or location for the component manual.
        /// </summary>
        public string? DeviceManual { get; init; }
    }
}

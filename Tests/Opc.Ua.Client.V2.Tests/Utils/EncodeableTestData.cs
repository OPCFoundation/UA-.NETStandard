// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    /// <summary>
    /// Simple wrapper for encodeable types used in test parameterization.
    /// </summary>
    /// <typeparam name="T">The encodeable type.</typeparam>
    public class EncodeableTestData<T>
        where T : class, IEncodeable
    {
        /// <summary>
        /// The actual value of the type.
        /// </summary>
        public T Value { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EncodeableTestData{T}"/> class.
        /// </summary>
        public EncodeableTestData()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EncodeableTestData{T}"/> class.
        /// </summary>
        /// <param name="value">The value to wrap.</param>
        public EncodeableTestData(T value) => Value = value;

        /// <inheritdoc/>
        public override string ToString() => Value?.ToString() ?? "(null)";
    }
}

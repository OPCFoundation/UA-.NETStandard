// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Xunit.Abstractions;

    /// <summary>
    /// Makes encodeable types serializable for xunit
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class XunitSerializableEncodeable<T> : IXunitSerializable
        where T : class, IEncodeable
    {
        /// <summary>
        /// The actual value of the type
        /// </summary>
        public T? Value { get; set; }

        /// <inheritdoc/>
        public XunitSerializableEncodeable()
        {
        }

        /// <inheritdoc/>
        public XunitSerializableEncodeable(T? value) => Value = value;

        public void Deserialize(IXunitSerializationInfo info)
        {
            var json = info.GetValue<string>(nameof(Value));
            if (json == null)
            {
                Value = null;
                return;
            }
            using var decoder = new JsonDecoder(json, new ServiceMessageContext());
            Value = (T?)decoder.ReadEncodeable(null, typeof(T));
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            if (Value == null)
            {
                info.AddValue(nameof(Value), null);
                return;
            }
            using var encoder = new JsonEncoder(new ServiceMessageContext(), true);
            encoder.WriteEncodeable(null, Value, typeof(T));
            info.AddValue(nameof(Value), encoder.CloseAndReturnText());
        }
    }
}

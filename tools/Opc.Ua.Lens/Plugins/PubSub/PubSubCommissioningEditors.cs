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
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;

namespace UaLens.Plugins.PubSub
{
    internal sealed partial class PubSubFieldDraft : ObservableObject
    {
        public PubSubFieldDraft(PubSubFieldConfiguration field)
        {
            ArgumentNullException.ThrowIfNull(field);
            Name = field.Name;
            Type = field.Type;
            DataTypeId = field.DataTypeId;
            SampleValue = field.SampleValue;
            SampleSchema = field.SampleSchema;
            MaxStringLength = field.MaxStringLength;
            FieldId = field.FieldId == Guid.Empty ? string.Empty : field.FieldId.ToString("D");
            ValueRank = field.ValueRank;
            Dimensions = string.Join(", ", field.ArrayDimensions.ToList()
                .Select(dimension => dimension.ToString(CultureInfo.InvariantCulture)));
            SourceNodeId = field.SourceNodeId;
            TargetNodeId = field.TargetNodeId;
        }

        public ObservableCollection<BuiltInType> Types { get; } =
            new(Enum.GetValues<BuiltInType>().Where(type =>
                PubSubConfigurationValidation.IsScalarType(type) || type == BuiltInType.ExtensionObject));

        public string SampleSummary => SampleValue.Length == 0
            ? "Generated sample (supported primitive types only)" : "Reviewed local sample; edit after schema changes";

        public void SetSample(PubSubFieldConfiguration field)
        {
            SampleValue = field.SampleValue;
            SampleSchema = field.SampleSchema;
        }

        public PubSubFieldConfiguration ToConfiguration()
        {
            if (FieldId.Length > 0 && !Guid.TryParseExact(FieldId, "D", out _))
            {
                throw new ArgumentException("Use a canonical field UUID or leave it empty.");
            }
            ArrayOf<uint> dimensions = [];
            if (!string.IsNullOrWhiteSpace(Dimensions))
            {
                string[] parts = Dimensions.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length > PubSubFieldSchema.MaximumRank)
                {
                    throw new ArgumentException("Use at most eight comma-separated dimensions.");
                }
                var values = new uint[parts.Length];
                for (int index = 0; index < parts.Length; index++)
                {
                    if (!uint.TryParse(
                        parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out values[index]))
                    {
                        throw new ArgumentException("Every dimension must be an unsigned integer.");
                    }
                }
                dimensions = values;
            }
            if (!PubSubFieldSchema.IsValidShape(ValueRank, dimensions))
            {
                throw new ArgumentException("Choose scalar (-1), array (1) or matrix (2-8) with bounded dimensions.");
            }
            return new PubSubFieldConfiguration
            {
                Name = Name,
                Type = Type,
                DataTypeId = DataTypeId,
                SampleValue = SampleValue,
                SampleSchema = SampleSchema,
                FieldId = FieldId.Length == 0 ? Guid.Empty : Guid.ParseExact(FieldId, "D"),
                ValueRank = ValueRank,
                ArrayDimensions = dimensions,
                MaxStringLength = MaxStringLength,
                SourceNodeId = SourceNodeId,
                TargetNodeId = TargetNodeId
            };
        }

        [ObservableProperty]
        public partial string Name { get; set; }

        [ObservableProperty]
        public partial BuiltInType Type { get; set; }

        [ObservableProperty]
        public partial string DataTypeId { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SampleSummary))]
        public partial string SampleValue { get; set; }

        [ObservableProperty]
        public partial string SampleSchema { get; set; }

        [ObservableProperty]
        public partial string FieldId { get; set; }

        [ObservableProperty]
        public partial int ValueRank { get; set; }

        [ObservableProperty]
        public partial string Dimensions { get; set; }

        [ObservableProperty]
        public partial uint MaxStringLength { get; set; }

        [ObservableProperty]
        public partial string SourceNodeId { get; set; }

        [ObservableProperty]
        public partial string TargetNodeId { get; set; }
    }

    internal sealed partial class PubSubActionInputEditor : ObservableObject
    {
        public PubSubActionInputEditor(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public ObservableCollection<BuiltInType> Types { get; } =
            new(Enum.GetValues<BuiltInType>().Where(PubSubActionInputs.IsSupportedType));

        public PubSubActionInputDraft ToDraft()
        {
            return new PubSubActionInputDraft(Name, Type, Text);
        }

        [ObservableProperty]
        public partial string Name { get; set; }

        [ObservableProperty]
        public partial BuiltInType Type { get; set; } = BuiltInType.Int32;

        [ObservableProperty]
        public partial string Text { get; set; } = string.Empty;
    }

    internal sealed partial class PubSubMaskOption : ObservableObject
    {
        public PubSubMaskOption(string name, uint flag, uint selected)
        {
            Name = name;
            Flag = flag;
            IsSelected = (flag & selected) != 0;
        }

        public string Name { get; }

        public uint Flag { get; }

        [ObservableProperty]
        public partial bool IsSelected { get; set; }
    }
}

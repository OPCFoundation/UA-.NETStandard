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
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;

namespace UaLens.Plugins.PubSub;

internal sealed partial class PubSubFieldDraft : ObservableObject
{
    public PubSubFieldDraft(PubSubFieldConfiguration field)
    {
        ArgumentNullException.ThrowIfNull(field);
        m_name = field.Name;
        m_type = field.Type;
        m_fieldId = field.FieldId == Guid.Empty ? string.Empty : field.FieldId.ToString("D");
        m_sourceNodeId = field.SourceNodeId;
        m_targetNodeId = field.TargetNodeId;
    }

    public ObservableCollection<BuiltInType> Types { get; } =
        new(Enum.GetValues<BuiltInType>().Where(PubSubConfigurationValidation.IsScalarType));

    public PubSubFieldConfiguration ToConfiguration()
    {
        if (FieldId.Length > 0 && !Guid.TryParseExact(FieldId, "D", out _))
        {
            throw new ArgumentException("Use a canonical field UUID or leave it empty.");
        }
        return new PubSubFieldConfiguration
        {
            Name = Name,
            Type = Type,
            FieldId = FieldId.Length == 0 ? Guid.Empty : Guid.ParseExact(FieldId, "D"),
            SourceNodeId = SourceNodeId,
            TargetNodeId = TargetNodeId
        };
    }

    [ObservableProperty]
    private string m_name;

    [ObservableProperty]
    private BuiltInType m_type;

    [ObservableProperty]
    private string m_fieldId;

    [ObservableProperty]
    private string m_sourceNodeId;

    [ObservableProperty]
    private string m_targetNodeId;
}

internal sealed partial class PubSubActionInputEditor : ObservableObject
{
    public PubSubActionInputEditor(string name)
    {
        m_name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public ObservableCollection<BuiltInType> Types { get; } =
        new(Enum.GetValues<BuiltInType>().Where(PubSubActionInputs.IsSupportedType));

    public PubSubActionInputDraft ToDraft()
    {
        return new PubSubActionInputDraft(Name, Type, Text);
    }

    [ObservableProperty]
    private string m_name;

    [ObservableProperty]
    private BuiltInType m_type = BuiltInType.Int32;

    [ObservableProperty]
    private string m_text = string.Empty;
}

internal sealed partial class PubSubMaskOption : ObservableObject
{
    public PubSubMaskOption(string name, uint flag, uint selected)
    {
        Name = name;
        Flag = flag;
        m_isSelected = (flag & selected) != 0;
    }

    public string Name { get; }

    public uint Flag { get; }

    [ObservableProperty]
    private bool m_isSelected;
}

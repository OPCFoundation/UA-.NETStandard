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
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using UaLens.Connection;
using UaLens.StructuredValues;

namespace UaLens.Plugins.Companions
{
    /// <summary>
    /// Isolated typed form state. Parsing creates a value, never a server request.
    /// </summary>
    internal sealed partial class CompanionInputEditor : ObservableObject
    {
        public CompanionInputEditor(
            CompanionInputDefinition definition,
            Action changed,
            Func<Task<string?>>? pickFile = null,
            Func<CompanionInputEditor, Task>? editValue = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            m_changed = changed ?? throw new ArgumentNullException(nameof(changed));
            m_pickFile = pickFile;
            m_editValue = editValue;
        }

        [ObservableProperty]
        public partial string Text { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool Checked { get; set; }

        [ObservableProperty]
        public partial bool Omitted { get; set; }

        public CompanionInputDefinition Definition { get; }

        public bool IsBoolean => !RequiresEditor && Definition.DataType == BuiltInType.Boolean;

        public bool RequiresEditor => Definition.RequiresEditor;

        public bool IsText => !RequiresEditor && !IsBoolean;

        public bool CanOmit =>
            !Definition.Required &&
            Definition.DataType == BuiltInType.ExtensionObject &&
            Definition.ValueRank == ValueRanks.Scalar;

        public string TypedSummary => Omitted
            ? "Optional structure omitted."
            : m_hasValue
                ? $"Reviewed {m_value.TypeInfo.BuiltInType}, rank {m_value.TypeInfo.ValueRank}."
                : "Open the typed editor and accept a value before preparing this task.";

        private bool CanPickFile => Definition.IsFileSource && m_pickFile is not null;

        public CompanionValue Capture()
        {
            if (Omitted)
            {
                if (!CanOmit)
                {
                    throw new InvalidOperationException($"{Definition.DisplayName} cannot be omitted.");
                }
                return new CompanionValue(Definition.Name, Variant.From(ExtensionObject.Null));
            }
            if (RequiresEditor)
            {
                if (!m_hasValue || m_context is null)
                {
                    throw new InvalidOperationException($"Edit and accept {Definition.DisplayName} first.");
                }
                m_requireCurrent?.Invoke();
                return new CompanionValue(Definition.Name, DataValueCodec.Snapshot(m_value, m_context))
                {
                    InputSchemaDigest = m_schemaDigest.Copy()
                };
            }
            if (Text.Length > 65536 || (Definition.Required && !IsBoolean && string.IsNullOrWhiteSpace(Text)))
            {
                throw new ArgumentException($"{Definition.DisplayName} requires a bounded, nonempty value.");
            }
            if (IsBoolean)
            {
                return new CompanionValue(Definition.Name, Variant.From(Checked));
            }
            if (!StructuredScalarValue.TryParse(
                Definition.DataType, Text, Variant.Null, out Variant value, out string? error))
            {
                throw new ArgumentException($"{Definition.DisplayName}: {error}");
            }
            return new CompanionValue(Definition.Name, value);
        }

        public Variant GetInitialValue()
        {
            if (!m_hasValue || m_context is null)
            {
                return Variant.Null;
            }
            m_requireCurrent?.Invoke();
            return DataValueCodec.Snapshot(m_value, m_context);
        }

        public void AcceptValue(
            Variant value, IServiceMessageContext context, Action requireCurrent, ByteString schemaDigest = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(requireCurrent);
            requireCurrent();
            m_value = DataValueCodec.Snapshot(value, context);
            m_context = context;
            m_requireCurrent = requireCurrent;
            m_schemaDigest = schemaDigest.Copy();
            m_hasValue = true;
            Omitted = false;
            m_changed();
            OnPropertyChanged(nameof(TypedSummary));
        }

        [RelayCommand]
        private Task EditAsync()
        {
            if (!RequiresEditor || m_editValue is null)
            {
                throw new InvalidOperationException("The typed task editor is unavailable.");
            }
            m_changed();
            return m_editValue(this);
        }

        partial void OnTextChanged(string value)
        {
            m_changed();
        }

        partial void OnCheckedChanged(bool value)
        {
            m_changed();
        }

        partial void OnOmittedChanged(bool value)
        {
            m_changed();
            OnPropertyChanged(nameof(TypedSummary));
        }

        [RelayCommand(CanExecute = nameof(CanPickFile))]
        private async Task PickFileAsync()
        {
            Func<Task<string?>> picker = m_pickFile ??
                throw new InvalidOperationException("A file picker is not available for this form.");
            string? path = await picker().ConfigureAwait(true);
            if (path is not null)
            {
                Text = path;
            }
        }

        private readonly Action m_changed;
        private readonly Func<Task<string?>>? m_pickFile;
        private readonly Func<CompanionInputEditor, Task>? m_editValue;
        private IServiceMessageContext? m_context;
        private Action? m_requireCurrent;
        private Variant m_value;
        private bool m_hasValue;
        private ByteString m_schemaDigest;
    }
}

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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;

namespace UaLens.Workspace;

internal enum CommandScope
{
    Application,
    Connection,
    Document,
    Selection
}

/// <summary>
/// One command identity shared by menus, toolbars, search and shortcut dispatch.
/// Applicability is live; busy state and execution come from the existing command.
/// </summary>
internal sealed class CommandDescriptor : ObservableObject
{
    public CommandDescriptor(
        string id,
        CommandScope scope,
        string label,
        string? shortcut,
        ICommand command,
        Func<bool>? applicability = null,
        bool allowInTextInput = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        Id = id;
        Scope = scope;
        Label = label;
        Shortcut = shortcut;
        m_action = command ?? throw new ArgumentNullException(nameof(command));
        m_applicability = applicability;
        AllowInTextInput = allowInTextInput;
        Command = new AsyncRelayCommand(ExecuteCoreAsync, () => IsApplicable);
        Command.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsApplicable));
        };
    }

    public string Id { get; }
    public CommandScope Scope { get; }
    public string Label { get; }
    public string? Shortcut { get; }
    public IAsyncRelayCommand Command { get; }
    public bool AllowInTextInput { get; }
    public bool IsApplicable => (m_applicability?.Invoke() ?? true) && m_action.CanExecute(null) && !IsBusy;
    public bool IsBusy => Command.IsRunning;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsApplicable)
        {
            return;
        }
        Task execution = Command.ExecuteAsync(null);
        using CancellationTokenRegistration registration = cancellationToken.Register(Command.Cancel);
        await execution.ConfigureAwait(true);
    }

    internal void Refresh()
    {
        Command.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsApplicable));
    }

    private async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!(m_applicability?.Invoke() ?? true) || !m_action.CanExecute(null))
        {
            return;
        }
        if (m_action is IAsyncRelayCommand asynchronous)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(asynchronous.Cancel);
            await asynchronous.ExecuteAsync(null).ConfigureAwait(true);
        }
        else
        {
            m_action.Execute(null);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private readonly ICommand m_action;
    private readonly Func<bool>? m_applicability;
}

/// <summary>
/// Validates the complete command set before publishing a new immutable snapshot.
/// Shortcut matching preserves text-editing keys and rejects ambiguous gestures.
/// </summary>
internal sealed class CommandRegistry : ObservableObject
{
    public CommandRegistry(ArrayOf<CommandDescriptor> commands = default)
    {
        Register(commands);
    }

    public ArrayOf<CommandDescriptor> All
    {
        get => m_all;
        private set => SetProperty(ref m_all, value);
    }

    /// <summary>
    /// Registers an atomic batch, including desktop-owned dialog commands. Duplicate
    /// IDs or shortcuts reject the entire batch, leaving the current registry intact.
    /// </summary>
    public void Register(ArrayOf<CommandDescriptor> commands)
    {
        var byId = new Dictionary<string, CommandDescriptor>(m_byId, StringComparer.Ordinal);
        var shortcuts = new Dictionary<string, CommandDescriptor>(m_shortcuts, StringComparer.Ordinal);
        foreach (CommandDescriptor command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentException.ThrowIfNullOrWhiteSpace(command.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(command.Label);
            ArgumentNullException.ThrowIfNull(command.Command);
            if (!byId.TryAdd(command.Id, command))
            {
                throw new ArgumentException($"Duplicate command ID '{command.Id}'.", nameof(commands));
            }
            if (command.Shortcut is not null)
            {
                string gesture = NormalizeShortcut(command.Shortcut);
                if (IsTextEditingShortcut(gesture))
                {
                    throw new ArgumentException(
                        $"'{command.Shortcut}' is reserved for text editing.", nameof(commands));
                }
                if (!shortcuts.TryAdd(gesture, command))
                {
                    throw new ArgumentException(
                        $"Duplicate command shortcut '{command.Shortcut}'.", nameof(commands));
                }
            }
        }
        m_byId = byId;
        m_shortcuts = shortcuts;
        All = [.. byId.Values];
    }

    public bool TryGet(string id, [NotNullWhen(true)] out CommandDescriptor? command)
    {
        ArgumentNullException.ThrowIfNull(id);
        return m_byId.TryGetValue(id, out command);
    }

    public bool TryResolveShortcut(
        string shortcut,
        bool isTextInput,
        [NotNullWhen(true)] out CommandDescriptor? command)
    {
        command = null;
        string gesture = NormalizeShortcut(shortcut);
        if (IsTextEditingShortcut(gesture)
            || !m_shortcuts.TryGetValue(gesture, out CommandDescriptor? candidate)
            || (isTextInput && !candidate.AllowInTextInput)
            || !candidate.IsApplicable)
        {
            return false;
        }
        command = candidate;
        return true;
    }

    /// <summary>
    /// Refreshes command availability after connection, selection or lifecycle changes.
    /// </summary>
    public void Refresh()
    {
        foreach (CommandDescriptor descriptor in All)
        {
            descriptor.Refresh();
        }
    }

    private static bool IsTextEditingShortcut(string shortcut)
        => shortcut is "CTRL+A" or "CTRL+C" or "CTRL+V" or "CTRL+X" or "CTRL+Z" or "CTRL+Y";

    private static string NormalizeShortcut(string shortcut)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcut);
        string[] parts = shortcut.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A shortcut must contain one key and optional modifiers.", nameof(shortcut));
        }

        var modifiers = new HashSet<string>(StringComparer.Ordinal);
        string? key = null;
        foreach (string part in parts)
        {
            string normalized = part.ToUpperInvariant();
            normalized = normalized switch
            {
                "CONTROL" => "CTRL",
                "WINDOWS" or "WIN" or "COMMAND" or "CMD" => "META",
                _ => normalized
            };
            if (normalized is "CTRL" or "ALT" or "SHIFT" or "META")
            {
                if (!modifiers.Add(normalized))
                {
                    throw new ArgumentException("A shortcut repeats a modifier.", nameof(shortcut));
                }
            }
            else if (key is null)
            {
                key = normalized;
            }
            else
            {
                throw new ArgumentException("A shortcut contains more than one key.", nameof(shortcut));
            }
        }
        if (key is null)
        {
            throw new ArgumentException("A shortcut must contain a key.", nameof(shortcut));
        }
        string prefix = string.Concat(
            s_modifierOrder
                .Where(modifiers.Contains)
                .Select(modifier => modifier + "+"));
        return prefix + key;
    }

    private Dictionary<string, CommandDescriptor> m_byId = new(StringComparer.Ordinal);
    private Dictionary<string, CommandDescriptor> m_shortcuts = new(StringComparer.Ordinal);
    private ArrayOf<CommandDescriptor> m_all = [];
    private static readonly string[] s_modifierOrder = ["CTRL", "ALT", "SHIFT", "META"];
}

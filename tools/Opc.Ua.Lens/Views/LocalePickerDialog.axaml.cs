// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Views;

/// <summary>
/// Edits the active session's preferred-locales list, calling
/// <c>ISession.ChangePreferredLocalesAsync</c> on Apply. Port of
/// <c>UA-.NETStandard-Samples/Samples/ClientControls.Net4/Common/SelectLocaleDlg.cs</c>
/// shaped for UaLens's connection service.
/// </summary>
internal sealed partial class LocalePickerDialog : Window
{
    public LocalePickerDialog(ConnectionService connection)
    {
        m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
        InitializeComponent();

        var newBox = this.RequiredControl<TextBox>("NewLocaleBox");
        var add = this.RequiredControl<Button>("AddButton");
        var list = this.RequiredControl<ListBox>("LocaleList");
        var up = this.RequiredControl<Button>("UpButton");
        var down = this.RequiredControl<Button>("DownButton");
        var remove = this.RequiredControl<Button>("RemoveButton");
        var apply = this.RequiredControl<Button>("ApplyButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        list.ItemsSource = m_locales;

        // Seed from the session if one is connected, else the .NET UI culture.
        if (connection.CurrentSession is { } session)
        {
            foreach (string l in session.PreferredLocales)
            {
                if (!string.IsNullOrEmpty(l))
                {
                    m_locales.Add(l);
                }
            }
        }
        if (m_locales.Count == 0)
        {
            m_locales.Add(System.Globalization.CultureInfo.CurrentUICulture.Name);
            if (!string.Equals(m_locales[0], "en-US", StringComparison.Ordinal))
            {
                m_locales.Add("en-US");
            }
        }

        add.Click += (_, _) =>
        {
            string v = (newBox.Text ?? string.Empty).Trim();
            if (v.Length == 0 || m_locales.Contains(v))
            {
                return;
            }
            m_locales.Add(v);
            newBox.Text = string.Empty;
            list.SelectedIndex = m_locales.Count - 1;
        };
        up.Click += (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i > 0)
            {
                (m_locales[i], m_locales[i - 1]) = (m_locales[i - 1], m_locales[i]);
                list.SelectedIndex = i - 1;
            }
        };
        down.Click += (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i >= 0 && i < m_locales.Count - 1)
            {
                (m_locales[i], m_locales[i + 1]) = (m_locales[i + 1], m_locales[i]);
                list.SelectedIndex = i + 1;
            }
        };
        remove.Click += (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i >= 0)
            {
                m_locales.RemoveAt(i);
                if (m_locales.Count > 0)
                {
                    list.SelectedIndex = Math.Min(i, m_locales.Count - 1);
                }
            }
        };
        apply.Click += async (_, _) =>
        {
            if (m_connection.CurrentSession is not { } session)
            {
                Close();
                return;
            }
            var snapshot = new ArrayOf<string>();
            foreach (string l in m_locales)
            {
                snapshot = snapshot.AddItem(l);
            }
            try
            {
                await session.ChangePreferredLocalesAsync(snapshot, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Title = $"Preferred locales — apply failed: {ex.Message}";
                return;
            }
            Close();
        };
        cancel.Click += (_, _) => Close();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private readonly ConnectionService m_connection;
    private readonly ObservableCollection<string> m_locales = new();
}

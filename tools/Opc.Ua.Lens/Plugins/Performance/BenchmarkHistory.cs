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
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;

namespace UaLens.Plugins.Performance;

/// <summary>
/// Bounded newest-first results and explicit comparison selection. Retention
/// never silently substitutes another run for the user's chosen baseline.
/// </summary>
internal sealed partial class BenchmarkHistory : ObservableObject
{
    public BenchmarkHistory()
    {
        Runs = new ReadOnlyObservableCollection<BenchmarkRunRow>(m_runs);
    }

    public ReadOnlyObservableCollection<BenchmarkRunRow> Runs { get; }

    public BenchmarkRunRow? SelectedRun
    {
        get => m_selectedRun;
        set
        {
            if (value is not null && !m_runs.Contains(value))
            {
                throw new ArgumentException("The selected run is not retained in this history.", nameof(value));
            }
            if (SetProperty(ref m_selectedRun, value))
            {
                RefreshComparison();
            }
        }
    }

    public BenchmarkRunRow? BaselineRun
    {
        get => m_baselineRun;
        private set
        {
            if (m_baselineRun is { } previous)
            {
                previous.IsBaseline = false;
            }
            if (SetProperty(ref m_baselineRun, value))
            {
                OnPropertyChanged(nameof(BaselineText));
                OnPropertyChanged(nameof(HasBaseline));
            }
            if (value is not null)
            {
                value.IsBaseline = true;
            }
        }
    }

    public BenchmarkComparison? Comparison
    {
        get => m_comparison;
        private set
        {
            if (SetProperty(ref m_comparison, value))
            {
                OnPropertyChanged(nameof(HasComparison));
            }
        }
    }

    public bool HasComparison => Comparison is not null;
    public bool HasBaseline => BaselineRun is not null;
    public string BaselineText => BaselineRun?.SelectionDisplay ?? "No baseline chosen";
    public string RetentionNotice { get; private set; } = string.Empty;
    public int DiscardedOnImport { get; private set; }

    public string Status
    {
        get
        {
            string comparison = BaselineRun is null
                ? "Select a run, choose Use as baseline, then select the run to compare."
                : SelectedRun is null ? "Choose a run to compare with the selected baseline."
                : ReferenceEquals(BaselineRun, SelectedRun)
                    ? "Baseline and selected run are the same; all deltas are zero."
                    : Comparison!.Summary;
            return string.IsNullOrEmpty(RetentionNotice) ? comparison : RetentionNotice + "\n" + comparison;
        }
    }

    public bool HighlightLatestThree
    {
        get => m_highlightLatestThree;
        set
        {
            if (SetProperty(ref m_highlightLatestThree, value))
            {
                RefreshHighlights();
            }
        }
    }

    public void Add(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Validate();
        if (m_runs.Any(row => row.Run.Id == run.Id))
        {
            throw new ArgumentException("The run is already retained in this history.", nameof(run));
        }

        var added = new BenchmarkRunRow(run);
        int index = 0;
        while (index < m_runs.Count && m_runs[index].Run.TimestampUtc > run.TimestampUtc)
        {
            index++;
        }
        m_runs.Insert(index, added);
        if (m_runs.Count > Capacity)
        {
            BenchmarkRunRow removed = m_runs[^1];
            m_runs.RemoveAt(m_runs.Count - 1);
            if (ReferenceEquals(BaselineRun, removed))
            {
                BaselineRun = null;
                RetentionNotice = "The chosen baseline was evicted by the 64-run limit; choose another baseline.";
            }
            if (ReferenceEquals(SelectedRun, removed))
            {
                m_selectedRun = null;
                OnPropertyChanged(nameof(SelectedRun));
            }
        }
        if (m_runs.Contains(added))
        {
            SelectedRun = added;
        }
        RefreshHighlights();
        RefreshComparison();
    }

    public void Replace(BenchmarkArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        archive.Validate();
        BenchmarkRunRow[] retained = archive.Runs.Span.ToArray()
            .OrderByDescending(run => run.TimestampUtc)
            .Take(Capacity)
            .Select(run => new BenchmarkRunRow(run))
            .ToArray();

        BaselineRun = null;
        SelectedRun = null;
        m_runs.Clear();
        foreach (BenchmarkRunRow row in retained)
        {
            m_runs.Add(row);
        }
        DiscardedOnImport = archive.Runs.Count - retained.Length;
        BaselineRun = retained.FirstOrDefault(row => row.Run.Id == archive.BaselineId);
        SelectedRun = retained.FirstOrDefault(row => row.Run.Id == archive.SelectedId) ?? retained.FirstOrDefault();
        RetentionNotice = DiscardedOnImport == 0 ? string.Empty : string.Format(CultureInfo.InvariantCulture,
            "Kept the newest {0} runs; discarded {1} older runs.", retained.Length, DiscardedOnImport);
        if (archive.BaselineId.HasValue && BaselineRun is null)
        {
            RetentionNotice += " The chosen baseline was not retained; choose another baseline.";
        }
        if (archive.SelectedId.HasValue && !retained.Any(row => row.Run.Id == archive.SelectedId))
        {
            RetentionNotice += " The selected run was not retained; showing the newest run.";
        }
        OnPropertyChanged(nameof(DiscardedOnImport));
        RefreshHighlights();
        RefreshComparison();
    }

    public BenchmarkArchive Capture()
    {
        return new BenchmarkArchive(
            new ArrayOf<BenchmarkRun>(m_runs.Select(row => row.Run).ToArray()),
            BaselineRun?.Run.Id,
            SelectedRun?.Run.Id);
    }

    private bool CanUseSelectedAsBaseline()
    {
        return SelectedRun is not null && !ReferenceEquals(SelectedRun, BaselineRun);
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedAsBaseline))]
    private void UseSelectedAsBaseline()
    {
        if (SelectedRun is null)
        {
            throw new InvalidOperationException("Select a retained run before choosing a baseline.");
        }
        BaselineRun = SelectedRun;
        RetentionNotice = string.Empty;
        RefreshComparison();
    }

    [RelayCommand(CanExecute = nameof(HasBaseline))]
    private void ClearBaseline()
    {
        BaselineRun = null;
        RetentionNotice = string.Empty;
        RefreshComparison();
    }

    private void RefreshComparison()
    {
        Comparison = BaselineRun is { } baseline && SelectedRun is { } selected
            ? new BenchmarkComparison(baseline.Run, selected.Run)
            : null;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(RetentionNotice));
        UseSelectedAsBaselineCommand.NotifyCanExecuteChanged();
        ClearBaselineCommand.NotifyCanExecuteChanged();
    }

    private void RefreshHighlights()
    {
        for (int i = 0; i < m_runs.Count; i++)
        {
            m_runs[i].IsHighlighted = HighlightLatestThree && i < 3;
        }
    }

    public const int Capacity = 64;

    private readonly ObservableCollection<BenchmarkRunRow> m_runs = [];
    private BenchmarkRunRow? m_selectedRun;
    private BenchmarkRunRow? m_baselineRun;
    private BenchmarkComparison? m_comparison;
    private bool m_highlightLatestThree;
}

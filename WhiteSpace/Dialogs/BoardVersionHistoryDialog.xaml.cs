using System;
using System.Collections.Generic;
using System.Windows;
using WhiteSpace.Models;

namespace WhiteSpace.Dialogs;

public partial class BoardVersionHistoryDialog : Window
{
    public sealed class VersionListItem
    {
        public string Key { get; init; } = "";
        public BoardVersionSnapshot Snapshot { get; init; } = null!;
        public string DisplayLabel { get; init; } = "";
    }

    public bool DeleteRequested { get; private set; }

    public BoardVersionHistoryDialog(IReadOnlyList<VersionListItem> items)
    {
        InitializeComponent();
        foreach (var item in items)
        {
            VersionsList.Items.Add(item);
        }

        if (VersionsList.Items.Count > 0)
        {
            VersionsList.SelectedIndex = 0;
        }
    }

    public VersionListItem? SelectedVersion =>
        VersionsList.SelectedItem as VersionListItem;

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedVersion == null)
        {
            return;
        }

        DeleteRequested = true;
        DialogResult = false;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

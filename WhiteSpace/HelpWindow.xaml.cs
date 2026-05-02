using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WhiteSpace.Services;

namespace WhiteSpace;

public partial class HelpWindow : Window
{
    private readonly Stack<string> _history = new();
    private bool _suppressHistory;

    public HelpWindow()
    {
        InitializeComponent();
        InitTopics();
    }

    private void InitTopics()
    {
        TopicsListBox.Items.Clear();
        foreach (var t in HelpContent.Topics)
            TopicsListBox.Items.Add(t);

        _suppressHistory = true;
        if (TopicsListBox.Items.Count > 0)
            TopicsListBox.SelectedIndex = 0;
        _suppressHistory = false;

        _history.Clear();
        if (TopicsListBox.SelectedItem is HelpContent.HelpTopic first)
        {
            _history.Push(first.Id);
            ApplyTopicDisplay(first);
        }

        SyncBackButton();
    }

    /// <summary>Выбрать раздел по идентификатору и сбросить стек «Назад».</summary>
    public void SelectTopic(string id)
    {
        var topic = HelpContent.Find(id);
        if (topic == null)
            return;

        for (var i = 0; i < TopicsListBox.Items.Count; i++)
        {
            if (TopicsListBox.Items[i] is HelpContent.HelpTopic t && t.Id == id)
            {
                _suppressHistory = true;
                TopicsListBox.SelectedIndex = i;
                _suppressHistory = false;

                _history.Clear();
                _history.Push(id);
                ApplyTopicDisplay(topic);
                SyncBackButton();
                return;
            }
        }
    }

    private void TopicsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHistory || TopicsListBox.SelectedItem is not HelpContent.HelpTopic topic)
            return;

        if (_history.Count == 0 || _history.Peek() != topic.Id)
            _history.Push(topic.Id);

        ApplyTopicDisplay(topic);
        SyncBackButton();
    }

    private void ApplyTopicDisplay(HelpContent.HelpTopic topic)
    {
        TopicTitleText.Text = topic.Title;
        ParagraphsItemsControl.ItemsSource = topic.Paragraphs;
    }

    private void SyncBackButton()
    {
        BackButton.IsEnabled = _history.Count > 1;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count <= 1)
            return;

        _history.Pop();
        var id = _history.Peek();

        _suppressHistory = true;
        for (var i = 0; i < TopicsListBox.Items.Count; i++)
        {
            if (TopicsListBox.Items[i] is HelpContent.HelpTopic t && t.Id == id)
            {
                TopicsListBox.SelectedIndex = i;
                break;
            }
        }

        _suppressHistory = false;

        if (HelpContent.Find(id) is { } topic)
            ApplyTopicDisplay(topic);

        SyncBackButton();
    }
}

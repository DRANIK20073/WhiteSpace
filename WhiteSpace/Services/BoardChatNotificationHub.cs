using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using WhiteSpace;

namespace WhiteSpace.Services;

public sealed class BoardChatNotificationItem : INotifyPropertyChanged
{
    private bool _isRead;

    public Guid BoardId { get; init; }

    public string BoardTitle { get; init; } = string.Empty;

    public string SenderName { get; init; } = string.Empty;

    public string MessagePreview { get; init; } = string.Empty;

    public string MessageId { get; init; } = string.Empty;

    public DateTime SentAtUtc { get; init; }

    public string ReceivedLocal => SentAtUtc.ToLocalTime().ToString("dd.MM HH:mm");

    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (_isRead == value)
            {
                return;
            }

            _isRead = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Подписка на чаты Firebase по всем доскам пользователя: тосты и список в центре уведомлений.
/// </summary>
public static class BoardChatNotificationHub
{
    private static readonly object Gate = new();

    private static CompositeDisposable? _subscriptions;

    private static FirebaseService? _firebase;

    private static Guid? _myUserId;

    private static readonly Dictionary<Guid, HashSet<string>> SeenMessageIds = new();

    private static IReadOnlyDictionary<Guid, string> _boardTitles =
        new Dictionary<Guid, string>();

    private static int _unreadCount;

    public static ObservableCollection<BoardChatNotificationItem> Items { get; } = new();

    public static int UnreadCount => _unreadCount;

    public static event Action? UnreadCountChanged;

    /// <summary>Доска, открытая сейчас у пользователя — всплывающий тост по её чату не показываем.</summary>
    public static Guid? ActiveBoardId { get; set; }

    private readonly record struct PendingChatNote(
        Guid BoardId,
        string BoardTitle,
        string SenderName,
        string Preview,
        string MessageId,
        DateTime SentAtUtc);

    public static void Stop()
    {
        lock (Gate)
        {
            _subscriptions?.Dispose();
            _subscriptions = null;
            _firebase = null;
            SeenMessageIds.Clear();
            _boardTitles = new Dictionary<Guid, string>();
            _myUserId = null;
        }

        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        app.Dispatcher.Invoke(() =>
        {
            Items.Clear();
            SetUnread(0);
        });
    }

    public static async Task SyncSubscriptionsAsync(SupabaseService supabase)
    {
        var user = SupabaseService.Client.Auth.CurrentUser;
        if (user == null)
        {
            Stop();
            return;
        }

        if (!Guid.TryParse(user.Id, out var myId))
        {
            return;
        }

        var boards = await supabase.GetAllAccessibleBoardsWithRoleAsync();
        var boardIds = boards.Select(b => b.Board.Id).Distinct().ToList();
        var titles = boards.ToDictionary(
            b => b.Board.Id,
            b => string.IsNullOrWhiteSpace(b.Board.Title) ? "Доска" : b.Board.Title!);

        lock (Gate)
        {
            _myUserId = myId;
            _boardTitles = titles;

            var keep = boardIds.ToHashSet();
            foreach (var key in SeenMessageIds.Keys.ToList())
            {
                if (!keep.Contains(key))
                {
                    SeenMessageIds.Remove(key);
                }
            }

            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();
            _firebase = new FirebaseService();

            foreach (var boardId in boardIds)
            {
                var capturedId = boardId;
                var idStr = capturedId.ToString();
                var sub = _firebase
                    .GetBoardChatMessagesObservable(idStr)
                    .Subscribe(messages => ProcessMessages(capturedId, messages));

                _subscriptions.Add(sub);
            }
        }
    }

    public static void MarkAllRead()
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        app.Dispatcher.Invoke(() =>
        {
            foreach (var item in Items)
            {
                item.IsRead = true;
            }

            RecountUnread();
        });
    }

    public static void RecountUnread()
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            SetUnread(Items.Count(x => !x.IsRead));
        }
        else
        {
            app.Dispatcher.Invoke(RecountUnread);
        }
    }

    public static void ClearAll()
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        app.Dispatcher.Invoke(() =>
        {
            Items.Clear();
            SetUnread(0);
        });
    }

    private static void ProcessMessages(Guid boardId, List<FirebaseChatMessage>? messages)
    {
        if (messages == null || Application.Current == null)
        {
            return;
        }

        List<PendingChatNote>? pending = null;

        lock (Gate)
        {
            if (_firebase == null)
            {
                return;
            }

            if (!SeenMessageIds.TryGetValue(boardId, out var seen))
            {
                seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                SeenMessageIds[boardId] = seen;
                foreach (var m in messages)
                {
                    if (!string.IsNullOrWhiteSpace(m.Id))
                    {
                        seen.Add(m.Id);
                    }
                }

                return;
            }

            var myIdStr = _myUserId?.ToString();
            var boardTitle = _boardTitles.TryGetValue(boardId, out var t) ? t : "Доска";

            foreach (var m in messages.OrderBy(x => x.SentAtUtc))
            {
                var id = m.Id?.Trim();
                if (string.IsNullOrEmpty(id) || seen.Contains(id))
                {
                    continue;
                }

                seen.Add(id);

                if (string.IsNullOrWhiteSpace(m.Text))
                {
                    continue;
                }

                if (string.Equals(m.UserId, myIdStr, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sender = string.IsNullOrWhiteSpace(m.UserName) ? "Участник" : m.UserName.Trim();
                var preview = m.Text.Trim();
                if (preview.Length > 120)
                {
                    preview = preview.Substring(0, 117) + "...";
                }

                pending ??= new List<PendingChatNote>();
                pending.Add(new PendingChatNote(boardId, boardTitle, sender, preview, id, m.SentAtUtc));
            }
        }

        if (pending == null || pending.Count == 0)
        {
            return;
        }

        var snapshot = pending;
        Application.Current.Dispatcher.BeginInvoke(() => ApplyPendingNotifications(snapshot));
    }

    private static void ApplyPendingNotifications(List<PendingChatNote> snapshot)
    {
        foreach (var note in snapshot)
        {
            var item = new BoardChatNotificationItem
            {
                BoardId = note.BoardId,
                BoardTitle = note.BoardTitle,
                SenderName = note.SenderName,
                MessagePreview = note.Preview,
                MessageId = note.MessageId,
                SentAtUtc = note.SentAtUtc
            };

            Items.Insert(0, item);
        }

        while (Items.Count > 80)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        RecountUnread();

        var active = ActiveBoardId;
        foreach (var note in snapshot)
        {
            if (active != note.BoardId)
            {
                HomeToastService.Show($"{note.BoardTitle}: {note.SenderName} — {note.Preview}");
            }
        }
    }

    private static void SetUnread(int n)
    {
        _unreadCount = n;
        UnreadCountChanged?.Invoke();
    }
}

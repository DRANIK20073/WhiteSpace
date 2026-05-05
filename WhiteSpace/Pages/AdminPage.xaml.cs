using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WhiteSpace;
using WhiteSpace.Services;

namespace WhiteSpace.Pages;

public partial class AdminPage : Page
{
    private readonly SupabaseService _service = new();
    private AdminDashboardData _dashboard = new();
    private bool _isConfiguredAdmin;
    private readonly DispatcherTimer _autoRefreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _isAutoRefreshInProgress;

    public ObservableCollection<AdminUserRow> Users { get; } = new();

    public ObservableCollection<AdminBoardRow> Boards { get; } = new();

    public ObservableCollection<AdminMemberRow> Members { get; } = new();

    public AdminPage()
    {
        InitializeComponent();
        DataContext = this;
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        Unloaded += AdminPage_Unloaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var prefs = AppPreferences.Load();
        WhiteSpaceThemeManager.Apply(prefs);
        UiAnimationHelper.ApplyFadeIn(AdminRootGrid, prefs.EnableAnimations);
        AdminTabControl.SelectedIndex = 0;

        _isConfiguredAdmin = await _service.IsCurrentUserAdminAsync();

        await LoadAdminDataAsync();
        _autoRefreshTimer.Start();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAdminDataAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        BoardChatNotificationHub.Stop();
        SessionStorage.ClearSession();
        SupabaseService.ClearLocalAdminSession();
        SupabaseService.Client.Auth.SignOut();
        NavigationService?.Navigate(new LoginPage());
    }

    private void Help_Click(object sender, RoutedEventArgs e) =>
        HelpService.Show(Window.GetWindow(this), "admin");

    private void AdminPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _autoRefreshTimer.Stop();
    }

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_isAutoRefreshInProgress || !IsLoaded)
        {
            return;
        }

        _isAutoRefreshInProgress = true;
        try
        {
            await LoadAdminDataAsync(isBackgroundRefresh: true);
        }
        finally
        {
            _isAutoRefreshInProgress = false;
        }
    }

    private async Task LoadAdminDataAsync(bool isBackgroundRefresh = false)
    {
        try
        {
            if (!isBackgroundRefresh)
            {
                IsEnabled = false;
                StatusTextBlock.Text = "Загрузка данных админки...";
            }

            _dashboard = await _service.GetAdminDashboardDataAsync();
            RefreshRows();
            UpdateStats();

            UpdatedAtTextBlock.Text = $"Обновлено: {FormatDateTime(_dashboard.LoadedAtUtc)}";
            StatusTextBlock.Text = isBackgroundRefresh
                ? $"Автообновление: {FormatDateTime(_dashboard.LoadedAtUtc)}"
                : "Данные загружены.";
        }
        finally
        {
            if (!isBackgroundRefresh)
            {
                IsEnabled = true;
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshRows();
    }

    private void ScopeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AdminTabControl == null || ScopeComboBox == null)
        {
            return;
        }

        if (ScopeComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var selectedScope = selectedItem.Content?.ToString() ?? "Пользователи";
            if (selectedScope == "Пользователи")
            {
                AdminTabControl.SelectedIndex = 0;
                UsersGrid?.Focus();
            }
            else if (selectedScope == "Доски")
            {
                AdminTabControl.SelectedIndex = 1;
                BoardsGrid?.Focus();
            }
            else if (selectedScope == "Права доступа")
            {
                AdminTabControl.SelectedIndex = 2;
                MembersGrid?.Focus();
            }
            else
            {
                AdminTabControl.SelectedIndex = 0;
            }
        }

        if (IsLoaded)
        {
            RefreshRows();
        }
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        ScopeComboBox.SelectedIndex = 0;
        RefreshRows();
    }

    private void RefreshRows()
    {
        var selectedScope = (ScopeComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Пользователи";
        var includeUsers = selectedScope == "Пользователи";
        var includeBoards = selectedScope == "Доски";
        var includeMembers = selectedScope == "Права доступа";
        var search = SearchBox?.Text?.Trim() ?? string.Empty;
        var normalizedSearch = search.ToLowerInvariant();

        var profilesById = _dashboard.Profiles
            .GroupBy(profile => profile.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var boardsById = _dashboard.Boards
            .GroupBy(board => board.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var membersByUser = _dashboard.Members.ToLookup(member => member.UserId);
        var membersByBoard = _dashboard.Members.ToLookup(member => member.BoardId);
        var shapesByBoard = _dashboard.Shapes.ToLookup(shape => shape.BoardId);
        var onlineUsersById = _dashboard.OnlineUsers
            .GroupBy(x => x.UserId)
            .ToDictionary(group => group.Key, group => group.Max(x => x.LastSeenUtc));
        var usernameByUserId = _dashboard.Members
            .Where(member => !string.IsNullOrWhiteSpace(member.AccountUsername))
            .GroupBy(member => member.UserId)
            .ToDictionary(group => group.Key, group => group.First().AccountUsername ?? string.Empty);

        Users.Clear();
        if (includeUsers)
        {
            var allUserIds = new HashSet<Guid>(_dashboard.Profiles.Select(profile => profile.Id));
            foreach (var memberUserId in _dashboard.Members.Select(member => member.UserId))
            {
                allUserIds.Add(memberUserId);
            }

            foreach (var userId in allUserIds)
            {
                profilesById.TryGetValue(userId, out var profile);
                var ownedBoardsCount = _dashboard.Boards.Count(board => board.OwnerId == userId);
                var profileMemberships = membersByUser[userId].ToList();
                var lastActivity = profileMemberships.Count > 0
                    ? profileMemberships.Max(member => member.JoinedAt)
                    : profile?.CreatedAt ?? DateTime.MinValue;

                onlineUsersById.TryGetValue(userId, out var lastSeenUtc);
                var isOnline = lastSeenUtc > DateTime.UtcNow.AddSeconds(-20);

                var row = new AdminUserRow
                {
                    Id = userId,
                    Email = EmptyFallback(profile?.Email, "без email"),
                    Username = !string.IsNullOrWhiteSpace(profile?.Username)
                        ? profile.Username!
                        : (usernameByUserId.TryGetValue(userId, out var accountUsername)
                            ? accountUsername
                            : $"Пользователь {ShortId(userId)}"),
                    CreatedText = FormatDateTime(profile?.CreatedAt ?? DateTime.MinValue),
                    OwnedBoardsCount = ownedBoardsCount,
                    MembershipsCount = profileMemberships.Count,
                    PresenceLabel = isOnline ? "Онлайн" : "Не в сети",
                    LastActivityText = FormatDateTime(lastActivity),
                    CanDeleteProfile = _isConfiguredAdmin && ownedBoardsCount == 0 && !IsCurrentUser(userId)
                };

                if (MatchesSearch(normalizedSearch, row.Email, row.Username, row.Id.ToString()))
                {
                    Users.Add(row);
                }
            }
        }

        Boards.Clear();
        if (includeBoards)
        {
            foreach (var board in _dashboard.Boards.OrderByDescending(board => board.CreatedAt))
            {
                profilesById.TryGetValue(board.OwnerId, out var owner);

                var row = new AdminBoardRow
                {
                    Id = board.Id,
                    Title = EmptyFallback(board.Title, "Без названия"),
                    OwnerLabel = GetProfileLabel(owner, board.OwnerId),
                    AccessCode = EmptyFallback(board.AccessCode, "нет"),
                    CreatedText = FormatDateTime(board.CreatedAt),
                    MembersCount = membersByBoard[board.Id].Count(),
                    ShapesCount = shapesByBoard[board.Id].Count(),
                    CanDeleteBoard = _isConfiguredAdmin,
                    CanClearContent = _isConfiguredAdmin
                };

                if (MatchesSearch(normalizedSearch, row.Title, row.OwnerLabel, row.AccessCode, row.Id.ToString()))
                {
                    Boards.Add(row);
                }
            }
        }

        Members.Clear();
        if (includeMembers)
        {
            foreach (var member in _dashboard.Members.OrderByDescending(member => member.JoinedAt))
            {
                boardsById.TryGetValue(member.BoardId, out var board);
                profilesById.TryGetValue(member.UserId, out var profile);

                var row = new AdminMemberRow
                {
                    BoardId = member.BoardId,
                    UserId = member.UserId,
                    BoardTitle = board?.Title ?? ShortId(member.BoardId),
                    UserLabel = GetMemberLabel(member, profile),
                    Role = member.Role,
                    RoleLabel = GetRoleLabel(member.Role),
                    ToggleRoleButtonLabel = string.Equals(member.Role, "viewer", StringComparison.OrdinalIgnoreCase)
                        ? "Сделать редактором"
                        : "Сделать наблюдателем",
                    JoinedText = FormatDateTime(member.JoinedAt),
                    CanManageAccess = _isConfiguredAdmin && !string.Equals(member.Role, "owner", StringComparison.OrdinalIgnoreCase)
                };

                if (MatchesSearch(normalizedSearch, row.BoardTitle, row.UserLabel, row.RoleLabel, row.BoardId.ToString(), row.UserId.ToString()))
                {
                    Members.Add(row);
                }
            }
        }

        StatusTextBlock.Text = $"Показано: {Users.Count} пользователей, {Boards.Count} досок, {Members.Count} доступов.";
    }

    private void UpdateStats()
    {
        var activeSince = DateTime.UtcNow.AddDays(-7);
        var activeUsers = _dashboard.Members
            .Where(member => member.JoinedAt >= activeSince)
            .Select(member => member.UserId)
            .Distinct()
            .Count();

        UsersCountTextBlock.Text = _dashboard.Profiles.Count.ToString();
        BoardsCountTextBlock.Text = _dashboard.Boards.Count.ToString();
        ShapesCountTextBlock.Text = _dashboard.Shapes.Count.ToString();
        ActiveUsersTextBlock.Text = activeUsers.ToString();
    }

    private void CopyUserId_Click(object sender, RoutedEventArgs e)
    {
        if (GetRow<AdminUserRow>(sender) is not { } row)
        {
            return;
        }

        CopyToClipboard(row.Id.ToString(), "ID пользователя скопирован.");
    }

    private async void DeleteUser_Click(object sender, RoutedEventArgs e)
    {
        if (GetRow<AdminUserRow>(sender) is not { } row)
        {
            return;
        }

        if (!AppDialogService.ShowConfirmation(
                $"Удалить профиль {row.Email} и его доступы к доскам?",
                "Удаление пользователя",
                "Удалить",
                "Отмена"))
        {
            return;
        }

        if (await _service.AdminDeleteProfileAsync(row.Id))
        {
            await LoadAdminDataAsync();
        }
    }

    private void CopyBoardCode_Click(object sender, RoutedEventArgs e)
    {
        if (GetRow<AdminBoardRow>(sender) is not { } row)
        {
            return;
        }

        CopyToClipboard(row.AccessCode, "Код доступа скопирован.");
    }

    private void OpenBoard_Click(object sender, RoutedEventArgs e)
    {
        if (GetRow<AdminBoardRow>(sender) is not { } row)
        {
            return;
        }

        NavigationService?.Navigate(new BoardPage(row.Id, true));
    }

    private async void ClearBoardContent_Click(object sender, RoutedEventArgs e)
    {
        if (GetRow<AdminBoardRow>(sender) is not { } row)
        {
            return;
        }

        if (!AppDialogService.ShowConfirmation(
                $"Удалить все элементы с доски \"{row.Title}\"?",
                "Удаление контента",
                "Удалить контент",
                "Отмена"))
        {
            return;
        }

        if (await _service.ClearBoardShapesAsync(row.Id))
        {
            await LoadAdminDataAsync();
        }
    }

    private async void DeleteBoard_Click(object sender, RoutedEventArgs e)
    {
        if (GetRow<AdminBoardRow>(sender) is not { } row)
        {
            return;
        }

        if (!AppDialogService.ShowConfirmation(
                $"Удалить доску \"{row.Title}\" вместе с участниками и контентом?",
                "Удаление доски",
                "Удалить",
                "Отмена"))
        {
            return;
        }

        if (await _service.AdminDeleteBoardAsync(row.Id))
        {
            await LoadAdminDataAsync();
        }
    }

    private async void ToggleRole_Click(object sender, RoutedEventArgs e)
    {
        if (GetRow<AdminMemberRow>(sender) is not { } row)
        {
            return;
        }

        var targetRole = string.Equals(row.Role, "viewer", StringComparison.OrdinalIgnoreCase)
            ? "editor"
            : "viewer";

        await UpdateMemberRoleAsync(row, targetRole);
    }

    private async Task UpdateMemberRoleAsync(AdminMemberRow row, string role)
    {
        if (await _service.UpdateBoardMemberRoleAsync(row.BoardId, row.UserId, role))
        {
            await LoadAdminDataAsync();
        }
    }

    private async void RemoveAccess_Click(object sender, RoutedEventArgs e)
    {
        if (GetRow<AdminMemberRow>(sender) is not { } row)
        {
            return;
        }

        if (!AppDialogService.ShowConfirmation(
                $"Убрать доступ пользователя {row.UserLabel} к доске \"{row.BoardTitle}\"?",
                "Удаление доступа",
                "Убрать",
                "Отмена"))
        {
            return;
        }

        if (await _service.RemoveBoardMemberAsync(row.BoardId, row.UserId))
        {
            await LoadAdminDataAsync();
        }
    }

    private static T? GetRow<T>(object sender) where T : class
    {
        return sender is Button button ? button.CommandParameter as T : null;
    }

    private static bool MatchesSearch(string normalizedSearch, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(normalizedSearch))
        {
            return true;
        }

        return values.Any(value => !string.IsNullOrWhiteSpace(value)
            && value.ToLowerInvariant().Contains(normalizedSearch));
    }

    private static string GetProfileLabel(Profile? profile, Guid fallbackId)
    {
        if (!string.IsNullOrWhiteSpace(profile?.Username))
        {
            return profile.Username;
        }

        if (!string.IsNullOrWhiteSpace(profile?.Email))
        {
            return profile.Email;
        }

        return ShortId(fallbackId);
    }

    private static string GetMemberLabel(BoardMember member, Profile? profile)
    {
        if (!string.IsNullOrWhiteSpace(member.AccountUsername))
        {
            return member.AccountUsername;
        }

        return GetProfileLabel(profile, member.UserId);
    }

    private static string GetRoleLabel(string? role)
    {
        return role?.ToLowerInvariant() switch
        {
            "owner" => "Владелец",
            "editor" => "Редактор",
            "viewer" => "Просмотр",
            _ => EmptyFallback(role, "Неизвестно")
        };
    }

    private static string FormatDateTime(DateTime value)
    {
        if (value == default)
        {
            return "нет данных";
        }

        return value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }

    private static string EmptyFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string ShortId(Guid id)
    {
        return id.ToString("N")[..8];
    }

    private static bool IsCurrentUser(Guid userId)
    {
        var currentUser = SupabaseService.Client.Auth.CurrentUser;
        return currentUser != null
            && Guid.TryParse(currentUser.Id, out var currentUserId)
            && currentUserId == userId;
    }

    private void CopyToClipboard(string value, string successMessage)
    {
        try
        {
            Clipboard.SetText(value);
            StatusTextBlock.Text = successMessage;
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Не удалось скопировать в буфер обмена: {ex.Message}", "Админка");
        }
    }
}

public sealed class AdminUserRow
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string CreatedText { get; set; } = string.Empty;

    public int OwnedBoardsCount { get; set; }

    public int MembershipsCount { get; set; }

    public string PresenceLabel { get; set; } = "Не в сети";

    public string LastActivityText { get; set; } = string.Empty;

    public bool CanDeleteProfile { get; set; }
}

public sealed class AdminBoardRow
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string OwnerLabel { get; set; } = string.Empty;

    public string AccessCode { get; set; } = string.Empty;

    public string CreatedText { get; set; } = string.Empty;

    public int MembersCount { get; set; }

    public int ShapesCount { get; set; }

    public bool CanDeleteBoard { get; set; }

    public bool CanClearContent { get; set; }
}

public sealed class AdminMemberRow
{
    public Guid BoardId { get; set; }

    public Guid UserId { get; set; }

    public string BoardTitle { get; set; } = string.Empty;

    public string UserLabel { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string RoleLabel { get; set; } = string.Empty;

    public string ToggleRoleButtonLabel { get; set; } = string.Empty;

    public string JoinedText { get; set; } = string.Empty;

    public bool CanManageAccess { get; set; }
}

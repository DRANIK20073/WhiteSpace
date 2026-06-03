using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using WhiteSpace.Pages;

namespace WhiteSpace.Services;

public static class BoardInviteNavigation
{
    /// <summary>Присоединение по отложенному коду из ссылки и переход на доску (нужна авторизация).</summary>
    public static async Task TryNavigateFromPendingAsync(NavigationService? nav)
    {
        if (nav == null)
        {
            return;
        }

        if (!PendingBoardInvite.TryPeek(out _))
        {
            return;
        }

        if (SupabaseService.Client?.Auth?.CurrentUser == null)
        {
            return;
        }

        if (!PendingBoardInvite.TryTake(out var code))
        {
            return;
        }

        var svc = new SupabaseService();
        var board = await svc.JoinBoardAsync(code);
        if (board == null)
        {
            ShowInviteFailure(
                nav,
                "Не удалось подключиться к доске по приглашению. Проверьте код или права доступа.");
            return;
        }

        UserHomePage.RememberBoardActivity(board.Id);
        AppNavigation.NavigateToBoard(nav, board.Id);
    }

    private static void ShowInviteFailure(NavigationService nav, string message)
    {
        if (nav.Content is UserHomePage)
        {
            HomeToastService.Show(message);
            return;
        }

        AppDialogService.ShowWarning(message, "Приглашение на доску");
    }
}

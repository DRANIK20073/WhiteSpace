using System.Threading.Tasks;
using System.Windows.Navigation;
using WhiteSpace.Pages;

namespace WhiteSpace.Services;

public static class BoardInviteNavigation
{
    /// <summary>Присоединение по отложенному коду и переход на доску (только с главной при авторизации).</summary>
    public static async Task TryNavigateFromPendingAsync(NavigationService? nav)
    {
        if (nav == null || nav.Content is not UserHomePage)
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
            HomeToastService.Show(
                "Не удалось подключиться к доске по приглашению. Проверьте код или права доступа.");
            return;
        }

        UserHomePage.RememberBoardActivity(board.Id);
        nav.Navigate(new BoardPage(board.Id));
    }
}

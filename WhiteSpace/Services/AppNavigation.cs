using System.Windows.Controls;
using System.Windows.Navigation;
using WhiteSpace.Pages;

namespace WhiteSpace.Services;

/// <summary>Навигация между страницами приложения через MainFrame.</summary>
public static class AppNavigation
{
    private static UserHomePage? _homePage;

    /// <summary>NavigationService главного окна или null, если окно ещё не готово.</summary>
    public static NavigationService? GetMainNavigationService() =>
        (System.Windows.Application.Current.MainWindow as MainWindow)?.MainFrame.NavigationService;

    /// <summary>Сбрасывает кэш главной — нужно после выхода из аккаунта.</summary>
    public static void ResetCachedPages() => _homePage = null;

    /// <summary>Один экземпляр UserHomePage на сессию, чтобы не терять состояние списка.</summary>
    public static UserHomePage GetOrCreateHomePage() => _homePage ??= new UserHomePage();

    /// <summary>Переход на главную с обновлением списка досок.</summary>
    public static void NavigateHome(NavigationService? navigationService)
    {
        if (navigationService == null)
        {
            return;
        }

        var home = GetOrCreateHomePage();
        var alreadyShowingHome = ReferenceEquals(navigationService.Content, home);

        if (!alreadyShowingHome)
        {
            navigationService.Navigate(home);
            ClearBackStack(navigationService);
        }

        home.RequestRefreshAfterNavigation();
    }

    /// <summary>Открывает доску по id без очистки back stack.</summary>
    public static void NavigateToBoard(NavigationService? navigationService, Guid boardId)
    {
        NavigateTo(navigationService, new BoardPage(boardId), clearBackStack: false);
    }

    /// <summary>Универсальный Navigate с опциональной очисткой истории.</summary>
    public static void NavigateTo(NavigationService? navigationService, Page page, bool clearBackStack = true)
    {
        if (navigationService == null || ReferenceEquals(navigationService.Content, page))
        {
            return;
        }

        navigationService.Navigate(page);
        if (clearBackStack)
        {
            ClearBackStack(navigationService);
        }
    }

    /// <summary>Выход на экран входа: сбрасывает кэш страниц.</summary>
    public static void NavigateToLogin(NavigationService? navigationService)
    {
        ResetCachedPages();
        NavigateTo(navigationService, new LoginPage());
    }

    /// <summary>Убирает все записи «назад», чтобы не вернуться на login случайно.</summary>
    public static void ClearBackStack(NavigationService navigationService)
    {
        while (navigationService.CanGoBack)
        {
            navigationService.RemoveBackEntry();
        }
    }
}

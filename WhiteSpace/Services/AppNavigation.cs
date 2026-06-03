using System.Windows.Controls;
using System.Windows.Navigation;
using WhiteSpace.Pages;

namespace WhiteSpace.Services;

public static class AppNavigation
{
    private static UserHomePage? _homePage;

    public static NavigationService? GetMainNavigationService() =>
        (System.Windows.Application.Current.MainWindow as MainWindow)?.MainFrame.NavigationService;

    public static void ResetCachedPages() => _homePage = null;

    public static UserHomePage GetOrCreateHomePage() => _homePage ??= new UserHomePage();

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

    public static void NavigateToBoard(NavigationService? navigationService, Guid boardId)
    {
        NavigateTo(navigationService, new BoardPage(boardId), clearBackStack: false);
    }

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

    public static void NavigateToLogin(NavigationService? navigationService)
    {
        ResetCachedPages();
        NavigateTo(navigationService, new LoginPage());
    }

    public static void ClearBackStack(NavigationService navigationService)
    {
        while (navigationService.CanGoBack)
        {
            navigationService.RemoveBackEntry();
        }
    }
}

using System.Windows;
using WhiteSpace.Pages;

namespace WhiteSpace
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            await SupabaseService.InitAsync();

            var session = SessionStorage.LoadSession();

            var window = new MainWindow();
            MainWindow = window;

            if (session != null &&
                !string.IsNullOrWhiteSpace(session.RefreshToken))
            {
                try
                {
                    await SupabaseService.Client.Auth.SetSession(
                        session.AccessToken,
                        session.RefreshToken,
                        false
                    );

                    if (SupabaseService.Client.Auth.CurrentUser != null)
                    {
                        window.MainFrame.Navigate(new UserHomePage());
                        window.Show();
                        return;
                    }
                }
                catch (Supabase.Gotrue.Exceptions.GotrueException)
                {
                    // ⬇️ refresh token мёртв — это нормально
                    SessionStorage.ClearSession();
                    await SupabaseService.Client.Auth.SignOut();
                }
            }

            window.MainFrame.Navigate(new LoginPage());
            window.Show();
        }
    }
}

using System.Windows;
using WhiteSpace.Pages;

namespace WhiteSpace
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Инициализация Supabase
            await SupabaseService.InitAsync();

            // 2. Загружаем сохранённую сессию
            var session = SessionStorage.LoadSession();

            // 3. Создаём окно, НО ПОКА НЕ ПОКАЗЫВАЕМ
            var window = new MainWindow();
            MainWindow = window;

            if (session != null)
            {
                await SupabaseService.Client.Auth.SetSession(
                    session.AccessToken,
                    session.RefreshToken,
                    false
                );

                if (SupabaseService.Client.Auth.CurrentUser != null)
                {
                    // 4️⃣ Навигация ДО Show()
                    window.MainFrame.Navigate(new UserHomePage());

                    window.Show();
                    return;
                }
            }

            // если нет сессии
            window.MainFrame.Navigate(new LoginPage());
            window.Show();
        }
    }
}

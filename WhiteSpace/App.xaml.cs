using System;
using System.Threading;
using System.Windows;
using WhiteSpace.Pages;
using WhiteSpace.Services;

namespace WhiteSpace
{
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;

        protected override async void OnStartup(StartupEventArgs e)
        {
            bool owned;
            try
            {
                _singleInstanceMutex = new Mutex(true, @"Local\WhiteSpace_SingleInstance", out owned);
            }
            catch
            {
                owned = false;
            }

            if (!owned)
            {
                var relayCode = InviteLaunchArgs.TryParseInviteCode(e.Args);
                if (!string.IsNullOrEmpty(relayCode))
                {
                    InviteRelay.WritePendingInvite(relayCode);
                }

                Environment.Exit(0);
                return;
            }

            GC.KeepAlive(_singleInstanceMutex);

            var inviteFromArgs = InviteLaunchArgs.TryParseInviteCode(e.Args);
            if (!string.IsNullOrEmpty(inviteFromArgs))
            {
                PendingBoardInvite.Set(inviteFromArgs);
            }

            base.OnStartup(e);

            WhiteSpaceThemeManager.Apply(AppPreferences.Load());

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

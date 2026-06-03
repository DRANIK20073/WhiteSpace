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
            var allowMultiInstance = InviteLaunchArgs.AllowsMultipleInstancesForCurrentProcess(e.Args);

            Mutex? mutex = null;
            var createdNew = false;
            try
            {
                mutex = new Mutex(true, @"Local\WhiteSpace_SingleInstance", out createdNew);
            }
            catch
            {
                createdNew = false;
            }

            if (!createdNew)
            {
                var relayCode = InviteLaunchArgs.TryParseInviteCode(e.Args);
                if (!string.IsNullOrEmpty(relayCode))
                {
                    InviteRelay.WritePendingInvite(relayCode);
                    Environment.Exit(0);
                    return;
                }

                if (!allowMultiInstance)
                {
                    mutex?.Dispose();
                    Environment.Exit(0);
                    return;
                }

                mutex?.Dispose();
                mutex = null;
            }
            else
            {
                _singleInstanceMutex = mutex;
                GC.KeepAlive(_singleInstanceMutex);
            }

            var inviteFromArgs = InviteLaunchArgs.TryParseInviteCode(e.Args);
            if (!string.IsNullOrEmpty(inviteFromArgs))
            {
                PendingBoardInvite.Set(inviteFromArgs);
            }

            base.OnStartup(e);

            WhiteSpaceThemeManager.Apply(AppPreferences.Load());

            await SupabaseService.InitAsync();
            InviteProtocolRegistration.EnsureRegistered();

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
                        var supabase = new SupabaseService();
                        if (await supabase.EnforceBanLogoutIfNeededAsync())
                        {
                            window.Show();
                            return;
                        }

                        window.Show();
                        AppNavigation.NavigateHome(window.MainFrame.NavigationService);
                        return;
                    }
                }
                catch (Supabase.Gotrue.Exceptions.GotrueException)
                {
                    // ⬇️ refresh token мёртв — это нормально
                    BoardChatNotificationHub.Stop();
                    SessionStorage.ClearSession();
                    await SupabaseService.Client.Auth.SignOut();
                }
            }

            window.MainFrame.Navigate(new LoginPage());
            AppNavigation.ClearBackStack(window.MainFrame.NavigationService);
            window.Show();
        }
    }
}

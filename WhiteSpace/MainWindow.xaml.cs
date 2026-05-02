using System;
using System.Windows;
using System.Windows.Threading;
using WhiteSpace.Services;

namespace WhiteSpace
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer? _inviteRelayTimer;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var prefs = AppPreferences.Load();
            WhiteSpaceThemeManager.Apply(prefs);
            UiAnimationHelper.ApplyFadeIn(WindowRoot, prefs.EnableAnimations);

            _inviteRelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _inviteRelayTimer.Tick += InviteRelayTimer_Tick;
            _inviteRelayTimer.Start();
        }

        private async void InviteRelayTimer_Tick(object? sender, EventArgs e)
        {
            if (!InviteRelay.TryReadAndClear(out var code))
            {
                return;
            }

            PendingBoardInvite.Set(code);
            await BoardInviteNavigation.TryNavigateFromPendingAsync(MainFrame.NavigationService);
        }
    }
}

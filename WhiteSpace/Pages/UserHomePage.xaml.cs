using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace WhiteSpace.Pages
{
    public partial class UserHomePage : Page, INotifyPropertyChanged
    {
        private string _userGreeting = "Добро пожаловать!";

        public string UserGreeting
        {
            get => _userGreeting;
            set
            {
                _userGreeting = value;
                OnPropertyChanged();
            }
        }

        public UserHomePage()
        {
            InitializeComponent();
            DataContext = this;
            LoadUserProfile();
        }

        private async void LoadUserProfile()
        {
            var service = new SupabaseService();
            var profile = await service.GetMyProfileAsync();

            if (profile != null && !string.IsNullOrEmpty(profile.Username))
            {
                UserGreeting = $"Здравствуйте, {profile.Username} 👋";
            }
            else
            {
                UserGreeting = "Здравствуйте!";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using System;
using System.Windows;

namespace WhiteSpace.Services
{
    public enum AppDialogType
    {
        Info,
        Success,
        Warning,
        Error,
        Question
    }

    public static class AppDialogService
    {
        public static void ShowInfo(string message, string title = "WhiteSpace")
        {
            ShowDialog(message, title, AppDialogType.Info);
        }

        public static void ShowSuccess(string message, string title = "WhiteSpace")
        {
            ShowDialog(message, title, AppDialogType.Success);
        }

        public static void ShowWarning(string message, string title = "WhiteSpace")
        {
            ShowDialog(message, title, AppDialogType.Warning);
        }

        public static void ShowError(string message, string title = "WhiteSpace")
        {
            ShowDialog(message, title, AppDialogType.Error);
        }

        public static bool ShowConfirmation(
            string message,
            string title = "Подтверждение",
            string primaryButtonText = "Да",
            string secondaryButtonText = "Нет")
        {
            return InvokeOnUiThread(() =>
            {
                var dialog = new AppDialogWindow(title, message, AppDialogType.Question, primaryButtonText, secondaryButtonText);
                return dialog.ShowDialog() == true;
            });
        }

        private static void ShowDialog(string message, string title, AppDialogType type)
        {
            InvokeOnUiThread(() =>
            {
                var dialog = new AppDialogWindow(title, message, type);
                dialog.ShowDialog();
                return true;
            });
        }

        private static T InvokeOnUiThread<T>(Func<T> action)
        {
            var app = Application.Current;

            if (app?.Dispatcher == null || app.Dispatcher.CheckAccess())
            {
                return action();
            }

            return app.Dispatcher.Invoke(action);
        }
    }
}

using System;
using System.Windows;

namespace WhiteSpace.Services
{
    /// <summary>Тип диалога для стилизации кнопок и иконки.</summary>
    public enum AppDialogType
    {
        Info,
        Success,
        Warning,
        Error,
        Question
    }

    /// <summary>Единая точка для модальных окон приложения (всегда из UI-потока).</summary>
    public static class AppDialogService
    {
        /// <summary>Информационное сообщение.</summary>
        public static void ShowInfo(string message, string title = "WhiteSpace")
        {
            ShowDialog(message, title, AppDialogType.Info);
        }

        /// <summary>Успешное действие (зелёная иконка).</summary>
        public static void ShowSuccess(string message, string title = "WhiteSpace")
        {
            ShowDialog(message, title, AppDialogType.Success);
        }

        /// <summary>Предупреждение без блокировки дальнейшей работы.</summary>
        public static void ShowWarning(string message, string title = "WhiteSpace")
        {
            ShowDialog(message, title, AppDialogType.Warning);
        }

        /// <summary>Ошибка — пользователь должен её увидеть.</summary>
        public static void ShowError(string message, string title = "WhiteSpace")
        {
            ShowDialog(message, title, AppDialogType.Error);
        }

        /// <summary>Да/Нет — возвращает true, если нажали primary.</summary>
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

        /// <summary>Диалог ввода текста; null — отмена.</summary>
        public static string? ShowTextInput(
            string title,
            string message,
            string confirmButtonText = "ОК",
            string cancelButtonText = "Отмена",
            string initialValue = "")
        {
            return InvokeOnUiThread(() =>
            {
                var dialog = new AppTextInputWindow(
                    title,
                    message,
                    confirmButtonText,
                    cancelButtonText,
                    initialValue);

                var result = dialog.ShowDialog() == true;
                return result ? dialog.InputText : null;
            });
        }

        /// <summary>Базовый ShowDialog для Info/Success/Warning/Error.</summary>
        private static void ShowDialog(string message, string title, AppDialogType type)
        {
            InvokeOnUiThread(() =>
            {
                var dialog = new AppDialogWindow(title, message, type);
                dialog.ShowDialog();
                return true;
            });
        }

        /// <summary>Маршалит вызов на Dispatcher, если мы не в UI-потоке.</summary>
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

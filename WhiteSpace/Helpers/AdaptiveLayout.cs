using System.Windows;
using System.Windows.Controls;

namespace WhiteSpace.Helpers
{
    /// <summary>Уровни ширины для переключения раскладки.</summary>
    public enum AdaptiveWidthTier
    {
        Wide,
        Medium,
        Narrow,
        Compact
    }

    public static class AdaptiveLayout
    {
        public const double TierWide = 1100;
        public const double TierMedium = 920;
        public const double TierNarrow = 720;

        public const double SidebarFullWidth = 248;
        public const double SidebarIconWidth = 72;

        public const double BoardParticipantsFullWidth = 320;
        public const double BoardParticipantsMediumWidth = 260;

        public static AdaptiveWidthTier GetTier(double width)
        {
            if (width >= TierWide)
            {
                return AdaptiveWidthTier.Wide;
            }

            if (width >= TierMedium)
            {
                return AdaptiveWidthTier.Medium;
            }

            if (width >= TierNarrow)
            {
                return AdaptiveWidthTier.Narrow;
            }

            return AdaptiveWidthTier.Compact;
        }

        /// <summary>Иконка всегда видна; подпись скрывается в компактном режиме.</summary>
        public static void SetIconLabelPair(bool showLabel, TextBlock label, FrameworkElement icon, double labelGap = 8)
        {
            label.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
            icon.Visibility = Visibility.Visible;
            label.Margin = showLabel ? new Thickness(labelGap, 0, 0, 0) : new Thickness(0);
        }

        /// <summary>Либо текст, либо иконка (для кнопок без постоянной иконки).</summary>
        public static void SetTextOrIconButton(bool showLabel, TextBlock label, FrameworkElement compactIcon)
        {
            label.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
            compactIcon.Visibility = showLabel ? Visibility.Collapsed : Visibility.Visible;
        }

        public static void SetCompactButtonPadding(Button button, bool compact, double fullHorizontal = 18, double compactHorizontal = 11)
        {
            var pad = button.Padding;
            var horizontal = compact ? compactHorizontal : fullHorizontal;
            button.Padding = new Thickness(horizontal, pad.Top, horizontal, pad.Bottom);
        }
    }
}

using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace WhiteSpace.Services;

public static class UiAnimationHelper
{
    public static void ApplyFadeIn(FrameworkElement? element, bool enableAnimations)
    {
        if (element == null || !enableAnimations)
        {
            return;
        }

        element.Opacity = 0;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public static void ApplyFadeVisibilityToggle(
        UIElement element,
        bool show,
        bool enableAnimations,
        Action? afterHide = null)
    {
        if (element == null)
        {
            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty, null);

        if (!enableAnimations)
        {
            element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            element.Opacity = 1;
            if (!show)
            {
                afterHide?.Invoke();
            }

            return;
        }

        if (show)
        {
            element.Visibility = Visibility.Visible;
            element.Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            element.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            return;
        }

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            element.Visibility = Visibility.Collapsed;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            afterHide?.Invoke();
        };
        element.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }
}

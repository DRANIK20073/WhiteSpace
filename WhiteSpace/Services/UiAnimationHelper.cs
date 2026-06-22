using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WhiteSpace.Services;

/// <summary>Общие fade/slide-анимации UI с учётом настройки EnableAnimations.</summary>
public static class UiAnimationHelper
{
    /// <summary>Плавное появление элемента с нулевой прозрачности.</summary>
    public static void ApplyFadeIn(FrameworkElement? element, bool enableAnimations, bool force = false)
    {
        if (element == null || !enableAnimations)
        {
            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty, null);

        if (!force && element.IsVisible && element.Opacity > 0.95)
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

    /// <summary>Мягкое появление при возврате на уже знакомую страницу.</summary>
    public static void ApplyReturnFadeIn(FrameworkElement? element, bool enableAnimations)
    {
        if (element == null || !enableAnimations)
        {
            if (element != null)
            {
                element.Opacity = 1;
            }

            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 0.86;
        var anim = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>Плавное исчезновение; по завершении вызывает onComplete.</summary>
    public static void ApplyFadeOut(FrameworkElement? element, bool enableAnimations, Action? onComplete = null)
    {
        if (element == null)
        {
            onComplete?.Invoke();
            return;
        }

        if (!enableAnimations)
        {
            element.Opacity = 1;
            onComplete?.Invoke();
            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty, null);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            onComplete?.Invoke();
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Показ/скрытие через opacity + Visibility с опциональным callback после hide.</summary>
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

    /// <summary>Сдвиг панели по горизонтали (выезд слева или справа).</summary>
    public static void AnimateHorizontalSlide(
        TranslateTransform transform,
        bool slideIn,
        double panelWidth,
        bool fromLeft,
        bool enableAnimations,
        Action? onComplete = null)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);

        var hidden = fromLeft ? -panelWidth : panelWidth;
        var visible = 0.0;
        var from = slideIn ? hidden : visible;
        var to = slideIn ? visible : hidden;

        if (!enableAnimations || panelWidth <= 0)
        {
            transform.X = to;
            onComplete?.Invoke();
            return;
        }

        if (slideIn)
        {
            transform.X = hidden;
        }

        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase
            {
                EasingMode = slideIn ? EasingMode.EaseOut : EasingMode.EaseIn
            }
        };
        anim.Completed += (_, _) =>
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = to;
            onComplete?.Invoke();
        };
        transform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <summary>Достаёт или создаёт TranslateTransform для анимации выезда панели.</summary>
    public static TranslateTransform GetOrCreateTranslateTransform(FrameworkElement element, bool anchorLeft)
    {
        if (element.RenderTransform is TranslateTransform existing)
        {
            return existing;
        }

        if (element.RenderTransform is TransformGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child is TranslateTransform tt)
                {
                    return tt;
                }
            }
        }

        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        element.RenderTransformOrigin = new Point(anchorLeft ? 0 : 1, 0.5);
        return transform;
    }
}

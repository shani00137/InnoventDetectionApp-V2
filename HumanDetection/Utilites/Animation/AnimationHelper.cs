using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HumanDetection.Utilites.Animation
{
    public static class AnimationHelper
    {
        public static async Task AnimateLightPulseAsync(UIElement element)
        {
            EnsureScaleTransform(element);

            var pulseAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 1.5,
                Duration = TimeSpan.FromSeconds(0.5),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(1)
            };

            var transform = (ScaleTransform)element.RenderTransform;
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);

            await Task.Delay(1000);
        }

        public static Task ScaleToAsync(UIElement element, double targetScale, int durationMs, bool easeIn)
        {
            EnsureScaleTransform(element);

            var tcs = new TaskCompletionSource<bool>();

            var easing = new CubicEase
            {
                EasingMode = easeIn ? EasingMode.EaseIn : EasingMode.EaseOut
            };

            var scaleX = new DoubleAnimation
            {
                To = targetScale,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };

            var scaleY = new DoubleAnimation
            {
                To = targetScale,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };

            scaleY.Completed += (s, e) => tcs.TrySetResult(true);

            var transform = (ScaleTransform)element.RenderTransform;
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

            return tcs.Task;
        }

        private static void EnsureScaleTransform(UIElement element)
        {
            if (element.RenderTransform is not ScaleTransform)
            {
                var scale = new ScaleTransform(1, 1);
                element.RenderTransform = scale;
                element.RenderTransformOrigin = new Point(0.5, 0.5);
            }
        }
    }
}

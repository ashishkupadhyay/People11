using System;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using People.Core.Interfaces;

namespace People.Views;

public sealed partial class OnboardingPage : Page
{
    private const float StartOffsetY = 220.0f;
    private const float OvershootY = -24.0f;
    private const float ReboundDipY = 7.0f;
    private const float FinalY = 0.0f;

    private bool _animationsStarted;

    public OnboardingPage()
    {
        InitializeComponent();
        Loaded += OnboardingPage_Loaded;
    }

    private void OnboardingPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_animationsStarted) return;
        _animationsStarted = true;

        StartStaggeredCanopyAnimation();
        CanopyCanvas.Opacity = 1.0;
    }

    private void StartStaggeredCanopyAnimation()
    {
        Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();

        var easePhase1 = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 0.84f),
            new Vector2(0.44f, 1.00f));

        var easePhase2 = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.42f, 0.00f),
            new Vector2(0.58f, 1.00f));

        var easePhase3 = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.25f, 1.00f),
            new Vector2(0.50f, 1.00f));

        var easeOpacity = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 0.84f),
            new Vector2(0.44f, 1.00f));

        const double canopyEpicenterX = 1200.0;
        const double canopyEpicenterY = 150.0;
        const double minCanopyDistance = 66.0;
        const double maxCanopyDistance = 778.0;
        const double maxStaggerDurationMs = 240.0;

        int imageIndex = 0;
        foreach (UIElement child in CanopyCanvas.Children)
        {
            if (child is not FrameworkElement item) continue;
            imageIndex++;

            double left = Canvas.GetLeft(item);
            double top = Canvas.GetTop(item);
            double width = item.Width > 0 ? item.Width : 116.0;
            double height = item.Height > 0 ? item.Height : 116.0;

            double cx = left + width / 2.0;
            double cy = top + height / 2.0;

            double distance = Math.Sqrt(Math.Pow(cx - canopyEpicenterX, 2) + Math.Pow(cy - canopyEpicenterY, 2));
            double normalizedDist = Math.Clamp((distance - minCanopyDistance) / (maxCanopyDistance - minCanopyDistance), 0.0, 1.0);

            int jitterMs = ((imageIndex * 37 + 17) % 21) - 10;
            double totalDelayMs = Math.Clamp(normalizedDist * maxStaggerDurationMs + jitterMs, 0.0, 300.0);
            TimeSpan delayTime = TimeSpan.FromMilliseconds(totalDelayMs);

            var translationAnim = compositor.CreateVector3KeyFrameAnimation();
            translationAnim.Target = "Translation";
            translationAnim.Duration = TimeSpan.FromMilliseconds(1000);
            translationAnim.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            translationAnim.DelayTime = delayTime;

            translationAnim.InsertKeyFrame(0.00f, new Vector3(0f, StartOffsetY, 0f));
            translationAnim.InsertKeyFrame(0.38f, new Vector3(0f, OvershootY, 0f), easePhase1);
            translationAnim.InsertKeyFrame(0.70f, new Vector3(0f, ReboundDipY, 0f), easePhase2);
            translationAnim.InsertKeyFrame(1.00f, new Vector3(0f, FinalY, 0f), easePhase3);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Target = "Opacity";
            opacityAnim.Duration = TimeSpan.FromMilliseconds(320);
            opacityAnim.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            opacityAnim.DelayTime = delayTime;

            opacityAnim.InsertKeyFrame(0.00f, 0.0f);
            opacityAnim.InsertKeyFrame(1.00f, 1.0f, easeOpacity);

            item.StartAnimation(translationAnim);
            item.StartAnimation(opacityAnim);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (App.Current.MainWindow is MainWindow mainWindow)
        {
            mainWindow.SetTitleBar(OnboardingTitleBar);
        }
    }

    private void GetStartedButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsService = App.Current.Services.GetRequiredService<ISettingsService>();
        settingsService.SetValue("HasCompletedOnboarding", true);

        if (this.Frame is Frame rootFrame)
        {
            rootFrame.Navigate(typeof(MainShellPage), null, new DrillInNavigationTransitionInfo());
            rootFrame.BackStack.Clear();
        }
    }
}

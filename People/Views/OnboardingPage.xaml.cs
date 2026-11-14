using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using People.Core.Interfaces;

namespace People.Views;

public sealed partial class OnboardingPage : Page
{
    private const float StartOffsetY = 220.0f;
    private const float OvershootY = -24.0f;
    private const float ReboundDipY = 7.0f;
    private const float FinalY = 0.0f;

    private const double HexDeltaX = 124.707658144959; // 72 * sqrt(3)
    private const double HexPitchY = 144.0;
    private const double CanopyCenterX = 2000.0;

    private static readonly BitmapImage[] s_profilePictures = new BitmapImage[31];

    private static readonly Dictionary<int, List<int>> s_curatedColumns = new()
    {
        { 0, new List<int> { 6, 12, 16 } },
        { -1, new List<int> { 30, 5, 11 } },
        { 1, new List<int> { 13, 2, 8 } },
        { -2, new List<int> { 4, 19, 21 } },
        { 2, new List<int> { 18, 24, 3 } },
        { -3, new List<int> { 29, 9, 15, 27 } },
        { 3, new List<int> { 7, 25, 30, 14 } },
        { -4, new List<int> { 17, 22, 14, 8 } },
        { 4, new List<int> { 20, 26, 23, 5 } },
        { -5, new List<int> { 1, 10, 13, 6, 25 } },
        { 5, new List<int> { 16, 27, 28, 12, 2 } },
        { -6, new List<int> { 16, 27, 28, 19, 9 } },
        { 6, new List<int> { 1, 19, 13, 29, 21 } }
    };

    private readonly Dictionary<int, int> _columnGeneratedRows = new();
    private readonly Dictionary<(int, int), int> _assignedAssets = new();

    private bool _animationsStarted;
    private int _currentMaxK = -1;
    private int _currentTargetRows = -1;

    public OnboardingPage()
    {
        InitializeComponent();
        Loaded += OnboardingPage_Loaded;
    }

    private void OnboardingPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_animationsStarted) return;

        double width = ActualWidth > 0 ? ActualWidth : (CanopyHost.ActualWidth > 0 ? CanopyHost.ActualWidth : 1200.0);
        double height = ActualHeight > 0 ? ActualHeight : (CanopyHost.ActualHeight > 0 ? CanopyHost.ActualHeight : 700.0);
        EnsureColumnsAndRows(width, height);

        TriggerCanopyEntranceWhenReady();
    }

    private void TriggerCanopyEntranceWhenReady()
    {
        if (_animationsStarted) return;

        // Verify if keystone central images are already decoded and ready
        var keyBmp1 = GetProfileImage(6);
        var keyBmp2 = GetProfileImage(12);

        if (keyBmp1.PixelWidth > 0 && keyBmp2.PixelWidth > 0)
        {
            ExecuteCanopyEntranceAnimation();
            return;
        }

        bool executed = false;
        void OnReady()
        {
            if (executed) return;
            executed = true;
            keyBmp1.ImageOpened -= OnBmpOpened;
            keyBmp2.ImageOpened -= OnBmpOpened;
            ExecuteCanopyEntranceAnimation();
        }

        void OnBmpOpened(object? s, RoutedEventArgs args) => DispatcherQueue.TryEnqueue(OnReady);

        keyBmp1.ImageOpened += OnBmpOpened;
        keyBmp2.ImageOpened += OnBmpOpened;

        // Safety fallback: if ImageOpened does not fire within 80ms, start entrance anyway
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (s, args) =>
        {
            timer.Stop();
            OnReady();
        };
        timer.Start();
    }

    private void ExecuteCanopyEntranceAnimation()
    {
        if (_animationsStarted) return;
        _animationsStarted = true;

        CanopyCanvas.Opacity = 1.0;
        StartStaggeredCanopyAnimation(CanopyCanvas.Children.OfType<FrameworkElement>());
    }

    private void OnboardingPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        EnsureColumnsAndRows(e.NewSize.Width, e.NewSize.Height);
    }

    private void EnsureColumnsAndRows(double width, double height)
    {
        int maxK = (int)Math.Ceiling((width / 2.0) / HexDeltaX) + 1;
        if (maxK < 3) maxK = 3;

        // Calculate rows required so every column spans the full window height from top edge to past bottom edge
        int targetRows = (int)Math.Ceiling(Math.Max(height, 600.0) / HexPitchY) + 2;
        if (targetRows < 4) targetRows = 4;

        if (maxK <= _currentMaxK && targetRows <= _currentTargetRows) return;
        _currentMaxK = Math.Max(_currentMaxK, maxK);
        _currentTargetRows = Math.Max(_currentTargetRows, targetRows);

        for (int k = -maxK; k <= maxK; k++)
        {
            int startRow = _columnGeneratedRows.TryGetValue(k, out int currentCount) ? currentCount : 0;
            if (startRow >= targetRows) continue;

            double left = GetColumnLeft(k);

            for (int row = startRow; row < targetRows; row++)
            {
                int assetId = GetOrAssignAsset(k, row);
                double top = GetImageTop(k, row);

                var img = new Image
                {
                    Source = GetProfileImage(assetId),
                    Width = 116,
                    Height = 116,
                    Stretch = Stretch.UniformToFill
                };
                AutomationProperties.SetAccessibilityView(img, AccessibilityView.Raw);
                Canvas.SetLeft(img, left);
                Canvas.SetTop(img, top);

                CanopyCanvas.Children.Add(img);

                if (_animationsStarted)
                {
                    img.Opacity = 1.0;
                }
            }

            _columnGeneratedRows[k] = targetRows;
        }
    }

    private static double GetColumnLeft(int k)
    {
        return (CanopyCenterX - 58.0) + (k * HexDeltaX);
    }

    private static double GetImageTop(int k, int row)
    {
        bool isEven = (Math.Abs(k) % 2 == 0);
        double baseTop = isEven ? 14.0 : -58.0;
        return baseTop + (row * HexPitchY);
    }

    private int GetOrAssignAsset(int k, int row)
    {
        if (_assignedAssets.TryGetValue((k, row), out int cached))
        {
            return cached;
        }

        if (s_curatedColumns.TryGetValue(k, out var list) && row < list.Count)
        {
            int curatedId = list[row];
            _assignedAssets[(k, row)] = curatedId;
            return curatedId;
        }

        var forbidden = new HashSet<int>();
        if (_assignedAssets.TryGetValue((k, row - 1), out int above)) forbidden.Add(above);
        if (_assignedAssets.TryGetValue((k, row + 1), out int below)) forbidden.Add(below);

        bool isEven = (Math.Abs(k) % 2 == 0);
        int r1 = isEven ? row : row - 1;
        int r2 = isEven ? row + 1 : row;

        if (_assignedAssets.TryGetValue((k - 1, r1), out int n1)) forbidden.Add(n1);
        if (_assignedAssets.TryGetValue((k - 1, r2), out int n2)) forbidden.Add(n2);
        if (_assignedAssets.TryGetValue((k + 1, r1), out int n3)) forbidden.Add(n3);
        if (_assignedAssets.TryGetValue((k + 1, r2), out int n4)) forbidden.Add(n4);

        int hash = Math.Abs(k * 37 + row * 19 + 7);
        int candidate = (hash % 30) + 1;
        int chosen = candidate;

        for (int step = 0; step < 30; step++)
        {
            int assetId = ((candidate - 1 + step) % 30) + 1;
            if (!forbidden.Contains(assetId))
            {
                chosen = assetId;
                break;
            }
        }

        _assignedAssets[(k, row)] = chosen;
        return chosen;
    }

    public static void PreloadProfilePictures()
    {
        for (int i = 1; i <= 30; i++)
        {
            GetProfileImage(i);
        }
    }

    private static BitmapImage GetProfileImage(int id)
    {
        if (id < 1 || id > 30) id = 1;
        if (s_profilePictures[id] == null)
        {
            var bmp = new BitmapImage
            {
                DecodePixelWidth = 116,
                DecodePixelHeight = 116,
                DecodePixelType = DecodePixelType.Logical,
                UriSource = new Uri($"ms-appx:///Assets/ProfilePictures/ProfilePicture{id}.png")
            };
            s_profilePictures[id] = bmp;
        }
        return s_profilePictures[id];
    }

    private void StartStaggeredCanopyAnimation(IEnumerable<FrameworkElement> elements)
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

        const double canopyEpicenterX = CanopyCenterX;
        const double canopyEpicenterY = 150.0;
        const double minCanopyDistance = 66.0;
        const double maxCanopyDistance = 900.0;
        const double maxStaggerDurationMs = 240.0;

        int imageIndex = 0;
        foreach (FrameworkElement item in elements)
        {
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

using System;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace People.Views;

/// <summary>
/// Custom SystemBackdrop providing Acrylic behind the taskbar flyout and
/// following Windows 11 "Show accent color on Start and taskbar" when enabled.
/// </summary>
public sealed class FlyoutAcrylicBackdrop : SystemBackdrop
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private DesktopAcrylicKind _kind;
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private ICompositionSupportsSystemBackdrop? _connectedTarget;
    private DispatcherQueue? _dispatcherQueue;
    private UISettings? _uiSettings;
    private bool _isCustomTinted;

    public event Action? SystemColorsRefreshed;

    public FlyoutAcrylicBackdrop(DesktopAcrylicKind kind = DesktopAcrylicKind.Base)
    {
        _kind = kind;
    }

    public DesktopAcrylicKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            if (_connectedTarget != null)
            {
                if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
                {
                    CreateAndAttachController(_connectedTarget);
                    ApplyControllerTint();
                }
                else
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        if (_connectedTarget != null)
                        {
                            CreateAndAttachController(_connectedTarget);
                            ApplyControllerTint();
                        }
                    });
                }
            }
        }
    }

    public static bool IsTaskbarAccentColorEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key?.GetValue("ColorPrevalence") is int prevalence)
            {
                return prevalence == 1;
            }
        }
        catch { }
        return false;
    }

    public static bool IsTaskbarLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key?.GetValue("SystemUsesLightTheme") is int light)
            {
                return light == 1;
            }
        }
        catch { }
        return false;
    }

    public void RefreshSystemColors()
    {
        if (_connectedTarget == null || _controller == null)
        {
            return;
        }

        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(ApplyControllerTint);
            return;
        }

        ApplyControllerTint();
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        _connectedTarget = connectedTarget;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);

        CreateAndAttachController(connectedTarget);

        _uiSettings ??= new UISettings();
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;

        ApplyControllerTint();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        if (_uiSettings != null)
        {
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            _uiSettings = null;
        }

        _controller?.Dispose();
        _controller = null;
        _configuration = null;
        _connectedTarget = null;
        _dispatcherQueue = null;
        _isCustomTinted = false;
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);

        _configuration = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        if (_controller != null && _configuration != null)
        {
            _controller.SetSystemBackdropConfiguration(_configuration);
            ApplyControllerTint();
        }
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        _dispatcherQueue?.TryEnqueue(ApplyControllerTint);
    }

    private void CreateAndAttachController(ICompositionSupportsSystemBackdrop target)
    {
        _controller?.Dispose();
        _controller = new DesktopAcrylicController
        {
            Kind = _kind
        };
        _controller.AddSystemBackdropTarget(target);
        if (_configuration != null)
        {
            _controller.SetSystemBackdropConfiguration(_configuration);
        }
        _isCustomTinted = false;
    }

    private void ApplyControllerTint()
    {
        if (_controller == null || _connectedTarget == null)
        {
            return;
        }

        if (!IsTaskbarAccentColorEnabled())
        {
            // Recreate controller when leaving custom tinting so WinUI restores built-in Base/Thin colors.
            if (_isCustomTinted)
            {
                CreateAndAttachController(_connectedTarget);
            }
            SystemColorsRefreshed?.Invoke();
            return;
        }

        try
        {
            _uiSettings ??= new UISettings();
            bool isLightTaskbar = IsTaskbarLightTheme();
            Color tintColor;
            Color fallbackColor;
            float tintOpacity;
            float luminosityOpacity;

            if (isLightTaskbar)
            {
                tintColor = _uiSettings.GetColorValue(UIColorType.AccentLight2);
                fallbackColor = _uiSettings.GetColorValue(UIColorType.AccentLight3);
                tintOpacity = _kind == DesktopAcrylicKind.Thin ? 0.65f : 0.78f;
                luminosityOpacity = 0.85f;
            }
            else
            {
                tintColor = _uiSettings.GetColorValue(UIColorType.AccentDark2);
                fallbackColor = _uiSettings.GetColorValue(UIColorType.AccentDark3);
                tintOpacity = _kind == DesktopAcrylicKind.Thin ? 0.68f : 0.82f;
                luminosityOpacity = _kind == DesktopAcrylicKind.Thin ? 0.80f : 0.90f;
            }

            _controller.TintColor = tintColor;
            _controller.FallbackColor = fallbackColor;
            _controller.TintOpacity = tintOpacity;
            _controller.LuminosityOpacity = luminosityOpacity;
            _isCustomTinted = true;
            SystemColorsRefreshed?.Invoke();
        }
        catch { }
    }
}

using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace People.Views;

public sealed class FlyoutAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _acrylicController;
    private DesktopAcrylicKind _kind;

    public DesktopAcrylicKind Kind
    {
        get => _kind;
        set
        {
            if (_kind != value)
            {
                _kind = value;
                if (_acrylicController != null)
                {
                    _acrylicController.Kind = value;
                }
            }
        }
    }

    public FlyoutAcrylicBackdrop(DesktopAcrylicKind kind = DesktopAcrylicKind.Base)
    {
        _kind = kind;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _acrylicController = new DesktopAcrylicController
        {
            Kind = _kind
        };

        var config = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
        _acrylicController.AddSystemBackdropTarget(connectedTarget);
        _acrylicController.SetSystemBackdropConfiguration(config);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        if (_acrylicController != null)
        {
            _acrylicController.RemoveSystemBackdropTarget(disconnectedTarget);
            _acrylicController.Dispose();
            _acrylicController = null;
        }
    }
}

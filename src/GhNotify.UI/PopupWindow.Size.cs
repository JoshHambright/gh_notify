using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>The popup's width (dragged from the left edge; the right edge stays on the tray corner) and text size.</summary>
public partial class PopupWindow
{
    double _appliedScale = 1;
    // While the left edge is dragged: where the pointer started (screen pixels) and the width then (unscaled).
    (double X, double Width)? _drag;
    double _dragWidth;

    void InitResizeGrip()
    {
        ResizeGrip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(ResizeGrip).Properties.IsLeftButtonPressed) return;
            e.Pointer.Capture(ResizeGrip);
            BeginWidthDrag(ScreenX(e));
            e.Handled = true;
        };
        ResizeGrip.PointerMoved += (_, e) => DragWidthTo(ScreenX(e));
        ResizeGrip.PointerReleased += (_, e) =>
        {
            e.Pointer.Capture(null);
            EndWidthDrag();
        };
        ResizeGrip.PointerCaptureLost += (_, _) => EndWidthDrag();
        ResizeGrip.DoubleTapped += (_, e) =>
        {
            _drag = null;
            _host.SetPopupWidth(PopupSize.DefaultWidth);
            ApplySize();
            e.Handled = true;
        };
    }

    double ScreenX(PointerEventArgs e) => this.PointToScreen(e.GetPosition(this)).X;

    double Scaling => RenderScaling > 0 ? RenderScaling : 1;

    /// <summary>The widest the popup can be on this screen, unscaled (null before it was first placed).</summary>
    double? FitsOnScreen() => _area is { } a ? a.Width / Scaling / _appliedScale - 40 : null;

    /// <summary>Applies the host's text size and width. Skipped mid-drag so a refresh can't snap the edge back.</summary>
    void ApplySize()
    {
        if (_drag is not null) return;
        var scale = _host.TextScale;
        if (scale != _appliedScale)
        {
            Scaler.LayoutTransform = scale == 1 ? null : new ScaleTransform(scale, scale);
            _appliedScale = scale;
        }
        SetWidth(PopupSize.ClampWidth(_host.PopupWidth, FitsOnScreen()));
    }

    void SetWidth(double unscaled)
    {
        var width = Math.Round(unscaled * _appliedScale);
        if (Width != width) Width = width; // SizeChanged re-anchors the popup bottom-right
    }

    // The drag steps are internal so tests can drive them without a real pointer.
    internal void BeginWidthDrag(double screenX)
    {
        _dragWidth = PopupSize.ClampWidth(_host.PopupWidth, FitsOnScreen());
        _drag = (screenX, _dragWidth);
    }

    /// <summary>Moving left widens: the right edge is anchored to the tray corner.</summary>
    internal void DragWidthTo(double screenX)
    {
        if (_drag is not { } d) return;
        _dragWidth = PopupSize.ClampWidth(d.Width + (d.X - screenX) / Scaling / _appliedScale, FitsOnScreen());
        SetWidth(_dragWidth);
    }

    internal void EndWidthDrag()
    {
        if (_drag is null) return;
        _drag = null;
        _host.SetPopupWidth(_dragWidth);
        ApplySize();
    }
}

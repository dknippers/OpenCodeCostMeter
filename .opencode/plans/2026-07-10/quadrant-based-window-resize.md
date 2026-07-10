## Task

Change the widget's resize behavior so that when the window grows or shrinks (due to expanding/collapsing the model breakdown), it does not always keep the top-left corner fixed. Instead, it should anchor the corner closest to the screen quadrant the widget is currently in:

| Quadrant | Fixed (anchor) corner | Expansion direction |
|----------|----------------------|---------------------|
| Top-left | Top-left | Right + Bottom |
| Top-right | Top-right | Left + Bottom |
| Bottom-right | Bottom-right | Left + Top |
| Bottom-left | Bottom-left | Right + Top |

## Feasibility

**Possible.** WPF's `SizeToContent="WidthAndHeight"` always resizes relative to the top-left corner. There is no built-in property to change the resize anchor. However, we can achieve the desired effect by hooking the `SizeChanged` event and manually adjusting the window's `Left` and `Top` coordinates to compensate for the width/height delta. This is a well-established two-step pattern:

1. Let WPF resize the window naturally (top-left anchored).
2. In the `SizeChanged` handler, compute the delta (`newSize - previousSize`).
3. Based on the window's position relative to the screen center, subtract the delta from `Left` and/or `Top` so the opposite edges appear fixed.

## Implementation Plan

### 1. Determine screen quadrant
- On every `SizeChanged`, locate the monitor the window is currently on using `System.Windows.Forms.Screen.FromHandle()`.
- Convert the window's center point from DIPs (device-independent pixels, WPF's coordinate space) to physical screen pixels using `PresentationSource.CompositionTarget.TransformToDevice`.
- Compare the window center against the monitor's working-area center to decide whether the window is in the right half and/or bottom half.

### 2. Adjust position on resize
- In `MainWindow.SizeChanged`, capture `e.PreviousSize` and `e.NewSize`.
- Guard against initial show (`PreviousSize.Width == 0`) and no-op changes.
- Calculate `deltaWidth = NewSize.Width - PreviousSize.Width` and `deltaHeight = NewSize.Height - PreviousSize.Height`.
- If the window is in the **right** half of the screen: `Left -= deltaWidth`.
- If the window is in the **bottom** half of the screen: `Top -= deltaHeight`.

### 3. Edge cases
- **First show / previous size zero:** skip adjustment; no meaningful delta exists.
- **No size change:** skip adjustment.
- **Multi-monitor:** `Screen.FromHandle` correctly identifies the monitor containing the window, so quadrant detection works across displays.
- **DPI / scaling:** the transform matrix handles per-monitor DPI automatically; the comparison is done in physical pixels, while the `Left`/`Top` adjustment stays in WPF DIPs (the delta from `SizeChanged` is already in DIPs).
- **Flicker:** the resize and the coordinate shift happen synchronously inside the same `SizeChanged` event, before the next render frame, so no intermediate state is visible.

## Files to modify

- `src/MainWindow.xaml.cs` — add `SizeChanged` handler, add `using System.Windows.Forms` and `using System.Windows.Interop`.

## Acceptance criteria

- [ ] Widget expands/collapses with no visible top-left anchoring.
- [ ] When the widget is in the top-right quadrant, the top-right corner stays in place.
- [ ] When the widget is in the bottom-right quadrant, the bottom-right corner stays in place.
- [ ] When the widget is in the bottom-left quadrant, the bottom-left corner stays in place.
- [ ] When the widget is in the top-left quadrant, behavior remains identical to before (top-left fixed).

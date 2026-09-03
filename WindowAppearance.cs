using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace AniTV;

public partial class MainWindow
{
    const uint MonitorDefaultToNearest = 2;
    const int WmGetMinMaxInfo = 0x0024;
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo { public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor,Work; public uint Flags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);

    void InstallWindowSizingHook()
    {
        if(PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(WindowSizingHook);
    }

    IntPtr WindowSizingHook(IntPtr window,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message!=WmGetMinMaxInfo) return IntPtr.Zero;
        var monitor=MonitorFromWindow(window,MonitorDefaultToNearest);
        var info=new MonitorInfo { Size=Marshal.SizeOf<MonitorInfo>() };
        if(monitor==IntPtr.Zero || !GetMonitorInfo(monitor,ref info)) return IntPtr.Zero;
        var sizing=Marshal.PtrToStructure<MinMaxInfo>(lParam);
        sizing.MaxPosition.X=info.Work.Left-info.Monitor.Left;
        sizing.MaxPosition.Y=info.Work.Top-info.Monitor.Top;
        sizing.MaxSize.X=info.Work.Right-info.Work.Left;
        sizing.MaxSize.Y=info.Work.Bottom-info.Work.Top;
        // Do not constrain the largest normal window to the work area: the player
        // uses an explicitly sized borderless normal window to cover the taskbar.
        Marshal.StructureToPtr(sizing,lParam,false);
        handled=true;
        return IntPtr.Zero;
    }

    Rect CurrentMonitorBounds()
    {
        var monitor=MonitorFromWindow(new WindowInteropHelper(this).Handle,MonitorDefaultToNearest);
        var info=new MonitorInfo { Size=Marshal.SizeOf<MonitorInfo>() };
        if(!GetMonitorInfo(monitor,ref info))
            return new Rect(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenWidth,SystemParameters.VirtualScreenHeight);
        var dpi=VisualTreeHelper.GetDpi(this);
        return new Rect(info.Monitor.Left/dpi.DpiScaleX,info.Monitor.Top/dpi.DpiScaleY,
            (info.Monitor.Right-info.Monitor.Left)/dpi.DpiScaleX,(info.Monitor.Bottom-info.Monitor.Top)/dpi.DpiScaleY);
    }
    void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void MaximizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    void ApplyFullscreenShell(bool fullscreen)
    {
        DesktopCaptionRow.Height = new GridLength(fullscreen ? 0 : 42);
        DesktopTitlebar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        WindowFrame.BorderThickness = new Thickness(fullscreen ? 0 : 1);
        DesktopChrome.CaptionHeight = fullscreen ? 0 : 42;
        DesktopChrome.ResizeBorderThickness = new Thickness(fullscreen ? 0 : 6);
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AniTV;

public partial class MainWindow
{
    readonly HashSet<int> consumedPlayerKeys = [];
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] static extern short GetKeyState(int key);

    static bool IsAniTvWindow(IntPtr window)
    {
        if(window==IntPtr.Zero) return false;
        GetWindowThreadProcessId(window,out var processId);
        return processId==(uint)Environment.ProcessId;
    }

    void InitializePlayerKeyboard()
    {
        // Receive messages before WPF routes them to a button, popup or VLC host.
        ComponentDispatcher.ThreadFilterMessage += PlayerKeyboardMessage;
        PreviewKeyDown += PlayerKeyboardPreview;
        PreviewKeyUp += PlayerKeyboardPreview;
        VideoOverlayRoot.PreviewKeyDown += PlayerKeyboardPreview;
        VideoOverlayRoot.PreviewKeyUp += PlayerKeyboardPreview;
        Closed += (_, _) => ComponentDispatcher.ThreadFilterMessage -= PlayerKeyboardMessage;
    }

    void PlayerKeyboardMessage(ref MSG message, ref bool handled)
    {
        if(handled || message.message is not (0x100 or 0x101)) return;
        if(!IsAniTvWindow(message.hwnd)) return;
        handled=HandlePlayerKey(message.wParam.ToInt32(),message.message==0x101,
            (message.lParam.ToInt64() & (1L << 30)) != 0);
    }

    void PlayerKeyboardPreview(object sender, KeyEventArgs e)
    {
        if(e.Handled) return;
        e.Handled=HandlePlayerKey(KeyInterop.VirtualKeyFromKey(e.Key),e.IsUp,e.IsRepeat);
    }

    bool HandlePlayerKey(int key, bool released, bool repeat)
    {
        // VLC's overlay can be a separate top-level window, especially outside fullscreen.
        if(!IsEnabled || !PlayerActive || !IsAniTvWindow(GetForegroundWindow()))
        {
            consumedPlayerKeys.Clear();
            return false;
        }
        if(released) return consumedPlayerKeys.Remove(key);
        // Read native modifier state before WPF has processed this message.
        var modifiers=ModifierKeys.None;
        if(GetKeyState(0x10)<0) modifiers|=ModifierKeys.Shift;
        if(GetKeyState(0x11)<0) modifiers|=ModifierKeys.Control;
        if(GetKeyState(0x12)<0) modifiers|=ModifierKeys.Alt;
        if(GetKeyState(0x5B)<0 || GetKeyState(0x5C)<0) modifiers|=ModifierKeys.Windows;
        var shortcut = modifiers == ModifierKeys.None && (key == 0x20 || key == 0x7A || key == 0x1B && isFullscreen) ||
            modifiers == ModifierKeys.Shift && key is 0x25 or 0x27;
        if(!shortcut) return false;
        consumedPlayerKeys.Add(key);
        if(!repeat) _ = ExecutePlayerShortcutAsync(key);
        return true;
    }

    async Task ExecutePlayerShortcutAsync(int key)
    {
        if(key is 0x1B or 0x7A) ToggleFullscreen();
        else if(!changingSource)
        {
            if(key == 0x20) PlayPause_Click(this, new RoutedEventArgs());
            else await MoveEpisodeAsync(key == 0x27 ? 1 : -1);
        }
    }
}

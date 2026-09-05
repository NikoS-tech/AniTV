using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AniTV;

public partial class MainWindow
{
    readonly HashSet<int> consumedPlayerKeys = [];
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);

    void InitializePlayerKeyboard()
    {
        // Receive messages before WPF routes them to a button, popup or VLC host.
        ComponentDispatcher.ThreadFilterMessage += PlayerKeyboardMessage;
        Closed += (_, _) => ComponentDispatcher.ThreadFilterMessage -= PlayerKeyboardMessage;
    }

    void PlayerKeyboardMessage(ref MSG message, ref bool handled)
    {
        if(handled || message.message is not (0x100 or 0x101)) return;
        var main = new WindowInteropHelper(this).Handle;
        if(main == IntPtr.Zero || !IsEnabled || !PlayerActive ||
            GetAncestor(GetForegroundWindow(), 3) != main || GetAncestor(message.hwnd, 3) != main)
        {
            consumedPlayerKeys.Clear();
            return;
        }
        var key = message.wParam.ToInt32();
        if(message.message == 0x101)
        {
            if(consumedPlayerKeys.Remove(key)) handled = true;
            return;
        }
        var modifiers = Keyboard.Modifiers;
        var shortcut = modifiers == ModifierKeys.None && (key == 0x20 || key == 0x7A || key == 0x1B && isFullscreen) ||
            modifiers == ModifierKeys.Shift && key is 0x25 or 0x27;
        if(!shortcut) return;
        handled = true;
        consumedPlayerKeys.Add(key);
        if((message.lParam.ToInt64() & (1L << 30)) != 0) return;
        _ = ExecutePlayerShortcutAsync(key);
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

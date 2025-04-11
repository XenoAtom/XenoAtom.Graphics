using NWindows;
using NWindows.Events;
using NWindows.Input;
using NWindows.Threading;
using System.Drawing;

namespace VkCube;

/// <summary>
/// Demonstrates how to create a simple cube using XenoAtom.Graphics.
/// </summary>
internal class Program
{
    static void Main(string[] args)
    {
        var mainWindow = Window.Create(new()
        {
            Title = "VkCube with XenoAtom.Graphics",
            StartPosition = WindowStartPosition.CenterScreen,
            BackgroundColor = GetCurrentThemeColor(),
        });


        var helloCube = new HelloCube(mainWindow);

        mainWindow.Events.Frame += (window, evt) =>
        {
            //Console.WriteLine($"Frame event: {evt.ChangeKind}");
            if (evt.ChangeKind == FrameChangeKind.ThemeChanged)
            {
                // Update the background color if the theme changed
                window.BackgroundColor = GetCurrentThemeColor();
            }

            // We always update the cube on frame events
            helloCube.Draw();
        };

        mainWindow.Events.Keyboard += (_, evt) =>
        {
            if (evt.IsDown)
            {
                if (evt.Key == Key.Escape)
                {
                    mainWindow.Close();
                    evt.Handled = true;
                }
                else if (evt.Key == Key.Enter && (evt.Modifiers & ModifierKeys.Alt) != 0)
                {
                    mainWindow.State = mainWindow.State == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
                    evt.Handled = true;
                }
            }
        };

        Dispatcher.Current.Events.Idle += DispatcherIdle;
        Dispatcher.Current.Run();

        helloCube.Dispose();

        void DispatcherIdle(Dispatcher dispatcher, NWindows.Threading.Events.IdleEvent evt)
        {
            helloCube.Draw();
            evt.SkipWaitForNextMessage = true;
            evt.Handled = true;
        }
    }

    static Color GetCurrentThemeColor() => WindowSettings.Theme == WindowTheme.Light ? Color.FromArgb(245, 245, 245) : Color.FromArgb(30, 30, 30);
}
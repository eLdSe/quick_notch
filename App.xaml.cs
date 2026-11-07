using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickNotchCs;

public sealed class ContextWheelPayload
{
    public string[] Files { get; set; } = Array.Empty<string>();
    public int X { get; set; }
    public int Y { get; set; }
}

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    public static bool IsOneShotWheelMode { get; private set; }
    public static string[]? PendingWheelFiles { get; private set; }
    public static int PendingWheelX { get; private set; }
    public static int PendingWheelY { get; private set; }

    public static string ContextWheelPayloadPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickNotch", "context_wheel.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        var cliFiles = ParseCliFiles(e.Args);
        WinApi.GetCursorPos(out var cursorPt);

        bool isNewInstance = true;
        try
        {
            _singleInstanceMutex = new Mutex(true, @"Local\QuickNotch_AppMutex_v2", out isNewInstance);
        }
        catch
        {
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            if (cliFiles.Count > 0)
            {
                try
                {
                    string path = ContextWheelPayloadPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var payload = new ContextWheelPayload { Files = cliFiles.ToArray(), X = cursorPt.X, Y = cursorPt.Y };
                    File.WriteAllText(path, JsonSerializer.Serialize(payload));
                    uint wheelMsg = WinApi.RegisterWindowMessage("QuickNotch_ContextWheel_Msg");
                    WinApi.PostMessage(WinApi.HWND_BROADCAST, wheelMsg, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }
            }
            else
            {
                // Уведомляем уже запущенный экземпляр развернуться
                uint wakeMsg = WinApi.RegisterWindowMessage(WinApi.WakeMessageName);
                WinApi.PostMessage(WinApi.HWND_BROADCAST, wakeMsg, IntPtr.Zero, IntPtr.Zero);
            }
            Shutdown();
            return;
        }

        if (cliFiles.Count > 0)
        {
            IsOneShotWheelMode = true;
            PendingWheelFiles = cliFiles.ToArray();
            PendingWheelX = cursorPt.X;
            PendingWheelY = cursorPt.Y;
        }

        // Принудительное аппаратное ускорение рендеринга через Direct3D / DirectX
        RenderOptions.ProcessRenderMode = RenderMode.Default;

        // Глобальные щиты от падений приложения
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            LogCrash(ev.ExceptionObject as Exception, "AppDomain.UnhandledException");
        };

        DispatcherUnhandledException += (s, ev) =>
        {
            ev.Handled = true; // Предотвращает аварийное закрытие WPF-приложения при ошибках UI-потока
            LogCrash(ev.Exception, "DispatcherUnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (s, ev) =>
        {
            ev.SetObserved(); // Предотвращает падение при необработанных исключениях в Task.Run / фоне
            LogCrash(ev.Exception, "TaskScheduler.UnobservedTaskException");
        };

        // Фоновая очистка временных файлов обновлений
        Task.Run(() =>
        {
            try
            {
                string? exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                foreach (var f in Directory.GetFiles(exeDir, "*delete_on_exit*"))
                {
                    try { File.Delete(f); } catch { }
                }
                foreach (var f in Directory.GetFiles(exeDir, "*.old*"))
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        });

        base.OnStartup(e);
    }

    public static void LogCrash(Exception? ex, string source)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n----------------------------------------\n";
            File.AppendAllText(path, entry);
        }
        catch { }
    }

    private static List<string> ParseCliFiles(string[] args)
    {
        var list = new List<string>();
        if (args == null || args.Length == 0) return list;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].Trim().Trim('"');
            if (string.Equals(a, "--wheel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--drag-wheel", StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(a) || Directory.Exists(a))
                list.Add(a);
        }
        return list;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}

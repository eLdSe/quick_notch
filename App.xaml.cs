using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickNotchCs;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "QuickNotch_SingleInstance_AppMutex_77f8a1", out bool isNewInstance);
        if (!isNewInstance)
        {
            // Уведомляем уже запущенный экземпляр развернуться
            uint wakeMsg = WinApi.RegisterWindowMessage(WinApi.WakeMessageName);
            WinApi.PostMessage(WinApi.HWND_BROADCAST, wakeMsg, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
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

        base.OnStartup(e);
    }

    private static void LogCrash(Exception? ex, string source)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n----------------------------------------\n";
            File.AppendAllText(path, entry);
        }
        catch { }
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

using System.Threading.Tasks;
using System.Windows;

namespace QuickNotchCs;

/// <summary>Порт bg.py: работа в фоне, результат возвращается в главный поток.</summary>
public static class Bg
{
    /// <summary>fn выполняется в потоке пула, done(result, error) — в главном.</summary>
    public static void Run<T>(Func<T> fn, Action<T?, Exception?> done)
    {
        var dispatcher = Application.Current.Dispatcher;
        Task.Run(() =>
        {
            T? result = default;
            Exception? error = null;
            try { result = fn(); }
            catch (Exception e) { error = e; }
            dispatcher.BeginInvoke(() =>
            {
                try { done(result, error); }
                catch (Exception ex) { App.LogCrash(ex, "Bg.Run.done"); }
            });
        });
    }

    /// <summary>Вариант для асинхронных вызовов WinRT.</summary>
    public static void RunAsync<T>(Func<Task<T>> fn, Action<T?, Exception?> done)
    {
        var dispatcher = Application.Current.Dispatcher;
        Task.Run(async () =>
        {
            T? result = default;
            Exception? error = null;
            try { result = await fn(); }
            catch (Exception e) { error = e; }
            await dispatcher.InvokeAsync(() =>
            {
                try { done(result, error); }
                catch (Exception ex) { App.LogCrash(ex, "Bg.RunAsync.done"); }
            });
        });
    }
}


using System;
using System.Diagnostics;

namespace tools
{
    public static class Profiler
    {
        // 用于测量无返回值的方法（Action）
        public static void Time(Action action, string description = "")
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            Console.WriteLine($"{description}耗时: {sw.ElapsedMilliseconds} ms");
        }

        // 用于测量有返回值的方法（Func<T>）
        public static T Time<T>(Func<T> func, string description = "")
        {
            var sw = Stopwatch.StartNew();
            var result = func();
            sw.Stop();
            Console.WriteLine($"{description}耗时: {sw.ElapsedMilliseconds} ms");
            return result;
        }
    }
}
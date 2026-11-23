using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace DteMcp.Vs
{
    /// <summary>Calls into Visual Studio over COM, retrying while it is busy and rejecting calls.</summary>
    internal static class Com
    {
        private const int RetryCount = 20;
        private const int RetryDelayMs = 250;

        private static readonly int RpcCallRejected = unchecked((int)0x80010001);
        private static readonly int RpcRetryLater = unchecked((int)0x8001010A);

        public static T Run<T>(Func<T> action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return action();
                }
                catch (COMException ex) when (attempt < RetryCount && IsBusy(ex))
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
        }

        public static void Do(Action action)
        {
            Run<object>(() =>
            {
                action();
                return null;
            });
        }

        /// <summary>Property reads that may legitimately fail (no source, optimized frame) return default.</summary>
        public static T Try<T>(Func<T> action)
        {
            try
            {
                return Run(action);
            }
            catch (Exception)
            {
                return default(T);
            }
        }

        private static bool IsBusy(COMException ex)
        {
            return ex.HResult == RpcCallRejected || ex.HResult == RpcRetryLater;
        }
    }

    /// <summary>Late-bound member access on COM objects, used by the generic object-model tool.</summary>
    internal static class ComReflection
    {
        public static object Get(object target, string member, object[] arguments)
        {
            return Invoke(target, member, BindingFlags.GetProperty | BindingFlags.InvokeMethod, arguments);
        }

        public static void Set(object target, string member, object[] arguments, object value)
        {
            object[] withValue = new object[arguments.Length + 1];
            arguments.CopyTo(withValue, 0);
            withValue[arguments.Length] = value;
            Invoke(target, member, BindingFlags.SetProperty, withValue);
        }

        private static object Invoke(object target, string member, BindingFlags flags, object[] arguments)
        {
            return Com.Run(() =>
            {
                try
                {
                    return target.GetType().InvokeMember(member, flags, null, target, arguments);
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    // Surface the real COMException so Com.Run can retry busy calls.
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    return null;
                }
            });
        }
    }
}

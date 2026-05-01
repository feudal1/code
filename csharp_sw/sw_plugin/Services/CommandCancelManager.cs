using System;
using System.Diagnostics;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace SolidWorksAddinStudy.Services
{
    internal static class CommandCancelManager
    {
        private static volatile bool _cancelRequested;

        public static bool IsCancelRequested => _cancelRequested;

        public static void Reset()
        {
            _cancelRequested = false;
        }

        public static void RequestCancel(SldWorks swApp)
        {
            _cancelRequested = true;
            try
            {
                swApp?.SendMsgToUser("已请求中止当前命令（Esc）");
            }
            catch
            {
                // ignore
            }
            Debug.WriteLine("命令取消已请求（Esc）");
        }

        public static void ThrowIfCancellationRequested()
        {
            if (_cancelRequested)
            {
                throw new OperationCanceledException("用户取消（Esc）");
            }
        }
    }

    internal sealed class EscCancelMessageFilter : IMessageFilter
    {
        private const int WM_KEYDOWN = 0x0100;
        private readonly SldWorks _swApp;

        public EscCancelMessageFilter(SldWorks swApp)
        {
            _swApp = swApp;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_KEYDOWN && (Keys)m.WParam == Keys.Escape)
            {
                CommandCancelManager.RequestCancel(_swApp);
            }

            return false;
        }
    }
}

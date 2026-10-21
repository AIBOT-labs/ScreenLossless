using System.Windows.Forms;

namespace ScreenLossless;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var settings=AppSettings.Load();
        L10n.Configure(settings.Language);Ui.Configure(settings.Theme);
        Application.ThreadException += (_, eventArgs) =>
            MessageBox.Show(eventArgs.Exception.Message, L10n.T("ScreenLossless 出错"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        using var instance = new SingleInstance();
        if (!instance.IsPrimary)
        {
            if (!instance.ActivateExistingAsync().GetAwaiter().GetResult())
                MessageBox.Show(L10n.T("已有 ScreenLossless 正在启动或退出，请稍候再试；也可以从任务栏或托盘打开。"), "ScreenLossless", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var context = new ScreenLosslessApplicationContext();
        // Create the message target before accepting activation requests.
        _ = context.Window.Handle;
        instance.Listen(() => context.RequestActivation());
        Application.Run(context);
    }
}
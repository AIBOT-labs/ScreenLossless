namespace ScreenLossless;
internal sealed record MainFormUiState(Rectangle Bounds,FormWindowState WindowState,int Page,string? LastFile,Bitmap? Preview,Rectangle PreviewBounds);
internal sealed class ScreenLosslessApplicationContext:ApplicationContext
{
    internal MainForm Window=>(MainForm)MainForm!;
    internal ScreenLosslessApplicationContext()
    {
        var settings=AppSettings.Load();
        L10n.Configure(settings.Language);Ui.Configure(settings.Theme);
        MainForm=CreateWindow();
    }
    private MainForm CreateWindow()
    {
        var window=new MainForm();window.LanguageChangeRequested+=SwitchLanguage;return window;
    }
    private void SwitchLanguage(object? sender,EventArgs e)
    {
        if(sender is not MainForm previous||previous!=MainForm)return;
        if(!previous.CanApplyLanguageChange){previous.CancelLanguageSwitch();return;}
        var snapshot=previous.CaptureLanguageUiState();
        // Detach the old form before closing it so the application message loop stays alive.
        MainForm=null;previous.Close();
        if(!previous.IsDisposed){MainForm=previous;previous.CancelLanguageSwitch();snapshot.Preview?.Dispose();return;}
        previous.Dispose();
        try
        {
            L10n.Configure(AppSettings.Load().Language);
            var next=CreateWindow();MainForm=next;next.RestoreLanguageUiState(snapshot);next.Show();next.Activate();
        }
        catch(Exception failure)
        {
            snapshot.Preview?.Dispose();MessageBox.Show(L10n.T("语言切换失败：")+failure.Message,"ScreenLossless",MessageBoxButtons.OK,MessageBoxIcon.Error);ExitThread();
        }
    }
    internal void RequestActivation()=>(MainForm as MainForm)?.RequestActivation();
}

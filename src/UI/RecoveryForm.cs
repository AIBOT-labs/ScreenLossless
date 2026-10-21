using System.Diagnostics;
using System.Windows.Forms;

namespace ScreenLossless;

internal sealed class RecoveryForm : ThemedForm
{
    private readonly string _folder;
    private readonly Capabilities _caps;
    private readonly DetailListBox _list = new() { Dock = DockStyle.Fill };
    private readonly Label _operationHint = new() { Dock = DockStyle.Fill, AutoEllipsis = true, BackColor = Ui.Surface, ForeColor = Ui.Subtle, Text = L10n.T("选择待恢复的录制素材。") };
    private readonly SoftButton _recover = new() { Text = L10n.T("恢复所选录制"), Glyph = "recover", Primary = true, Enabled = false };
    private readonly SoftButton _refresh = new() { Text = L10n.T("刷新"), Glyph = "refresh" };
    private readonly SoftButton _clean=new(){Text=L10n.T("清理素材"),Enabled=false};
    private readonly ChoiceBox _filter=new(){Dock=DockStyle.Fill};
    private readonly SoftButton _open = new() { Text = L10n.T("打开素材目录"), Glyph = "folder", Enabled = false };
    private readonly Label _empty = DialogLayout.Text(L10n.T("没有待恢复的录制\n当前保存目录中的录制均已完成。"), 10, false, Ui.Subtle);
    private readonly Label _count = DialogLayout.Text("", 8.5f, false, Ui.Subtle);
    private bool _busy;
    private CancellationTokenSource? _cancellation;

    public RecoveryForm(string folder, Capabilities caps)
    {
        _folder = folder; _caps = caps; SuspendLayout(); DialogLayout.Style(this);
        Text = L10n.T("恢复录制 · ScreenLossless");
        ClientSize = new Size(840, 590); MinimumSize = new Size(680, 480);
        var root = DialogLayout.Grid(new[] { 100f }, 70, -55, -45, 52);
        root.Padding = new Padding(24); SetContent(root);
        var title = DialogLayout.Grid(new[] { 100f }, 34, -100);
        title.Controls.Add(DialogLayout.Text(L10n.T("录制恢复与素材管理"), 18, true), 0, 0);
        title.Controls.Add(DialogLayout.Text(L10n.T("恢复中断录制，查看占用；已恢复素材可确认后清理。"), 9, false, Ui.Subtle), 0, 1);
        root.Controls.Add(title, 0, 0);
        var sessions = DialogLayout.Card(); sessions.Margin = new Padding(0, 0, 0, 12);
        var sessionGrid = DialogLayout.Grid(new[] { 100f }, 36, 28, -100);
        var sessionHeader = DialogLayout.Grid(new[] { 100f, -180f }, -100);
        sessionHeader.Controls.Add(DialogLayout.Text(L10n.T("录制素材"), 10, true), 0, 0);
        _filter.Items.AddRange(new[]{L10n.T("待恢复"),L10n.T("已恢复"),L10n.T("全部素材")});_filter.SelectedIndex=0;sessionHeader.Controls.Add(_filter,1,0);
        sessionGrid.Controls.Add(sessionHeader, 0, 0);sessionGrid.Controls.Add(_count,0,1);
        var listHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Ui.Surface };
        _empty.TextAlign = ContentAlignment.MiddleCenter; _empty.AutoEllipsis = false;
        listHost.Controls.Add(new ThemedScrollHost(_list)); listHost.Controls.Add(_empty); sessionGrid.Controls.Add(listHost, 0, 2);
        sessions.Controls.Add(sessionGrid); root.Controls.Add(sessions, 0, 1);
        _list.TitleText = item => ((RecoverableRecording)item).Manifest.TargetFile;
        _list.DetailText=item=>{var x=(RecoverableRecording)item;return x.Error is not null?L10n.T("清单无法读取 · 查看提示"):L10n.F($"{x.Manifest.CreatedAt:yyyy-MM-dd HH:mm:ss} · {(x.Manifest.CompletedFile is null?L10n.T("待恢复"):L10n.T("已恢复"))} · {x.Manifest.Segments.Count} 个片段 · {RecoveryStorage.SizeText(x.Bytes)}");};
        var hintCard = DialogLayout.Card(); var hintGrid = DialogLayout.Grid(new[] { 100f }, 28, -100);
        hintGrid.Controls.Add(DialogLayout.Text(L10n.T("操作提示"), 10, true), 0, 0);
        hintGrid.Controls.Add(_operationHint, 0, 1); hintCard.Controls.Add(hintGrid); root.Controls.Add(hintCard, 0, 2);
        var actions = DialogLayout.Grid(new[] { 100f, -92f, -108f, -144f, -156f }, -100);
        actions.Margin = new Padding(0, 12, 0, 0);
        actions.Controls.Add(DialogLayout.Text(L10n.T("录像文件保留"), 8.5f, false, Ui.Subtle), 0, 0);
        DialogLayout.Button(actions,_refresh,1);DialogLayout.Button(actions,_clean,2);DialogLayout.Button(actions,_open,3);DialogLayout.Button(actions,_recover,4);
        root.Controls.Add(actions, 0, 3);
        _recover.Click += async (_, _) => await RecoverAsync(); _refresh.Click += (_, _) => RefreshSessions();
        _filter.SelectedIndexChanged+=(_,_)=>{if(!_busy)RefreshSessions();};
        _clean.Click+=async(_,_)=>await CleanupSelectedAsync();
        _open.Click += (_, _) =>
        {
            if (_list.SelectedItem is not RecoverableRecording session) return;
            try { Process.Start(new ProcessStartInfo(session.Directory) { UseShellExecute = true }); }
            catch (Exception e) { ShowNotice(e.Message); }
        };
        _list.SelectedIndexChanged += (_, _) =>
        {
            UpdateSelection();
            if ((_list.SelectedItem as RecoverableRecording)?.Error is { } error) ShowNotice(L10n.T("无法读取清单：") + error);
        };
        Shown += (_, _) => { RefreshSessions(); if(!_caps.Available)ShowNotice(L10n.T("请返回主界面配置 FFmpeg，再恢复录制。")); };
        FormClosing += (_, e) => { if (_busy) { e.Cancel = true; ShowNotice(L10n.T("正在合成，请完成后再关闭。")); } };
        ResumeLayout(true);
    }
    private void UpdateSelection()
    {
        _recover.Enabled = (_busy && _cancellation is {IsCancellationRequested:false}) || !_busy && _caps.Available && _list.SelectedItem is RecoverableRecording { Error: null,Manifest.CompletedFile:null };
        _open.Enabled = !_busy && _list.SelectedItem is RecoverableRecording;
        _clean.Enabled=!_busy&&_list.SelectedItem is RecoverableRecording {Error:null,Manifest.CompletedFile:not null};
    }
    private void ShowNotice(string message) => _operationHint.Text = message;
    private void RefreshSessions()
    {
        _list.Items.Clear();
        try
        {
            var sessions=RecordingFinalizer.Discover(_folder,true).Where(x=>_filter.SelectedIndex==2||(_filter.SelectedIndex==1)==(x.Manifest.CompletedFile is not null)).ToArray();
            foreach(var session in sessions)_list.Items.Add(session);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            else { _recover.Enabled = false; ShowNotice(L10n.T("当前保存目录没有待恢复的录制。")); }
            _count.Text=L10n.F($"{_list.Items.Count} 项会话 · 素材占用 {RecoveryStorage.SizeText(sessions.Sum(x=>x.Bytes))}");
            _empty.Text=_filter.SelectedIndex==1?L10n.T("没有已恢复的素材"):L10n.T("没有符合条件的录制素材");
            _empty.Visible = _list.Items.Count == 0; _list.Visible = !_empty.Visible; UpdateSelection();
        }
        catch (Exception e) { ShowNotice(L10n.T("读取素材目录失败：") + e.Message); }
    }
    private async Task CleanupSelectedAsync(bool confirmed=false)
    {
        if(_busy||_list.SelectedItem is not RecoverableRecording {Error:null,Manifest.CompletedFile:not null} session)return;
        if(!confirmed&&MessageBox.Show(this,L10n.F($"删除已恢复录制的原始素材，释放约 {RecoveryStorage.SizeText(session.Bytes)}？\n\n已保存的录像会保留。素材删除后无法撤销。"),
            L10n.T("清理已恢复素材"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        _busy=true;_filter.Enabled=_refresh.Enabled=_list.Enabled=false;UpdateSelection();
        try
        {
            long bytes=await Task.Run(()=>RecoveryStorage.CleanupCompleted(_folder,session.Directory));
            ShowNotice(L10n.T("已清理原始素材：")+RecoveryStorage.SizeText(bytes)+L10n.T("，录像文件保留。"));RefreshSessions();
        }catch(Exception e){ShowNotice(L10n.T("清理失败：")+e.Message);}
        finally{_busy=false;_filter.Enabled=_refresh.Enabled=_list.Enabled=true;UpdateSelection();}
    }
    private async Task RecoverAsync()
    {
        if(_busy){_cancellation?.Cancel();_recover.Enabled=false;_recover.Text=L10n.T("取消中…");return;}
        if (_list.SelectedItem is not RecoverableRecording session || session.Error is not null||session.Manifest.CompletedFile is not null) return;
        using var request=new CancellationTokenSource();_cancellation=request;
        _busy = true; _recover.Text=L10n.T("取消恢复"); _recover.Enabled = true; _refresh.Enabled = false; _list.Enabled = false; _open.Enabled = false;_filter.Enabled=false;_clean.Enabled=false;
        try
        {
            string file = await RecordingFinalizer.ExportAsync(_caps, session.Directory, session.Manifest, ShowNotice,p=>{if(!IsDisposed)_count.Text=L10n.F($"恢复进度 {p:P0}");},request.Token);
            ShowNotice(L10n.T("恢复成功：") + file);
            RefreshSessions();
        }
        catch(OperationCanceledException){ShowNotice(L10n.T("已取消恢复，原始素材已保留。"));}
        catch (Exception e) { ShowNotice(L10n.T("恢复失败，原始素材已保留：") + e.Message); }
        finally { _cancellation=null;_recover.Text=L10n.T("恢复所选录制");_busy = false; _refresh.Enabled = true; _list.Enabled = true;_filter.Enabled=true; UpdateSelection(); }
    }
}

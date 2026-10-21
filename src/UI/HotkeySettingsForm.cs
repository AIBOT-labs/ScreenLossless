namespace ScreenLossless;

internal static class HotkeyBindings
{
    internal static readonly int[] Defaults = { (int)(Keys.Control|Keys.Alt|Keys.F9), (int)(Keys.Control|Keys.Alt|Keys.F10), (int)(Keys.Control|Keys.Alt|Keys.F8) };
    internal static readonly Keys[] Modifiers = { Keys.Control|Keys.Alt, Keys.Control|Keys.Shift, Keys.Alt|Keys.Shift, Keys.Control|Keys.Alt|Keys.Shift };
    internal static readonly Keys[] KeyCodes = Enumerable.Range((int)Keys.F1,24).Concat(Enumerable.Range((int)Keys.A,26)).Concat(Enumerable.Range((int)Keys.D0,10)).Select(k=>(Keys)k).ToArray();
    internal static string? Validate(int[]? keys)
    {
        if(keys is null || keys.Length!=3)return L10n.T("请设置三个快捷键。");
        if(keys.Any(k=>!Modifiers.Contains((Keys)k&Keys.Modifiers)||!KeyCodes.Contains((Keys)k&Keys.KeyCode)))return L10n.T("请选择有效的组合键。");
        if(keys.Distinct().Count()!=3)return L10n.T("三个快捷键不能重复。");
        return null;
    }
    internal static string Format(int binding)
    {
        var k=(Keys)binding;
        return ((k&Keys.Control)!=0?"Ctrl + ":"")+((k&Keys.Alt)!=0?"Alt + ":"")+((k&Keys.Shift)!=0?"Shift + ":"")+(k&Keys.KeyCode);
    }
    internal static bool Register(IntPtr window,int id,int binding)
    {
        var key=(Keys)binding;
        uint modifiers=WindowsInterop.ModNoRepeat;
        if((key&Keys.Control)!=0)modifiers|=WindowsInterop.ModControl;
        if((key&Keys.Alt)!=0)modifiers|=WindowsInterop.ModAlt;
        if((key&Keys.Shift)!=0)modifiers|=4;
        return WindowsInterop.RegisterHotKey(window,id,modifiers,(uint)(key&Keys.KeyCode));
    }
}

internal sealed class HotkeySettingsForm : ThemedForm
{
    private readonly ChoiceBox[] _modifiers=Enumerable.Range(0,3).Select(_=>new ChoiceBox{Dock=DockStyle.Fill}).ToArray();
    private readonly ChoiceBox[] _keys=Enumerable.Range(0,3).Select(_=>new ChoiceBox{Dock=DockStyle.Fill}).ToArray();
    internal HotkeySettingsForm(int[] current,Func<int[],string?> apply)
    {
        SuspendLayout();DialogLayout.Style(this);Text=L10n.T("自定义快捷键");ClientSize=new Size(580,400);MinimumSize=Size;
        var root=DialogLayout.Grid(new[]{100f},38,40,64,64,64,42,44);root.Padding=new Padding(22);SetContent(root);
        root.Controls.Add(DialogLayout.Text(L10n.T("自定义全局快捷键"),17,true),0,0);
        root.Controls.Add(DialogLayout.Text(L10n.T("选择组合键；如已被其他程序占用，会保留原来的设置。"),9,false,Ui.Subtle),0,1);
        var names=new[]{L10n.T("开始 / 停止录制"),L10n.T("暂停 / 继续录制"),L10n.T("保存截图")};
        for(int i=0;i<3;i++)
        {
            var row=DialogLayout.Grid(new[]{L10n.IsEnglish?-180f:-140f,60f,40f},36);row.Margin=new Padding(0,10,0,10);
            row.Controls.Add(DialogLayout.Text(names[i],9,true),0,0);
            _modifiers[i].Items.AddRange(new object[]{"Ctrl + Alt","Ctrl + Shift","Alt + Shift","Ctrl + Alt + Shift"});
            _keys[i].Items.AddRange(HotkeyBindings.KeyCodes.Cast<object>().ToArray());
            _modifiers[i].Margin=new Padding(0,0,10,0);row.Controls.Add(_modifiers[i],1,0);row.Controls.Add(_keys[i],2,0);root.Controls.Add(row,0,i+2);
        }
        void Fill(int[] values){for(int i=0;i<3;i++){_modifiers[i].SelectedIndex=Array.IndexOf(HotkeyBindings.Modifiers,(Keys)values[i]&Keys.Modifiers);_keys[i].SelectedIndex=Array.IndexOf(HotkeyBindings.KeyCodes,(Keys)values[i]&Keys.KeyCode);}}
        Fill(current);
        var error=DialogLayout.Text("",9,false,Ui.Danger);root.Controls.Add(error,0,5);
        var actions=DialogLayout.Grid(new[]{100f,-96f,-96f},44);
        var reset=new SoftButton{Text=L10n.T("恢复默认")};var cancel=new SoftButton{Text=L10n.T("取消")};var save=new SoftButton{Text=L10n.T("应用"),Primary=true};
        reset.Click+=(_,_)=>Fill(HotkeyBindings.Defaults);cancel.Click+=(_,_)=>Close();
        save.Click+=(_,_)=>{
            var bindings=Enumerable.Range(0,3).Select(i=>(int)(HotkeyBindings.Modifiers[Math.Max(0,_modifiers[i].SelectedIndex)]|HotkeyBindings.KeyCodes[Math.Max(0,_keys[i].SelectedIndex)])).ToArray();
            string? failure=HotkeyBindings.Validate(bindings)??apply(bindings);
            if(failure is not null){error.Text=failure;return;}DialogResult=DialogResult.OK;Close();
        };
        DialogLayout.Button(actions,reset,0);DialogLayout.Button(actions,cancel,1);DialogLayout.Button(actions,save,2);root.Controls.Add(actions,0,6);
        AcceptButton=save;CancelButton=cancel;ResumeLayout(true);
    }
}
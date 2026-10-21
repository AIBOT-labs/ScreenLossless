using System.Globalization;

namespace ScreenLossless;

internal sealed class NumberSettingsForm : ThemedForm
{
    private readonly TextBox[] _inputs;
    private readonly Label _error=DialogLayout.Text("",9,false,Ui.Danger);
    internal int[] Values { get; private set; }
    internal bool AdjustOnScreen { get; private set; }
    internal NumberSettingsForm(string title, string hint, string[] labels, int[] values, Func<int[],string?> validate, bool allowScreenAdjust=false)
    {
        SuspendLayout();DialogLayout.Style(this);Text=title;ClientSize=new Size(560,202+labels.Length*64+(allowScreenAdjust?44:0));MinimumSize=Size;
        Values=values.ToArray();_inputs=values.Select(value=>new TextBox{Text=value.ToString(CultureInfo.InvariantCulture)}).ToArray();
        var rows=new List<float>{36,40};rows.AddRange(labels.Select(_=>64f));rows.Add(38);rows.Add(44);if(allowScreenAdjust)rows.Add(44);
        var root=DialogLayout.Grid(new[]{100f},rows.ToArray());root.Padding=new Padding(22);SetContent(root);
        root.Controls.Add(DialogLayout.Text(title,17,true),0,0);root.Controls.Add(DialogLayout.Text(hint,9,false,Ui.Subtle),0,1);
        for(int i=0;i<labels.Length;i++)
        {
            var field=DialogLayout.Grid(new[]{100f},24,36);field.Margin=new Padding(0,0,0,4);
            field.Controls.Add(DialogLayout.Text(labels[i],9,true),0,0);field.Controls.Add(new TextField(_inputs[i]){Dock=DockStyle.Fill},0,1);root.Controls.Add(field,0,i+2);
        }
        root.Controls.Add(_error,0,labels.Length+2);
        var actions=DialogLayout.Grid(new[]{50f,50f},44);
        var cancel=new SoftButton{Text=L10n.T("取消")};var apply=new SoftButton{Text=L10n.T("应用"),Primary=true};
        DialogLayout.Button(actions,cancel,0);DialogLayout.Button(actions,apply,1);root.Controls.Add(actions,0,labels.Length+3);
        cancel.Click+=(_,_)=>{DialogResult=DialogResult.Cancel;Close();};
        bool Read()
        {
            var result=new int[_inputs.Length];
            for(int i=0;i<result.Length;i++)if(!int.TryParse(_inputs[i].Text.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out result[i])){_error.Text=L10n.T("请输入有效整数。");_inputs[i].Focus();return false;}
            if(validate(result) is {} message){_error.Text=message;return false;}
            Values=result;return true;
        }
        apply.Click+=(_,_)=>{if(Read()){DialogResult=DialogResult.OK;Close();}};
        if(allowScreenAdjust)
        {
            var screen=new SoftButton{Text=L10n.T("在屏幕上拖动调整"),Glyph="region",Dock=DockStyle.Fill,Margin=new Padding(0,6,0,0)};
            screen.Click+=(_,_)=>{if(Read()){AdjustOnScreen=true;DialogResult=DialogResult.OK;Close();}};
            root.Controls.Add(screen,0,labels.Length+4);
        }
        AcceptButton=apply;CancelButton=cancel;ResumeLayout(true);
    }
}
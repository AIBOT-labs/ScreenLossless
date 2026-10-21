namespace ScreenLossless;

internal sealed class RegionIndicator : CaptureOverlay
{
    // All native hit-test pixels, including resize grips, stay outside the recorded crop.
    protected override bool ExcludeFromRecording => false;
    private static readonly Color OutlineDark = Color.FromArgb(24, 31, 46);
    private static readonly Color OutlineLight = Color.FromArgb(250, 252, 255);
    private const int StrokeWidth = 3;
    private Rectangle _selection, _original;
    private Color _color=Ui.Accent;
    private bool _editable, _dragging;
    private Point _dragAnchor;
    private int _dragEdge;
    internal bool Editable => _editable;
    internal bool IsDragging => _dragging;
    internal Rectangle Selection => _selection;
    internal int Inset => _editable ? 12 : 4;
    internal event Action<Rectangle,bool>? SelectionEdited;
    internal event Action? CloseRequested;

    public RegionIndicator()
    {
        AutoScaleMode=AutoScaleMode.None;Opacity=.94;
        KeyPreview=true;
        KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape&&_dragging){FinishDrag(false);e.Handled=e.SuppressKeyPress=true;}};
        AccessibleName=L10n.T("录制选区边框");
        AccessibleDescription=L10n.T("未录制时拖动虚线移动；拖动八个边缘方块调整尺寸。区域内部鼠标穿透。");
    }
    protected override CreateParams CreateParams
    {
        get { var cp=base.CreateParams;if(!_editable)cp.ExStyle|=0x20;else cp.ExStyle&=~(0x20|0x8000000);return cp; }
    }
    internal void SetEditable(bool editable)
    {
        if(_editable==editable)return;
        _editable=editable;
        if(_dragging)FinishDrag(false);
        if(IsHandleCreated)UpdateStyles();
        if(_selection.Width>=64)RebuildMask();
        Cursor=editable?Cursors.SizeAll:Cursors.Default;
        Invalidate();
    }
    internal void SetSelection(Rectangle selection,RecorderState state)
    {
        if(selection.Width<64||selection.Height<64)return;
        var color=state==RecorderState.Paused?Ui.Warning:Ui.Accent;
        if(_selection!=selection){_selection=selection;RebuildMask();Invalidate();}
        if(_color!=color){_color=color;Invalidate();}
    }
    internal Rectangle MoveGrip => new(Inset+_selection.Width/4-8,Inset-9,16,8);
    internal (Rectangle Bounds,int Edge)[] ResizeGrips()
    {
        int x=Inset,y=Inset,w=_selection.Width,h=_selection.Height;
        return new[]{
            (new Rectangle(x-9,y-9,8,8),5),(new Rectangle(x+w+1,y-9,8,8),6),
            (new Rectangle(x-9,y+h+1,8,8),9),(new Rectangle(x+w+1,y+h+1,8,8),10),
            (new Rectangle(x+w/2-8,y-9,16,8),4),(new Rectangle(x+w/2-8,y+h+1,16,8),8),
            (new Rectangle(x-9,y+h/2-8,8,16),1),(new Rectangle(x+w+1,y+h/2-8,8,16),2)
        };
    }
    private IEnumerable<Rectangle> BorderSegments()
    {
        int inset=Inset,w=_selection.Width,h=_selection.Height;
        const int dash=10,step=18;
        // A dark 3px segment surrounds a 1px light core. Keep the entire stroke
        // outside the crop, including its end caps; gaps remain genuine holes.
        int outside=StrokeWidth+1;
        for(int x=inset;x<inset+w;x+=step)
        {
            int length=Math.Min(dash,inset+w-x);
            yield return new Rectangle(x,inset-outside,length,StrokeWidth);
            yield return new Rectangle(x,inset+h+1,length,StrokeWidth);
        }
        for(int y=inset;y<inset+h;y+=step)
        {
            int length=Math.Min(dash,inset+h-y);
            yield return new Rectangle(inset-outside,y,StrokeWidth,length);
            yield return new Rectangle(inset+w+1,y,StrokeWidth,length);
        }
        foreach(int x in new[]{inset-outside,inset+w-9})
            foreach(int y in new[]{inset-outside,inset+h+1})yield return new Rectangle(x,y,13,StrokeWidth);
        foreach(int x in new[]{inset-outside,inset+w+1})
            foreach(int y in new[]{inset-outside,inset+h-9})yield return new Rectangle(x,y,StrokeWidth,13);
    }
    private void RebuildMask()
    {
        Bounds=Rectangle.Inflate(_selection,Inset,Inset);
        var mask=new Region();mask.MakeEmpty();
        foreach(var segment in BorderSegments())mask.Union(segment);
        if(_editable){foreach(var grip in ResizeGrips())mask.Union(grip.Bounds);mask.Union(MoveGrip);}
        var old=Region;Region=mask;old?.Dispose();
    }
    internal int HitEdge(Point point)
    {
        if(!_editable||Region is null||!Region.IsVisible(point))return 0;
        foreach(var grip in ResizeGrips())if(grip.Bounds.Contains(point))return grip.Edge;
        return 16; // Dashes move the whole selection; the eight grips resize it.
    }
    private static Cursor EdgeCursor(int edge)=>edge switch
    {
        1 or 2=>Cursors.SizeWE,4 or 8=>Cursors.SizeNS,5 or 10=>Cursors.SizeNWSE,
        6 or 9=>Cursors.SizeNESW,16=>Cursors.SizeAll,_=>Cursors.Default
    };
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.Button==MouseButtons.Right&&_dragging){FinishDrag(false);return;}
        if(e.Button!=MouseButtons.Left||!_editable||_dragging)return;
        int edge=HitEdge(e.Location);if(edge==0)return;
        Activate(); // Explicit edge click owns foreground capture; showing/raising remains non-activating.
        _original=_selection;_dragAnchor=PointToScreen(e.Location);_dragEdge=edge;_dragging=true;
        Capture=true;Cursor=EdgeCursor(edge);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if(_dragging)UpdateDrag(PointToScreen(e.Location));
        else if(_editable)Cursor=EdgeCursor(HitEdge(e.Location));
    }
    private void UpdateDrag(Point screen)
    {
        var desktop=SystemInformation.VirtualScreen;
        var local=new Rectangle(_original.X-desktop.X,_original.Y-desktop.Y,_original.Width,_original.Height);
        var next=RegionPicker.Adjust(local,new Point(screen.X-_dragAnchor.X,screen.Y-_dragAnchor.Y),_dragEdge,desktop.Size);
        next.Offset(desktop.Location);
        if(next==_selection)return;
        _selection=next;RebuildMask();Invalidate();SelectionEdited?.Invoke(next,false);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if(e.Button!=MouseButtons.Left||!_dragging)return;
        UpdateDrag(PointToScreen(e.Location));FinishDrag(true);
    }
    internal void FinishDrag(bool commit)
    {
        if(!_dragging)return;
        _dragging=false;Capture=false;
        if(!commit){_selection=_original;RebuildMask();Invalidate();}
        SelectionEdited?.Invoke(_selection,true);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if(_dragging&&!Capture)FinishDrag(false);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if(e.CloseReason==CloseReason.UserClosing)
        {
            e.Cancel=true;FinishDrag(true);CloseRequested?.Invoke();
        }
        base.OnFormClosing(e);
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel=true;base.OnDpiChanged(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics=e.Graphics;
        graphics.Clear(OutlineDark);
        using var light=new SolidBrush(OutlineLight);
        foreach(var segment in BorderSegments())
        {
            var core=Rectangle.Inflate(segment,-1,-1);
            if(core.Width>0&&core.Height>0)graphics.FillRectangle(light,core);
        }
        using var accent=new Pen(_color,1);
        // Small corner marks carry recording/paused color; visibility of the
        // long outline never depends on the theme or the underlying picture.
        int inset=Inset,w=_selection.Width,h=_selection.Height;
        foreach(int x in new[]{inset-3,inset+w+2})
            foreach(int y in new[]{inset-3,inset+h+2})
            {
                int dx=x<inset?1:-1,dy=y<inset?1:-1;
                graphics.DrawLine(accent,x,y,x+dx*5,y);
                graphics.DrawLine(accent,x,y,x,y+dy*5);
            }
        if(!_editable)return;
        using var outline=new Pen(OutlineDark,1);
        graphics.FillRectangle(light,MoveGrip);
        var move=MoveGrip;int mid=move.Top+move.Height/2;
        var moveOutline=move;moveOutline.Width--;moveOutline.Height--;graphics.DrawRectangle(outline,moveOutline);
        graphics.DrawLine(accent,move.Left+3,mid,move.Right-4,mid);
        graphics.DrawLines(accent,new[]{new Point(move.Left+5,mid-2),new Point(move.Left+3,mid),new Point(move.Left+5,mid+2)});
        graphics.DrawLines(accent,new[]{new Point(move.Right-6,mid-2),new Point(move.Right-4,mid),new Point(move.Right-6,mid+2)});
        foreach(var grip in ResizeGrips())
        {
            graphics.FillRectangle(light,grip.Bounds);
            var rect=grip.Bounds;rect.Width--;rect.Height--;graphics.DrawRectangle(outline,rect);
            rect.Inflate(-1,-1);graphics.DrawRectangle(accent,rect);
        }
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x21){m.Result=new IntPtr(_editable?1:3);return;} // Explicit editing click activates for reliable capture outside the thin border.
        if(m.Msg==0x84)
        {
            long packed=m.LParam.ToInt64();
            var point=PointToClient(new Point(unchecked((short)packed),unchecked((short)(packed>>16))));
            m.Result=new IntPtr(HitEdge(point)==0?-1:1);return;
        }
        base.WndProc(ref m);
    }
}
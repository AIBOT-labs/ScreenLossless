using NAudio.CoreAudioApi;

namespace ScreenLossless;

internal sealed record AudioDeviceChoice(string Id, string Name, bool Available = true)
{
    public override string ToString() => Name;
}

internal static class AudioDevices
{
    internal static List<AudioDeviceChoice> Enumerate(DataFlow flow)
    {
        var choices=new List<AudioDeviceChoice> { new("",flow==DataFlow.Render ? L10n.T("跟随系统默认播放设备") : L10n.T("跟随系统默认麦克风")) };
        using var enumerator=new MMDeviceEnumerator();
        foreach(var device in enumerator.EnumerateAudioEndPoints(flow,DeviceState.Active))
            using(device) choices.Add(new(device.ID,device.FriendlyName));
        return choices;
    }
    internal static int Populate(ChoiceBox box,IEnumerable<AudioDeviceChoice> choices,string selectedId)
    {
        box.BeginUpdate();
        try
        {
            box.Items.Clear(); box.Items.AddRange(choices);
            int index=Enumerable.Range(0,box.Items.Count).FirstOrDefault(i=>((AudioDeviceChoice)box.Items[i]).Id==selectedId,-1);
            if(index<0&&!string.IsNullOrEmpty(selectedId))
            {
                box.Items.Add(new AudioDeviceChoice(selectedId,L10n.T("已选设备不可用 · 请重新选择"),false)); index=box.Items.Count-1;
            }
            box.SelectedIndex=index<0 ? 0 : index;
            return box.Items.Count;
        }
        finally {box.EndUpdate();}
    }
}
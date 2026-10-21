namespace ScreenLossless;

internal readonly record struct StorageDecision(long Reserve,bool Warn,bool Stop);
internal static class StorageGuard
{
    internal const long Margin=512L*1024*1024;
    internal static StorageDecision Evaluate(long available,long workspaceBytes,int copies=2)
    {
        copies=Math.Clamp(copies,2,4);
        long reserve=workspaceBytes>(long.MaxValue-Margin)/copies?long.MaxValue:Math.Max(0,workspaceBytes)*copies+Margin;
        long warning=reserve>long.MaxValue-1073741824L?long.MaxValue:reserve+1073741824L;
        return new(reserve,available<Math.Max(4L*1024*1024*1024,warning),available<=reserve);
    }
    internal static long? AvailableBytes(string folder)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace; }catch{return null;}
    }
    internal static long WorkspaceBytes(string folder)
    {
        if(!Directory.Exists(folder))return 0;
        long size=0;
        // Directory entries already carry size metadata on Windows; retain it
        // instead of issuing a fresh file-attribute query for every source part.
        foreach(var info in new DirectoryInfo(folder).EnumerateFiles())
        {
            // Intermediate mux files are not part of the source-size estimate.
            if(info.Name.StartsWith("video-",StringComparison.OrdinalIgnoreCase)||info.Name.StartsWith("audio-",StringComparison.OrdinalIgnoreCase))
                size=checked(size+info.Length);
        }
        return size;
    }
}

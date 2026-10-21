namespace ScreenLossless;

internal static class RecoveryStorage
{
    internal static long Size(string directory)
    {
        var root=new DirectoryInfo(directory);
        if((root.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException(L10n.T("素材目录不能是链接。"));
        long bytes=0;
        foreach(var file in root.EnumerateFiles())
        {if((file.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException(L10n.T("素材包含链接，无法清理。"));bytes=checked(bytes+file.Length);}
        foreach(var child in root.EnumerateDirectories())bytes=checked(bytes+Size(child.FullName));
        return bytes;
    }
    internal static string SizeText(long bytes)=>bytes>=1073741824?$"{bytes/1073741824d:0.00} GB":bytes>=1048576?$"{bytes/1048576d:0.0} MB":bytes>=1024?$"{bytes/1024d:0.0} KB":$"{bytes} B";
    internal static long CleanupCompleted(string outputFolder,string directory)
    {
        string folder=Path.GetFullPath(outputFolder);
        string root=Path.GetFullPath(Path.Combine(folder,".screenlossless-work"));
        string target=Path.GetFullPath(directory);
        if(!string.Equals(Path.GetDirectoryName(target),root,StringComparison.OrdinalIgnoreCase)
            ||!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidOperationException(L10n.T("只能清理当前保存目录中的独立素材会话。"));
        var manifest=RecordingManifest.Load(target);
        if(string.IsNullOrWhiteSpace(manifest.CompletedFile))throw new InvalidOperationException(L10n.T("未恢复的素材不能清理。"));
        string completed=RecordingManifest.ResolveFile(folder,manifest.CompletedFile);
        if(!File.Exists(completed)||new FileInfo(completed).Length==0)throw new InvalidOperationException(L10n.T("已保存的录像不存在，保留素材以便重新恢复。"));
        long bytes=Size(target); // Validate the complete tree before any deletion.
        Directory.Delete(target,true);
        return bytes;
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    private static byte[] ToolPayload((string Name,byte[] Bytes)[] files,bool badHash=false)
    {
        using var memory=new MemoryStream();
        using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            foreach(var file in files){using var stream=zip.CreateEntry(file.Name).Open();stream.Write(file.Bytes);}
            using var index=zip.CreateEntry("bundle-manifest.json").Open();
            JsonSerializer.Serialize(index,new{FormatVersion=1,Files=files.Select(file=>new{Name=file.Name,Length=file.Bytes.Length,Sha256=badHash?new string('0',64):Convert.ToHexString(SHA256.HashData(file.Bytes)).ToLowerInvariant()}).ToArray()});
        }
        return memory.ToArray();
    }
    private static async Task TestBundledToolsAsync()
    {
        string root=Path.Combine(_root,"bundle");Directory.CreateDirectory(root);
        var files=new[]{("ffmpeg.exe",Encoding.UTF8.GetBytes("test ffmpeg")),("ffprobe.exe",Encoding.UTF8.GetBytes("test ffprobe")),("ffmpeg-licenses/COPYING.txt",Encoding.UTF8.GetBytes("test license")),("FFmpeg-BUILD-RECORD.json",Encoding.UTF8.GetBytes("{}"))};
        byte[] bytes=ToolPayload(files);
        string path;using(var stream=new MemoryStream(bytes))path=BundledTools.EnsureFrom(stream,root);
        Check(path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.ReadAllBytes(path).SequenceEqual(files[0].Item2),"Standalone components extract inside their versioned per-user cache");
        string directory=Path.GetDirectoryName(path)!;
        Check(File.Exists(Path.Combine(directory,"ffprobe.exe"))&&File.Exists(Path.Combine(directory,"ffmpeg-licenses/COPYING.txt")),"Probe and license materials extract with the encoder");
        var stamp=DateTime.UtcNow.AddHours(-1);File.SetLastWriteTimeUtc(path,stamp);
        using(var stream=new MemoryStream(bytes))Check(BundledTools.EnsureFrom(stream,root)==path&&File.GetLastWriteTimeUtc(path)==stamp,"A warm start verifies and reuses valid tools without rewriting them");
        File.WriteAllText(path,"damaged");using(var stream=new MemoryStream(bytes))BundledTools.EnsureFrom(stream,root);
        Check(File.ReadAllBytes(path).SequenceEqual(files[0].Item2),"Damaged cached tools are repaired from their embedded original");
        string probe=Path.Combine(directory,"ffprobe.exe");File.Delete(probe);using(var stream=new MemoryStream(bytes))BundledTools.EnsureFrom(stream,root);
        Check(File.ReadAllBytes(probe).SequenceEqual(files[1].Item2),"A missing cached probe is repaired automatically");
        var newer=files.ToArray();newer[0]=("ffmpeg.exe",Encoding.UTF8.GetBytes("updated ffmpeg"));
        using(var stream=new MemoryStream(ToolPayload(newer)))Check(BundledTools.EnsureFrom(stream,root)!=path,"Changed payload hashes select a new cache instead of stale previous tools");
        var concurrent=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>{using var stream=new MemoryStream(bytes);return BundledTools.EnsureFrom(stream,root);})));
        Check(concurrent.All(item=>item==path)&&!Directory.EnumerateFiles(root,"*.tmp",SearchOption.AllDirectories).Any(),"Concurrent preparations agree on one intact cache and leave no partial files");
        foreach(string invalid in new[]{"../escaped.exe","C:/escaped.exe","folder/../escaped.exe","folder\\escaped.exe","file:stream"})
        {
            var entries=files.Append((invalid,Encoding.UTF8.GetBytes("invalid"))).ToArray();bool rejected=false;
            try{using var stream=new MemoryStream(ToolPayload(entries));BundledTools.EnsureFrom(stream,root);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Component extraction rejects unsafe output names: "+invalid);
        }
        bool damaged=false;try{using var stream=new MemoryStream(ToolPayload(files,true));BundledTools.EnsureFrom(stream,root);}catch(InvalidDataException){damaged=true;}
        Check(damaged&&!Directory.EnumerateFiles(root,"*.tmp",SearchOption.AllDirectories).Any(),"A corrupt embedded checksum publishes no invalid tool or temporary file");
        Check(BundledTools.IsManagedPath(Path.Combine(BundledTools.CacheBase,"version","ffmpeg.exe"))&&!BundledTools.IsManagedPath(Path.Combine(root,"custom-ffmpeg.exe")),"Bundled cache paths remain distinguishable from explicitly selected external encoders");
        string custom=Path.Combine(root,"custom-ffmpeg.exe");File.WriteAllText(custom,"custom");
        Check(HardwareProbe.LocateFfmpeg(custom)==custom,"An explicitly selected external encoder keeps precedence");
        Check(BundledTools.Locate() is null,"Non-standalone builds do not require or prepare an embedded payload");
        string actualPayload=Path.Combine(Environment.CurrentDirectory,".tools","standalone-payload","tools.zip");
        if(File.Exists(actualPayload))
        {
            using var actual=File.OpenRead(actualPayload);
            string actualEncoder=BundledTools.EnsureFrom(actual,Path.Combine(root,"actual-release"));
            Check(File.Exists(actualEncoder)&&File.Exists(Path.Combine(Path.GetDirectoryName(actualEncoder)!,"ffprobe.exe")),"The actual release ZIP extracts through the .NET 8 runtime used by the application");
        }
    }
}

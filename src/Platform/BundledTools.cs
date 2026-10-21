using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenLossless;

// Only the Standalone edition embeds this payload. External recording tools stay
// separate processes; their notices and exact build record travel with them.
internal static class BundledTools
{
    private const string ResourceName="ScreenLossless.Tools.zip";
    private const string IndexName="bundle-manifest.json";
    private sealed record PayloadFile(string Name,long Length,string Sha256);
    private sealed record PayloadManifest(int FormatVersion,PayloadFile[] Files);
    internal static string CacheBase=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ScreenLossless","components");
    internal static bool IsManagedPath(string? path)
    {
        if(string.IsNullOrWhiteSpace(path))return false;
        try{return Path.GetFullPath(path).StartsWith(Path.GetFullPath(CacheBase)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        catch(ArgumentException){return false;}
    }
    internal static string? Locate()
    {
        using var archive=typeof(BundledTools).Assembly.GetManifestResourceStream(ResourceName);
        if(archive is null)return null;
        CheckDirectories(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),CacheBase);
        return EnsureFrom(archive,CacheBase);
    }
    private static string Hash(Stream stream)=>Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    private static string Resolve(string root,string name)
    {
        if(string.IsNullOrEmpty(name)||name.Contains('\\')||name.Contains(':')||Path.IsPathRooted(name)
            ||name.Split('/').Any(part=>part is "" or "." or ".."||part.EndsWith('.')||part.EndsWith(' ')))
            throw new InvalidDataException("Invalid component path.");
        string path=Path.GetFullPath(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)));
        if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Component path is outside its cache.");
        return path;
    }
    private static void CheckDirectories(string root,string directory)
    {
        // Reject links both at the cache root and on the path to each output file.
        for(string? current=directory;current is not null;current=Path.GetDirectoryName(current))
        {
            if(Directory.Exists(current)&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("A component cache directory is a link.");
            if(string.Equals(current,root,StringComparison.OrdinalIgnoreCase))return;
        }
        throw new InvalidDataException("Component directory is outside its cache.");
    }
    private static bool Matches(string path,PayloadFile file)
    {
        if(!File.Exists(path))return false;
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("A component cache file is a link.");
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        return input.Length==file.Length&&string.Equals(Hash(input),file.Sha256,StringComparison.OrdinalIgnoreCase);
    }
    internal static string EnsureFrom(Stream payload,string cacheBase)
    {
        using var archive=new ZipArchive(payload,ZipArchiveMode.Read,true);
        var index=archive.GetEntry(IndexName)??throw new InvalidDataException("Missing component index.");
        if(index.Length>256*1024)throw new InvalidDataException("Component index is too large.");
        byte[] json;using(var input=index.Open())using(var memory=new MemoryStream()){input.CopyTo(memory);json=memory.ToArray();}
        var manifest=JsonSerializer.Deserialize<PayloadManifest>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        if(manifest is not {FormatVersion:1,Files.Length:>0}||manifest.Files.Length>256)throw new InvalidDataException("Invalid component index.");
        string basePath=Path.GetFullPath(cacheBase).TrimEnd(Path.DirectorySeparatorChar);
        string id=Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
        string root=Path.Combine(basePath,id);
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var file in manifest.Files)
        {
            if(file is null||string.IsNullOrEmpty(file.Name)||string.IsNullOrEmpty(file.Sha256))throw new InvalidDataException("Invalid component entry.");
            Resolve(root,file.Name);
            if(!names.Add(file.Name)||file.Length<0||file.Length>64L*1024*1024||file.Sha256.Length!=64||!file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid component entry.");
            total=checked(total+file.Length);
            if(total>64L*1024*1024)throw new InvalidDataException("Component payload is too large.");
            var entry=archive.GetEntry(file.Name);
            if(entry is null||entry.Length!=file.Length)throw new InvalidDataException("Component payload does not match its index: "+file.Name);
        }
        if(!names.Contains("ffmpeg.exe")||!names.Contains("ffprobe.exe")||archive.Entries.Count!=names.Count+1)
            throw new InvalidDataException("Recording tools are missing or duplicated.");
        // Cache identity includes all payload hashes. A new embedded version cannot
        // silently reuse a previous build selected in saved settings.
        string userKey=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(basePath))).Substring(0,16);
        using var gate=new Mutex(false,@"Local\ScreenLossless.Components."+userKey+"."+id);
        bool owned=false;
        try
        {
            try{owned=gate.WaitOne(TimeSpan.FromSeconds(30));}catch(AbandonedMutexException){owned=true;}
            if(!owned)throw new TimeoutException("Component preparation is already in progress.");
            CheckDirectories(basePath,basePath);Directory.CreateDirectory(basePath);
            CheckDirectories(basePath,root);Directory.CreateDirectory(root);
            foreach(var file in manifest.Files)
            {
                string destination=Resolve(root,file.Name),directory=Path.GetDirectoryName(destination)!;
                CheckDirectories(basePath,directory);Directory.CreateDirectory(directory);
                if(Matches(destination,file))continue;
                string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    using(var input=archive.GetEntry(file.Name)!.Open())
                    using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
                    if(!Matches(temporary,file))throw new InvalidDataException("Component checksum failed: "+file.Name);
                    File.Move(temporary,destination,true);
                }
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            return Resolve(root,"ffmpeg.exe");
        }
        finally{if(owned)gate.ReleaseMutex();}
    }
}

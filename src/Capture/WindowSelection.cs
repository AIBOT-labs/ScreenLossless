namespace ScreenLossless;

internal sealed class WindowSelection
{
    public string ProcessPath { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string Title { get; set; } = "";
    internal bool Valid => !string.IsNullOrWhiteSpace(ProcessPath)&&!string.IsNullOrWhiteSpace(ClassName)&&!string.IsNullOrWhiteSpace(Title);
    internal WindowInfo? Resolve(IEnumerable<(WindowInfo Window,WindowSelection? Identity)> candidates)
    {
        if(!Valid)return null;
        var matches=candidates.Where(c=>c.Identity is {} id&&id.ProcessPath.Equals(ProcessPath,StringComparison.OrdinalIgnoreCase)
            &&id.ClassName==ClassName&&id.Title==Title).Select(c=>c.Window).Take(2).ToArray();
        return matches.Length==1?matches[0]:null;
    }
}
internal sealed class FloatingPosition
{
    public int X {get;set;}
    public int Y {get;set;}
    internal Point Point => new(X,Y);
}

using System.Net;
using System.Text;

namespace SevenDaysModManager.Core;

/// <summary>One file in a mod folder on the server: its path inside the mod (with '/'), its size and, when known, when it was written.</summary>
public sealed record RemoteFile(string Path, long Size, DateTime? Modified = null);

/// <summary>
/// A server's Mods folder, wherever it is: a folder (a shared folder, a mapped drive, or a local dedicated server's
/// install) or an FTP server. Paths inside a mod use '/'.
/// </summary>
public interface IModsFolder : IDisposable
{
    /// <summary>Where it is, for messages: "\\\\server\\7dtd\\Mods", "ftp://host/Mods".</summary>
    string Describe { get; }
    /// <summary>The mod folders in it (top-level folder names). Creates nothing.</summary>
    IReadOnlyList<string> ModFolders();
    /// <summary>Every file in one mod folder, with sizes.</summary>
    IReadOnlyList<RemoteFile> Files(string modFolder);
    /// <summary>A small text file of a mod (its ModInfo.xml), or null when it isn't there.</summary>
    string? ReadText(string modFolder, string path);
    void Upload(string localFile, string modFolder, string path);
    void DeleteFile(string modFolder, string path);
    /// <summary>Removes a mod folder and everything in it.</summary>
    void DeleteFolder(string modFolder);
}

/// <summary>A Mods folder on a disk or network share.</summary>
public sealed class LocalModsFolder : IModsFolder
{
    readonly string _root;
    public LocalModsFolder(string root) => _root = root;
    public string Describe => _root;

    public IReadOnlyList<string> ModFolders() =>
        Directory.Exists(_root) ? Directory.GetDirectories(_root).Select(Path.GetFileName).Where(n => !n!.StartsWith('.')).Select(n => n!).ToList() : new List<string>();

    public IReadOnlyList<RemoteFile> Files(string modFolder)
    {
        var dir = Path.Combine(_root, modFolder);
        if (!Directory.Exists(dir)) return Array.Empty<RemoteFile>();
        return new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Select(f => new RemoteFile(Path.GetRelativePath(dir, f.FullName).Replace('\\', '/'), f.Length, f.LastWriteTimeUtc)).ToList();
    }

    public string? ReadText(string modFolder, string path)
    {
        var dir = Path.Combine(_root, modFolder);
        if (!Directory.Exists(dir)) return null;
        // ModInfo.xml in any letter case.
        var file = Directory.EnumerateFiles(dir).FirstOrDefault(f => Path.GetFileName(f).Equals(path, StringComparison.OrdinalIgnoreCase));
        return file == null ? null : File.ReadAllText(file);
    }

    public void Upload(string localFile, string modFolder, string path)
    {
        var dest = Path.Combine(_root, modFolder, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(localFile, dest, true);
        // Keep the time, so the next compare sees the same file.
        File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(localFile));
    }

    public void DeleteFile(string modFolder, string path)
    {
        var f = Path.Combine(_root, modFolder, path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(f)) File.Delete(f);
    }

    public void DeleteFolder(string modFolder)
    {
        var d = Path.Combine(_root, modFolder);
        if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    public void Dispose() { }
}

/// <summary>
/// A Mods folder on an FTP (or FTPS) server, the way most game hosts give access to a dedicated server's files.
/// Uses .NET's FtpWebRequest: old, but built in, and FTP is all that most hosts offer besides SFTP.
/// </summary>
#pragma warning disable SYSLIB0014 // FtpWebRequest is obsolete but is the only FTP client in .NET.
public sealed class FtpModsFolder : IModsFolder
{
    readonly string _host;
    readonly int _port;
    readonly string _root;
    readonly NetworkCredential _cred;
    readonly bool _tls;

    public FtpModsFolder(string host, int port, string user, string password, string root, bool tls)
    {
        _host = host.Trim();
        if (_host.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)) _host = _host[6..];
        _host = _host.TrimEnd('/');
        _port = port <= 0 ? 21 : port;
        _root = "/" + root.Trim().Replace('\\', '/').Trim('/');
        _cred = new NetworkCredential(user.Length == 0 ? "anonymous" : user, password);
        _tls = tls;
    }

    public string Describe => $"ftp{(_tls ? "es" : "")}://{_host}{(_port == 21 ? "" : ":" + _port)}{_root}";

    Uri UriOf(string path)
    {
        var parts = (_root.TrimEnd('/') + "/" + path.TrimStart('/')).Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString);
        return new Uri($"ftp://{_host}:{_port}/{string.Join("/", parts)}");
    }

    FtpWebRequest Request(string path, string method)
    {
        var r = (FtpWebRequest)WebRequest.Create(UriOf(path));
        r.Method = method;
        r.Credentials = _cred;
        r.EnableSsl = _tls;
        r.UseBinary = true;
        r.UsePassive = true;
        r.KeepAlive = true;
        r.Timeout = 30000;
        return r;
    }

    List<FtpEntry> List(string path)
    {
        try
        {
            using var resp = (FtpWebResponse)Request(path + "/", WebRequestMethods.Ftp.ListDirectoryDetails).GetResponse();
            using var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
            var list = new List<FtpEntry>();
            while (r.ReadLine() is { } line)
                if (FtpListing.Parse(line) is { } e && e.Name is not "." and not "..") list.Add(e);
            return list;
        }
        catch (WebException e) when (e.Response is FtpWebResponse { StatusCode: FtpStatusCode.ActionNotTakenFileUnavailable })
        {
            return new List<FtpEntry>();
        }
    }

    public IReadOnlyList<string> ModFolders() => List("").Where(e => e.IsFolder && !e.Name.StartsWith('.')).Select(e => e.Name).ToList();

    public IReadOnlyList<RemoteFile> Files(string modFolder)
    {
        var files = new List<RemoteFile>();
        void Walk(string rel, int depth)
        {
            foreach (var e in List(modFolder + (rel.Length > 0 ? "/" + rel : "")))
            {
                var p = rel.Length > 0 ? rel + "/" + e.Name : e.Name;
                if (e.IsFolder) { if (depth < 12) Walk(p, depth + 1); }
                else files.Add(new RemoteFile(p, e.Size));
            }
        }
        Walk("", 0);
        return files;
    }

    public string? ReadText(string modFolder, string path)
    {
        var name = List(modFolder).FirstOrDefault(e => !e.IsFolder && e.Name.Equals(path, StringComparison.OrdinalIgnoreCase))?.Name;
        if (name == null) return null;
        using var resp = Request(modFolder + "/" + name, WebRequestMethods.Ftp.DownloadFile).GetResponse();
        using var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
        return r.ReadToEnd();
    }

    readonly HashSet<string> _made = new(StringComparer.Ordinal);

    void MakeFolders(string path)
    {
        var sofar = "";
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            sofar = sofar.Length == 0 ? part : sofar + "/" + part;
            if (!_made.Add(sofar)) continue;
            try { using var _ = Request(sofar, WebRequestMethods.Ftp.MakeDirectory).GetResponse(); }
            catch (WebException e) when (e.Response is FtpWebResponse { StatusCode: FtpStatusCode.ActionNotTakenFileUnavailable }) { } // already there
        }
    }

    public void Upload(string localFile, string modFolder, string path)
    {
        var full = modFolder + "/" + path;
        var dir = full.Contains('/') ? full[..full.LastIndexOf('/')] : "";
        MakeFolders(dir);
        var r = Request(full, WebRequestMethods.Ftp.UploadFile);
        using (var src = File.OpenRead(localFile))
        using (var dst = r.GetRequestStream()) src.CopyTo(dst);
        using var _ = r.GetResponse();
    }

    public void DeleteFile(string modFolder, string path)
    {
        try { using var _ = Request(modFolder + "/" + path, WebRequestMethods.Ftp.DeleteFile).GetResponse(); }
        catch (WebException e) when (e.Response is FtpWebResponse { StatusCode: FtpStatusCode.ActionNotTakenFileUnavailable }) { }
    }

    public void DeleteFolder(string modFolder)
    {
        void Remove(string rel)
        {
            foreach (var e in List(rel))
            {
                if (e.IsFolder) Remove(rel + "/" + e.Name);
                else { using var _ = Request(rel + "/" + e.Name, WebRequestMethods.Ftp.DeleteFile).GetResponse(); }
            }
            using var __ = Request(rel, WebRequestMethods.Ftp.RemoveDirectory).GetResponse();
        }
        Remove(modFolder);
        _made.RemoveWhere(p => p == modFolder || p.StartsWith(modFolder + "/", StringComparison.Ordinal));
    }

    public void Dispose() { }
}
#pragma warning restore SYSLIB0014

/// <summary>One line of an FTP directory listing.</summary>
public sealed record FtpEntry(string Name, bool IsFolder, long Size);

/// <summary>Reads FTP LIST output, which servers write in either the Unix ("drwxr-xr-x ...") or the Windows/IIS ("01-31-24  09:15PM  &lt;DIR&gt; name") style.</summary>
public static class FtpListing
{
    static readonly System.Text.RegularExpressions.Regex Unix = new(
        @"^([\-dlbcps])[\w\-]{9}[+@.]?\s+\d+\s+\S+\s+\S+\s+(\d+)\s+\w{3}\s+\d{1,2}\s+(?:\d{1,2}:\d{2}|\d{4})\s(.+)$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly System.Text.RegularExpressions.Regex Windows = new(
        @"^\d{1,2}-\d{1,2}-\d{2,4}\s+\d{1,2}:\d{2}(?:[AP]M)?\s+(<DIR>|\d+)\s+(.+)$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The entry a line describes, or null for lines that aren't entries ("total 12").</summary>
    public static FtpEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var m = Unix.Match(line);
        if (m.Success)
        {
            var name = m.Groups[3].Value;
            if (m.Groups[1].Value == "l") { var arrow = name.IndexOf(" -> ", StringComparison.Ordinal); if (arrow > 0) name = name[..arrow]; }
            var folder = m.Groups[1].Value == "d";
            return new FtpEntry(name, folder, folder ? 0 : long.Parse(m.Groups[2].Value));
        }
        m = Windows.Match(line);
        if (m.Success)
        {
            var dir = m.Groups[1].Value.Equals("<DIR>", StringComparison.OrdinalIgnoreCase);
            return new FtpEntry(m.Groups[2].Value, dir, dir ? 0 : long.Parse(m.Groups[1].Value));
        }
        return null;
    }
}

using System.Text.Json;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;

namespace DoodleNook.App.Files;

/// <summary>A recovery copy left behind by a DoodleNook session that did not close normally.</summary>
public sealed record RecoveryEntry(string SessionId, string DnkPath, string MetaPath, string? OriginalPath, string DisplayName, DateTimeOffset SavedAt, bool IsAnimation);

/// <summary>
/// Autosave and crash recovery, entirely local (%LOCALAPPDATA%\DoodleNook\Recovery). While DoodleNook runs, a lock file
/// marks the session as alive; unsaved work is copied into <c>&lt;session&gt;.dnk</c> (never over the user's own file).
/// A normal close, or saving, removes the copy. At the next start, copies from sessions that are no longer running
/// are offered for recovery. Copies are only deleted when the user recovers (and the work is safe again) or discards them.
/// </summary>
public sealed class RecoveryStore : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private FileStream? _lock;

    public RecoveryStore(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory;
        SessionId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoodleNook", "Recovery");

    public string Directory { get; }
    public string SessionId { get; }
    public string OwnDnk => Path.Combine(Directory, SessionId + ".dnk");
    private string OwnMeta => Path.Combine(Directory, SessionId + ".json");
    private string LockPath(string session) => Path.Combine(Directory, session + ".lock");

    /// <summary>Marks this session as running (other DoodleNook windows won't offer its copies for recovery).</summary>
    public void StartSession()
    {
        System.IO.Directory.CreateDirectory(Directory);
        _lock = new FileStream(LockPath(SessionId), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
    }

    /// <summary>Recovery copies whose session is no longer running, newest first.</summary>
    public IReadOnlyList<RecoveryEntry> FindRecoverable()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var result = new List<RecoveryEntry>();
        foreach (var meta in System.IO.Directory.GetFiles(Directory, "*.json"))
        {
            string session = Path.GetFileNameWithoutExtension(meta);
            if (session == SessionId || IsAlive(session)) continue;
            string dnk = Path.ChangeExtension(meta, ".dnk");
            if (!File.Exists(dnk)) continue;
            try
            {
                var m = JsonSerializer.Deserialize<Meta>(File.ReadAllText(meta), Json);
                if (m is null) continue;
                result.Add(new RecoveryEntry(session, dnk, meta, m.OriginalPath, m.DisplayName ?? "Untitled", m.SavedAt, m.IsAnimation));
            }
            catch (Exception)
            {
                // An unreadable description: still offer the picture itself.
                result.Add(new RecoveryEntry(session, dnk, meta, null, "Untitled", File.GetLastWriteTimeUtc(dnk), false));
            }
        }
        return result.OrderByDescending(e => e.SavedAt).ToList();
    }

    private bool IsAlive(string session)
    {
        var path = LockPath(session);
        if (!File.Exists(path)) return false;
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false; // nobody holds it: a stale lock from a crash
        }
        catch (IOException)
        {
            return true; // another DoodleNook is running with it
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Writes this session's recovery copy (call from a background thread; the snapshot is independent of the document).</summary>
    public void Write(DnkSnapshot snapshot, string? originalPath, string displayName)
    {
        System.IO.Directory.CreateDirectory(Directory);
        DnkFile.Save(snapshot, OwnDnk);
        var meta = new Meta { OriginalPath = originalPath, DisplayName = displayName, SavedAt = DateTimeOffset.Now, IsAnimation = snapshot.IsAnimation };
        AnimationExport.WriteAtomically(OwnMeta, JsonSerializer.SerializeToUtf8Bytes(meta, Json));
    }

    public Document Load(RecoveryEntry entry) => DnkFile.Load(entry.DnkPath);

    public void Discard(RecoveryEntry entry)
    {
        TryDelete(entry.DnkPath);
        TryDelete(entry.MetaPath);
    }

    /// <summary>Removes this session's copy (the work was saved, or there is nothing unsaved).</summary>
    public void ClearOwn()
    {
        TryDelete(OwnDnk);
        TryDelete(OwnMeta);
    }

    /// <summary>Normal close: no recovery is needed.</summary>
    public void EndSession()
    {
        ClearOwn();
        _lock?.Dispose();
        _lock = null;
    }

    /// <summary>For tests: behave like a crash (the lock is released but the copy stays).</summary>
    internal void SimulateCrash()
    {
        _lock?.Dispose();
        _lock = null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // Leave it; it will be offered again or cleaned up later.
        }
    }

    public void Dispose() => _lock?.Dispose();

    private sealed class Meta
    {
        public string? OriginalPath { get; set; }
        public string? DisplayName { get; set; }
        public DateTimeOffset SavedAt { get; set; }
        public bool IsAnimation { get; set; }
    }
}

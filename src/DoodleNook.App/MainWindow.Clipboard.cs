using DoodleNook.App.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App;

/// <summary>
/// Mission 012 — pasting pictures from other programs. Ctrl+V pastes the newest thing copied: DoodleNook's own copy, or a
/// picture copied in Paint, a browser, a screenshot tool or Explorer (a copied image file). External pictures keep their
/// transparency, land on a new layer (never on the active one, so a locked layer is safe) and come with transform
/// handles. Edit › Paste as reference does the same at 35 % and locks the layer once it is in place. Problems (an empty
/// clipboard, text instead of a picture, a busy or unreadable clipboard, a picture too large) get a short message.
/// </summary>
public partial class MainWindow
{
    private ISystemClipboard? _systemClipboard;
    private uint? _ownCopyStamp;
    private bool _pasting;

    /// <summary>The system clipboard as DoodleNook reads pictures from it (tests set a fake).</summary>
    internal ISystemClipboard SystemClipboard
    {
        get => _systemClipboard ??= ClipboardPictures.ForWindow(this);
        set => _systemClipboard = value;
    }

    public void Copy(bool cut)
    {
        if (!Session.Copy(cut)) return;
        _ownCopyStamp = ChangeCount();
        ShowToast(cut ? "Cut" : "Copied");
    }

    public void Paste() => _ = PasteAsync(asReference: false);

    public void PasteAsReference() => _ = PasteAsync(asReference: true);

    /// <summary>Pastes the newest copied picture as a new layer (or as a reference). False when nothing was pasted.</summary>
    public async Task<bool> PasteAsync(bool asReference)
    {
        if (_pasting) return false;
        // DoodleNook's own copy, when nothing has been copied anywhere since (or the platform can't tell).
        if (Session.Clipboard is { } own && (ChangeCount() is not { } now || now == _ownCopyStamp)) return PasteOwn(own, asReference);
        _pasting = true;
        try
        {
            var doc = Session.Document;
            ClipboardContent content;
            try
            {
                content = await SystemClipboard.ReadAsync();
            }
            catch (Exception ex)
            {
                content = ClipboardContent.Failed($"The clipboard couldn't be read ({ex.GetType().Name}).");
            }
            var (image, problem) = content.MayHavePicture ? await Task.Run(() => ClipboardPictures.Decode(content)) : ClipboardPictures.Decode(content);
            if (!ReferenceEquals(doc, Session.Document)) return false; // another picture was opened meanwhile
            if (image is not null)
            {
                PasteExternal(image, asReference);
                return true;
            }
            if (Session.Clipboard is { } fallback) return PasteOwn(fallback, asReference);
            ShowToast(problem ?? "Nothing to paste. Copy a picture first.");
            return false;
        }
        finally
        {
            _pasting = false;
        }
    }

    private uint? ChangeCount()
    {
        try
        {
            return SystemClipboard.ChangeCount;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool PasteOwn(RasterImage own, bool asReference)
    {
        if (asReference)
        {
            Session.PlaceReference(own);
            ShowToast(ReferenceToast(scaled: false));
            return true;
        }
        if (!Session.Paste()) return false;
        ShowToast("Pasted as a new layer · press V or Ctrl+T to move it");
        return true;
    }

    private void PasteExternal(RasterImage image, bool asReference)
    {
        var fitted = FitToCanvas(image);
        bool scaled = !ReferenceEquals(fitted, image);
        if (asReference)
        {
            Session.PlaceReference(fitted);
            ShowToast(ReferenceToast(scaled));
            return;
        }
        Session.PlaceImage(fitted, "Pasted image");
        string fit = scaled ? " · scaled to fit the canvas" : "";
        string colours = Session.Document.PaletteLocked ? $" · colours matched to “{Session.Document.Palette.Name}”" : "";
        if (Session.BeginTransform("Place pasted image")) ShowToast($"Pasted on a new layer{fit}{colours} · drag the handles, Enter to finish");
        else ShowToast($"Pasted on a new layer{fit}{colours}");
    }

    private static string ReferenceToast(bool scaled) =>
        $"Reference pasted at 35 %{(scaled ? ", scaled to fit" : "")} · drag the handles to place it, Enter locks it";
}

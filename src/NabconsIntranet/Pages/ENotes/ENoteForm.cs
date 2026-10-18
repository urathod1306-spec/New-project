namespace NabconsIntranet.Pages.ENotes;

/// <summary>Fields of the draft form (New and Edit). Only these named fields are read from the request.</summary>
public sealed class ENoteForm
{
    public string? Subject { get; set; }
    public string? Category { get; set; }
    public string? Body { get; set; }
    public bool Confidential { get; set; }
    public bool Declaration { get; set; }
    public long RowVersion { get; set; }
}

public sealed record ENoteFormView(ENoteForm Form, List<string> Categories, int MaxBodyChars, bool ConfidentialLocked, bool ShowDeclaration,
    bool ShowFiles, string Accept, int MaxFileMB, string? Error);

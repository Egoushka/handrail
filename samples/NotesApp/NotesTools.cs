using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Handrail;

namespace NotesApp;

public record NoteSummary(Guid Id, string Title);

public record NoteDetail(Guid Id, string Title, string Body);

public class NotesTools(INoteStore notes)
{
    /// <summary>Search the user's notes by text. Use before get_note.</summary>
    [WebMcpTool("search_notes", ReadOnly = true, ReturnsUntrusted = true)]
    public IReadOnlyList<NoteSummary> Search(
        [Description("Words to look for")] string query,
        [Range(1, 20)] int limit = 5) =>
        notes.All()
            .Where(n => n.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || n.Body.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(n => new NoteSummary(n.Id, n.Title))
            .ToList();

    /// <summary>Read one note in full.</summary>
    [WebMcpTool("get_note", ReadOnly = true, ReturnsUntrusted = true)]
    public NoteDetail? Get(Guid id) => notes.Find(id) is { } n ? new NoteDetail(n.Id, n.Title, n.Body) : null;

    /// <summary>Create a note.</summary>
    [WebMcpTool("add_note", Consequential = true, Confirm = Confirm.Page)]
    public NoteSummary Add(
        [StringLength(100, MinimumLength = 1)] string title,
        [StringLength(2000)] string body = "")
    {
        var note = notes.Add(title, body);
        return new NoteSummary(note.Id, note.Title);
    }

    /// <summary>Permanently delete a note.</summary>
    [WebMcpTool("delete_note", Consequential = true, Confirm = Confirm.Page)]
    public bool Delete(Guid id) => notes.Delete(id);
}

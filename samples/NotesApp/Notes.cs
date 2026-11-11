using System.Collections.Concurrent;

namespace NotesApp;

public record Note(Guid Id, string Title, string Body);

public interface INoteStore
{
    IReadOnlyList<Note> All();

    Note? Find(Guid id);

    Note Add(string title, string body);

    bool Delete(Guid id);
}

public sealed class InMemoryNoteStore : INoteStore
{
    private readonly ConcurrentDictionary<Guid, Note> _notes = new();

    public InMemoryNoteStore()
    {
        Add("Groceries", "Milk, eggs, coffee");
        Add("Trip plan", "Book the train to Lviv on Friday");
    }

    public IReadOnlyList<Note> All() => _notes.Values.OrderBy(n => n.Title).ToList();

    public Note? Find(Guid id) => _notes.GetValueOrDefault(id);

    public Note Add(string title, string body)
    {
        var note = new Note(Guid.NewGuid(), title, body);
        _notes[note.Id] = note;
        return note;
    }

    public bool Delete(Guid id) => _notes.TryRemove(id, out _);
}

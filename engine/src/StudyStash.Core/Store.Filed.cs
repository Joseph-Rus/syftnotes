using StudyStash.Core.Filing;

namespace StudyStash.Core;

/// <summary>
/// Submit &amp; Organize's undo log: every file it moved or sent to the Trash, by run, with where it was, where it went
/// and its SHA-256, so a whole run can be put back from one place. The table is made the first time something is
/// filed, which only test builds do for now.
/// </summary>
public sealed partial class Store
{
    bool filedTable;

    void MakeFiledTable()
    {
        if (filedTable) return;
        Exec("""
            CREATE TABLE IF NOT EXISTS filed_moves (
                seq INTEGER PRIMARY KEY AUTOINCREMENT,
                run TEXT NOT NULL,
                at TEXT,
                class_name TEXT,
                assignment TEXT,
                from_path TEXT,
                to_path TEXT,
                kind TEXT,
                hash TEXT,
                undone TEXT
            );
            CREATE INDEX IF NOT EXISTS filed_moves_run ON filed_moves(run, seq);
            """);
        filedTable = true;
    }

    /// <summary>Writes down one file filed; the move as kept, with its number.</summary>
    public FiledMove AddFiledMove(FiledMove m)
    {
        lock (gate)
        {
            MakeFiledTable();
            Exec("INSERT INTO filed_moves(run, at, class_name, assignment, from_path, to_path, kind, hash) VALUES(?,?,?,?,?,?,?,?)",
                m.Run, m.At, m.ClassName, m.Assignment, m.From, m.To, m.Kind, m.Hash);
            using var cmd = Command("SELECT last_insert_rowid()");
            return m with { Seq = Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) };
        }
    }

    /// <summary>A run's files, in the order they were filed.</summary>
    public List<FiledMove> FiledMoves(string run)
    {
        lock (gate)
        {
            MakeFiledTable();
            using var cmd = Command("SELECT seq, run, at, class_name, assignment, from_path, to_path, kind, hash, undone FROM filed_moves WHERE run=? ORDER BY seq", run);
            using var r = cmd.ExecuteReader();
            string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
            var list = new List<FiledMove>();
            while (r.Read())
                list.Add(new FiledMove(r.GetInt64(0), S(1), S(2), S(3), S(4), S(5), S(6), S(7), S(8), r.IsDBNull(9) ? null : r.GetString(9)));
            return list;
        }
    }

    /// <summary>This file was put back (when: <paramref name="at"/>), so undoing its run again leaves it alone.</summary>
    public void MarkFiledUndone(long seq, string at)
    {
        lock (gate)
        {
            MakeFiledTable();
            Exec("UPDATE filed_moves SET undone=? WHERE seq=?", at, seq);
        }
    }
}

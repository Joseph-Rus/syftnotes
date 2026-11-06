using StudyStash.Core.Filing;

namespace StudyStash.Core;

/// <summary>
/// Submit &amp; Organize's undo log: every file it moved or sent to the Trash, by run, with where it was, where it went
/// and its SHA-256, so a whole run can be put back from one place. A row is written before its move (pending) and
/// marked done after it, so a move a crash cut short is still in the log. The table is made the first time something
/// is filed, which only test builds do for now.
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
                undone TEXT,
                status TEXT
            );
            CREATE INDEX IF NOT EXISTS filed_moves_run ON filed_moves(run, seq);
            """);
        // A table made before moves were written down ahead of time gets the column (its rows were all done).
        var cols = new HashSet<string>();
        using (var cmd = Command("PRAGMA table_info(filed_moves)"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) cols.Add(r.GetString(r.GetOrdinal("name")));
        if (!cols.Contains("status")) Exec("ALTER TABLE filed_moves ADD COLUMN status TEXT DEFAULT 'done'");
        filedTable = true;
    }

    /// <summary>Writes down a file about to be filed, as pending; the move as kept, with its number.</summary>
    public FiledMove AddFiledMove(FiledMove m)
    {
        lock (gate)
        {
            MakeFiledTable();
            Exec("INSERT INTO filed_moves(run, at, class_name, assignment, from_path, to_path, kind, hash, status) VALUES(?,?,?,?,?,?,?,?,'pending')",
                m.Run, m.At, m.ClassName, m.Assignment, m.From, m.To, m.Kind, m.Hash);
            using var cmd = Command("SELECT last_insert_rowid()");
            return m with { Seq = Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture), Pending = true };
        }
    }

    /// <summary>The file was filed, and is now at <paramref name="to"/> (for the Trash, known only now).</summary>
    public void FinishFiledMove(long seq, string to)
    {
        lock (gate)
        {
            MakeFiledTable();
            Exec("UPDATE filed_moves SET status='done', to_path=? WHERE seq=?", to, seq);
        }
    }

    /// <summary>The move didn't happen (the file is where it was): it leaves the log.</summary>
    public void DropFiledMove(long seq)
    {
        lock (gate)
        {
            MakeFiledTable();
            Exec("DELETE FROM filed_moves WHERE seq=?", seq);
        }
    }

    /// <summary>A run's files, in the order they were filed.</summary>
    public List<FiledMove> FiledMoves(string run)
    {
        lock (gate)
        {
            MakeFiledTable();
            using var cmd = Command("SELECT seq, run, at, class_name, assignment, from_path, to_path, kind, hash, undone, status FROM filed_moves WHERE run=? ORDER BY seq", run);
            using var r = cmd.ExecuteReader();
            string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
            var list = new List<FiledMove>();
            while (r.Read())
                list.Add(new FiledMove(r.GetInt64(0), S(1), S(2), S(3), S(4), S(5), S(6), S(7), S(8), r.IsDBNull(9) ? null : r.GetString(9), S(10) == "pending"));
            return list;
        }
    }

    /// <summary>The last <paramref name="most"/> runs, newest first: each with when it was, its class and assignment, how
    /// many files it filed (a move still pending counts: Undo follows it), and how many of those were put back since.</summary>
    public List<FiledRun> FiledRuns(int most = 20)
    {
        lock (gate)
        {
            MakeFiledTable();
            using var cmd = Command("""
                SELECT run, MIN(at), MIN(class_name), MIN(assignment), COUNT(*), COUNT(undone) FROM filed_moves
                GROUP BY run ORDER BY MAX(seq) DESC LIMIT ?
                """, most);
            using var r = cmd.ExecuteReader();
            string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
            var list = new List<FiledRun>();
            while (r.Read()) list.Add(new FiledRun(S(0), S(1), S(2), S(3), r.GetInt32(4), r.GetInt32(5)));
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

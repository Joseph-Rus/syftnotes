namespace PartsImport;

static class Program
{
    static int Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "sheet":
            {
                // sheet out.png files...: the raw files side by side, to choose from.
                var items = args.Skip(2).Select(f => (Path.GetFileNameWithoutExtension(f), File.ReadAllText(f))).ToList();
                Preview.Sheet(items, args[1]);
                return 0;
            }
        }
        Console.WriteLine("usage: sheet out.png files... | build");
        return 1;
    }
}

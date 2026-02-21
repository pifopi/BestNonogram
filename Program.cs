namespace BestNonogram
{
    enum PuzzleType { Color, BW }
    enum PuzzleDifficulty { TrueNonogram, OtherNonogram }
    enum Order { XP, XPBySize }
    enum Filter { All, TrueNonogramOnly }

    abstract class PuzzleBase { }

    class LastDonePuzzle : PuzzleBase
    {
        public required string Name { get; set; }
        public required DateTime LastDone { get; set; }
    }

    sealed class LastDonePuzzleMap : CsvHelper.Configuration.ClassMap<LastDonePuzzle>
    {
        public LastDonePuzzleMap()
        {
            Map(m => m.Name).Name("Name");
            Map(m => m.LastDone).Name("LastDone");
        }
    }

    class Puzzle : PuzzleBase
    {
        public required string Name { get; set; }
        public required int XP { get; set; }
        public required int Size { get; set; }
        public required PuzzleDifficulty Difficulty { get; set; }
        public required PuzzleType Type { get; set; }
        public required DateTime LastDone { get; set; }
    }

    sealed class PuzzleMap : CsvHelper.Configuration.ClassMap<Puzzle>
    {
        public PuzzleMap(PuzzleType puzzleType, List<LastDonePuzzle> lastDonePuzzles)
        {
            Map(m => m.Name).Name("Puzzle ID:Puzzle Name");
            Map(m => m.XP).Convert(args =>
            {
                string? XP = args.Row.GetField(4);
                System.Diagnostics.Debug.Assert(XP != null, "XP field is missing in the CSV");
                return int.Parse(XP.Replace("~", ""));
            });
            Map(m => m.Size).Convert(args =>
            {
                string? size = args.Row.GetField("Size");
                System.Diagnostics.Debug.Assert(size != null, "Size field is missing in the CSV");
                string[] sizes = size.Split('x');
                System.Diagnostics.Debug.Assert(sizes.Count() == 2, $"Size field is invalid {size}");
                int width = int.Parse(sizes[0]);
                int height = int.Parse(sizes[1]);
                return int.Min(width, height);
            });
            Map(m => m.Difficulty).Convert(args =>
            {
                string? type = args.Row.GetField("Puzzle<br>type");
                if (type is not null && type.Contains("True_nonogram_icon.png"))
                {
                    return PuzzleDifficulty.TrueNonogram;
                }
                else
                {
                    return PuzzleDifficulty.OtherNonogram;
                }
            });
            Map(m => m.Type).Constant(puzzleType);
            Map(m => m.LastDone).Convert(args =>
            {
                string? name = args.Row.GetField("Puzzle ID:Puzzle Name");
                LastDonePuzzle? lastDonePuzzle = lastDonePuzzles.Find(p => p.Name == name);
                return lastDonePuzzle != null ? lastDonePuzzle.LastDone : DateTime.MinValue;
            });
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            string directory = "../../../config/";
            string lastDonePuzzlesFile = Path.Combine(directory, "LastDonePuzzles.csv");
            List<LastDonePuzzle> lastDonePuzzles = GetPuzzlesFromCsv<LastDonePuzzle>(lastDonePuzzlesFile, new LastDonePuzzleMap());

            List<Puzzle> allColors = GetAllPuzzles(directory, PuzzleType.Color, lastDonePuzzles);
            List<Puzzle> allBWs = GetAllPuzzles(directory, PuzzleType.BW, lastDonePuzzles);

            while (true)
            {
                Console.WriteLine();
                PrintBest("color (XP)               ", FilterPuzzles(allColors, Order.XP,       Filter.All),                allColors.Count);
                PrintBest("color (XP/Size)          ", FilterPuzzles(allColors, Order.XPBySize, Filter.All),                allColors.Count);
                PrintBest("B&W (XP)                 ", FilterPuzzles(allBWs,    Order.XP,       Filter.All),                allBWs.Count);
                PrintBest("B&W (XP/Size)            ", FilterPuzzles(allBWs,    Order.XPBySize, Filter.All),                allBWs.Count);
                PrintBest("true nonogram (XP)       ", FilterPuzzles(allBWs,    Order.XP,       Filter.TrueNonogramOnly),   allBWs.Count);
                PrintBest("true nonogram (XP/Size)  ", FilterPuzzles(allBWs,    Order.XPBySize, Filter.TrueNonogramOnly),   allBWs.Count);

                Console.WriteLine("Enter Puzzle Name to mark as done:");
                string? input = Console.ReadLine();
                if (string.IsNullOrEmpty(input))
                {
                    continue;
                }

                List<Puzzle> allPuzzles = [.. allColors, .. allBWs];
                Puzzle? puzzle = allPuzzles.Find(p => p.Name == input);
                if (puzzle is null)
                {
                    Console.WriteLine("Puzzle not found.");
                }
                else
                {
                    puzzle.LastDone = DateTime.Now;
                    UpdateLastUsed(puzzle.Name, lastDonePuzzlesFile, lastDonePuzzles);
                }
            }
        }

        static List<Puzzle> GetAllPuzzles(string directory, PuzzleType puzzleType, List<LastDonePuzzle> lastDonePuzzles)
        {
            string file = puzzleType switch
            {
                PuzzleType.Color => "Colors.csv",
                PuzzleType.BW => "BWs.csv",
                _ => throw new Exception("Invalid puzzle type")
            };
            PuzzleMap map = new PuzzleMap(puzzleType, lastDonePuzzles);
            return GetPuzzlesFromCsv<Puzzle>(Path.Combine(directory, file), map);
        }

        static List<T> GetPuzzlesFromCsv<T>(string csvFile, CsvHelper.Configuration.ClassMap map)
            where T : PuzzleBase
        {
            Console.WriteLine($"Reading csv file : {csvFile}");

            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);
            using (var reader = new StreamReader(csvFile))
            using (var csv = new CsvHelper.CsvReader(reader, config))
            {
                csv.Context.RegisterClassMap(map);
                return csv.GetRecords<T>().ToList();
            }
        }

        static List<Puzzle> FilterPuzzles(List<Puzzle> input, Order order, Filter filter)
        {
            IEnumerable<Puzzle> puzzles = input.AsEnumerable();

            DateTime cutoff = DateTime.Now.AddDays(-31);
            puzzles = puzzles.Where(p => p.LastDone < cutoff);

            switch (order)
            {
                case Order.XP:
                    puzzles = puzzles.OrderByDescending(p => p.XP);
                    break;
                case Order.XPBySize:
                    puzzles = puzzles.OrderByDescending(p => (float)p.XP / p.Size);
                    break;
                default:
                    throw new Exception("Invalid order");
            }

            switch (filter)
            {
                case Filter.All:
                    break;
                case Filter.TrueNonogramOnly:
                    puzzles = puzzles.Where(p => p.Difficulty == PuzzleDifficulty.TrueNonogram);
                    break;
                default:
                    throw new Exception("Invalid filter");
            }

            return puzzles.ToList();
        }

        static void UpdateLastUsed(string name, string lastDonePuzzlesFile, List<LastDonePuzzle> lastDonePuzzles)
        {
            LastDonePuzzle? lastUsed = lastDonePuzzles.Find(p => p.Name == name);
            if (lastUsed == null)
            {
                lastDonePuzzles.Add(new LastDonePuzzle { Name = name, LastDone = DateTime.Now });
            }
            else
            {
                lastUsed.LastDone = DateTime.Now;
            }
            WritePuzzlesToCsv(lastDonePuzzlesFile, lastDonePuzzles);
        }

        static void WritePuzzlesToCsv(string lastDonePuzzlesFile, List<LastDonePuzzle> lastDonePuzzles)
        {
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);
            using (var writer = new StreamWriter(lastDonePuzzlesFile))
            using (var csv = new CsvHelper.CsvWriter(writer, config))
            {
                csv.WriteRecords(lastDonePuzzles);
            }
        }

        static void PrintBest(string label, List<Puzzle> filtered, int total)
        {
            Puzzle? puzzle = filtered.FirstOrDefault();
            string description = puzzle != null
                ? $"{puzzle.Name,-50}, XP:{puzzle.XP}, Size: {puzzle.Size}"
                : "NONE";
            Console.WriteLine($"Best {label} ({filtered.Count,4}/{total,4} left): {description}");
        }
    }
}
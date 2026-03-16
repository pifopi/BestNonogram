namespace BestNonogram
{
    enum PuzzleType { Color, BW }
    enum PuzzleDifficulty { TrueNonogram, OtherNonogram }
    enum Order { XP, XPBySize }
    enum Filter { All, TrueNonogramOnly }

    class LastDonePuzzle
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

    class Puzzle
    {
        public required string Name { get; set; }
        public required string Author { get; set; }
        public required int XP { get; set; }
        public required int Width { get; set; }
        public required int Height { get; set; }
        public required PuzzleDifficulty Difficulty { get; set; }
        public required PuzzleType Type { get; set; }
        public required DateTime LastDone { get; set; }

        public int Size => Math.Min(Width, Height);
        public string BestSide => Size == Height ? "Height" : "Width";
        public float XPBySize => (float)XP / Size;
    }

    class Program
    {
        private static readonly Discord.WebSocket.DiscordSocketClient _client = new(
            new Discord.WebSocket.DiscordSocketConfig()
            {
                GatewayIntents = Discord.GatewayIntents.AllUnprivileged | Discord.GatewayIntents.MessageContent
            }
        );

        private static string _directory = $"{AppDomain.CurrentDomain.BaseDirectory}../../../config/";

        private static string _lastDonePuzzlesFile = Path.Combine(_directory, "LastDonePuzzles.csv");
        private static List<LastDonePuzzle> _lastDonePuzzles = GetPuzzlesFromCsv(_lastDonePuzzlesFile, new LastDonePuzzleMap());
        private static List<Puzzle> _colorPuzzles = GetPuzzlesFromLua(Path.Combine(_directory, "Colors.lua"), PuzzleType.Color);
        private static List<Puzzle> _BWPuzzles = GetPuzzlesFromLua(Path.Combine(_directory, "BWs.lua"), PuzzleType.BW);

        private static string _colorImage = "Color.png";
        private static string _BWImage = "BW.png";
        private static string _trueNonogramImage = "TrueNonogram.png";

        static async Task Main(string[] args)
        {
            string token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? throw new Exception("DISCORD_BOT_TOKEN is not set");
            await _client.LoginAsync(Discord.TokenType.Bot, token);
            await _client.StartAsync();
            _client.Ready += OnReady;
            await Task.Delay(-1);
        }

        static async Task OnReady()
        {

            ulong channelId = 1479864933985550398;
            Discord.IMessageChannel channel = await _client.GetChannelAsync(channelId) as Discord.IMessageChannel ?? throw new Exception("Channel is null");

            Discord.FileAttachment[] attachments = [
                new(Path.Combine(_directory, _colorImage)),
                new(Path.Combine(_directory, _BWImage)),
                new(Path.Combine(_directory, _trueNonogramImage))
            ];

            while (true)
            {
                Discord.Embed[] embeds =
                [
                    CreateEmbed(PuzzleType.Color, Order.XP, Filter.All),
                    CreateEmbed(PuzzleType.Color, Order.XPBySize, Filter.All),
                    CreateEmbed(PuzzleType.BW, Order.XP, Filter.All),
                    CreateEmbed(PuzzleType.BW, Order.XPBySize, Filter.All),
                    CreateEmbed(PuzzleType.BW, Order.XP, Filter.TrueNonogramOnly),
                    CreateEmbed(PuzzleType.BW, Order.XPBySize, Filter.TrueNonogramOnly),
                ];
                await channel.SendFilesAsync(attachments, embeds: embeds);
                await channel.SendMessageAsync("Enter puzzle name to mark as done.");

                TaskCompletionSource completedPuzzle = new();
                async Task handler(Discord.WebSocket.SocketMessage message)
                {
                    if (message.Channel.Id != channelId)
                    {
                        return;
                    }
                    if (message.Author.IsBot)
                    {
                        return;
                    }
                    List<Puzzle> allPuzzles = [.. _colorPuzzles, .. _BWPuzzles];
                    Puzzle? puzzle = allPuzzles.Find(p => p.Name == message.Content);
                    if (puzzle is null)
                    {
                        await channel.SendMessageAsync($"Puzzle {message.Content} not found.");
                    }
                    else
                    {
                        UpdateLastUsed(puzzle);
                        await channel.SendMessageAsync($"Puzzle {message.Content} updated.");
                        completedPuzzle.SetResult();
                    }
                }
                _client.MessageReceived += handler;
                await Task.WhenAll(completedPuzzle.Task);
                _client.MessageReceived -= handler;
            }
        }

        static List<LastDonePuzzle> GetPuzzlesFromCsv(string csvFile, CsvHelper.Configuration.ClassMap map)
        {
            CsvHelper.Configuration.CsvConfiguration config = new(System.Globalization.CultureInfo.InvariantCulture);
            using StreamReader reader = new(csvFile);
            using CsvHelper.CsvReader csv = new(reader, config);
            csv.Context.RegisterClassMap(map);
            return csv.GetRecords<LastDonePuzzle>().ToList();
        }

        static List<Puzzle> GetPuzzlesFromLua(string luaFile, PuzzleType puzzleType)
        {
            MoonSharp.Interpreter.Script lua = new();
            var result = lua.DoFile(luaFile);

            List<Puzzle> puzzles = new();
            foreach (var item in result.Table.Values)
            {
                var table = item.Table;

                string name = table.Get("link").String;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                string author = table.Get("author").String;

                int xp = new Func<int>(() =>
                {
                    string xp = table.Get("new_xp").String;
                    xp = xp.Replace("~", "");
                    return int.Parse(xp);
                })();

                (int width, int height) = new Func<(int, int)>(() =>
                {
                    string size = table.Get("size").String;
                    string[] sizes = size.Split('x');
                    System.Diagnostics.Debug.Assert(sizes.Count() == 2, $"Size field is invalid {size}");
                    int width = int.Parse(sizes[0]);
                    int height = int.Parse(sizes[1]);
                    return (width, height);
                })();

                PuzzleDifficulty difficulty = new Func<PuzzleDifficulty>(() =>
                {
                    string type = table.Get("puzzle_type").String;
                    return type == "1" ? PuzzleDifficulty.TrueNonogram : PuzzleDifficulty.OtherNonogram;
                })();

                DateTime lastDone = new Func<DateTime>(() =>
                {
                    LastDonePuzzle? lastDonePuzzle = _lastDonePuzzles.Find(p => p.Name == name);
                    return lastDonePuzzle != null ? lastDonePuzzle.LastDone : DateTime.MinValue;
                })();

                puzzles.Add(new Puzzle
                {
                    Name = name,
                    Author = author,
                    XP = xp,
                    Width = width,
                    Height = height,
                    Difficulty = difficulty,
                    Type = puzzleType,
                    LastDone = lastDone
                });
            }
            return puzzles;
        }

        static Discord.Embed CreateEmbed(PuzzleType puzzleType, Order order, Filter filter)
        {
            List<Puzzle> puzzles = puzzleType switch
            {
                PuzzleType.Color => _colorPuzzles,
                PuzzleType.BW => _BWPuzzles,
                _ => throw new Exception("Invalid puzzle type")
            };
            List<Puzzle> filteredPuzzles = FilterPuzzles(puzzles, order, filter);

            Discord.EmbedBuilder builder = new();
            string thumbmail = puzzleType switch
            {
                PuzzleType.Color => _colorImage,
                PuzzleType.BW => filter == Filter.TrueNonogramOnly ? _trueNonogramImage : _BWImage,
                _ => throw new Exception("Invalid puzzle type")
            };
            builder.WithThumbnailUrl($"attachment://{thumbmail}");

            string label = order switch
            {
                Order.XP => "XP",
                Order.XPBySize => "XP/Size",
                _ => throw new Exception("Invalid order")
            };
            builder.WithTitle($"{label} ({filteredPuzzles.Count}/{puzzles.Count} left)");

            Puzzle? puzzle = filteredPuzzles.FirstOrDefault();
            if (puzzle is not null)
            {
                builder.AddField("Name", $"{puzzle.Name}");
                builder.AddField("Description", $"{puzzle.XP}xp {puzzle.Width}x{puzzle.Height} ({puzzle.BestSide})");
            }

            return builder.Build();
        }

        static List<Puzzle> FilterPuzzles(List<Puzzle> input, Order order, Filter filter)
        {
            IEnumerable<Puzzle> puzzles = input.AsEnumerable();

            DateTime cutoff = DateTime.Now.AddDays(-31);
            puzzles = puzzles.Where(p => p.LastDone < cutoff);

            //TODO hack, wait for a real column deleted or something
            puzzles = puzzles.Where(p => p.Author != "FuryBreaker");

            puzzles = filter switch
            {
                Filter.All => puzzles,
                Filter.TrueNonogramOnly => puzzles.Where(p => p.Difficulty == PuzzleDifficulty.TrueNonogram),
                _ => throw new Exception("Invalid filter")
            };

            puzzles = order switch
            {
                Order.XP => puzzles.OrderByDescending(p => p.XP),
                Order.XPBySize => puzzles.OrderByDescending(p => p.XPBySize),
                _ => throw new Exception("Invalid order"),
            };
            return puzzles.ToList();
        }

        static void UpdateLastUsed(Puzzle puzzle)
        {
            puzzle.LastDone = DateTime.Now;
            string name = puzzle.Name;
            LastDonePuzzle? lastUsed = _lastDonePuzzles.Find(p => p.Name == name);
            if (lastUsed is null)
            {
                _lastDonePuzzles.Add(new LastDonePuzzle { Name = name, LastDone = DateTime.Now });
            }
            else
            {
                lastUsed.LastDone = DateTime.Now;
            }
            WritePuzzlesToCsv(_lastDonePuzzlesFile, _lastDonePuzzles);
        }

        static void WritePuzzlesToCsv(string lastDonePuzzlesFile, List<LastDonePuzzle> lastDonePuzzles)
        {
            CsvHelper.Configuration.CsvConfiguration config = new(System.Globalization.CultureInfo.InvariantCulture);
            using StreamWriter writer = new(lastDonePuzzlesFile);
            using CsvHelper.CsvWriter csv = new(writer, config);
            csv.WriteRecords(lastDonePuzzles);
        }
    }
}
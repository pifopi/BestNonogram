namespace BestNonogram
{
    enum PuzzleType { Color, BW }
    enum PuzzleDifficulty { TrueNonogram, OtherNonogram }
    enum Order { XP, XPBySize }
    enum Filter { All, TrueNonogramOnly }

    abstract class IPuzzle { }

    class LastDonePuzzle : IPuzzle
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

    class Puzzle : IPuzzle
    {
        public required string Name { get; set; }
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
            Map(m => m.Width).Convert(args =>
            {
                var (width, _) = ParseSize(args);
                return width;
            });
            Map(m => m.Height).Convert(args =>
            {
                var (_, height) = ParseSize(args);
                return height;
            });
            Map(m => m.Difficulty).Convert(args =>
            {
                string? type = args.Row.GetField("Puzzle<br>type");
                System.Diagnostics.Debug.Assert(type != null, "Type field is missing in the CSV");
                if (type.Contains("True_nonogram_icon.png"))
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

        (int, int) ParseSize(CsvHelper.ConvertFromStringArgs args)
        {
            string? size = args.Row.GetField("Size");
            System.Diagnostics.Debug.Assert(size != null, "Size field is missing in the CSV");
            string[] sizes = size.Split('x');
            System.Diagnostics.Debug.Assert(sizes.Count() == 2, $"Size field is invalid {size}");
            int width = int.Parse(sizes[0]);
            int height = int.Parse(sizes[1]);
            return (width, height);
        }
    }

    class Program
    {
        private static readonly Discord.WebSocket.DiscordSocketClient _client = new(
            new Discord.WebSocket.DiscordSocketConfig()
            {
                GatewayIntents = Discord.GatewayIntents.AllUnprivileged | Discord.GatewayIntents.MessageContent
            }
        );

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
            string directory = "../../../config/";
            string lastDonePuzzlesFile = Path.Combine(directory, "LastDonePuzzles.csv");
            List<LastDonePuzzle> lastDonePuzzles = GetPuzzlesFromCsv<LastDonePuzzle>(lastDonePuzzlesFile, new LastDonePuzzleMap());

            List<Puzzle> allColors = GetAllPuzzles(directory, PuzzleType.Color, lastDonePuzzles);
            List<Puzzle> allBWs = GetAllPuzzles(directory, PuzzleType.BW, lastDonePuzzles);

            ulong channelId = 1479864933985550398;
            Discord.IMessageChannel channel = await _client.GetChannelAsync(1479864933985550398) as Discord.IMessageChannel ?? throw new Exception("Channel is null");

            string colorImage = "Color.png";
            string BWImage = "BW.png";
            string trueNonogramImage = "TrueNonogram.png";

            while (true)
            {
                Discord.Embed[] embeds =
                [
                    CreateEmbed(colorImage       , "XP"     ,  FilterPuzzles(allColors, Order.XP,       Filter.All),              allColors.Count),
                    CreateEmbed(colorImage       , "XP/Size",  FilterPuzzles(allColors, Order.XPBySize, Filter.All),              allColors.Count),
                    CreateEmbed(BWImage          , "XP"     ,  FilterPuzzles(allBWs,    Order.XP,       Filter.All),              allBWs.Count),
                    CreateEmbed(BWImage          , "XP/Size",  FilterPuzzles(allBWs,    Order.XPBySize, Filter.All),              allBWs.Count),
                    CreateEmbed(trueNonogramImage, "XP"     ,  FilterPuzzles(allBWs,    Order.XP,       Filter.TrueNonogramOnly), allBWs.Count),
                    CreateEmbed(trueNonogramImage, "XP/Size",  FilterPuzzles(allBWs,    Order.XPBySize, Filter.TrueNonogramOnly), allBWs.Count),
                ];
                await channel.SendFilesAsync([
                    new Discord.FileAttachment(Path.Combine(directory, colorImage)),
                    new Discord.FileAttachment(Path.Combine(directory, BWImage)),
                    new Discord.FileAttachment(Path.Combine(directory, trueNonogramImage))
                ], embeds: embeds);
                await channel.SendMessageAsync("Enter Puzzle Name to mark as done");

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
                    List<Puzzle> allPuzzles = [.. allColors, .. allBWs];
                    Puzzle? puzzle = allPuzzles.Find(p => p.Name == message.Content);
                    if (puzzle is null)
                    {
                        await channel.SendMessageAsync($"Puzzle {message.Content} not found.");
                    }
                    else
                    {
                        puzzle.LastDone = DateTime.Now;
                        UpdateLastUsed(puzzle.Name, Path.Combine("../../../config/", "LastDonePuzzles.csv"), lastDonePuzzles);
                        await channel.SendMessageAsync($"Puzzle {message.Content} updated.");
                        completedPuzzle.SetResult();
                    }
                }
                _client.MessageReceived += handler;
                await Task.WhenAll(completedPuzzle.Task);
                _client.MessageReceived -= handler;
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
            where T : IPuzzle
        {
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

            switch (order)
            {
                case Order.XP:
                    puzzles = puzzles.OrderByDescending(p => p.XP);
                    break;
                case Order.XPBySize:
                    puzzles = puzzles.OrderByDescending(p => p.XPBySize);
                    break;
                default:
                    throw new Exception("Invalid order");
            }

            return puzzles.ToList();
        }

        static void UpdateLastUsed(string name, string lastDonePuzzlesFile, List<LastDonePuzzle> lastDonePuzzles)
        {
            LastDonePuzzle? lastUsed = lastDonePuzzles.Find(p => p.Name == name);
            if (lastUsed is null)
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

        static Discord.Embed CreateEmbed(string image, string label, List<Puzzle> filtered, int total)
        {
            Discord.EmbedBuilder builder = new();
            builder.WithTitle($"Best {label} ({filtered.Count}/{total} left)");
            builder.WithThumbnailUrl($"attachment://{image}");

            Puzzle? puzzle = filtered.FirstOrDefault();
            if (puzzle is not null)
            {
                builder.AddField("Name", $"{puzzle.Name}");
                builder.AddField("Size", $"{puzzle.Width}x{puzzle.Height} ({puzzle.BestSide})");
            }

            return builder.Build();
        }
    }
}
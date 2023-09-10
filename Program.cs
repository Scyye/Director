using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;

namespace Director
{
    public class Program
    {
        private const string Password = "password";
        public static void Main(string[] args)
        {
            while (true)
            {
                Console.WriteLine("Enter Password: ");
                string password = Console.ReadLine();

                if (!password.Equals(Password))
                {
                    Console.WriteLine("Password Incorrect.");
                    break;
                }
                bool decided = false;
                string decision = string.Empty;
                while (!decided)
                {
                    Console.WriteLine("[S]witch or [W]iiu? It is recommended to wait around 3 or 4 minutes as to not run into a 429");
                    decision = Console.ReadLine();
                    decided = decision.ToLower()== "s" || decision.ToLower() == "w";
                    Console.WriteLine(decision + " " + decided);
                    if (decided)
                        break;
                    Console.WriteLine("Unknown character: " + decision + " please use S/W");
                }
                Directory.CreateDirectory("output");

                if (decision.ToLower() == "s")
                {
                    try
                    {
                        File.Create("output\\switch.3dw");
                        File.WriteAllBytes("output\\switch.3dw", GBUtils.GetEverySingleSubmission(GBGame.Switch).ToBytes());
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("There was an error:\n" + e.Message);
                        break;
                    }
                } else
                {
                    try
                    {
                        File.Create("output\\wiiu.3dw");
                        File.WriteAllBytes("output\\wiiu.3dw", GBUtils.GetEverySingleSubmission(GBGame.WiiU).ToBytes());
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("There was an error:\n" + e.Message);
                        break;
                    }
                }
                Console.WriteLine("Finished: " + decision);
            }
        }
    }

    public enum GBGame : ulong
    {
        WiiU = 5872,
        Switch = 8482
    }


    static class GBLinks
    {
        public static string GetSubmissionsLink(GBGame game, ulong page = 1)
        {
            page = Math.Clamp(page, 1, ulong.MaxValue);
            var gameid = (ulong)game;
            return $"https://api.gamebanana.com/Core/List/New?gameid={gameid}&page={page}&itemtype=Mod";
        }
        public static string GetSubmissionDataLink(ulong id)
        {
            return $"https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={id}&fields=name,Owner().name,Files().aFiles()";
        }
    }

    public sealed class GbModInfo
    {
        public string Name = string.Empty;
        public string Owner = string.Empty;
        public List<AFile> Files = new();
        public static GbModInfo Parse(JsonArray array)
        {
            GbModInfo result = new()
            {
                Name = array[0]?.Deserialize<string>() ?? string.Empty,
                Owner = array[1]?.Deserialize<string>() ?? string.Empty
            };
            foreach (var item in array.Skip(2))
            {
                var inner = item?.AsObject();
                if (inner is not null)
                    foreach (var (_, value) in inner)
                        if (value is not null)
                        {
                            var o = value.AsObject();
                            AFile file = new();
                            file.Parse(o);
                            result.Files.Add(file);
                        }
            }
            return result;
        }
        public static implicit operator GbModInfo(JsonArray array) => Parse(array);
        public void Deconstruct(out string name, out string owner, out AFile[] files)
        {
            name = Name;
            owner = Owner;
            files = Files.ToArray();
        }
        public GbModInfo() { }
        public GbModInfo(string name, string owner, IEnumerable<AFile> files)
        {
            Name = name;
            Owner = owner;
            Files = new(files);
        }
    }

    public sealed class AFile
    {
        public ulong idRow;
        public string sFile = string.Empty;
        public ulong nFilesize;
        public string sDescription = string.Empty;
        // Could be a DateTime??? Unsure. (Lord-G)
        public ulong tsDateAdded;
        public ulong nDownloadCount;
        public string sAnalysisState = string.Empty;
        public string sDownloadUrl = string.Empty;
        public string sMd5Checksum = string.Empty;
        public string sClamAvResult = string.Empty;
        public string sAnalysisResult = string.Empty;
        public bool bContainsExe;
        // Deserialize refused to work on AFile itself, wrote this hack instead.
        public void Parse(JsonObject obj)
        {
            foreach (var field in GetType().GetFields())
            {
                object? o = obj['_' + field.Name].Deserialize(field.FieldType);
                field.SetValue(this, o);
            }
        }
    }

    public static class GBUtils
    {
        public static ulong[] GetSubmissions(GBGame game, ulong page = 1)
        {
            List<ulong> items = new();
            using var client = new HttpClient();
            var data = client.GetStringAsync(GBLinks.GetSubmissionsLink(game, page)).GetAwaiter().GetResult();
            JsonDocument doc = JsonDocument.Parse(data);
            var arr = doc.Deserialize<JsonArray>() ?? new JsonArray();
            foreach (var item in arr)
            {
                var inner = item?.AsArray();
                var id = inner?[1];
                if (id is not null)
                    items.Add(id.Deserialize<ulong>());
            }
            return items.ToArray();
        }

        public static GbModInfo GetSubmissionData(ulong id)
        {
            using var client = new HttpClient();
            var data = client.GetStringAsync(GBLinks.GetSubmissionDataLink(id)).GetAwaiter().GetResult();
            var doc = JsonDocument.Parse(data);
            var arr = doc.Deserialize<JsonArray>() ?? new JsonArray();
            return arr;
        }

        public static Dictionary<ulong, GbModInfo> GetAllSubmissions(GBGame game, ulong page = 1)
        {
            Console.WriteLine("Getting submissions for " + game);
            return GetSubmissions(game, page).Select(x => (x, GetSubmissionData(x)))
                .ToDictionary(x => x.x, x => x.Item2);
        }

        public static Dictionary<ulong, GbModInfo> GetEverySingleSubmission(GBGame game)
        {
            Dictionary<ulong, GbModInfo> result = new();
            ulong page = 1;
            var subs = GetAllSubmissions(game, page++);
            Console.WriteLine(subs.Count);
            while (subs.Count != 0)
            {
                Console.WriteLine(subs.Count);
                foreach (var (key, value) in subs)
                {
                    result.TryAdd(key, value);
                    Console.WriteLine("Added: " + value.Name + " by " + value.Owner);
                }
                subs = GetAllSubmissions(game, page++);
            }
            Console.WriteLine(result.ToString());
            return result;
        }
        // TODO: Lord, write documentation for this. -Scyye
        public static byte[] ToBytes(this Dictionary<ulong, GbModInfo> dict)
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream, Encoding.UTF8);
            writer.Write(dict.Count);
            foreach (var item in dict.Keys)
                writer.Write(item); 
            
            foreach (var (name, owner, files) in dict.Values)
            {
                writer.Write(name);
                writer.Write(owner);
                writer.Write(files.Length);
                foreach (var file in files)
                {
                    writer.Write(file.idRow);
                    writer.Write(file.sFile);
                    writer.Write(file.nFilesize);
                    writer.Write(file.sDescription);
                    writer.Write(file.tsDateAdded);
                    writer.Write(file.nDownloadCount);
                    writer.Write(file.sAnalysisState);
                    writer.Write(file.sDownloadUrl);
                    writer.Write(file.sMd5Checksum);
                    writer.Write(file.sClamAvResult);
                    writer.Write(file.sAnalysisResult);
                    writer.Write(file.bContainsExe);
                }
            }
            return stream.ToArray();
        }

    }

    public static class CachedGBMods
    {
        public static Dictionary<ulong, GbModInfo> AllWiiUModsCached = new();
        public static Dictionary<ulong, GbModInfo> AllSwitchModsCached = new();
    }
}

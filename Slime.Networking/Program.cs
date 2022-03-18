using Logging.Net;

using Newtonsoft.Json.Linq;

using Spectre.Console;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace Slime.Networking
{
    public class Program
    {
        private static string OutputFolder = "out/";
        private static string CodeName = "Lena";
        private static string MinecraftVersion { get; set; } = "1.18.2";
        private static string MinecraftDataUrl { get; set; } 
            = "https://raw.githubusercontent.com/PrismarineJS/minecraft-data/master/data/pc/{0}/protocol.json";

        //                       Namespace          Bind               Packetname          Name    Type
        private static Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>> Packets { get; set; }
        //                       Namespace          Bind               Id       Packetname
        private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> PacketMappings { get; set; }

        private static string CurrentNamespace { get; set; } = "";
        private static string CurrentDirection { get; set; } = "";

        // Output
        private static string Out { get; set; } = "";
        private static int Tabs { get; set; } = 0;

        public static void Main(string[] args)
        {
            Logger.UsedLogger = new Logging.Net.Spectre.SpectreLogger();
            Logger.Info("Starting Slime.Networking code generator");

            var wc = new WebClient();

            string content = "";

            try
            {
                Logger.Info("Fetching protocol.json for " + MinecraftVersion);
                content = wc.DownloadString(string.Format(MinecraftDataUrl, MinecraftVersion));
            }
            catch(Exception e)
            {
                Logger.Error("Http error. Maybe unvalid version");
                AnsiConsole.WriteException(e);
            }

            // Init cache
            Packets = new();
            PacketMappings = new();

            JObject root = JObject.Parse(content);

            foreach(var ns in root.Children().ToList())
            {
                var prop = (JProperty)ns;

                if(prop.Name == "types")
                {
                    foreach (var tc in prop.Value.Children().ToList())
                    {
                        if (tc.HasValues)
                        {
                            if (tc.First.ToString() == "native")
                            {
                                var p = (JProperty)tc;

                                Logger.Info($"Type: {p.Name} > native");
                            }
                        }
                    }
                }
                else
                {
                    Logger.Info($"Detected namespace {prop.Name}");

                    CurrentNamespace = prop.Name;
                    PacketMappings.Add(CurrentNamespace, new());
                    Packets.Add(CurrentNamespace, new());

                    ParseNamespace(prop.Value);
                }
            }

            Logger.Info("Generating");

            Directory.CreateDirectory(OutputFolder);

            #region Mapping

            Logger.Info("Generating mapping");

            New();
            WriteMappingHeader();
            
            foreach(var ns in PacketMappings.Keys)
            {
                foreach(var dir in PacketMappings[ns].Keys)
                {
                    if(dir == "Client")
                    {
                        foreach(var key in PacketMappings[ns][dir].Keys)
                        {
                            Write($"pr.Add({key}, \"{FormatName(ns)}\",  {FormatName(PacketMappings[ns][dir][key])});");
                        }
                    }
                }
            }

            WriteMappingMiddle();

            foreach (var ns in PacketMappings.Keys)
            {
                foreach (var dir in PacketMappings[ns].Keys)
                {
                    if (dir == "Server")
                    {
                        foreach (var key in PacketMappings[ns][dir].Keys)
                        {
                            Write($"pr.Add({key}, \"{FormatName(ns)}\", {FormatName(PacketMappings[ns][dir][key])});");
                        }
                    }
                }
            }

            WriteMappingEnd();

            Save($"{OutputFolder}{CodeName}Mapping.g.cs");

            #endregion

            #region Packets

            Logger.Info("Generating packets");

            foreach(var ns in Packets.Keys)
            {
                foreach(var dir in Packets[ns].Keys)
                {
                    foreach(var name in Packets[ns][dir].Keys)
                    {
                        New();

                        WritePacketHeader(FormatName(ns), dir, FormatName(name));

                        foreach(var key in Packets[ns][dir][name].Keys)
                        {
                            Write($"public {Packets[ns][dir][name][key]} {key} " + "{ get; set; }");
                        }

                        WritePacketEncode();

                        foreach (var key in Packets[ns][dir][name].Keys)
                        {
                            Write($"ms.Write{Packets[ns][dir][name][key]}({key});");
                        }

                        WritePacketDecode();

                        foreach (var key in Packets[ns][dir][name].Keys)
                        {
                            Write($"{key} = ms.Read{Packets[ns][dir][name][key]}();");
                        }

                        WritePacketEnd();

                        Directory.CreateDirectory($"{OutputFolder}{CodeName}/{FormatName(ns)}/{dir}/");
                        Save($"{OutputFolder}{CodeName}/{FormatName(ns)}/{dir}/{FormatName(name)}.g.cs");
                    }
                }
            }

            #endregion

            Logger.Info("Done!");
            Console.ReadLine();
        }

        public static void ParseNamespace(JToken token)
        {
            var toClient = token["toClient"];
            var toServer = token["toServer"];

            CurrentDirection = "Client";
            PacketMappings[CurrentNamespace].Add(CurrentDirection, new());
            Packets[CurrentNamespace].Add(CurrentDirection, new());
            ParsePackets(toClient);
            CurrentDirection = "Server";
            PacketMappings[CurrentNamespace].Add(CurrentDirection, new());
            Packets[CurrentNamespace].Add(CurrentDirection, new());
            ParsePackets(toServer);
        }

        public static void ParsePackets(JToken token)
        {
            var types = token["types"];

            foreach(var packet in types.Children().ToList())
            {
                var proppacket = (JProperty)packet;
                var array1 = (JArray)proppacket.Value;
                var value = array1[1];

                if(proppacket.Name == "packet")
                {
                    var mappart = ((JArray)value)[0];

                    var type = (JArray)mappart["type"];
                    
                    var mappings = type[1]["mappings"];

                    foreach(var mapping in mappings.Children().ToList())
                    {
                        var mapprop = (JProperty)mapping;

                        Logger.Info($"Mapping {mapprop.Name} -> {mapprop.Value}");

                        PacketMappings[CurrentNamespace][CurrentDirection].Add(mapprop.Name, mapprop.Value.ToString());
                    }
                }
                else
                {
                    Logger.Info($"Packet detected: {proppacket.Name}");

                    Packets[CurrentNamespace][CurrentDirection].Add(proppacket.Name, new());

                    foreach(var packettype in value.Children().ToList())
                    {
                        var name = (JProperty)packettype.Children().ToArray()[0];
                        var type = (JProperty)packettype.Children().ToArray()[1];

                        Packets[CurrentNamespace][CurrentDirection][proppacket.Name].Add(name.Value.ToString(), type.Value.ToString());
                    }
                }
            }
        }

        public static void AddTab()
        {
            Tabs++;
        }

        public static void RemoveTab()
        {
            Tabs--;
        }

        public static void Write(string txt)
        {
            var tabs = "";

            for(int i = 0; i <= Tabs; i++)
            {
                tabs += "\t";
            }

            Out += tabs + txt + "\n";
        }
    
        public static void New()
        {
            Out = "";
            Tabs = 0;
        }

        public static void Save(string filename)
        {
            File.WriteAllText(filename, Out);
        }

        public static void WriteMappingHeader()
        {
            Write($"// This file was generated with Slime.Networking for Version {MinecraftVersion}");
            Write("");
            Write("using Slime.Networking;");
            Write("");
            Write($"namespace Slime.Networking.Versions.{CodeName}");
            Write("{");
            AddTab();
            Write($"public class {CodeName}Mapping : IMapping");
            Write("{");
            AddTab();
            Write("public static void AddClientPackets(PacketRegistry pr)");
            Write("{");
            AddTab();
        }

        public static void WriteMappingMiddle()
        {
            RemoveTab();
            Write("}");
            Write("");
            Write("public static void AddServerPackets(PacketRegistry pr)");
            Write("{");
            AddTab();
        }

        public static void WriteMappingEnd()
        {
            RemoveTab();
            Write("}");
            RemoveTab();
            Write("}");
            RemoveTab();
            Write("}");
        }

        public static void WritePacketHeader(string ns, string dir, string packetname)
        {
            Write($"// This file was generated with Slime.Networking for Version {MinecraftVersion}");
            Write("");
            Write("using Slime.Networking;");
            Write("");
            Write($"namespace Slime.Networking.Versions.{CodeName}.{ns}.{dir}");
            Write("{");
            AddTab();
            Write($"public class {packetname} : IPacket, IDeEncodeAble");
            Write("{");
            AddTab();
        }

        public static void WritePacketEncode()
        {
            Write("");
            Write("public void Encode(MinecraftStream ms)");
            Write("{");
            AddTab();
        }

        public static void WritePacketDecode()
        {
            RemoveTab();
            Write("}");
            Write("");
            Write("public void Decode(MinecraftStream ms)");
            Write("{");
            AddTab();
        }

        public static void WritePacketEnd()
        {
            RemoveTab();
            Write("}");
            RemoveTab();
            Write("}");
            RemoveTab();
            Write("}");
        }

        public static string FormatName(string input)
        {
            var res = input.Replace("packet_", "");

            res = res.Substring(0, 1).ToUpper() + res.Substring(1, res.Length - 1);

            while(res.Contains("_"))
            {
                int index = res.IndexOf("_");
                string after = res.Substring(index + 1, 1);

                res = res.Replace("_" + after, after.ToUpper());
            }

            return res;
        }
    }
}

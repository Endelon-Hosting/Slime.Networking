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
        public static void Main(string[] args)
        {
            Logger.UsedLogger = new Logging.Net.Spectre.SpectreLogger();
            Logger.Info("Starting Slime.Networking code generator");

            new Generator("Lena","1.18.2","out/").GenerateCode();
        }
    }
}

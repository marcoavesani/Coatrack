using System;

namespace Coatrack
{
    /// <summary>Build helper: writes the app icon (.ico) that build.ps1 embeds in Coatrack.exe.</summary>
    static class IconTool
    {
        static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("usage: IconTool <output.ico>");
                return 2;
            }
            HatIcon.WriteAppIco(args[0]);
            Console.WriteLine("wrote " + args[0]);
            return 0;
        }
    }
}

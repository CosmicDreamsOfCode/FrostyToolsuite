using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FrostySdk;
using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using System.Reflection;

namespace FrostyCmd
{
    class Program
    {
        static void Main(string[] args)
        {
            bool shaders = false;
            if (args.Length == 0)
            {
                if (Console.ReadLine() == "s")
                {
                    shaders = true;
                }
            }
            else if (args[0] == "s")
            {
                shaders = true;
            }

            if (shaders)
            {
                // create shader bin
                FrostyShaderDbBuilder builder = new FrostyShaderDbBuilder();
                builder.Build();
            }
            else
            {
                // create profile bin
                ProfileCreator profileCreator = new ProfileCreator();
                profileCreator.CreateProfiles();
            }
        }
    }
}

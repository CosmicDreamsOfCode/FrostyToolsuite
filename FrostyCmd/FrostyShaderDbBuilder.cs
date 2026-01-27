using FrostySdk.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FrostyCmd
{
    public class FrostyShaderDbBuilder
    {
        private string binPath = @"..\..\..\..\Shaders\Bin";
        public class Shader
        {
            public string name;
            public long offset;
            public byte[] data;
        }
        public void Build()
        {
            using (NativeWriter writer = new NativeWriter(new FileStream(@"..\..\..\..\FrostyPlugin\Shaders.bin", FileMode.Create)))
            {
                //create lists of the shaders
                List<Shader> vertexShaders = new List<Shader>();
                List<Shader> pixelShaders = new List<Shader>();
                foreach (string file in Directory.EnumerateFiles(binPath))
                {
                    Shader shader = new Shader();
                    shader.name = Path.GetFileNameWithoutExtension(file);
                    if (Path.GetExtension(file) == ".vso")
                    {
                        shader.data = File.ReadAllBytes(file);
                        vertexShaders.Add(shader);
                    }
                    else if (Path.GetExtension(file) == ".pso")
                    {
                        shader.data = File.ReadAllBytes(file);
                        pixelShaders.Add(shader);
                    }
                }

                writer.Write(vertexShaders.Count);
                writer.Write((long)0x18); //vertex shader list start offset
                writer.Write(pixelShaders.Count);
                writer.Write(0xdeadbeefdeadbeef);

                //write most of the header out
                foreach (Shader shader in vertexShaders)
                {
                    writer.WriteNullTerminatedString(shader.name);
                    writer.Write(shader.data.Length);
                    writer.Write(0xdeadbeefdeadbeef);
                }

                long pos = writer.Position;
                writer.Position = 0x10;
                writer.Write(pos); //pixel shader list start
                writer.Position = pos;

                foreach (Shader shader in pixelShaders)
                {
                    writer.WriteNullTerminatedString(shader.name);
                    writer.Write(shader.data.Length);
                    writer.Write(0xdeadbeefdeadbeef);
                }

                //write dxbc
                foreach (Shader shader in vertexShaders)
                {
                    shader.offset = writer.Position;
                    writer.Write(shader.data);
                }

                foreach (Shader shader in pixelShaders)
                {
                    shader.offset = writer.Position;
                    writer.Write(shader.data);
                }

                //FINALLY we update all the dummies we wrote earlier
                writer.Position = 0x18;
                foreach (Shader shader in vertexShaders)
                {
                    writer.Position += shader.name.Length + 5;
                    writer.Write(shader.offset);
                }
                foreach (Shader shader in pixelShaders)
                {
                    writer.Position += shader.name.Length + 5;
                    writer.Write(shader.offset);
                }
            }
        }
    }
}

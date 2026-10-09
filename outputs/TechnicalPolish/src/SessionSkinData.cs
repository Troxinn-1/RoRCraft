using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RoRCraftPolish {
    // Raw RGBA, top row first. Session binding prevents stale account skin reuse.
    internal sealed class SessionSkinData {
        internal readonly string Account,Hash;
        internal readonly bool Slim;
        internal readonly byte[] Pixels;
        internal static SessionSkinData Create(string account,bool slim,byte[] pixels) {
            if(pixels==null || pixels.Length!=16384) throw new InvalidDataException("Expected 64x64 RGBA skin");
            using(var sha=SHA256.Create()) return new SessionSkinData(account,slim,pixels,sha.ComputeHash(pixels));
        }
        internal byte[] Encode(string session) {
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream)) {
                writer.Write(0x5243534B);writer.Write(1);writer.Write(Encoding.ASCII.GetBytes(session));
                writer.Write(Encoding.ASCII.GetBytes(Account));writer.Write(Slim?1:0);writer.Write(64);writer.Write(64);
                using(var sha=SHA256.Create()) writer.Write(sha.ComputeHash(Pixels));
                writer.Write(Pixels);return stream.ToArray();
            }
        }
        private SessionSkinData(string account,bool slim,byte[] pixels,byte[] hash) {
            Account=account;Slim=slim;Pixels=pixels;Hash=BitConverter.ToString(hash);
        }
        internal static SessionSkinData Read(byte[] data,string session) {
            if(data==null || data.Length!=120+64*64*4) throw new InvalidDataException("Invalid skin payload size");
            using(var reader=new BinaryReader(new MemoryStream(data))) {
                if(reader.ReadInt32()!=0x5243534B || reader.ReadInt32()!=1) throw new InvalidDataException("Unsupported skin payload");
                string supplied=Encoding.ASCII.GetString(reader.ReadBytes(32));Guid id;
                if(session==null || session.Length!=32 || supplied!=session || !Guid.TryParseExact(session,"N",out id)) throw new InvalidDataException("Skin belongs to another launch session");
                string account=Encoding.ASCII.GetString(reader.ReadBytes(36));
                if(!Guid.TryParseExact(account,"D",out id) || id==Guid.Empty) throw new InvalidDataException("Invalid Minecraft profile identity");
                int model=reader.ReadInt32(),width=reader.ReadInt32(),height=reader.ReadInt32();
                if((model!=0 && model!=1) || width!=64 || height!=64) throw new InvalidDataException("Unsupported Minecraft skin model or dimensions");
                var expected=reader.ReadBytes(32);var pixels=reader.ReadBytes(64*64*4);
                using(var sha=SHA256.Create()) {
                    byte[] actual=sha.ComputeHash(pixels);int difference=0;
                    for(int n=0;n<32;n++) difference|=actual[n]^expected[n];
                    if(difference!=0) throw new InvalidDataException("Skin pixels failed integrity validation");
                }
                return new SessionSkinData(account,model==1,pixels,expected);
            }
        }
    }
}

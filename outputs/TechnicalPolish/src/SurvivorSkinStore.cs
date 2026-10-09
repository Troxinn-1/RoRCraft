using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

namespace RoRCraftPolish {
    // Profile UUID + stable survivor body name, never a single global player skin.
    internal sealed class SurvivorSkinStore {
        private readonly string directory,settings;
        private readonly Dictionary<string,string> assignments=new Dictionary<string,string>();
        private readonly Dictionary<string,byte[]> pixels=new Dictionary<string,byte[]>();
        internal SurvivorSkinStore(string path) {
            directory=path;settings=Path.Combine(path,"assignments.tsv");Directory.CreateDirectory(path);
            if(File.Exists(settings)) foreach(var line in File.ReadAllLines(settings)) {
                var parts=line.Split('\t');
                if(parts.Length==3 && Valid(parts[2])) assignments[parts[0]+"\t"+parts[1]]=parts[2];
            }
        }
        private static string Key(string profile,string survivor) {return profile+"\t"+Convert.ToBase64String(Encoding.UTF8.GetBytes(survivor));}
        private static bool Valid(string id) {
            if(id==null || id.Length!=66 || id[64]!=':' || (id[65]!='0' && id[65]!='1')) return false;
            for(int i=0;i<64;i++) if(!Uri.IsHexDigit(id[i])) return false;
            return true;
        }
        internal static void Atomic(string path,byte[] bytes) {
            string temp=path+".tmp";File.WriteAllBytes(temp,bytes);
            if(File.Exists(path)) File.Replace(temp,path,null);else File.Move(temp,path);
        }
        internal string Import(string png,bool slim) {
            var file=new FileInfo(png);if(!file.Exists || file.Length>1024*1024) throw new InvalidDataException("Choose a PNG up to 1 MB");
            var encoded=File.ReadAllBytes(png);
            if(encoded.Length<33 || encoded[0]!=137 || encoded[1]!=80 || encoded[2]!=78 || encoded[3]!=71 ||
                encoded[16]!=0 || encoded[17]!=0 || encoded[18]!=0 || encoded[19]!=64 ||
                encoded[20]!=0 || encoded[21]!=0 || encoded[22]!=0 || encoded[23]!=64)
                throw new InvalidDataException("Skin must be a 64x64 PNG");
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try {
                if(!ImageConversion.LoadImage(texture,encoded) || texture.width!=64 || texture.height!=64)
                    throw new InvalidDataException("Skin must be a 64x64 PNG");
                var colors=texture.GetPixels32();var raw=new byte[16384];
                for(int y=0;y<64;y++) for(int x=0;x<64;x++) {
                    var color=colors[(63-y)*64+x];int n=(y*64+x)*4;
                    raw[n]=color.r;raw[n+1]=color.g;raw[n+2]=color.b;
                    raw[n+3]=(byte)((x<32 && y<16 || y>=16 && y<32 || x>=16 && x<48 && y>=48)?255:color.a);
                }
                string hash;using(var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(raw)).Replace("-","").ToLowerInvariant();
                var id=hash+":"+(slim?"1":"0");
                Atomic(Path.Combine(directory,hash+".rgba"),raw);
                Atomic(Path.Combine(directory,hash+".name"),Encoding.UTF8.GetBytes(Path.GetFileNameWithoutExtension(png)));
                pixels[hash]=raw;return id;
            } finally {UnityEngine.Object.Destroy(texture);}
        }
        internal string Assignment(string profile,string survivor) {string value;return assignments.TryGetValue(Key(profile,survivor),out value)?value:null;}
        internal void Assign(string profile,string survivor,string id) {
            if(id!=null && !Valid(id)) throw new InvalidDataException("Invalid skin identity");
            var key=Key(profile,survivor);var updated=new Dictionary<string,string>(assignments);
            if(id==null) updated.Remove(key);else updated[key]=id;
            var lines=new List<string>();foreach(var pair in updated) lines.Add(pair.Key+"\t"+pair.Value);
            lines.Sort(StringComparer.Ordinal);Atomic(settings,Encoding.UTF8.GetBytes(string.Join("\n",lines.ToArray())));
            assignments.Clear();foreach(var pair in updated) assignments.Add(pair.Key,pair.Value);
        }
        internal SessionSkinData Resolve(SessionSkinData account,string survivor,out string failure) {
            failure=null;var id=Assignment(account.Account,survivor);if(id==null) return account;
            try {
                var hash=id.Substring(0,64);byte[] raw;
                if(!pixels.TryGetValue(hash,out raw)) {
                    raw=File.ReadAllBytes(Path.Combine(directory,hash+".rgba"));
                    if(raw.Length!=16384) throw new InvalidDataException("Invalid saved skin size");
                    string actual;using(var sha=SHA256.Create()) actual=BitConverter.ToString(sha.ComputeHash(raw)).Replace("-","").ToLowerInvariant();
                    if(actual!=hash) throw new InvalidDataException("Saved skin integrity failed");
                    pixels[hash]=raw;
                }
                return SessionSkinData.Create(account.Account,id[65]=='1',raw);
            } catch(Exception error) {failure=error.Message;return account;}
        }
        internal string[] Available() {
            var list=new List<string>();foreach(var file in Directory.GetFiles(directory,"*.rgba")) {
                var hash=Path.GetFileNameWithoutExtension(file);if(hash.Length==64) {list.Add(hash+":0");list.Add(hash+":1");}
            }return list.ToArray();
        }
        internal string Label(string id) {
            var file=Path.Combine(directory,id.Substring(0,64)+".name");
            return (File.Exists(file)?File.ReadAllText(file):id.Substring(0,8))+ (id.EndsWith(":1")?" (Slim)":" (Classic)");
        }
    }
}

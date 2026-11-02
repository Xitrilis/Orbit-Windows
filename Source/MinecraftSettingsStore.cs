using System;
using System.IO;
using Newtonsoft.Json;

namespace Orbit
{
    // One owner for the file shared by Prism and Horizon prevents stale whole-file writes.
    public sealed class MinecraftSettingsStore
    {
        readonly object gate = new object();
        readonly LocalDocument<MinecraftSettings> document;
        long revision;
        public MinecraftSettingsStore(string dataDirectory)
        {
            document = new LocalDocument<MinecraftSettings>(Path.Combine(dataDirectory, "minecraft-settings.json"), 65536, MinecraftFiles.ValidateSettings);
        }
        static MinecraftSettings Copy(MinecraftSettings value) => JsonConvert.DeserializeObject<MinecraftSettings>(JsonConvert.SerializeObject(value));
        public long Revision { get { lock (gate) return revision; } }
        public bool ReadOnly { get { lock (gate) return document.ReadOnly; } }
        public string Warning { get { lock (gate) return document.Warning; } }
        public MinecraftSettings Snapshot() { lock (gate) return Copy(document.Value); }
        public void Update(Action<MinecraftSettings> change)
        {
            lock (gate)
            {
                var next = Copy(document.Value);
                change(next);
                document.Save(next);
                revision++;
            }
        }
    }
}

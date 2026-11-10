using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace Coatrack
{
    [DataContract]
    public class Instance
    {
        public const string DefaultId = "default";

        [DataMember(Name = "id", Order = 1)] public string Id;
        [DataMember(Name = "name", Order = 2)] public string Name;
        /// <summary>Passed to Claude as --user-data-dir. Null for the default instance.</summary>
        [DataMember(Name = "appDataPath", Order = 3, EmitDefaultValue = false)] public string AppDataPath;
        /// <summary>Passed to Claude as CLAUDE_CONFIG_DIR. Null for the default instance.</summary>
        [DataMember(Name = "claudeCodePath", Order = 4, EmitDefaultValue = false)] public string ClaudeCodePath;

        public bool IsDefault { get { return Id == DefaultId; } }

        public static Instance NewDefault()
        {
            return new Instance { Id = DefaultId, Name = "Default" };
        }

        public static Instance Create(string name, string appData, string claudeCode)
        {
            return new Instance
            {
                Id = Guid.NewGuid().ToString("D"),
                Name = name.Trim(),
                AppDataPath = PathUtil.Standardise(appData),
                ClaudeCodePath = PathUtil.Standardise(claudeCode)
            };
        }

        public override string ToString() { return Name; }
    }

    [DataContract]
    public class Config
    {
        [DataMember(Name = "version", Order = 1)] public int Version;
        [DataMember(Name = "instances", Order = 2)] public List<Instance> Instances;
        [DataMember(Name = "confirmBeforeSwitching", Order = 3)] public bool ConfirmBeforeSwitching;
        /// <summary>Custom path to Claude's exe. Null means find it automatically.</summary>
        [DataMember(Name = "claudePath", Order = 4, EmitDefaultValue = false)] public string ClaudePath;
        /// <summary>The instance launched last, used when a sign-in link arrives and Claude isn't running.</summary>
        [DataMember(Name = "lastInstanceId", Order = 5, EmitDefaultValue = false)] public string LastInstanceId;

        public Config() { SetDefaults(); }

        [OnDeserializing]
        void OnDeserializing(StreamingContext c) { SetDefaults(); }

        void SetDefaults()
        {
            Version = 1;
            Instances = new List<Instance>();
            ConfirmBeforeSwitching = true;
        }

        public Instance DefaultInstance
        {
            get { return Instances.First(i => i.IsDefault); }
        }

        public Instance FindById(string id)
        {
            if (id == null) return null;
            return Instances.FirstOrDefault(i => i.Id == id);
        }

        /// <summary>
        /// Matches the --user-data-dir of a running Claude to an instance.
        /// Null/empty means the default instance. Returns null when no instance matches ("Unknown instance").
        /// </summary>
        public Instance MatchUserDataDir(string userDataDir)
        {
            if (string.IsNullOrEmpty(userDataDir)) return DefaultInstance;
            string std = PathUtil.Standardise(userDataDir) ?? userDataDir;
            foreach (Instance i in Instances)
            {
                if (i.IsDefault) continue;
                if (PathUtil.AreEqual(i.AppDataPath, std) || PathUtil.AreEqual(i.AppDataPath, userDataDir)) return i;
            }
            return null;
        }

        /// <summary>Makes sure the default instance exists, comes first and has no paths.</summary>
        public void Normalise()
        {
            if (Instances == null) Instances = new List<Instance>();
            Instances.RemoveAll(i => i == null || string.IsNullOrEmpty(i.Id));
            Instance def = Instances.FirstOrDefault(i => i.IsDefault);
            if (def == null) def = Instance.NewDefault();
            Instances.Remove(def);
            def.AppDataPath = null;
            def.ClaudeCodePath = null;
            if (string.IsNullOrWhiteSpace(def.Name)) def.Name = "Default";
            Instances.Insert(0, def);
        }
    }

    /// <summary>Loads and saves config.json atomically.</summary>
    public class ConfigStore
    {
        public readonly string Folder;
        public string FilePath { get { return Path.Combine(Folder, "config.json"); } }

        public ConfigStore(string folder)
        {
            Folder = folder;
        }

        public static string DefaultFolder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);
            }
        }

        public static string DefaultInstancesFolder
        {
            get { return Path.Combine(DefaultFolder, "Instances"); }
        }

        public Config Load()
        {
            Config c = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var ser = new DataContractJsonSerializer(typeof(Config));
                        c = (Config)ser.ReadObject(fs);
                    }
                }
            }
            catch (Exception)
            {
                // Keep a copy of a config we couldn't read rather than silently losing it.
                try { File.Copy(FilePath, FilePath + ".unreadable", true); } catch (Exception) { }
                c = null;
            }
            if (c == null) c = new Config();
            c.Normalise();
            return c;
        }

        public void Save(Config c)
        {
            c.Normalise();
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (XmlDictionaryWriter w = JsonReaderWriterFactory.CreateJsonWriter(fs, new UTF8Encoding(false), false, true, "  "))
            {
                var ser = new DataContractJsonSerializer(typeof(Config));
                ser.WriteObject(w, c);
                w.Flush();
                fs.Flush(true);
            }
            if (File.Exists(FilePath))
                File.Replace(tmp, FilePath, null, true);
            else
                File.Move(tmp, FilePath);
        }
    }
}

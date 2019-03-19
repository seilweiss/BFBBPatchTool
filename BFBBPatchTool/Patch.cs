using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HipHopFile;
using static HipHopFile.Functions;

namespace BFBBPatchTool
{
    public class Patch
    {
        public enum PatchType
        {
            ADD,
            DELETE,
            MODIFY
        }

        public bool isUninstall = false;

        public List<AddedFile> addedFiles = new List<AddedFile>();
        public List<ModifiedFile> modifiedFiles = new List<ModifiedFile>();
        public List<DeletedFile> deletedFiles = new List<DeletedFile>();
        public List<HipFile> hipFiles = new List<HipFile>();

        public Patch() { }

        public Patch(string path)
        {
            Read(path);
        }

        public Patch(string root1, string root2)
        {
            Scan(root1, root2);
        }

        public void Scan(string root1, string root2)
        {
            Console.WriteLine();
            Console.WriteLine("Scanning...");
            Console.WriteLine();

            List<string> files1 = GetFiles(root1);
            List<string> files2 = GetFiles(root2);

            foreach (string file in files1)
            {
                if (files2.Contains(file))
                {
                    string path1 = root1 + file;
                    string path2 = root2 + file;

                    if (Util.FileIsHip(file))
                    {
                        HipFile hip = new HipFile(file);
                        GetHipFileChanges(ref hip, path1, path2);

                        if (hip.addedAssets.Count > 0 || hip.modifiedAssets.Count > 0 || hip.deletedAssets.Count > 0)
                        {
                            hipFiles.Add(hip);

                            PrintFile(PatchType.MODIFY, file);

                            foreach (AddedAsset asset in hip.addedAssets)
                            {
                                PrintAsset(PatchType.ADD, asset.name);
                            }

                            foreach (ModifiedAsset asset in hip.modifiedAssets)
                            {
                                PrintAsset(PatchType.MODIFY, asset.name);
                            }

                            foreach (DeletedAsset asset in hip.deletedAssets)
                            {
                                PrintAsset(PatchType.DELETE, asset.name);
                            }
                        }
                    }
                    else if (!Util.FilesAreEqual(path1, path2))
                    {
                        byte[] data = File.ReadAllBytes(path2);
                        modifiedFiles.Add(new ModifiedFile(file, data));

                        PrintFile(PatchType.MODIFY, file);
                    }

                    files2.Remove(file);
                }
                else
                {
                    deletedFiles.Add(new DeletedFile(file));

                    PrintFile(PatchType.DELETE, file);
                }
            }

            foreach (string file in files2)
            {
                string path = root2 + file;
                byte[] data = File.ReadAllBytes(path);

                addedFiles.Add(new AddedFile(file, data));

                PrintFile(PatchType.ADD, file);
            }

            Console.WriteLine();
            Console.WriteLine("Done scanning.");
            Console.WriteLine();
            Console.WriteLine("Added files:          " + addedFiles.Count.ToString());
            Console.WriteLine("Modified files:       " + modifiedFiles.Count.ToString());
            Console.WriteLine("Deleted files:        " + deletedFiles.Count.ToString());
            Console.WriteLine("Modified HIP files:   " + hipFiles.Count.ToString());
            Console.WriteLine();
        }

        public Patch Commit(string root)
        {
            Patch uninstall = new Patch();
            uninstall.isUninstall = true;

            Console.WriteLine();
            Console.WriteLine("Patching...");
            Console.WriteLine();

            foreach (AddedFile file in addedFiles)
            {
                string path = root + file.path;

                DeletedFile backup = new DeletedFile(file.path);
                uninstall.deletedFiles.Add(backup);
                
                File.WriteAllBytes(path, file.data);

                PrintFile(PatchType.ADD, file.path);
            }

            foreach (ModifiedFile file in modifiedFiles)
            {
                string path = root + file.path;

                if (File.Exists(path))
                {
                    byte[] data = File.ReadAllBytes(path);
                    ModifiedFile backup = new ModifiedFile(file.path, data);
                    uninstall.modifiedFiles.Add(backup);
                }

                File.WriteAllBytes(path, file.data);

                PrintFile(PatchType.MODIFY, file.path);
            }

            foreach (DeletedFile file in deletedFiles)
            {
                string path = root + file.path;

                if (File.Exists(path))
                {
                    byte[] data = File.ReadAllBytes(path);
                    AddedFile backup = new AddedFile(file.path, data);
                    uninstall.addedFiles.Add(backup);
                }

                File.Delete(path);

                PrintFile(PatchType.DELETE, file.path);
            }

            foreach (HipFile file in hipFiles)
            {
                string path = root + file.path;

                HipFile backup = new HipFile(file.path);
                HipSection[] hip = HipFileToHipArray(path);

                Section_HIPA hipa = (Section_HIPA)hip[0];
                Section_PACK pack = (Section_PACK)hip[1];
                Section_DICT dict = (Section_DICT)hip[2];
                Section_STRM strm = (Section_STRM)hip[3];

                List<Section_AHDR> ahdrList = dict.ATOC.AHDRList;
                List<Section_LHDR> lhdrList = dict.LTOC.LHDRList;

                Dictionary<uint, Section_AHDR> ahdrDict = ahdrList.ToDictionary(s => s.assetID);

                PrintFile(PatchType.MODIFY, file.path);

                foreach (AddedAsset asset in file.addedAssets)
                {
                    Section_ADBG adbg = new Section_ADBG(asset.alignment, asset.name, asset.filename, asset.checksum);
                    Section_AHDR ahdr = new Section_AHDR(asset.id, new string(asset.type), (AHDRFlags)asset.flags, adbg);

                    ahdr.data = asset.data;

                    DeletedAsset backupAsset = new DeletedAsset(asset);
                    backup.deletedAssets.Add(backupAsset);

                    ahdrList.Add(ahdr);

                    Section_LHDR lhdr = lhdrList[asset.layer];
                    lhdr.assetIDlist.Add(asset.id);

                    PrintAsset(PatchType.ADD, asset.name);
                }

                foreach (ModifiedAsset asset in file.modifiedAssets)
                {
                    if (!ahdrDict.ContainsKey(asset.id)) break;
                    
                    Section_ADBG adbg = new Section_ADBG(asset.alignment, asset.name, asset.filename, asset.checksum);
                    Section_AHDR ahdr = new Section_AHDR(asset.id, new string(asset.type), (AHDRFlags)asset.flags, adbg);
                    
                    ahdr.data = asset.data;

                    Section_AHDR oldAHDR = ahdrDict[asset.id];
                    ahdrList.Remove(oldAHDR);
                    ahdrList.Add(ahdr);

                    ModifiedAsset backupAsset = new ModifiedAsset(oldAHDR, hip);
                    backup.modifiedAssets.Add(backupAsset);

                    PrintAsset(PatchType.MODIFY, asset.name);
                }

                foreach (DeletedAsset asset in file.deletedAssets)
                {
                    if (!ahdrDict.ContainsKey(asset.id)) break;

                    Section_AHDR ahdr = ahdrDict[asset.id];

                    ahdrList.Remove(ahdr);

                    AddedAsset backupAsset = new AddedAsset(ahdr, hip);
                    backup.addedAssets.Add(backupAsset);

                    Section_LHDR lhdr = lhdrList[asset.layer];
                    lhdr.assetIDlist.Remove(asset.id);

                    PrintAsset(PatchType.DELETE, asset.name);
                }
                
                pack.PCNT.AHDRCount = ahdrList.Count;

                hip = SetupStream(ref hipa, ref pack, ref dict, ref strm);
                File.WriteAllBytes(path, HipArrayToFile(hip));

                uninstall.hipFiles.Add(backup);
            }

            Console.WriteLine("Done patching.");
            Console.WriteLine();

            return uninstall;
        }

        public void Write(string path)
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(path));
            writer.Write("PIPA".ToCharArray());

            writer.Write(isUninstall);

            writer.Write((short)addedFiles.Count);
            foreach (AddedFile file in addedFiles)
            {
                file.Write(writer);
            }

            writer.Write((short)modifiedFiles.Count);
            foreach (ModifiedFile file in modifiedFiles)
            {
                file.Write(writer);
            }

            writer.Write((short)deletedFiles.Count);
            foreach (DeletedFile file in deletedFiles)
            {
                file.Write(writer);
            }

            writer.Write((short)hipFiles.Count);
            foreach (HipFile file in hipFiles)
            {
                file.Write(writer);
            }

            writer.Close();
        }

        public void Read(string path)
        {
            BinaryReader reader = new BinaryReader(File.OpenRead(path));
            
            if (!reader.ReadChars(4).SequenceEqual("PIPA".ToCharArray()))
                throw new Exception("Not a valid PIPA file");

            isUninstall = reader.ReadBoolean();

            short numAddedFiles = reader.ReadInt16();
            for (int i = 0; i < numAddedFiles; i++)
            {
                addedFiles.Add(new AddedFile(reader));
            }

            short numModifiedFiles = reader.ReadInt16();
            for (int i = 0; i < numModifiedFiles; i++)
            {
                modifiedFiles.Add(new ModifiedFile(reader));
            }

            short numDeletedFiles = reader.ReadInt16();
            for (int i = 0; i < numDeletedFiles; i++)
            {
                deletedFiles.Add(new DeletedFile(reader));
            }

            short numHipFiles = reader.ReadInt16();
            for (int i = 0; i < numHipFiles; i++)
            {
                hipFiles.Add(new HipFile(reader));
            }

            reader.Close();
        }

        public void Print()
        {
            foreach (AddedFile file in addedFiles)
            {
                PrintFile(PatchType.ADD, file.path);
            }

            foreach (ModifiedFile file in modifiedFiles)
            {
                PrintFile(PatchType.MODIFY, file.path);
            }

            foreach (DeletedFile file in deletedFiles)
            {
                PrintFile(PatchType.DELETE, file.path);
            }

            foreach (HipFile file in hipFiles)
            {
                PrintFile(PatchType.MODIFY, file.path);

                foreach (AddedAsset asset in file.addedAssets)
                {
                    PrintAsset(PatchType.ADD, asset.name);
                }

                foreach (ModifiedAsset asset in file.modifiedAssets)
                {
                    PrintAsset(PatchType.MODIFY, asset.name);
                }

                foreach (DeletedAsset asset in file.deletedAssets)
                {
                    PrintAsset(PatchType.DELETE, asset.name);
                }
            }
        }

        public static List<string> GetFiles(string root)
        {
            return Directory.EnumerateFiles(root, "*.ini")
                .Union(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(s => Util.FileIsHip(s)))
                .Select(s => s.Substring(root.Length).ToLower())
                .ToList();
        }

        public static void GetHipFileChanges(ref HipFile hipFile, string path1, string path2)
        {
            HipSection[] hip1 = HipFileToHipArray(path1);
            HipSection[] hip2 = HipFileToHipArray(path2);

            Dictionary<uint, Section_AHDR> ahdrs1 = Util.HipArrayToAHDRDict(hip1);
            Dictionary<uint, Section_AHDR> ahdrs2 = Util.HipArrayToAHDRDict(hip2);

            foreach (uint assetID in ahdrs1.Keys)
            {
                if (ahdrs2.ContainsKey(assetID))
                {
                    byte[] data1 = ahdrs1[assetID].data;
                    byte[] data2 = ahdrs2[assetID].data;

                    if (!Util.ByteArraysAreEqual(data1, data2))
                    {
                        hipFile.modifiedAssets.Add(new ModifiedAsset(ahdrs2[assetID], hip2));
                    }

                    ahdrs2.Remove(assetID);
                }
                else
                {
                    hipFile.deletedAssets.Add(new DeletedAsset(ahdrs1[assetID], hip1));
                }
            }

            foreach (Section_AHDR ahdr in ahdrs2.Values)
            {
                hipFile.addedAssets.Add(new AddedAsset(ahdr, hip2));
            }
        }

        public static void PrintFile(PatchType type, string file)
        {
            Console.WriteLine("{0} {1}", type, file);
        }

        public static void PrintAsset(PatchType type, string asset)
        {
            Console.WriteLine("    {0} {1}", type, asset);
        }
    }

    public abstract class PatchItem
    {
        public abstract void Write(BinaryWriter writer);
    }

    public class AddedFile : PatchItem
    {
        public string path;
        public byte[] data;

        public AddedFile(string path, byte[] data)
        {
            this.path = path;
            this.data = data;
        }

        public AddedFile(BinaryReader reader)
        {
            path = Util.ReadString(reader);
            int size = reader.ReadInt32();
            data = reader.ReadBytes(size);
        }

        public override void Write(BinaryWriter writer)
        {
            Util.WriteString(writer, path);
            writer.Write(data.Length);
            writer.Write(data);
        }
    }

    public class ModifiedFile : PatchItem
    {
        public string path;
        public byte[] data;

        public ModifiedFile(string path, byte[] data)
        {
            this.path = path;
            this.data = data;
        }

        public ModifiedFile(BinaryReader reader)
        {
            path = Util.ReadString(reader);
            int size = reader.ReadInt32();
            data = reader.ReadBytes(size);
        }

        public override void Write(BinaryWriter writer)
        {
            Util.WriteString(writer, path);
            writer.Write(data.Length);
            writer.Write(data);
        }
    }

    public class DeletedFile : PatchItem
    {
        public string path;

        public DeletedFile(string path)
        {
            this.path = path;
        }

        public DeletedFile(BinaryReader reader)
        {
            path = Util.ReadString(reader);
        }

        public override void Write(BinaryWriter writer)
        {
            Util.WriteString(writer, path);
        }
    }

    public class AddedAsset : PatchItem
    {
        public uint id;
        public string name;
        public string filename;
        public char[] type;
        public short layer;
        public int flags;
        public int alignment;
        public int checksum;
        public byte[] data;

        public AddedAsset(Section_AHDR ahdr, HipSection[] hip)
        {
            id = ahdr.assetID;
            name = ahdr.ADBG.assetName;
            filename = ahdr.ADBG.assetFileName;
            type = ahdr.assetType.ToString().PadRight(4).ToCharArray();
            layer = (short)Util.AssetGetLayerIndex(ahdr, hip);
            flags = (int)ahdr.flags;
            alignment = ahdr.ADBG.alignment;
            checksum = ahdr.ADBG.checksum;
            data = ahdr.data;
        }

        public AddedAsset(BinaryReader reader)
        {
            id = reader.ReadUInt32();
            name = Util.ReadString(reader);
            filename = Util.ReadString(reader);
            type = reader.ReadChars(4);
            layer = reader.ReadInt16();
            flags = reader.ReadInt32();
            alignment = reader.ReadInt32();
            checksum = reader.ReadInt32();
            int size = reader.ReadInt32();
            data = reader.ReadBytes(size);
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(id);
            Util.WriteString(writer, name);
            Util.WriteString(writer, filename);

            foreach (char c in type)
                writer.Write((byte)c);

            writer.Write(layer);
            writer.Write(flags);
            writer.Write(alignment);
            writer.Write(checksum);
            writer.Write(data.Length);
            writer.Write(data);
        }
    }

    public class ModifiedAsset : PatchItem
    {
        public uint id;
        public string name;
        public string filename;
        public char[] type;
        public short layer;
        public int flags;
        public int alignment;
        public int checksum;
        public byte[] data;

        public ModifiedAsset(Section_AHDR ahdr, HipSection[] hip)
        {
            id = ahdr.assetID;
            name = ahdr.ADBG.assetName;
            filename = ahdr.ADBG.assetFileName;
            type = ahdr.assetType.ToString().PadRight(4).ToCharArray();
            layer = (short)Util.AssetGetLayerIndex(ahdr, hip);
            flags = (int)ahdr.flags;
            alignment = ahdr.ADBG.alignment;
            checksum = ahdr.ADBG.checksum;
            data = ahdr.data;
        }

        public ModifiedAsset(BinaryReader reader)
        {
            id = reader.ReadUInt32();
            name = Util.ReadString(reader);
            filename = Util.ReadString(reader);
            type = reader.ReadChars(4);
            layer = reader.ReadInt16();
            flags = reader.ReadInt32();
            alignment = reader.ReadInt32();
            checksum = reader.ReadInt32();
            int size = reader.ReadInt32();
            data = reader.ReadBytes(size);
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(id);
            Util.WriteString(writer, name);
            Util.WriteString(writer, filename);
            writer.Write(type);
            writer.Write(layer);
            writer.Write(flags);
            writer.Write(alignment);
            writer.Write(checksum);
            writer.Write(data.Length);
            writer.Write(data);
        }
    }

    public class DeletedAsset : PatchItem
    {
        public uint id;
        public string name;
        public short layer;

        public DeletedAsset(AddedAsset asset)
        {
            id = asset.id;
            name = asset.name;
            layer = asset.layer;
        }

        public DeletedAsset(Section_AHDR ahdr, HipSection[] hip)
        {
            id = ahdr.assetID;
            name = ahdr.ADBG.assetName;
            layer = (short)Util.AssetGetLayerIndex(ahdr, hip);
        }

        public DeletedAsset(BinaryReader reader)
        {
            id = reader.ReadUInt32();
            name = Util.ReadString(reader);
            layer = reader.ReadInt16();
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(id);
            Util.WriteString(writer, name);
            writer.Write(layer);
        }
    }

    public class HipFile : PatchItem
    {
        public string path;

        public List<AddedAsset> addedAssets;
        public List<ModifiedAsset> modifiedAssets;
        public List<DeletedAsset> deletedAssets;

        public HipFile(string path)
        {
            this.path = path;

            addedAssets = new List<AddedAsset>();
            modifiedAssets = new List<ModifiedAsset>();
            deletedAssets = new List<DeletedAsset>();
        }

        public HipFile(string path, List<AddedAsset> addedAssets, List<ModifiedAsset> modifiedAssets, List<DeletedAsset> deletedAssets)
        {
            this.path = path;

            this.addedAssets = new List<AddedAsset>(addedAssets);
            this.modifiedAssets = new List<ModifiedAsset>(modifiedAssets);
            this.deletedAssets = new List<DeletedAsset>(deletedAssets);
        }

        public HipFile(BinaryReader reader)
        {
            path = Util.ReadString(reader);

            addedAssets = new List<AddedAsset>();
            modifiedAssets = new List<ModifiedAsset>();
            deletedAssets = new List<DeletedAsset>();

            short numAddedAssets = reader.ReadInt16();
            for (int i = 0; i < numAddedAssets; i++)
            {
                addedAssets.Add(new AddedAsset(reader));
            }

            short numModifiedAssets = reader.ReadInt16();
            for (int i = 0; i < numModifiedAssets; i++)
            {
                modifiedAssets.Add(new ModifiedAsset(reader));
            }

            short numDeletedAssets = reader.ReadInt16();
            for (int i = 0; i < numDeletedAssets; i++)
            {
                deletedAssets.Add(new DeletedAsset(reader));
            }
        }

        public override void Write(BinaryWriter writer)
        {
            Util.WriteString(writer, path);

            writer.Write((short)addedAssets.Count);
            foreach (AddedAsset asset in addedAssets)
            {
                asset.Write(writer);
            }

            writer.Write((short)modifiedAssets.Count);
            foreach (ModifiedAsset asset in modifiedAssets)
            {
                asset.Write(writer);
            }

            writer.Write((short)deletedAssets.Count);
            foreach (DeletedAsset asset in deletedAssets)
            {
                asset.Write(writer);
            }
        }
    }

    public class Util
    {
        public static string ReadString(BinaryReader reader)
        {
            List<byte> bytes = new List<byte>();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                byte ch = reader.ReadByte();
                if (ch == 0) break;

                bytes.Add(ch);
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        public static void WriteString(BinaryWriter writer, string str)
        {
            writer.Write(Encoding.UTF8.GetBytes(str));
            writer.Write((byte)0);
        }

        public static bool FilesAreEqual(string path1, string path2)
        {
            if (new FileInfo(path1).Length != new FileInfo(path2).Length)
                return false;

            return ByteArraysAreEqual(File.ReadAllBytes(path1), File.ReadAllBytes(path2));
        }

        public static bool ByteArraysAreEqual(byte[] data1, byte[] data2)
        {
            if (data1.Length != data2.Length)
                return false;

            for (int i = 0; i < data1.Length; i++)
            {
                if (data1[i] != data2[i])
                    return false;
            }

            return true;
        }

        public static List<Section_AHDR> HipArrayToAHDRList(HipSection[] hip)
        {
            foreach (HipSection section in hip)
            {
                if (section is Section_DICT DICT)
                {
                    return DICT.ATOC.AHDRList;
                }
            }

            return null;
        }

        public static List<Section_LHDR> HipArrayToLHDRList(HipSection[] hip)
        {
            foreach (HipSection section in hip)
            {
                if (section is Section_DICT DICT)
                {
                    return DICT.LTOC.LHDRList;
                }
            }

            return null;
        }

        public static Dictionary<uint, Section_AHDR> AHDRListToAHDRDict(List<Section_AHDR> ahdrList)
        {
            Dictionary<uint, Section_AHDR> ahdrDict = new Dictionary<uint, Section_AHDR>();

            foreach (Section_AHDR ahdr in ahdrList)
            {
                ahdrDict.Add(ahdr.assetID, ahdr);
            }

            return ahdrDict;
        }

        public static Dictionary<uint, Section_AHDR> HipArrayToAHDRDict(HipSection[] hip)
        {
            return AHDRListToAHDRDict(HipArrayToAHDRList(hip));
        }

        public static Section_PACK HipArrayToPACK(HipSection[] hip)
        {
            foreach (HipSection section in hip)
            {
                if (section is Section_PACK PACK)
                {
                    return PACK;
                }
            }

            return null;
        }

        public static int AssetGetLayerIndex(Section_AHDR AHDR, HipSection[] hip)
        {
            foreach (HipSection section in hip)
            {
                if (section is Section_DICT DICT)
                {
                    List<Section_LHDR> LHDRList = DICT.LTOC.LHDRList;
                    for (int i = 0; i < LHDRList.Count; i++)
                    {
                        Section_LHDR LHDR = LHDRList[i];
                        if (LHDR.assetIDlist.Contains(AHDR.assetID))
                        {
                            return i;
                        }
                    }
                }
            }

            return -1;
        }

        public static bool FileIsHip(string path)
        {
            string ext = Path.GetExtension(path).ToLower();

            return (ext == ".hip" || ext == ".hop");
        }
    }
}

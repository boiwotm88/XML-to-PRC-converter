﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace paracobNET
{
    internal class Disassembler
    {
        string Filepath { get; set; }

        BinaryReader Reader { get; set; }
        ulong[] HashTable { get; set; }
        Dictionary<int, IOrderedEnumerable<KeyValuePair<int, int>>> RefEntries { get; set; }

        int HashTableSize { get; set; }
        int RefTableSize { get; set; }
        int HashStart { get { return 0x10; } }
        int RefStart { get { return 0x10 + HashTableSize; } }
        int ParamStart { get { return 0x10 + HashTableSize + RefTableSize; } }

        public Disassembler(string filepath)
        {
            Filepath = filepath;
            RefEntries = new Dictionary<int, IOrderedEnumerable<KeyValuePair<int, int>>>();
        }

        public ParamStruct Start()
        {
            using (Reader = new BinaryReader(File.OpenRead(Filepath)))
            {
                if (Reader.BaseStream.Length < ParamFile.Magic.Length)
                    throw new InvalidDataException("File size is too small");
                if (!IsValidMagic())
                {
                    //TODO: use events instead of exceptions to allow
                    //user app to determine what to do in weird cases
                    //(e.g. user manually replaces "paracobn" header)
                    throw new InvalidHeaderException("File contains an invalid header. Ensure that the file is a valid param file and that it is not compressed.");
                }

                HashTableSize = Reader.ReadInt32();
                RefTableSize = Reader.ReadInt32();
                HashTable = new ulong[HashTableSize / 8];

                SetHashTable();

                Reader.BaseStream.Seek(ParamStart, SeekOrigin.Begin);
                if ((ParamType)Reader.ReadByte() == ParamType.@struct)
                {
                    Reader.BaseStream.Position -= 1;
                    return Read() as ParamStruct;
                }
                else
                    throw new InvalidDataException("File does not have a root");
            }
        }

        private IParam Read()
        {
            byte key = Reader.ReadByte();
            if (!Enum.IsDefined(typeof(ParamType), key))
                throw new NotImplementedException($"Unimplemented param type '{key}' at {Reader.BaseStream.Position - 1}");
            ParamType type = (ParamType)key;

            IParam param;
            switch (type)
            {
                case ParamType.@struct:
                    {
                        int startPos = (int)Reader.BaseStream.Position - 1;
                        int size = Reader.ReadInt32();
                        int structRefOffset = Reader.ReadInt32();

                        var str = new ParamStruct(size);

                        IOrderedEnumerable<KeyValuePair<int, int>> hashOffsets;
                        if (RefEntries.TryGetValue(structRefOffset, out var refEntry))
                            hashOffsets = refEntry;
                        else
                        {
                            Reader.BaseStream.Position = structRefOffset + RefStart;
                            var tempHashOffsets = new List<KeyValuePair<int, int>>(size);
                            for (int i = 0; i < size; i++)
                            {
                                int hashIndex = Reader.ReadInt32();
                                int paramOffset = Reader.ReadInt32();
                                tempHashOffsets.Add(new KeyValuePair<int, int>(hashIndex, paramOffset));
                            }
                            hashOffsets = tempHashOffsets.OrderBy(a => a.Key);
                            RefEntries.Add(structRefOffset, hashOffsets);
                        }

                        foreach (var pair in hashOffsets)
                        {
                            Reader.BaseStream.Position = startPos + pair.Value;
                            ulong hash = HashTable[pair.Key];
                            IParam child = Read();
                            str.Nodes.Add(hash, child);
                        }

                        param = str;
                        break;
                    }
                case ParamType.list:
                    {
                        int startPos = (int)Reader.BaseStream.Position - 1;
                        int count = Reader.ReadInt32();
                        uint[] offsets = new uint[count];

                        var list = new ParamList(count);

                        //all elements should be the same type but it's not enforced

                        for (int i = 0; i < offsets.Length; i++)
                            offsets[i] = Reader.ReadUInt32();

                        for (int i = 0; i < count; i++)
                        {
                            Reader.BaseStream.Position = startPos + offsets[i];
                            list.Nodes.Add(Read());
                        }

                        param = list;
                        break;
                    }
                default:
                    {
                        var value = new ParamValue(type);
                        object v = null;

                        switch (type)
                        {
                            case ParamType.@bool:
                                v = Reader.ReadByte() != 0;
                                break;
                            case ParamType.@sbyte:
                                v = Reader.ReadSByte();
                                break;
                            case ParamType.@byte:
                                v = Reader.ReadByte();
                                break;
                            case ParamType.@short:
                                v = Reader.ReadInt16();
                                break;
                            case ParamType.@ushort:
                                v = Reader.ReadUInt16();
                                break;
                            case ParamType.@int:
                                v = Reader.ReadInt32();
                                break;
                            case ParamType.@uint:
                                v = Reader.ReadUInt32();
                                break;
                            case ParamType.@float:
                                v = Reader.ReadSingle();
                                break;
                            case ParamType.hash40:
                                v = HashTable[Reader.ReadUInt32()];
                                break;
                            case ParamType.@string:
                                long returnTo = Reader.BaseStream.Position;
                                Reader.BaseStream.Seek(RefStart + Reader.ReadInt32(), SeekOrigin.Begin);
                                string s = ""; char c;
                                while ((c = Reader.ReadChar()) != 0)
                                    s += c;
                                Reader.BaseStream.Seek(returnTo, SeekOrigin.Begin);
                                v = s;
                                break;
                        }

                        value.Value = v;
                        param = value;
                        break;
                    }
            }
            return param;
        }

        private bool IsValidMagic()
        {
            int len = ParamFile.Magic.Length;

            Reader.BaseStream.Position = 0;
            for (int i = 0; i < len; i++)
            {
                if (ParamFile.Magic[i] != Reader.ReadByte())
                    return false;
            }
            return true;
        }

        private void SetHashTable()
        {
            Reader.BaseStream.Position = HashStart;
            for (int i = 0; i < HashTable.Length; i++)
                HashTable[i] = Reader.ReadUInt64();
        }
    }
}

﻿using System;
using System.IO;
using System.Linq;
using UAssetAPI.StructureSerializers;

namespace UAssetAPI
{
    public class AssetWriter
    {
        public string path;
        public AssetReader data;

        private byte[] MakeHeader(BinaryReader reader)
        {
            reader.BaseStream.Seek(0, SeekOrigin.Begin);
            var stre = new MemoryStream(reader.ReadBytes(193));
            BinaryWriter writer = new BinaryWriter(stre);

            writer.Seek(24, SeekOrigin.Begin); // 24
            writer.Write(data.sectionSixOffset);

            writer.Seek(41, SeekOrigin.Begin); // 41
            writer.Write(data.sectionOneStringCount);

            writer.Seek(57, SeekOrigin.Begin); // 57
            writer.Write(data.dataCategoryCount);
            writer.Write(data.sectionThreeOffset); // 61
            writer.Write(data.sectionTwoLinkCount); // 65
            writer.Write(data.sectionTwoOffset); // 69 (haha funny)
            writer.Write(data.sectionFourOffset); // 73
            writer.Write(data.sectionFiveStringCount); // 77
            writer.Write(data.sectionFiveOffset); // 81

            writer.Seek(113, SeekOrigin.Begin); // 113
            writer.Write(data.dataCategoryCount);

            writer.Write(data.headerIndexList.Count); // 117

            writer.Seek(165, SeekOrigin.Begin); // 165
            writer.Write(data.sectionSixOffset - 4);

            writer.Seek(169, SeekOrigin.Begin); // 169
            writer.Write(data.fileSize - 4);

            return stre.ToArray();
        }

        public byte[] WriteData(BinaryReader reader)
        {
            var stre = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stre);

            // Header
            writer.Seek(0, SeekOrigin.Begin);
            writer.Write(MakeHeader(reader));

            // Section 1
            writer.Seek(193, SeekOrigin.Begin);
            data.sectionOneStringCount = data.headerIndexList.Count;
            for (int i = 0; i < data.headerIndexList.Count; i++)
            {
                writer.WriteUString(data.headerIndexList[i].Item1);
                writer.Write(data.headerIndexList[i].Item2);
            }

            // Section 2
            if (data.links.Count > 0)
            {
                data.sectionTwoOffset = (int)writer.BaseStream.Position;
                data.sectionTwoLinkCount = data.links.Count;
                for (int i = 0; i < data.links.Count; i++)
                {
                    writer.Write(data.links[i].bbase);
                    writer.Write(data.links[i].bclass);
                    writer.Write(data.links[i].link);
                    writer.Write(data.links[i].property);
                }
            }
            else
            {
                data.sectionTwoOffset = 0;
            }

            // Section 3
            if (data.categories.Count > 0)
            {
                data.sectionThreeOffset = (int)writer.BaseStream.Position;
                data.dataCategoryCount = data.categories.Count;
                for (int i = 0; i < data.categories.Count; i++)
                {
                    CategoryReference us = data.categories[i];
                    writer.Write(us.connection);
                    writer.Write(us.connect);
                    writer.Write(us.category);
                    writer.Write(us.link);
                    writer.Write(us.typeIndex);
                    writer.Write(us.garbage1);
                    writer.Write(us.type);
                    writer.Write(us.lengthV);
                    writer.Write(us.garbage2);
                    writer.Write(us.startV);
                    writer.Write(us.garbage3);
                }
            }
            else
            {
                data.sectionThreeOffset = 0;
            }

            // Section 4
            if (data.categoryIntReference.Count > 0)
            {
                data.sectionFourOffset = (int)writer.BaseStream.Position;
                for (int i = 0; i < data.categories.Count; i++)
                {
                    int[] currentData = data.categoryIntReference[i];
                    writer.Write(currentData.Length);
                    for (int j = 0; j < currentData.Length; j++)
                    {
                        writer.Write(currentData[j]);
                    }
                }
            }
            else
            {
                data.sectionFourOffset = 0;
            }

            // Section 5
            if (data.categoryStringReference.Count > 0)
            {
                data.sectionFiveOffset = (int)writer.BaseStream.Position;
                data.sectionFiveStringCount = data.categoryStringReference.Count;
                for (int i = 0; i < data.categoryStringReference.Count; i++)
                {
                    writer.WriteUString(data.categoryStringReference[i]);
                }
            }
            else
            {
                data.sectionFiveOffset = 0;
            }

            // Section 6
            writer.Write((int)0);
            int oldOffset = data.sectionSixOffset;
            data.sectionSixOffset = (int)writer.BaseStream.Position;
            int[] categoryStarts = new int[data.categoryData.Count];
            if (data.categoryData.Count > 0)
            {
                for (int i = 0; i < data.categoryData.Count; i++)
                {
                    categoryStarts[i] = (int)writer.BaseStream.Position;
                    Category us = data.categoryData[i];
                    if (us.IsRaw)
                    {
                        writer.Write(us.RawData);
                        continue;
                    }

                    for (int j = 0; j < us.Data.Count; j++)
                    {
                        PropertyData current = us.Data[j];
                        MainSerializer.Write(current, data, writer);
                    }
                    writer.Write((long)data.SearchHeaderReference("None"));
                    writer.Write(Enumerable.Repeat((byte)0, us.NumExtraZeros).ToArray());
                }
            }
            writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0xC1, 0x83, 0x2A, 0x9E });

            // Old Behavior
            /*reader.BaseStream.Seek(oldOffset, SeekOrigin.Begin);
            writer.Write(reader.ReadBytes((int)reader.BaseStream.Length - oldOffset));*/

            // Rewrite Section 3
            if (data.categories.Count > 0)
            {
                writer.Seek(data.sectionThreeOffset, SeekOrigin.Begin);
                for (int i = 0; i < data.categories.Count; i++)
                {
                    CategoryReference us = data.categories[i];
                    if (categoryStarts[i] == 0)
                    {
                        categoryStarts[i] = us.startV;
                    }
                    else
                    {
                        us.startV = categoryStarts[i];
                    }
                    writer.Write(us.connection);
                    writer.Write(us.connect);
                    writer.Write(us.category);
                    writer.Write(us.link);
                    writer.Write(us.typeIndex);
                    writer.Write(us.garbage1);
                    writer.Write(us.type);
                    writer.Write(us.lengthV); // !!!
                    writer.Write(us.garbage2);
                    writer.Write(categoryStarts[i]); // !!!
                    writer.Write(us.garbage3);
                }
            }

            // Rewrite Header
            data.fileSize = (int)stre.Length;
            writer.Seek(0, SeekOrigin.Begin);
            writer.Write(MakeHeader(reader));

            return stre.ToArray();
        }

        public void Write(string output)
        {
            byte[] newData;
            using (FileStream f = File.Open(path, FileMode.Open, FileAccess.Read))
            {
                f.Seek(0, SeekOrigin.Begin);
                newData = WriteData(new BinaryReader(f));
            }

            using (FileStream f = File.Open(output, FileMode.Create, FileAccess.Write))
            {
                f.Write(newData, 0, newData.Length);
            }
        }

        public AssetWriter(string input)
        {
            this.path = input;
            using (FileStream f = File.Open(path, FileMode.Open, FileAccess.Read))
            {
                data = new AssetReader(new BinaryReader(f));
            }
        }
    }
}
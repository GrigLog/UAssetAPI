﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UAssetAPI.StructureSerializers;

namespace UAssetAPI
{
    public class AssetReader
    {
        public string path;
        internal int sectionSixOffset;
        internal int sectionOneStringCount;
        internal int dataCategoryCount;
        internal int sectionThreeOffset;
        internal int sectionTwoLinkCount;
        internal int sectionTwoOffset;
        internal int sectionFourOffset;
        internal int sectionFiveStringCount;
        internal int sectionFiveOffset;
        internal int fileSize;

        public IList<Tuple<string, int>> headerIndexList; // string, GUID
        public IList<Link> links; // base, class, link, connection
        public IList<CategoryReference> categories; // connection, connect, category, link, typeIndex, type, length, start, garbage data
        public IList<int[]> categoryIntReference;
        public IList<string> categoryStringReference;
        public IList<Category> categoryData;

        private int[] understoodSectionSixTypes = new int[]
        {
            41,
            49,
            57
        };

        private void ReadHeader(BinaryReader reader)
        {
            reader.BaseStream.Seek(41, SeekOrigin.Begin); // 41
            sectionOneStringCount = reader.ReadInt32();

            Debug.Assert(reader.ReadInt32() == 193); // 45

            reader.BaseStream.Seek(57, SeekOrigin.Begin); // 57
            dataCategoryCount = reader.ReadInt32();
            sectionThreeOffset = reader.ReadInt32(); // 61
            sectionTwoLinkCount = reader.ReadInt32(); // 65
            sectionTwoOffset = reader.ReadInt32(); // 69 (haha funny)
            sectionFourOffset = reader.ReadInt32(); // 73
            sectionFiveStringCount = reader.ReadInt32(); // 77
            sectionFiveOffset = reader.ReadInt32(); // 81

            reader.BaseStream.Seek(113, SeekOrigin.Begin); // 113
            Debug.Assert(reader.ReadInt32() == dataCategoryCount);

            Debug.Assert(reader.ReadInt32() == sectionOneStringCount); // 117

            reader.BaseStream.Seek(165, SeekOrigin.Begin); // 165
            sectionSixOffset = reader.ReadInt32() + 4;

            reader.BaseStream.Seek(169, SeekOrigin.Begin); // 169
            fileSize = reader.ReadInt32() + 4;
        }

        private void Read(BinaryReader reader)
        {
            // Header
            byte[] header = reader.ReadBytes(185);
            ReadHeader(new BinaryReader(new MemoryStream(header)));

            // Section 1
            reader.BaseStream.Position += 8;

            headerIndexList = new List<Tuple<string, int>>();
            for (int i = 0; i < sectionOneStringCount; i++)
            {
                var str = reader.ReadUStringWithGUID(out int guid);
                headerIndexList.Add(Tuple.Create(str, guid));
            }

            // Section 2
            links = new List<Link>(); // base, class, link, connection
            if (sectionTwoOffset > 0)
            {
                reader.BaseStream.Seek(sectionTwoOffset, SeekOrigin.Begin);
                for (int i = 0; i < sectionTwoLinkCount; i++)
                {
                    long bbase = reader.ReadInt64();
                    long bclass = reader.ReadInt64();
                    int link = reader.ReadInt32();
                    long property = reader.ReadInt64();
                    links.Add(new Link(bbase, bclass, link, property));
                }
            }

            // Section 3
            categories = new List<CategoryReference>(); // connection, connect, category, link, typeIndex, type, length, start, garbage1, garbage2, garbage3
            if (sectionThreeOffset > 0)
            {
                reader.BaseStream.Seek(sectionThreeOffset, SeekOrigin.Begin);
                for (int i = 0; i < dataCategoryCount; i++)
                {
                    int connection = reader.ReadInt32();
                    int connect = reader.ReadInt32();
                    int category = reader.ReadInt32();
                    int link = reader.ReadInt32();
                    int typeIndex = reader.ReadInt32();
                    int garbage1 = reader.ReadInt32();
                    int type = reader.ReadInt32();
                    int lengthV = reader.ReadInt32(); // !!!
                    int garbage2 = reader.ReadInt32();
                    int startV = reader.ReadInt32(); // !!!

                    categories.Add(new CategoryReference(connection, connect, category, link, typeIndex, type, lengthV, startV, garbage1, garbage2, reader.ReadBytes(104 - (10 * 4))));
                }
            }

            // Section 4
            categoryIntReference = new List<int[]>();
            if (sectionFourOffset > 0)
            {
                reader.BaseStream.Seek(sectionFourOffset, SeekOrigin.Begin);
                for (int i = 0; i < dataCategoryCount; i++)
                {
                    int size = reader.ReadInt32();
                    int[] data = new int[size];
                    for (int j = 0; j < size; j++)
                    {
                        data[j] = reader.ReadInt32();
                    }
                    categoryIntReference.Add(data);
                }
            }

            // Section 5
            categoryStringReference = new List<string>();
            if (sectionFiveOffset > 0)
            {
                reader.BaseStream.Seek(sectionFiveOffset, SeekOrigin.Begin);
                for (int i = 0; i < sectionFiveStringCount; i++)
                {
                    categoryStringReference.Add(reader.ReadUString());
                }
            }

            // Section 6
            if (sectionSixOffset > 0)
            {
                categoryData = new List<Category>();
                for (int i = 0; i < categories.Count; i++)
                {
                    reader.BaseStream.Seek(categories[i].startV, SeekOrigin.Begin);
                    if (!understoodSectionSixTypes.Contains(categories[i].type))
                    {
                        categoryData.Add(new Category(reader.ReadBytes(categories[i].lengthV)));
                        continue;
                    }

                    Category us = new Category();

                    try
                    {
                        PropertyData data;
                        while ((data = MainSerializer.Read(this, reader)) != null)
                        {
                            us.Data.Add(data);
                        }

                        int nextStarting = (int)reader.BaseStream.Length - 8;
                        if ((categories.Count - 1) > i) nextStarting = categories[i + 1].startV;
                        us.NumExtraZeros = nextStarting - (int)reader.BaseStream.Position;
                        Debug.Assert(us.NumExtraZeros == 0 || us.NumExtraZeros == 4);
                    }
                    catch (FormatException)
                    {
                        //Console.WriteLine("Failed to parse category " + i + ": " + ex.ToString());
                        reader.BaseStream.Seek(categories[i].startV, SeekOrigin.Begin);
                        us = new Category(reader.ReadBytes(categories[i].lengthV));
                    }
                    finally
                    {
                        categoryData.Add(us);
                    }
                }
            }
        }

        public string GetHeaderReference(int index)
        {
            if (index <= 0) return Convert.ToString(-index);
            if (index > headerIndexList.Count) return Convert.ToString(index);
            return headerIndexList[index].Item1;
        }

        public int SearchHeaderReference(string search)
        {
            for (int i = 0; i < headerIndexList.Count; i++)
            {
                if (headerIndexList[i].Item1.Equals(search)) return i;
            }
            return -1;
        }

        public int GetLinkReference(int index)
        {
            return (int)(index < 0 ? links[Utils.IndexToUIndex(index)].property : -index);
        }

        public AssetReader(BinaryReader reader)
        {
            Read(reader);
        }

        public AssetReader(string path)
        {
            this.path = path;
            using (BinaryReader reader = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                Read(reader);
            }
        }
    }
}
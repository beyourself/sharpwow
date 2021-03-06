﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SlimDX.Direct3D9;
using SlimDX;

namespace SharpWoW.ADT.Wotlk
{
    public partial class ADTChunk : IADTChunk
    {
        public ADTChunk(ADTFile parent, Stormlib.MPQFile file, MCIN info)
        {
            mParent = parent;
            mFile = file;
            mInfo = info;
        }

        public bool ProcessSyncLoad()
        {
            return true;
        }

        public bool PreLoadChunk()
        {
            mFile.Position = mInfo.ofsMcnk;
            if (ReadSignature() != "MCNK")
                return false;

            uint size = mFile.Read<uint>();
            // +8 -> MCNK + size = 8 bytes
            if (size + 8 != mInfo.size)
                return false;

            mHeader = mFile.Read<MCNK>();
            var posY = (32.0f * Utils.Metrics.Tilesize - mHeader.position.X) - Utils.Metrics.MidPoint;
            var posX = (32.0f * Utils.Metrics.Tilesize - mHeader.position.Y) - Utils.Metrics.MidPoint;
            mHeader.position.X = posX;
            mHeader.position.Y = posY;

            mFile.Position = mInfo.ofsMcnk + mHeader.ofsHeight;
            if (ReadSignature() != "MCVT")
                return false;

            size = mFile.Read<uint>();
            float[] height = new float[145];
            uint counter = 0;
            Vector3 minPos = new Vector3(999999.9f);
            Vector3 maxPos = new Vector3(-999999.9f);
            minPos.X = mHeader.position.X;
            minPos.Y = mHeader.position.Y;
            maxPos.X = minPos.X + Utils.Metrics.Chunksize;
            maxPos.Y = minPos.Y + Utils.Metrics.Chunksize;
            for (int i = 0; i < 17; ++i)
            {
                for (int j = 0; j < (((i % 2) != 0) ? 8 : 9); ++j)
                {
                    float x, y, z;
                    z = mFile.Read<float>() + mHeader.position.Z; 
                    y = i * Utils.Metrics.Unitsize * 0.5f + mHeader.position.Y;
                    x = j * Utils.Metrics.Unitsize + mHeader.position.X;

                    if ((i % 2) != 0)
                        x += 0.5f * Utils.Metrics.Unitsize;

                    if (z < minPos.Z)
                        minPos.Z = z;
                    if (z > maxPos.Z)
                        maxPos.Z = z;

                    vertices[counter] = new ADTVertex()
                    {
                        X = x,
                        Y = y,
                        Z = z,
                        U = ADTStaticData.TexCoords[counter, 0],
                        V = ADTStaticData.TexCoords[counter, 1],
                        S = ADTStaticData.AlphaCoords[counter, 0],
                        T = ADTStaticData.AlphaCoords[counter, 1]
                    };

                    ++counter;
                }
            }

            mBox = new BoundingBox(minPos, maxPos);
            MinPosition = minPos;
            MaxPosition = maxPos;

            mFile.Position = mInfo.ofsMcnk + mHeader.ofsLayer;
            if (ReadSignature() != "MCLY")
                return false;

            mFile.Read<uint>();

            for (int i = 0; i < mHeader.nLayers; ++i)
            {
                var layer = mFile.Read<MCLY>();
                mLayers.Add(layer);
                if ((layer.flags & 0x40) != 0)
                    mTextureFlags[i] = 1;
            }

            LoadAlphaData();
            if (!LoadNormals())
                return false;

            LoadShadows();

            mFile.Position = mInfo.ofsMcnk + mHeader.ofsRefs + 0x08;
            for (uint i = 0; i < mHeader.nDoodadRefs; ++i)
                mRefs.Add(mFile.Read<uint>());
            for (uint i = 0; i < mHeader.nMapObjRefs; ++i)
                mWmoRefs.Add(mFile.Read<uint>());

            LoadLiquids();

            return true;
        }

        private bool LoadNormals()
        {
            mFile.Position = mInfo.ofsMcnk + mHeader.ofsNormal;
            if (ReadSignature() != "MCNR")
                return false;

            uint size = mFile.Read<uint>();
            uint counter = 0;
            for (uint i = 0; i < 17; ++i)
            {
                for (uint j = 0; j < (((i % 2) != 0) ? 8u : 9u); ++j)
                {
                    float nx = -((float)(mFile.Read<sbyte>()) / 127.0f);
                    float ny = -((float)(mFile.Read<sbyte>()) / 127.0f);
                    float nz = (float)(mFile.Read<sbyte>()) / 127.0f;
                    vertices[counter].NX = nx;
                    vertices[counter].NY = ny;
                    vertices[counter++].NZ = nz;
                }
            }

            return true;
        }

        private bool LoadShadows()
        {
            mFile.Position = mInfo.ofsMcnk + mHeader.ofsShadow;
            if (ReadSignature() != "MCSH")
                return false;

            uint size = mFile.Read<uint>();
            uint curPtr = 0;
            for (int i = 0; i < 64; ++i)
            {
                for (int j = 0; j < 8; ++j)
                {
                    byte mask = mFile.Read<byte>();
                    for (int k = 0; k < 8; ++k)
                        ShadowData[curPtr++] = ((mask & (1 << k)) == 0) ? (byte)0xFF : (byte)0xCC;
                }
            }

            return true;
        }

        private bool LoadAlphaData()
        {
            for (int i = 0; i < 64; ++i)
            {
                for (int j = 0; j < 64; ++j)
                {
                    float x = i * ADTStaticData.HoleSize;
                    float y = j * ADTStaticData.HoleSize;
                    uint stepx = (uint)Math.Floor(x / ADTStaticData.HoleLen);
                    uint stepy = (uint)Math.Floor(y / ADTStaticData.HoleLen);

                    byte factor = (byte)((mHeader.holes & (ADTStaticData.HoleBitmap[stepx, stepy])) != 0 ? 0 : 1);
                    //AlphaFloats[i * 64 + j, 3] = 0xFF * factor;
                    AlphaData[(i * 64 + j) * 4 + 3] = (byte)(0xFF * factor);
                }
            }

            mFile.Position = mInfo.ofsMcnk + mHeader.ofsAlpha + 8;
            for (int i = 1; i < mHeader.nLayers; ++i)
            {
                //mFile.Position = mInfo.ofsMcnk + mHeader.ofsAlpha + 8 + mLayers[i].offsetMCAL;

                if ((mLayers[i].flags & 0x200) != 0)
                {
                    loadAlphaSelfCompress(i);
                    continue;
                }
                
                byte[] fileData = mFile.Read(2048);

                uint bufferPtr = 0;
                uint mapPtr = 0;
                for (int j = 0; j < 63; j++)
                {
                    for (int k = 0; k < 32; k++)
                    {

                        float x = i * ADTStaticData.HoleSize * 2;
                        float y = j * ADTStaticData.HoleSize;
                        uint stepx = (uint)Math.Floor(x / ADTStaticData.HoleLen);
                        uint stepy = (uint)Math.Floor(y / ADTStaticData.HoleLen);

                        byte factor = (byte)((mHeader.holes & (ADTStaticData.HoleBitmap[stepx, stepy])) != 0 ? 0 : 1);
                        x += ADTStaticData.HoleSize;
                        stepx = (uint)Math.Floor(x / ADTStaticData.HoleLen);
                        byte factor2 = (byte)((mHeader.holes & (ADTStaticData.HoleBitmap[stepx, stepy])) != 0 ? 0 : 1);

                        AlphaData[mapPtr * 4 + i - 1] = (byte)((((255 * ((int)(fileData[bufferPtr] & 0x0F))) / (float)0x0F)) * factor);
                        AlphaFloats[mapPtr, i - 1] = (ushort)((AlphaData[mapPtr * 4 + i - 1] / 255.0f) * 65535.0f);
                        ++mapPtr;
                        AlphaData[mapPtr * 4 + i - 1] = (byte)((((255 * ((int)(fileData[bufferPtr] & (k != 31 ? 0xF0 : 0x0F)))) / (k != 31 ? 0xF0 : 0x0F))) * factor2);
                        AlphaFloats[mapPtr, i - 1] = (ushort)((AlphaData[mapPtr * 4 + i - 1] / 255.0f) * 65535.0f);
                        ++mapPtr;
                        ++bufferPtr;
                    }
                }

                for (uint j = 0; j < 256; ++j)
                {
                    AlphaData[(63 * 256) + j] = AlphaData[(62 * 256) + j];
                }
            }

            return true;
        }

        private unsafe void loadAlphaSelfCompress(int layer)
        {
            var handle = mFile.GetPointer();
            byte* p = (byte*)handle.AddrOfPinnedObject().ToPointer();
            p += mFile.Position;
            uint counterIn = 0;
            uint counterOut = 0;
            while (counterOut < 4096)
            {
                byte curByte = p[counterIn];
                bool fill = (curByte & 0x80) != 0;
                uint n = (uint)(curByte & 0x7F);
                ++counterIn;

                for (uint k = 0; k < n; ++k)
                {
                    AlphaData[counterOut * 4 + layer - 1] = p[counterIn];
                    AlphaFloats[counterOut, layer - 1] = p[counterIn];
                    ++counterOut;
                    if (!fill)
                        ++counterIn;
                }
                if (fill)
                    ++counterIn;
            }
            handle.Free();
        }

        ///////////////////////////////////TODO//////////////////////////////////////
        //Cleanup and implement the rendering stuff
        //implement MH2O in ADTFile (this is pretty messy imho but it's Blizzards fault ~Night)
        struct mclqVertex
        {
            public short Unk1, Unk2;
            public float Height;
        }

        private void LoadMCLQ()
        {
            mFile.Position = mInfo.ofsMcnk + mHeader.ofsLiquid;
            var sig = ReadSignature();
            if (sig != "MCLQ")
                return;

            int n = mFile.Read<int>();

            float min = mFile.ReadFloat();
            float max = mFile.ReadFloat(); //or the other way around? max/min? y u no more accurate wowdev?

            mclqVertex[] vertices = new mclqVertex[9 * 9];

            vertices[0].Unk1 = vertices[0].Unk2 = 0; //le stfu vs! remove me
            vertices[0].Height = 0f; //le stfu vs! remove me

            mFile.Read(vertices);

            byte[] flags = new byte[8 * 8];
            mFile.Read(flags);

            byte[] junk = mFile.Read(0x54); //is this really just junk?
        }

        private void LoadLiquids()
        {
            if (mHeader.sizeLiqud > 8)
                LoadMCLQ();
        }
        /////////////////////////////////////////////////////////////////////////////

        public void Unload()
        {
            if (mMesh != null || mAlphaTexture != null)
            {
                Game.GameManager.GraphicsThread.CallOnThread(
                    () =>
                    {
                        if (mAlphaTexture != null)
                            ADTAlphaHandler.AddFreeTexture(mAlphaTexture);

                        if (mShadowTexture != null)
                            ADTAlphaHandler.AddFreeShadowTexture(mShadowTexture);

                        if (mMesh != null)
                        {
                            mMesh.Dispose();
                            mMesh = null;
                        }
                    },
                    true
                );
            }

            foreach (var inst in mDoodadInstances)
            {
                foreach (var id in inst.Value)
                    Game.GameManager.M2ModelManager.RemoveInstance(inst.Key, id);
            }

            foreach (var id in mConfirmedUniqueID)
            {
                var name = mParent.DoodadNames[mParent.ModelIdentifiers[(int)mParent.ModelDefinitions[(int)id].idMMID]];
                ADTManager.RemoveUniqueMdxId(name, mParent.ModelDefinitions[(int)id].uniqueId);
            }

            mParent = null;
            mFile = null;
            mLayers.Clear();
            mLayers = null;
            AlphaData = null;
            AlphaFloats = null;
        }

        public override string getLayerTexture(MCLY ly)
        {
            return mParent.getTextureName((int)ly.textureId);
        }

        public void Render(SlimDX.Matrix preTransform)
        {
            if (Game.GameManager.GraphicsThread.GraphicsManager.Camera.ViewFrustum.Contains(mBox, Matrix.Identity) == ContainmentType.Disjoint)
                return;

            if (mHeader.nLayers == 0)
                return;

            if (mMesh == null)
                LoadMesh();

            if (mAlphaTexture == null)
                LoadAlphaTexture();

            if (mShadowTexture == null)
                LoadShadowTexture();

            if (mAlphaDirty)
            {
                LoadAlphaTexture();
                mAlphaDirty = false;
            }

            foreach (var re in mRefs)
            {
                try
                {
                    var name = mParent.DoodadNames[mParent.ModelIdentifiers[(int)mParent.ModelDefinitions[(int)re].idMMID]];
                    var uniqueID = mParent.ModelDefinitions[(int)re].uniqueId;

                    if (ADTManager.AddUniqueMDXId(name, uniqueID) == false)
                        continue;

                    mConfirmedUniqueID.Add(re);
                    var id = Game.GameManager.M2ModelManager.AddInstance(name, mParent.ModelDefinitions[(int)re]);
                    if (!mDoodadInstances.ContainsKey(name))
                        mDoodadInstances.Add(name, new List<uint>());
                    mDoodadInstances[name].Add(id);
                }
                catch (Exception)
                {
                }
            }

            foreach (var re in mWmoRefs)
            {
                try
                {
                    var modf = mParent.WMODefinitions[(int)re];
                    var name = mParent.WMONames[mParent.WMOIdentifiers[(int)modf.idMWID]];
                    
                    var id = Models.WMO.WMOManager.AddInstance(name, mParent.WMODefinitions[(int)re].Position, modf.uniqueId, mParent.WMODefinitions[(int)re].Rotation);
                }
                catch (Exception)
                { 
                }
            }

            mRefs.Clear();
            mWmoRefs.Clear();

            var shdr = Video.ShaderCollection.TerrainShader;
            shdr.SetTechnique(mHeader.nLayers - 1);
            shdr.SetTexture("alphaTexture", mAlphaTexture);
            shdr.SetTexture("shadowTexture", mShadowTexture);
            Matrix matChunk = preTransform * Matrix.Identity;

            shdr.SetValue("matrixViewProj", (matChunk * Game.GameManager.GraphicsThread.GraphicsManager.Camera.ViewProj));
            shdr.SetValue("matrixWorld", matChunk);
            for(int i = 0; i < 4; ++i)
                shdr.SetValue("TextureFlags" + i, mTextureFlags[i]);
            for (int i = 0; i < mLayers.Count; ++i)
                shdr.SetTexture("blendTexture" + i, mParent.GetTexture((int)mLayers[i].textureId));

            shdr.DoRender((SlimDX.Direct3D9.Device d) =>
                {
                    mMesh.DrawSubset(0);
                }
            );

            ADTManager.VisibleChunks.Add(this);
        }

        private void LoadMesh()
        {
            mMesh = new Mesh(Game.GameManager.GraphicsThread.GraphicsManager.Device,
                256, 145, MeshFlags.Managed, ADTVertex.FVF);

            var vb = mMesh.LockVertexBuffer(LockFlags.None);
            vb.WriteRange(vertices);
            mMesh.UnlockVertexBuffer();

            var ib = mMesh.LockIndexBuffer(LockFlags.None);
            ib.WriteRange(ADTStaticData.Indices);
            mMesh.UnlockIndexBuffer();
        }

        private void LoadAlphaTexture()
        {
            if (mAlphaTexture == null)
                mAlphaTexture = ADTAlphaHandler.FreeTexture();
            if (mAlphaTexture == null)
                mAlphaTexture = new SlimDX.Direct3D9.Texture(Game.GameManager.GraphicsThread.GraphicsManager.Device, 64, 64, 1, Usage.None, Format.A8R8G8B8, Pool.Managed);
            Surface baseSurf = mAlphaTexture.GetSurfaceLevel(0);
            System.Drawing.Rectangle rec = System.Drawing.Rectangle.FromLTRB(0, 0, 64, 64);
            Surface.FromMemory(baseSurf, AlphaData, Filter.Default, 0, Format.A8R8G8B8, 4 * 64, rec);
            baseSurf.Dispose();
        }

        private void LoadShadowTexture()
        {
            if (mShadowTexture == null)
                mShadowTexture = ADTAlphaHandler.FreeShadowTexture();
            if (mShadowTexture == null)
                mShadowTexture = new Texture(Game.GameManager.GraphicsThread.GraphicsManager.Device, 64, 64, 1, Usage.None, Format.A8, Pool.Managed);
            
            Surface baseSurf = mShadowTexture.GetSurfaceLevel(0);
            System.Drawing.Rectangle rec = System.Drawing.Rectangle.FromLTRB(0, 0, 64, 64);
            Surface.FromMemory(baseSurf, ShadowData, Filter.None, 0, Format.A8, 64, rec);
            baseSurf.Dispose();
        }

        private void RecalcNormals()
        {
            for(uint i = 0; i < 145; ++i)
            {
                Vector3 N1, N2, N3, N4;
                Vector3 P1, P2, P3, P4;

                P1.X = vertices[i].X - Utils.Metrics.Unitsize * 0.5f;
                P1.Y = vertices[i].Y - Utils.Metrics.Unitsize * 0.5f;
                P1.Z = vertices[i].Z;
                Game.GameManager.WorldManager.GetLandHeightFast(P1.X, P1.Y, ref P1.Z);

                P2.X = vertices[i].X + Utils.Metrics.Unitsize * 0.5f;
                P2.Y = vertices[i].Y - Utils.Metrics.Unitsize * 0.5f;
                P2.Z = vertices[i].Z;
                Game.GameManager.WorldManager.GetLandHeightFast(P2.X, P2.Y, ref P2.Z);

                P3.X = vertices[i].X + Utils.Metrics.Unitsize * 0.5f;
                P3.Y = vertices[i].Y + Utils.Metrics.Unitsize * 0.5f;
                P3.Z = vertices[i].Z;
                Game.GameManager.WorldManager.GetLandHeightFast(P3.X, P3.Y, ref P3.Z);

                P4.X = vertices[i].X - Utils.Metrics.Unitsize * 0.5f;
                P4.Y = vertices[i].Y + Utils.Metrics.Unitsize * 0.5f;
                P4.Z = vertices[i].Z;
                Game.GameManager.WorldManager.GetLandHeightFast(P4.X, P4.Y, ref P4.Z);

                Vector3 vert = new Vector3(vertices[i].X, vertices[i].Y, vertices[i].Z);

                N1 = Vector3.Cross((P2 - vert), (P1 - vert));
                N2 = Vector3.Cross((P3 - vert), (P2 - vert));
                N3 = Vector3.Cross((P4 - vert), (P3 - vert));
                N4 = Vector3.Cross((P1 - vert), (P4 - vert));

                var Norm = N1 + N2 + N3 + N4;
                Norm.Normalize();
                Norm *= -1;

                vertices[i].NX = Norm.X;
                vertices[i].NY = Norm.Y;
                vertices[i].NZ = Norm.Z;
            }
        }

        public override MCLY getLayer(int index)
        {
            return mLayers[index];
        }

        public Vector3 MinPosition { get; private set; }
        public Vector3 MaxPosition { get; private set; }

        private ADTFile mParent;
        private Stormlib.MPQFile mFile;
        private MCIN mInfo;
        private Mesh mMesh;
        private List<MCLY> mLayers = new List<MCLY>();
        byte[] ShadowData = new byte[4096];
        private byte[] AlphaData = new byte[4096 * 4];
        private ushort[,] AlphaFloats = new ushort[4096, 3];
        private ADTVertex[] vertices = new ADTVertex[145];
        private BoundingBox mBox;
        private Texture mAlphaTexture = null;
        private Texture mShadowTexture = null;
        private int[] mTextureFlags = new int[4] { 0, 0, 0, 0 };
        private List<uint> mRefs = new List<uint>();
        private List<uint> mWmoRefs = new List<uint>();
        private Dictionary<string, List<uint>> mDoodadInstances = new Dictionary<string, List<uint>>();
        private bool mAlphaDirty = false;
        private List<uint> mConfirmedUniqueID = new List<uint>();

        private string ReadSignature()
        {
            byte[] bytes = mFile.Read(4);
            bytes = bytes.Reverse().ToArray();
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

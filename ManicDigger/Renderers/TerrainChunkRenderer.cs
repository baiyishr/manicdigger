﻿using System;
using System.Collections.Generic;
using System.Text;
using OpenTK;
using System.Drawing;
using ManicDigger.Collisions;

namespace ManicDigger
{
    public class TerrainChunkRenderer
    {
        [Inject]
        public ITerrainInfo mapstorage;
        [Inject]
        public IGameData data;
        [Inject]
        public IBlockDrawerTorch blockdrawertorch;
        [Inject]
        public Config3d config3d;
        [Inject]
        public ITerrainRenderer terrainrenderer; //textures
        [Inject]
        public IShadows shadows;
        RailMapUtil railmaputil;
        public bool DONOTDRAWEDGES = true;
        public int chunksize = 16; //16x16
        public int texturesPacked = 16;
        public float BlockShadow = 0.6f;
        public bool ENABLE_ATLAS1D = true;
        int maxblocktypes = 256;
        byte[] currentChunk;
        bool started = false;
        int mapsizex; //cache
        int mapsizey;
        int mapsizez;
        void Start()
        {
            currentChunk = new byte[(chunksize + 2) * (chunksize + 2) * (chunksize + 2)];
            currentChunkShadows = new byte[chunksize + 2, chunksize + 2, chunksize + 2];
            currentChunkDraw = new byte[chunksize, chunksize, chunksize];
            currentChunkDrawCount = new byte[chunksize, chunksize, chunksize, 6];
            mapsizex = mapstorage.MapSizeX;
            mapsizey = mapstorage.MapSizeY;
            mapsizez = mapstorage.MapSizeZ;
            started = true;
            istransparent = data.IsTransparent;
            iswater = data.IsWater;
            isvalid = data.IsValid;
            maxlight = shadows.maxlight;
            maxlightInverse = 1f / maxlight;
        }
        int maxlight;
        float maxlightInverse;
        bool[] istransparent;
        bool[] iswater;
        bool[] isvalid;
        public IEnumerable<VerticesIndicesToLoad> MakeChunk(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0) { yield break; }
            if (!started) { Start(); }
            if (x >= mapsizex / chunksize
                || y >= mapsizey / chunksize
                || z >= mapsizez / chunksize) { yield break; }
            if (ENABLE_ATLAS1D)
            {
                toreturnatlas1d = new VerticesIndices[maxblocktypes / terrainrenderer.terrainTexturesPerAtlas];
                toreturnatlas1dtransparent = new VerticesIndices[maxblocktypes / terrainrenderer.terrainTexturesPerAtlas];
                for (int i = 0; i < toreturnatlas1d.Length; i++)
                {
                    toreturnatlas1d[i] = new VerticesIndices();
                    toreturnatlas1dtransparent[i] = new VerticesIndices();
                }
            }
            //else //torch block
            {
                toreturnmain = new VerticesIndices();
                toreturntransparent = new VerticesIndices();
            }
            GetExtendedChunk(x, y, z);
            if (IsSolidChunk(currentChunk)) { yield break; }
            ResetCurrentShadows();
            shadows.OnMakeChunk(x, y, z);
            CalculateVisibleFaces(currentChunk);
            CalculateTilingCount(currentChunk, x * chunksize, y * chunksize, z * chunksize);
            CalculateBlockPolygons(x, y, z);
            foreach (VerticesIndicesToLoad v in GetFinalVerticesIndices(x, y, z))
            {
                yield return v;
            }
        }
        IEnumerable<VerticesIndicesToLoad> GetFinalVerticesIndices(int x, int y, int z)
        {
            if (ENABLE_ATLAS1D)
            {
                for (int i = 0; i < toreturnatlas1d.Length; i++)
                {
                    if (toreturnatlas1d[i].indices.Count > 0)
                    {
                        yield return new VerticesIndicesToLoad()
                        {
                            indices = toreturnatlas1d[i].indices.ToArray(),
                            vertices = toreturnatlas1d[i].vertices.ToArray(),
                            position =
                                new Vector3(x * chunksize, y * chunksize, z * chunksize),
                            texture = terrainrenderer.terrainTextures1d[i % terrainrenderer.terrainTexturesPerAtlas],
                        };
                    }
                }
                for (int i = 0; i < toreturnatlas1dtransparent.Length; i++)
                {
                    if (toreturnatlas1dtransparent[i].indices.Count > 0)
                    {
                        yield return new VerticesIndicesToLoad()
                        {
                            indices = toreturnatlas1dtransparent[i].indices.ToArray(),
                            vertices = toreturnatlas1dtransparent[i].vertices.ToArray(),
                            position =
                                new Vector3(x * chunksize, y * chunksize, z * chunksize),
                            texture = terrainrenderer.terrainTextures1d[i % terrainrenderer.terrainTexturesPerAtlas],
                            transparent = true,
                        };
                    }
                }
            }
            //else //torch block
            {
                if (toreturnmain.indices.Count > 0)
                {
                    yield return new VerticesIndicesToLoad()
                    {
                        indices = toreturnmain.indices.ToArray(),
                        vertices = toreturnmain.vertices.ToArray(),
                        position =
                            new Vector3(x * chunksize, y * chunksize, z * chunksize),
                        texture = terrainrenderer.terrainTexture,
                    };
                }
                if (toreturntransparent.indices.Count > 0)
                {
                    yield return new VerticesIndicesToLoad()
                    {
                        indices = toreturntransparent.indices.ToArray(),
                        vertices = toreturntransparent.vertices.ToArray(),
                        position =
                            new Vector3(x * chunksize, y * chunksize, z * chunksize),
                        transparent = true,
                        texture = terrainrenderer.terrainTexture,
                    };
                }
            }
        }
        private void CalculateBlockPolygons(int x, int y, int z)
        {
            for (int xx = 0; xx < chunksize; xx++)
            {
                for (int yy = 0; yy < chunksize; yy++)
                {
                    for (int zz = 0; zz < chunksize; zz++)
                    {
                        int xxx = x * chunksize + xx;
                        int yyy = y * chunksize + yy;
                        int zzz = z * chunksize + zz;
                        if (currentChunkDraw[xx, yy, zz] != 0)
                        {
                            BlockPolygons(xxx, yyy, zzz, currentChunk);
                        }
                    }
                }
            }
        }
        private void ResetCurrentShadows()
        {
            for (int xx = 0; xx < chunksize + 2; xx++)
            {
                for (int yy = 0; yy < chunksize + 2; yy++)
                {
                    for (int zz = 0; zz < chunksize + 2; zz++)
                    {
                        currentChunkShadows[xx, yy, zz] = byte.MaxValue;
                    }
                }
            }
        }
        private bool IsSolidChunk(byte[] currentChunk)
        {
            int block = currentChunk[0];
            for (int i = 0; i < currentChunk.Length; i++)
            {
                if (currentChunk[i] != currentChunk[0])
                {
                    return false;
                }
            }
            return true;
        }
        private void GetExtendedChunk(int x, int y, int z)
        {
            int chunksize = this.chunksize;
            byte[] mapchunk = mapstorage.GetChunk(x * chunksize, y * chunksize, z * chunksize);
            int mainpos = 1 + (1 + chunksize + 2) + (1 + (chunksize + 2) * (chunksize + 2));//(1,1,1)
            //for (int i = 0; i < chunksize * chunksize * chunksize; i++)
            //{
            //    currentChunk[i + mainpos] = mapchunk[i];
            //}
            for (int xx = 1; xx < chunksize + 1; xx++)
            {
                for (int yy = 1; yy < chunksize + 1; yy++)
                {
                    for (int zz = 1; zz < chunksize + 1; zz++)
                    {
                        currentChunk[MapUtil.Index(xx, yy, zz, chunksize + 2, chunksize + 2)]
                            = mapchunk[MapUtil.Index(xx - 1, yy - 1, zz - 1, chunksize, chunksize)];
                    }
                }
            }
            //copy borders of this chunk

            //z-1
            if (z > 0)
            {
                byte[] mapchunk_ = mapstorage.GetChunk(x * chunksize, y * chunksize, (z - 1) * chunksize);
                for (int xx = 1; xx < chunksize + 1; xx++)
                {
                    for (int yy = 1; yy < chunksize + 1; yy++)
                    {
                        currentChunk[MapUtil.Index(xx, yy, 0, chunksize + 2, chunksize + 2)]
                            = mapchunk_[MapUtil.Index(xx - 1, yy - 1, chunksize - 1, chunksize, chunksize)];
                    }
                }
            }
            //z+1
            if ((z + 1) < mapsizez / chunksize)
            {
                byte[] mapchunk_ = mapstorage.GetChunk(x * chunksize, y * chunksize, (z + 1) * chunksize);
                for (int xx = 1; xx < chunksize + 1; xx++)
                {
                    for (int yy = 1; yy < chunksize + 1; yy++)
                    {
                        currentChunk[MapUtil.Index(xx, yy, chunksize + 1, chunksize + 2, chunksize + 2)]
                            = mapchunk_[MapUtil.Index(xx - 1, yy - 1, 0, chunksize, chunksize)];
                    }
                }
            }
            //x - 1
            if (x > 0)
            {
                byte[] mapchunk_ = mapstorage.GetChunk((x - 1) * chunksize, y * chunksize, z * chunksize);
                for (int zz = 1; zz < chunksize + 1; zz++)
                {
                    for (int yy = 1; yy < chunksize + 1; yy++)
                    {
                        currentChunk[MapUtil.Index(0, yy, zz, chunksize + 2, chunksize + 2)]
                            = mapchunk_[MapUtil.Index(chunksize - 1, yy - 1, zz - 1, chunksize, chunksize)];
                    }
                }
            }
            //x + 1
            if ((x + 1) < mapsizex / chunksize)
            {
                byte[] mapchunk_ = mapstorage.GetChunk((x + 1) * chunksize, y * chunksize, z * chunksize);
                for (int zz = 1; zz < chunksize + 1; zz++)
                {
                    for (int yy = 1; yy < chunksize + 1; yy++)
                    {
                        currentChunk[MapUtil.Index(chunksize + 1, yy, zz, chunksize + 2, chunksize + 2)]
                            = mapchunk_[MapUtil.Index(0, yy - 1, zz - 1, chunksize, chunksize)];
                    }
                }
            }
            //y - 1
            if (y > 0)
            {
                byte[] mapchunk_ = mapstorage.GetChunk(x * chunksize, (y - 1) * chunksize, z * chunksize);
                for (int xx = 1; xx < chunksize + 1; xx++)
                {
                    for (int zz = 1; zz < chunksize + 1; zz++)
                    {
                        currentChunk[MapUtil.Index(xx, 0, zz, chunksize + 2, chunksize + 2)]
                            = mapchunk_[MapUtil.Index(xx - 1, chunksize - 1, zz - 1, chunksize, chunksize)];
                    }
                }
            }
            //y + 1
            if (y > 0)
            {
                byte[] mapchunk_ = mapstorage.GetChunk(x * chunksize, (y + 1) * chunksize, z * chunksize);
                for (int xx = 1; xx < chunksize + 1; xx++)
                {
                    for (int zz = 1; zz < chunksize + 1; zz++)
                    {
                        currentChunk[MapUtil.Index(xx, chunksize + 1, zz, chunksize + 2, chunksize + 2)]
                            = mapchunk_[MapUtil.Index(xx - 1, 0, zz - 1, chunksize, chunksize)];
                    }
                }
            }
        }
        VerticesIndices toreturnmain;
        VerticesIndices toreturntransparent;
        VerticesIndices[] toreturnatlas1d;
        VerticesIndices[] toreturnatlas1dtransparent;
        class VerticesIndices
        {
            public List<ushort> indices = new List<ushort>();
            public List<VertexPositionTexture> vertices = new List<VertexPositionTexture>();
        }
        private bool IsValidPos(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0)
            {
                return false;
            }
            if (x >= mapsizex || y >= mapsizey || z >= mapsizez)
            {
                return false;
            }
            return true;
        }
        byte[, ,] currentChunkShadows;
        byte[, ,] currentChunkDraw;
        byte[, , ,] currentChunkDrawCount;
        byte[] currentChunk1d;
        void CalculateVisibleFaces(byte[] currentChunk)
        {
            int chunksize = this.chunksize;
            currentChunk1d = currentChunk;
            for (int xx = 1; xx < chunksize + 1; xx++)
            {
                for (int yy = 1; yy < chunksize + 1; yy++)
                {
                    for (int zz = 1; zz < chunksize + 1; zz++)
                    {
                        int pos = MapUtil.Index(xx, yy, zz, chunksize + 2, chunksize + 2);
                        byte tt = currentChunk[pos];
                        if (tt == 0) { continue; }
                        int draw = (int)TileSideFlags.None;
                        //z+1
                        {
                            int pos2 = pos + (chunksize + 2) * (chunksize + 2);
                            byte tt2 = currentChunk1d[pos2];
                            if (tt2 == 0
                                || (iswater[tt2] && (!iswater[tt]))
                                || istransparent[tt2])
                            {
                                draw |= (int)TileSideFlags.Top;
                            }
                        }
                        //z-1
                        {
                            int pos2 = pos - (chunksize + 2) * (chunksize + 2);
                            byte tt2 = currentChunk1d[pos2];
                            if (tt2 == 0
                                || (iswater[tt2] && (!iswater[tt]))
                                || istransparent[tt2])
                            {
                                draw |= (int)TileSideFlags.Bottom;
                            }
                        }
                        //x-1
                        {
                            int pos2 = pos - 1;
                            byte tt2 = currentChunk1d[pos2];
                            if (tt2 == 0
                                || (iswater[tt2] && (!iswater[tt]))
                                || istransparent[tt2])
                            {
                                draw |= (int)TileSideFlags.Front;
                            }
                        }
                        //x+1
                        {
                            int pos2 = pos + 1;
                            byte tt2 = currentChunk1d[pos2];
                            if (tt2 == 0
                                || (iswater[tt2] && (!iswater[tt]))
                                || istransparent[tt2])
                            {
                                draw |= (int)TileSideFlags.Back;
                            }
                        }
                        //y-1
                        {
                            int pos2 = pos - (chunksize + 2);
                            byte tt2 = currentChunk1d[pos2];
                            if (tt2 == 0
                                || (iswater[tt2] && (!iswater[tt]))
                                || istransparent[tt2])
                            {
                                draw |= (int)TileSideFlags.Left;
                            }
                        }
                        //y-1
                        {
                            int pos2 = pos + (chunksize + 2);
                            byte tt2 = currentChunk1d[pos2];
                            if (tt2 == 0
                                || (iswater[tt2] && (!iswater[tt]))
                                || istransparent[tt2])
                            {
                                draw |= (int)TileSideFlags.Right;
                            }
                        }
                        currentChunkDraw[xx - 1, yy - 1, zz - 1] = (byte)draw;
                    }
                }
            }
        }
        private void CalculateTilingCount(byte[] currentChunk, int startx, int starty, int startz)
        {
            Array.Clear(currentChunkDrawCount, 0, currentChunkDrawCount.Length);
            for (int xx = 1; xx < chunksize + 1; xx++)
            {
                for (int yy = 1; yy < chunksize + 1; yy++)
                {
                    for (int zz = 1; zz < chunksize + 1; zz++)
                    {
                        byte tt = currentChunk[MapUtil.Index(xx, yy, zz, chunksize + 2, chunksize + 2)];
                        int x = startx + xx - 1;
                        int y = starty + yy - 1;
                        int z = startz + zz - 1;
                        int draw = currentChunkDraw[xx - 1, yy - 1, zz - 1];
                        if ((draw & (int)TileSideFlags.Top) != 0)
                        {
                            int shadowratioTop = GetShadowRatio(xx, yy, zz + 1, x, y, z + 1);
                            currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Top] = (byte)GetTilingCount(currentChunk, xx, yy, zz, tt, x, y, z, shadowratioTop, TileSide.Top);
                        }
                        if ((draw & (int)TileSideFlags.Bottom) != 0)
                        {
                            int shadowratioTop = GetShadowRatio(xx, yy, zz - 1, x, y, z - 1);
                            currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Bottom] = (byte)GetTilingCount(currentChunk, xx, yy, zz, tt, x, y, z, shadowratioTop, TileSide.Bottom);
                        }
                        if ((draw & (int)TileSideFlags.Front) != 0)
                        {
                            int shadowratioTop = GetShadowRatio(xx - 1, yy, zz, x - 1, y, z);
                            currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Front] = (byte)GetTilingCount(currentChunk, xx, yy, zz, tt, x, y, z, shadowratioTop, TileSide.Front);
                        }
                        if ((draw & (int)TileSideFlags.Back) != 0)
                        {
                            int shadowratioTop = GetShadowRatio(xx + 1, yy, zz, x + 1, y, z);
                            currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Back] = (byte)GetTilingCount(currentChunk, xx, yy, zz, tt, x, y, z, shadowratioTop, TileSide.Back);
                        }
                        if ((draw & (int)TileSideFlags.Left) != 0)
                        {
                            int shadowratioTop = GetShadowRatio(xx, yy - 1, zz, x, y - 1, z);
                            currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Left] = (byte)GetTilingCount(currentChunk, xx, yy, zz, tt, x, y, z, shadowratioTop, TileSide.Left);
                        }
                        if ((draw & (int)TileSideFlags.Right) != 0)
                        {
                            int shadowratioTop = GetShadowRatio(xx, yy + 1, zz, x, y + 1, z);
                            currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Right] = (byte)GetTilingCount(currentChunk, xx, yy, zz, tt, x, y, z, shadowratioTop, TileSide.Right);
                        }
                    }
                }
            }
        }
        private void BlockPolygons(int x, int y, int z, byte[] currentChunk)
        {
            int xx = x % chunksize + 1;
            int yy = y % chunksize + 1;
            int zz = z % chunksize + 1;
            var tt = currentChunk[MapUtil.Index(xx, yy, zz, chunksize + 2, chunksize + 2)];
            if (!isvalid[tt])
            {
                return;
            }
            byte drawtop = currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Top];
            byte drawbottom = currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Bottom];
            byte drawfront = currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Front];
            byte drawback = currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Back];
            byte drawleft = currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Left];
            byte drawright = currentChunkDrawCount[xx - 1, yy - 1, zz - 1, (int)TileSide.Right];
            int tiletype = tt;
            if (drawtop == 0 && drawbottom == 0 && drawfront == 0 && drawback == 0 && drawleft == 0 && drawright == 0)
            {
                return;
            }
            FastColor color = mapstorage.GetTerrainBlockColor(x, y, z);
            FastColor colorShadowSide = new FastColor(color.A,
                (int)(color.R * BlockShadow),
                (int)(color.G * BlockShadow),
                (int)(color.B * BlockShadow));
            if (DONOTDRAWEDGES)
            {
                //if the game is fillrate limited, then this makes it much faster.
                //(39fps vs vsync 75fps)
                //bbb.
                if (z == 0) { drawbottom = 0; }
                if (x == 0) { drawfront = 0; }
                if (x == 256 - 1) { drawback = 0; }
                if (y == 0) { drawleft = 0; }
                if (y == 256 - 1) { drawright = 0; }
            }
            float flowerfix = 0;
            if (data.IsBlockFlower(tiletype))
            {
                drawtop = 0;
                drawbottom = 0;
                flowerfix = 0.5f;
            }
            RailDirectionFlags rail = data.GetRail(tiletype);
            float blockheight = 1;//= data.GetTerrainBlockHeight(tiletype);
            if (rail != RailDirectionFlags.None)
            {
                blockheight = 0.3f;
                /*
                RailPolygons(myelements, myvertices, x, y, z, rail);
                return;
                */
            }
            if (tt == data.TileIdSingleStairs)
            {
                blockheight = 0.5f;
            }
            if (tt == data.TileIdTorch)
            {
                TorchType type = TorchType.Normal;
                if (CanSupportTorch(currentChunk[MapUtil.Index(xx - 1, yy, zz, chunksize + 2, chunksize + 2)])) { type = TorchType.Front; }
                if (CanSupportTorch(currentChunk[MapUtil.Index(xx + 1, yy, zz, chunksize + 2, chunksize + 2)])) { type = TorchType.Back; }
                if (CanSupportTorch(currentChunk[MapUtil.Index(xx, yy - 1, zz, chunksize + 2, chunksize + 2)])) { type = TorchType.Left; }
                if (CanSupportTorch(currentChunk[MapUtil.Index(xx, yy + 1, zz, chunksize + 2, chunksize + 2)])) { type = TorchType.Right; }
                blockdrawertorch.AddTorch(toreturnmain.indices, toreturnmain.vertices, x, y, z, type);
                return;
            }
            //slope
            float blockheight00 = blockheight;
            float blockheight01 = blockheight;
            float blockheight10 = blockheight;
            float blockheight11 = blockheight;
            if (rail != RailDirectionFlags.None)
            {
                if (railmaputil == null)
                {
                    railmaputil = new RailMapUtil() { data = data, mapstorage = mapstorage };
                }
                RailSlope slope = railmaputil.GetRailSlope(x, y, z);
                if (slope == RailSlope.TwoRightRaised)
                {
                    blockheight10 += 1;
                    blockheight11 += 1;
                }
                if (slope == RailSlope.TwoLeftRaised)
                {
                    blockheight00 += 1;
                    blockheight01 += 1;
                }
                if (slope == RailSlope.TwoUpRaised)
                {
                    blockheight00 += 1;
                    blockheight10 += 1;
                }
                if (slope == RailSlope.TwoDownRaised)
                {
                    blockheight01 += 1;
                    blockheight11 += 1;
                }
            }
            FastColor curcolor = color;
            //top
            if (drawtop > 0)
            {
                curcolor = color;
                int shadowratio = GetShadowRatio(xx, yy, zz + 1, x, y, z + 1);
                if (shadowratio != maxlight)
                {
                    float shadowratiof = ((float)shadowratio) * maxlightInverse;
                    curcolor = new FastColor(color.A,
                        (int)(color.R * shadowratiof),
                        (int)(color.G * shadowratiof),
                        (int)(color.B * shadowratiof));
                }
                int sidetexture = data.GetTileTextureId(tiletype, TileSide.Top);
                int tilecount = drawtop;
                VerticesIndices toreturn = GetToReturn(tt, sidetexture);
                RectangleF texrec = TextureAtlas.TextureCoords1d(sidetexture, terrainrenderer.terrainTexturesPerAtlas, tilecount);
                short lastelement = (short)toreturn.vertices.Count;
                toreturn.vertices.Add(new VertexPositionTexture(x + 0.0f, z + blockheight00, y + 0.0f, texrec.Left, texrec.Top, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 0.0f, z + blockheight01, y + 1.0f, texrec.Left, texrec.Bottom, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1.0f * tilecount, z + blockheight10, y + 0.0f, texrec.Right, texrec.Top, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1.0f * tilecount, z + blockheight11, y + 1.0f, texrec.Right, texrec.Bottom, curcolor));
                toreturn.indices.Add((ushort)(lastelement + 0));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 2));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 3));
                toreturn.indices.Add((ushort)(lastelement + 2));
            }
            //bottom - same as top, but z is 1 less.
            if (drawbottom > 0)
            {
                curcolor = colorShadowSide;
                int shadowratio = GetShadowRatio(xx, yy, zz - 1, x, y, z - 1);
                if (shadowratio != maxlight)
                {
                    float shadowratiof = ((float)shadowratio) * maxlightInverse;
                    curcolor = new FastColor(color.A,
                        (int)(Math.Min(curcolor.R, color.R * shadowratiof)),
                        (int)(Math.Min(curcolor.G, color.G * shadowratiof)),
                        (int)(Math.Min(curcolor.B, color.B * shadowratiof)));
                }
                int sidetexture = data.GetTileTextureId(tiletype, TileSide.Bottom);
                int tilecount = drawbottom;
                VerticesIndices toreturn = GetToReturn(tt, sidetexture);
                RectangleF texrec = TextureAtlas.TextureCoords1d(sidetexture, terrainrenderer.terrainTexturesPerAtlas, tilecount);
                short lastelement = (short)toreturn.vertices.Count;
                toreturn.vertices.Add(new VertexPositionTexture(x + 0.0f, z, y + 0.0f, texrec.Left, texrec.Top, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 0.0f, z, y + 1.0f, texrec.Left, texrec.Bottom, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1.0f * tilecount, z, y + 0.0f, texrec.Right, texrec.Top, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1.0f * tilecount, z, y + 1.0f, texrec.Right, texrec.Bottom, curcolor));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 0));
                toreturn.indices.Add((ushort)(lastelement + 2));
                toreturn.indices.Add((ushort)(lastelement + 3));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 2));
            }
            //front
            if (drawfront > 0)
            {
                curcolor = color;
                int shadowratio = GetShadowRatio(xx - 1, yy, zz, x - 1, y, z);
                if (shadowratio != maxlight)
                {
                    float shadowratiof = ((float)shadowratio) * maxlightInverse;
                    curcolor = new FastColor(color.A,
                        (int)(color.R * shadowratiof),
                        (int)(color.G * shadowratiof),
                        (int)(color.B * shadowratiof));
                }
                int sidetexture = data.GetTileTextureId(tiletype, TileSide.Front);
                int tilecount = drawfront;
                VerticesIndices toreturn = GetToReturn(tt, sidetexture);
                RectangleF texrec = TextureAtlas.TextureCoords1d(sidetexture, terrainrenderer.terrainTexturesPerAtlas, tilecount);
                short lastelement = (short)toreturn.vertices.Count;
                toreturn.vertices.Add(new VertexPositionTexture(x + 0 + flowerfix, z + 0, y + 0, texrec.Left, texrec.Bottom, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 0 + flowerfix, z + 0, y + 1 * tilecount, texrec.Right, texrec.Bottom, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 0 + flowerfix, z + blockheight00, y + 0, texrec.Left, texrec.Top, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 0 + flowerfix, z + blockheight01, y + 1 * tilecount, texrec.Right, texrec.Top, curcolor));
                toreturn.indices.Add((ushort)(lastelement + 0));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 2));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 3));
                toreturn.indices.Add((ushort)(lastelement + 2));
            }
            //back - same as front, but x is 1 greater.
            if (drawback > 0)
            {
                curcolor = color;
                int shadowratio = GetShadowRatio(xx + 1, yy, zz, x + 1, y, z);
                if (shadowratio != maxlight)
                {
                    float shadowratiof = ((float)shadowratio) * maxlightInverse;
                    curcolor = new FastColor(color.A,
                        (int)(color.R * shadowratiof),
                        (int)(color.G * shadowratiof),
                        (int)(color.B * shadowratiof));
                }
                int sidetexture = data.GetTileTextureId(tiletype, TileSide.Back);
                int tilecount = drawback;
                VerticesIndices toreturn = GetToReturn(tt, sidetexture);
                RectangleF texrec = TextureAtlas.TextureCoords1d(sidetexture, terrainrenderer.terrainTexturesPerAtlas, tilecount);
                short lastelement = (short)toreturn.vertices.Count;
                toreturn.vertices.Add(new VertexPositionTexture(x + 1 - flowerfix, z + 0, y + 0, texrec.Right, texrec.Bottom, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1 - flowerfix, z + 0, y + 1 * tilecount, texrec.Left, texrec.Bottom, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1 - flowerfix, z + blockheight10, y + 0, texrec.Right, texrec.Top, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1 - flowerfix, z + blockheight11, y + 1 * tilecount, texrec.Left, texrec.Top, curcolor));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 0));
                toreturn.indices.Add((ushort)(lastelement + 2));
                toreturn.indices.Add((ushort)(lastelement + 3));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 2));
            }
            if (drawleft > 0)
            {
                curcolor = colorShadowSide;
                int shadowratio = GetShadowRatio(xx, yy - 1, zz, x, y - 1, z);
                if (shadowratio != maxlight)
                {
                    float shadowratiof = ((float)shadowratio) * maxlightInverse;
                    curcolor = new FastColor(color.A,
                        (int)(Math.Min(curcolor.R, color.R * shadowratiof)),
                        (int)(Math.Min(curcolor.G, color.G * shadowratiof)),
                        (int)(Math.Min(curcolor.B, color.B * shadowratiof)));
                }

                int sidetexture = data.GetTileTextureId(tiletype, TileSide.Left);
                int tilecount = drawleft;
                VerticesIndices toreturn = GetToReturn(tt, sidetexture);
                RectangleF texrec = TextureAtlas.TextureCoords1d(sidetexture, terrainrenderer.terrainTexturesPerAtlas, tilecount);
                short lastelement = (short)toreturn.vertices.Count;
                toreturn.vertices.Add(new VertexPositionTexture(x + 0, z + 0, y + 0 + flowerfix, texrec.Right, texrec.Bottom, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 0, z + blockheight00, y + 0 + flowerfix, texrec.Right, texrec.Top, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1 * tilecount, z + 0, y + 0 + flowerfix, texrec.Left, texrec.Bottom, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1 * tilecount, z + blockheight10, y + 0 + flowerfix, texrec.Left, texrec.Top, curcolor));
                toreturn.indices.Add((ushort)(lastelement + 0));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 2));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 3));
                toreturn.indices.Add((ushort)(lastelement + 2));
            }
            //right - same as left, but y is 1 greater.
            if (drawright > 0)
            {
                curcolor = colorShadowSide;
                int shadowratio = GetShadowRatio(xx, yy + 1, zz, x, y + 1, z);
                if (shadowratio != maxlight)
                {
                    float shadowratiof = ((float)shadowratio) * maxlightInverse;
                    curcolor = new FastColor(color.A,
                        (int)(Math.Min(curcolor.R, color.R * shadowratiof)),
                        (int)(Math.Min(curcolor.G, color.G * shadowratiof)),
                        (int)(Math.Min(curcolor.B, color.B * shadowratiof)));
                }

                int sidetexture = data.GetTileTextureId(tiletype, TileSide.Right);
                int tilecount = drawright;
                VerticesIndices toreturn = GetToReturn(tt, sidetexture);
                RectangleF texrec = TextureAtlas.TextureCoords1d(sidetexture, terrainrenderer.terrainTexturesPerAtlas, tilecount);
                short lastelement = (short)toreturn.vertices.Count;
                toreturn.vertices.Add(new VertexPositionTexture(x + 0, z + 0, y + 1 - flowerfix, texrec.Left, texrec.Bottom, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 0, z + blockheight01, y + 1 - flowerfix, texrec.Left, texrec.Top, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1 * tilecount, z + 0, y + 1 - flowerfix, texrec.Right, texrec.Bottom, curcolor));
                toreturn.vertices.Add(new VertexPositionTexture(x + 1 * tilecount, z + blockheight11, y + 1 - flowerfix, texrec.Right, texrec.Top, curcolor));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 0));
                toreturn.indices.Add((ushort)(lastelement + 2));
                toreturn.indices.Add((ushort)(lastelement + 3));
                toreturn.indices.Add((ushort)(lastelement + 1));
                toreturn.indices.Add((ushort)(lastelement + 2));
            }
        }
        private int GetTilingCount(byte[] currentChunk, int xx, int yy, int zz, byte tt, int x, int y, int z, int shadowratio, TileSide dir)
        {
            //fixes tree Z-fighting
            if (istransparent[currentChunk[MapUtil.Index(xx, yy, zz, chunksize + 2, chunksize + 2)]]
                && !data.IsTransparentTileFully(currentChunk[MapUtil.Index(xx, yy, zz, chunksize + 2, chunksize + 2)])) { return 1; }
            if (dir == TileSide.Top || dir == TileSide.Bottom)
            {
                int shadowz = dir == TileSide.Top ? 1 : -1;
                int newxx = xx + 1;
                for (; ; )
                {
                    if (newxx >= chunksize + 1) { break; }
                    if (currentChunk[MapUtil.Index(newxx, yy, zz, chunksize + 2, chunksize + 2)] != tt) { break; }
                    int shadowratio2 = GetShadowRatio(newxx, yy, zz + shadowz, x + (newxx - xx), y, z + shadowz);
                    if (shadowratio != shadowratio2) { break; }
                    if (currentChunkDrawCount[newxx - 1, yy - 1, zz - 1, (int)dir] == 0) { break; } // fixes water and rail problem (chunk-long stripes)
                    currentChunkDrawCount[newxx - 1, yy - 1, zz - 1, (int)dir] = 0;
                    newxx++;
                }
                return newxx - xx;
            }
            else if (dir == TileSide.Front || dir == TileSide.Back)
            {
                int shadowx = dir == TileSide.Front ? -1 : 1;
                int newyy = yy + 1;
                for (; ; )
                {
                    if (newyy >= chunksize + 1) { break; }
                    if (currentChunk[MapUtil.Index(xx, newyy, zz, chunksize + 2, chunksize + 2)] != tt) { break; }
                    int shadowratio2 = GetShadowRatio(xx + shadowx, newyy, zz, x + shadowx, y + (newyy - yy), z);
                    if (shadowratio != shadowratio2) { break; }
                    if (currentChunkDrawCount[xx - 1, newyy - 1, zz - 1, (int)dir] == 0) { break; } // fixes water and rail problem (chunk-long stripes)
                    currentChunkDrawCount[xx - 1, newyy - 1, zz - 1, (int)dir] = 0;
                    newyy++;
                }
                return newyy - yy;
            }
            else
            {
                int shadowy = dir == TileSide.Left ? -1 : 1;
                int newxx = xx + 1;
                for (; ; )
                {
                    if (newxx >= chunksize + 1) { break; }
                    if (currentChunk[MapUtil.Index(newxx, yy, zz, chunksize + 2, chunksize + 2)] != tt) { break; }
                    int shadowratio2 = GetShadowRatio(newxx, yy + shadowy, zz, x + (newxx - xx), y + shadowy, z);
                    if (shadowratio != shadowratio2) { break; }
                    if (currentChunkDrawCount[newxx - 1, yy - 1, zz - 1, (int)dir] == 0) { break; } // fixes water and rail problem (chunk-long stripes)
                    currentChunkDrawCount[newxx - 1, yy - 1, zz - 1, (int)dir] = 0;
                    newxx++;
                }
                return newxx - xx;
            }
        }
        private VerticesIndices GetToReturn(byte tiletype, int textureid)
        {
            if (ENABLE_ATLAS1D)
            {
                if (!(istransparent[tiletype] || iswater[tiletype]))
                {
                    return toreturnatlas1d[textureid / terrainrenderer.terrainTexturesPerAtlas];
                }
                else
                {
                    return toreturnatlas1dtransparent[textureid / terrainrenderer.terrainTexturesPerAtlas];
                }
            }
            else
            {
                if (!(istransparent[tiletype] || iswater[tiletype]))
                {
                    return toreturnmain;
                }
                else
                {
                    return toreturntransparent;
                }
            }
        }
        int GetShadowRatio(int xx, int yy, int zz, int globalx, int globaly, int globalz)
        {
            if (currentChunkShadows[xx, yy, zz] == byte.MaxValue)
            {
                if (IsValidPos(globalx, globaly, globalz))
                {
                    currentChunkShadows[xx, yy, zz] = (byte)mapstorage.GetLight(globalx, globaly, globalz);
                }
                else
                {
                    currentChunkShadows[xx, yy, zz] = (byte)maxlight;
                }
            }
            return currentChunkShadows[xx, yy, zz];
        }
        private bool CanSupportTorch(byte blocktype)
        {
            return blocktype != data.TileIdEmpty
                && blocktype != data.TileIdTorch;
        }
    }
}
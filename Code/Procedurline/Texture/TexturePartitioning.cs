using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Contains a partitioning of a texture. All pixels in the texture belong to exactly one partition.
    /// Partitions can be manually created, or by e.g. <see cref="TexturePartitioning.CreateColorComponentPartitions" />.
    /// </summary>
    public sealed class TexturePartitioning {
        /// <summary>
        /// Creates a partitioning of a texture based on color components. A color component consists of adjacent pixels with the same or similar colors.
        /// </summary>
        /// <param name="maxColDst">
        /// The maximum distance two colors can be to be considered "similar". Distance is defined as the Euclidean distance between the color vectors.
        /// </param>
        public static unsafe TexturePartitioning CreateColorComponentPartitions(TextureData tex, int maxColDst = 0) {
            int w = tex.Width, h = tex.Height;
            TexturePartitioning pt = new TexturePartitioning(tex);
            Point[,] prevPixels = new Point[w, h];
            for(int x = 0; x < w; x++) {
                for(int y = 0; y < h; y++) {
                    if(pt[x,y] == 0) {
                        //Run a DFS from this pixel
                        int partId = pt.AddPartition();

                        int curX = x, curY = y;
                        prevPixels[x, y] = new Point(x, y);
                        while(true) {
                            //Set partition ID of pixel
                            pt[curX, curY] = partId;

                            //Goto next pixel
                            Point curPix = new Point(curX, curY);
                            Color curCol = tex[curX, curY];
                                 if(curX > 0   && pt[curX-1, curY] == 0 && curCol.GetSquaredDistance(tex[curX-1, curY]) <= maxColDst * maxColDst) prevPixels[curX--, curY] = curPix;
                            else if(curX < w-1 && pt[curX+1, curY] == 0 && curCol.GetSquaredDistance(tex[curX+1, curY]) <= maxColDst * maxColDst) prevPixels[curX++, curY] = curPix;
                            else if(curY > 0   && pt[curX, curY-1] == 0 && curCol.GetSquaredDistance(tex[curX, curY-1]) <= maxColDst * maxColDst) prevPixels[curX, curY--] = curPix;
                            else if(curY < h-1 && pt[curX, curY+1] == 0 && curCol.GetSquaredDistance(tex[curX, curY+1]) <= maxColDst * maxColDst) prevPixels[curX, curY++] = curPix;
                            else {
                                //Return to previous pixel
                                Point prevPix = prevPixels[curX, curY];
                                if(prevPix == curPix) break;
                                curX = prevPix.X;
                                curY = prevPix.Y;
                            }
                        }
                    }
                }
            }

            return pt;
        }

        private int numParts = 1;
        private int[,] partIds;

        public TexturePartitioning(TextureData texData) {
            Texture = texData;
            partIds = new int[texData.Width, texData.Height];
        }

        /// <summary>
        /// Adds a new partition, and returns its ID.
        /// </summary>
        public int AddPartition() => numParts++;

        /// <summary>
        /// Returns an enumerable over all pixels in the given partition.
        /// </summary>
        public IEnumerable<Point> GetPartitionPixels(int id) {
            for(int x = 0; x < Texture.Width; x++) {
                for(int y = 0; y < Texture.Height; y++) {
                    if(partIds[x,y] == id) yield return new Point(x, y);
                }
            }
        }

        /// <summary>
        /// Finds the partition which fullfills the given condition, or <c>-1</c> if no such partition exists.
        /// </summary>
        public int FindPartition(Func<int, Point, bool> cond) {
            bool[] triedPart = new bool[numParts];
            for(int x = 0; x < Texture.Width; x++) {
                for(int y = 0; y < Texture.Height; y++) {
                    int partId = partIds[x,y];
                    if(triedPart[partId]) continue;
                    triedPart[partId] = true;

                    if(cond(partId, new Point(x, y))) return partId;
                }
            }
            return -1;
        }

        /// <summary>
        /// Finds the partitions which fullfill the given condition.
        /// </summary>
        public IEnumerable<int> FindPartitions(Func<int, Point, bool> cond) {
            bool[] triedPart = new bool[numParts];
            for(int x = 0; x < Texture.Width; x++) {
                for(int y = 0; y < Texture.Height; y++) {
                    int partId = partIds[x,y];
                    if(triedPart[partId]) continue;
                    triedPart[partId] = true;

                    if(cond(partId, new Point(x, y))) yield return partId;
                }
            }
        }

        /// <summary>
        /// Exchanges a partition's ID. This replaces the ID of all pixels belonging to the partition with the given new ID.
        /// If the ID already belongs to a partition, this effectively merges the two.
        /// </summary>
        public unsafe void ExchangePartitionID(int oldId, int newId) {
            fixed(int *partIdsPtr = partIds) {
                int *idp = partIdsPtr;
                for(int rem = Texture.Width*Texture.Height; --rem >= 0;) {
                    if(*idp == oldId) *idp = newId;
                    idp++;
                }
            }
        }

        /// <summary>
        /// Merge touching paritions fulfilling the the condition.
        /// </summary>
        public void MergeTouchingParitions(Func<int, int, Point, Point, bool> cond) {
            //Do merge rounds
            bool didMerge;
            do {
                didMerge = false;

                //Iterate over all touching partitions
                for(int x = 0; x < Texture.Width-1; x++) {
                    for(int y = 0; y < Texture.Height-1; y++) {
                        int partA = partIds[x,y], partB = partIds[x+1,y], partC = partIds[x,y+1];
                        if(partA != partB && cond(partA, partB, new Point(x,y), new Point(x+1,y))) ExchangePartitionID(partB, partA);
                        if(partA != partC && cond(partA, partC, new Point(x,y), new Point(x,y+1))) ExchangePartitionID(partC, partA);
                    }
                }
            } while(didMerge);
        }

        public TextureData Texture { get; }
        public int NumPartitions => numParts;

        /// <summary>
        /// Gets or sets the pixel's partition ID.
        /// </summary>
        public int this[int x, int y] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => partIds[x, y];
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set {
                if(value < 0 || numParts <= value) throw new ArgumentOutOfRangeException("value");
                partIds[x, y] = value;
            }
        }

        /// <summary>
        /// Gets or sets the pixel's partition ID.
        /// </summary>
        public int this[Point p] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => partIds[p.X, p.Y];
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set {
                if(value < 0 || numParts <= value) throw new ArgumentOutOfRangeException("value");
                partIds[p.X, p.Y] = value;
            }
        }
    }
}
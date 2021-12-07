using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Celeste;
using System.Collections;

namespace Celeste.Mod.Procedurline {
    public class TextureData : IEnumerable<Point> {
        private int width, height;
        private Color[] pixels;

        public TextureData(int width, int height) : this(width, height, new Color[width*height]) {}
        public TextureData(int width, int height, Color[] pixels) {
            if(width <= 0 || height <= 0) throw new ArgumentException();
            if(pixels.Length != width*height) throw new ArgumentException();
            this.width = width;
            this.height = height;
            this.pixels = pixels;
        }

        public TextureData(TextureData other) : this(other.Width, other.Height) {
            Array.Copy(other.Pixels, pixels, pixels.Length);
        }

        public int Width => width;
        public int Height => height;
        public Color[] Pixels => pixels;

        public TextureData GetSubsection(int x, int y, int width, int height) {
            if(width <= 0 || height <= 0) throw new ArgumentException();
            if(!InBounds(x, y) || !InBounds(x+width-1, y+height-1)) throw new IndexOutOfRangeException();
            
            //Transfer pixel data
            TextureData subSect = new TextureData(width, height);
            foreach(Point p in subSect) subSect[p] = this[x+p.X, y+p.Y];
            return subSect;
        }
        public TextureData GetSubsection(Rectangle rect) => GetSubsection(rect.X, rect.Y, rect.Width, rect.Height);

        public void SetSubsection(int x, int y, TextureData subSect) {
            if(!InBounds(x, y) || !InBounds(x+subSect.Width-1, y+subSect.Height-1)) throw new IndexOutOfRangeException();
            
            //Transfer pixel data
            foreach(Point p in subSect) this[x+p.X, y+p.Y] = subSect[p];
        }
        public void SetSubsection(Point off, TextureData subSect) => SetSubsection(off.X, off.Y, subSect);

        public bool InBounds(int x, int y) {
            return 0 <= x && 0 <= y && x < width && y < height;
        }
        public bool InBounds(Point p) => InBounds(p.X, p.Y);

        public IEnumerator<Point> GetEnumerator() {
            for(int x = 0; x < width; x++) {
                for(int y = 0; y < height; y++) {
                    yield return new Point(x, y);
                }
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        public Color this[int x, int y] {
            get {
                if(!InBounds(x, y)) throw new IndexOutOfRangeException();
                return pixels[y*width + x];
            }
            set {
                if(!InBounds(x, y)) throw new IndexOutOfRangeException();
                pixels[y*width + x] = value;
            }
        }
        public Color this[Point p] {
            get => this[p.X, p.Y];
            set => this[p.X, p.Y] = value;
        }
    }

    public class TextureHeap {
        private class Node {
            private Rectangle rect;
            private TextureData data;
            private Node[,] children;

            public Node(Rectangle rect) {
                this.rect = rect;
                this.data = null;
                this.children = null;
            }
            
            public Node Allocate(TextureData tex) {
                //If the node contains data or is too small, we can't allocate from it
                if(data != null || rect.Width < tex.Width || rect.Height < tex.Height) return null;
                
                if(children != null) {
                    //Try to allocate from child nodes
                    for(int cy = 0; cy <= 1; cy++) {
                        for(int cx = 0; cx <= 1; cx++) {
                            Node a = children[cx, cy].Allocate(tex);
                            if(a != null) return a;
                        }
                    }

                    return null;
                }

                //Could this texture fit inside of a child?
                int childWidth = (rect.Width-1) / 2, childHeight = (rect.Height-1) / 2;
                if(tex.Width <= childWidth && tex.Height <= childHeight) {
                    //Create children & allocate in first
                    children = new Node[2,2];
                    for(int cx = 0; cx <= 1; cx++) {
                        for(int cy = 0; cy <= 1; cy++) {
                            children[cx, cy] = new Node(new Rectangle(rect.X + cx*(childWidth+1), rect.Y + cy*(childHeight+1), childWidth, childHeight));
                        }
                    }

                    return children[0,0].Allocate(tex);
                }

                //Allocate the entire node
                data = tex;
                return this;
            }

            public void TransferData(TextureData tex) {
                //If this node contains data, transfer it
                if(data != null) tex.SetSubsection(new Point(rect.X, rect.Y), data);
                else if(children != null) {
                    //Transfer children
                    for(int cx = 0; cx <= 1; cx++) {
                        for(int cy = 0; cy <= 1; cy++) {
                            if(children[cx, cy] != null) children[cx, cy].TransferData(tex);
                        }
                    }
                }
            }

            public Node DoubleSize() {
                if(rect.X != 0 || rect.Y != 0) throw new InvalidOperationException();
                
                //Create new node
                Node n = new Node(new Rectangle(0, 0, rect.Width*2+1, rect.Height*2+1));

                if(data != null || children != null) {
                    n.children = new Node[2,2];
                    for(int cx = 0; cx <= 1; cx++) {
                        for(int cy = 0; cy <= 1; cy++) {
                            n.children[cx, cy] = (cx == 0 && cy == 0) ? this : new Node(new Rectangle(cx*(rect.Width+1), cy*(rect.Height+1), rect.Width, rect.Height));
                        }
                    }
                }

                return n;
            }

            public Rectangle Rectangle => rect;
        }

        private Node root = new Node(new Rectangle(0, 0, 8, 8));

        public Rectangle AddTexture(TextureData texture) {
            if(texture.Width <= 0 || texture.Height <= 0) throw new ArgumentException("Size must be bigger than zero");
            while(true) {
                //Try to allocate from root node
                Node n = root.Allocate(texture);
                if(n != null) return n.Rectangle;

                //Double root node size
                root = root.DoubleSize();
            }
        }

        public TextureData CreateHeapTexture() {
            TextureData tex = new TextureData(root.Rectangle.Width, root.Rectangle.Height);
            root.TransferData(tex);
            return tex;
        }
    }
}
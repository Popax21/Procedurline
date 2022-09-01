using System;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements a simple texture heap, which can be used to merge multiple texture data buffers into one big atlas
    /// </summary>
    public sealed class TextureHeap {
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
                if(data != null || rect.Width < tex.Width+1 || rect.Height < tex.Height+1) return null;
                
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
                if(tex.Width+1 <= childWidth && tex.Height+1 <= childHeight) {
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
                if(data != null) data.Copy(tex, dstRect: new Rectangle(rect.X, rect.Y, data.Width, data.Height));
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

        /// <summary>
        /// Adds texture data to the heap
        /// </summary>
        /// <returns>
        /// Returns the subrectangle of the atlas texture corresponding to the added texture
        /// </returns>
        public Rectangle AddTexture(TextureData texture) {
            if(texture.Width <= 0 || texture.Height <= 0) throw new ArgumentException("Size must be bigger than zero");
            while(true) {
                //Try to allocate from root node
                Node n = root.Allocate(texture);
                if(n != null) return new Rectangle(n.Rectangle.X, n.Rectangle.Y, texture.Width, texture.Height);

                //Double root node size
                root = root.DoubleSize();
            }
        }

        /// <summary>
        /// Creates the atlas texture
        /// </summary>
        public TextureData CreateAtlasTexture() {
            TextureData tex = new TextureData(root.Rectangle.Width, root.Rectangle.Height);
            root.TransferData(tex);
            return tex;
        }

        public int AtlasWidth => root.Rectangle.Width;
        public int AtlasHeight => root.Rectangle.Height;
    }
}
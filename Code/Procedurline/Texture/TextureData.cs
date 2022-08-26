using System;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Contains and wraps texture data in a more accesible way
    /// </summary>
    public unsafe sealed class TextureData : IDisposable {
        //Sketchy FNA stuff for better performance
        private delegate void FNA3D_GetTextureData2D(IntPtr device, IntPtr texture, int x, int y, int w, int h, int level, IntPtr data, int dataLength);
        private delegate void FNA3D_SetTextureData2D(IntPtr device, IntPtr texture, int x, int y, int w, int h, int level, IntPtr data, int dataLength);

        private static readonly FieldInfo fna_GLDevice, fna_texture;
        private static readonly FNA3D_GetTextureData2D fna_GetTextureData2D;
        private static readonly FNA3D_SetTextureData2D fna_SetTextureData2D;

        static TextureData() {
            if(Everest.Flags.IsFNA) {
                fna_GLDevice = typeof(GraphicsDevice).GetField("GLDevice", BindingFlags.NonPublic | BindingFlags.Instance);
                fna_texture = typeof(Texture).GetField("texture", BindingFlags.NonPublic | BindingFlags.Instance);

                Type fna3d = typeof(Texture).Assembly.GetType("Microsoft.Xna.Framework.Graphics.FNA3D", true, true);
                fna_GetTextureData2D = (FNA3D_GetTextureData2D) fna3d.GetMethod("FNA3D_GetTextureData2D", BindingFlags.Public | BindingFlags.Static).CreateDelegate(typeof(FNA3D_GetTextureData2D));
                fna_SetTextureData2D = (FNA3D_SetTextureData2D) fna3d.GetMethod("FNA3D_SetTextureData2D", BindingFlags.Public | BindingFlags.Static).CreateDelegate(typeof(FNA3D_SetTextureData2D));
            }
        }

        private int width, height;
        private Color* data;
        private StackTrace allocTrace;

        public TextureData(int width, int height) {
            this.width = width;
            this.height = height;

            //Allocate data
            data = (Color*) Marshal.AllocHGlobal(width * height * 4);
            GC.AddMemoryPressure(width * height * 4);

            //Create a stack trace if leak debugging is enabled
            if(ProcedurlineModule.Settings?.DebugTextureLeaks ?? false) allocTrace = new StackTrace();
        }

        ~TextureData() {
            if(data != null) {
                Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Detected leaked texture data {width}x{height} [{width*height*4} bytes]");
                if(allocTrace != null) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, allocTrace.ToString());
                Dispose();
            }
        }

        public void Dispose() {
            //Free data
            if(data != null) {
                GC.RemoveMemoryPressure(width * height * 4);
                Marshal.FreeHGlobal((IntPtr) data);
                data = null;
                allocTrace = null;
            }
        }

        /// <summary>
        /// Clones the texture data
        /// </summary>
        public TextureData Clone() {
            if(data == null) throw new ObjectDisposedException("TextureData");

            TextureData clone = new TextureData(width, height);
            Buffer.MemoryCopy(data, clone.data, width*height*4, width*height*4);
            return clone;
        }

        private void CheckRectangleBounds(Rectangle rect) {
            if(
                rect.X < 0 || rect.Width  < 0 || rect.X + rect.Width  > width  ||
                rect.Y < 0 || rect.Height < 0 || rect.Y + rect.Height > height
            ) throw new ArgumentException("Invalid subtexture rectangle!");
        }

        /// <summary>
        /// Copies data from this texture into a different texture data buffer
        /// </summary>
        public void Copy(TextureData dst, Rectangle? srcRect = null, Rectangle? dstRect = null) {
            if(data == null || dst.data == null) throw new ObjectDisposedException("TextureData");

            //Obtain copy parameters
            int sx = 0, sy = 0, sw = width, sh = height;
            if(srcRect is Rectangle sr) {
                CheckRectangleBounds(sr);
                sx = sr.X;
                sy = sr.Y;
                sw = sr.Width;
                sh = sr.Height;
            }

            int dx = 0, dy = 0, dw = dst.width, dh = dst.height;
            if(dstRect is Rectangle dr) {
                dst.CheckRectangleBounds(dr);
                dx = dr.X;
                dy = dr.Y;
                dw = dr.Width;
                dh = dr.Height;
            }

            if(sw != dw || sh != dh) throw new ArgumentException("Mismatching source and destination size!");

            //Copy data
            Color* sp = data + sy*width + sx, dp = dst.data + dy*dst.width + dx;
            for(int nr = sh; --nr >= 0;) {
                Buffer.MemoryCopy(sp, dp, dw*4, sw*4);
                sp += width;
                dp += dst.width;
            }
        }

        private void CheckTextureBounds(Texture2D tex, Rectangle? rect = null) {
            if(rect != null) {
                Rectangle r = rect.Value;
                if(
                    r.X < 0 || r.Width  < 0 || r.X + r.Width  > tex.Width  ||
                    r.Y < 0 || r.Height < 0 || r.Y + r.Height > tex.Height ||
                    width != r.Width || height != r.Height
                ) throw new ArgumentException("Invalid subtexture rectangle!");
            } else {
                if(width != tex.Width || height != tex.Height) throw new ArgumentException("Invalid texture size!");
            }
        }

        /// <summary>
        /// Download data from the specified (sub)texture
        /// </summary>
        public void DownloadData(Texture2D tex, Rectangle? rect = null) {
            if(data == null) throw new ObjectDisposedException("TextureData");
            CheckTextureBounds(tex, rect);
    
            if(Everest.Flags.IsFNA) {
                fna_GetTextureData2D(
                    (IntPtr) fna_GLDevice.GetValue(tex.GraphicsDevice), (IntPtr) fna_texture.GetValue(tex),
                    rect?.X ?? 0, rect?.Y ?? 0, rect?.Width ?? tex.Width, rect?.Height ?? tex.Height, 0,
                    (IntPtr) data, width*height*4
                );
            }else {
                //XNA fallback
                byte[] bdata = new byte[width*height*4];
                tex.GetData<byte>(bdata);
                Marshal.Copy(bdata, 0, (IntPtr) data, width*height*4);
            }
        }

        /// <summary>
        /// Upload data to the specified (sub)texture
        /// </summary>
        public void UploadData(Texture2D tex, Rectangle? rect = null) {
            if(data == null) throw new ObjectDisposedException("TextureData");
            CheckTextureBounds(tex, rect);

            if(Everest.Flags.IsFNA) {
                fna_SetTextureData2D(
                    (IntPtr) fna_GLDevice.GetValue(tex.GraphicsDevice), (IntPtr) fna_texture.GetValue(tex),
                    rect?.X ?? 0, rect?.Y ?? 0, rect?.Width ?? tex.Width, rect?.Height ?? tex.Height, 0,
                    (IntPtr) data, width*height*4
                );
            } else {
                //XNA fallback
                byte[] bdata = new byte[width*height*4];
                Marshal.Copy((IntPtr) data, bdata, 0, width*height*4);
                tex.SetData<byte>(bdata);
            }
        }

        /// <summary>
        /// Enumerates over all pixels in the texture.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerable<Point> EnumeratePixels() {
            for(int x = 0; x < width; x++) {
                for(int y = 0; y < height; y++) {
                    yield return new Point(x, y);
                }
            }
        }

        /// <summary>
        /// Checks if the given pixel coordinates are in the bounds of the texture.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInBounds(int x, int y) => 0 <= x && x < width && 0 <= y && y < height;

        /// <summary>
        /// Checks if the given pixel coordinates are in the bounds of the texture.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInBounds(Point p) => 0 <= p.X && p.X < width && 0 <= p.Y && p.Y < height;

        public int Width {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => width;
        }

        public int Height {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => height;
        }

        /// <summary>
        /// WARNING: No bounds checks are performed! Make sure you provide valid coordinates!
        /// </summary>
        public Color this[int x, int y] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => data[y*width + x];
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => data[y*width + x] = value;
        }

        /// <summary>
        /// WARNING: No bounds checks are performed! Make sure you provide valid coordinates!
        /// </summary>
        public Color this[Point p] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => data[p.Y*width + p.X];
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => data[p.Y*width + p.X] = value;
        }

        /// <summary>
        /// Returns the raw data of the texture data buffer
        /// </summary>
        public Color* RawData => data;
    }
}
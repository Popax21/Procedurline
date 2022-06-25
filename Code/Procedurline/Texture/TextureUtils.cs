using Microsoft.Xna.Framework;

namespace Celeste.Mod.Procedurline {
    public static class TextureUtils {
        /// <summary>
        /// Applies the given color matrix to all pixels in the texture.
        /// </summary>
        public static void ApplyColorMatrix(this TextureData tex, Matrix colMatrix) {
            for(int x = 0; x < tex.Width; x++) {
                for(int y = 0; y < tex.Height; y++) {
                    tex[x,y] = tex[x,y].ApplyMatrix(colMatrix);
                }
            }
        }

        /// <summary>
        /// Applies the given color matrix to all pixels in the texture whose HSV saturation and value is above a certain threshold.
        /// </summary>
        public static void ApplyColorMatrix(this TextureData tex, Matrix colMatrix, float satThresh, float valThresh) {
            for(int x = 0; x < tex.Width; x++) {
                for(int y = 0; y < tex.Height; y++) {
                    tex[x,y] = tex[x,y].ApplyMatrix(colMatrix, satThresh, valThresh);
                }
            }
        }
    }
}
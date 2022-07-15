using System;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    public static class ColorUtils {
        private static readonly float ONE_THIRD = 1f / 3f, SQRT_ONE_THIRD = (float) Math.Sqrt(ONE_THIRD);

        public static readonly Matrix InverseColorMatrix = new Matrix(
            -1, 0, 0, 1,
            0, -1, 0, 1,
            0, 0, -1, 1,
            0, 0, 0,  1
        );

        //Source: https://docs.microsoft.com/en-us/windows/win32/direct2d/grayscale-effect
        public static readonly Matrix GrayscaleColorMatrix = new Matrix(
            0.299f, 0.299f, 0.299f, 0,
            0.587f, 0.287f, 0.287f, 0,
            0.114f, 0.114f, 0.144f, 0,
            0,      0,      0,      1
        );

        /// <summary>
        /// Removes the alpha component from the color by setting it to 255
        /// </summary>
        public static Color RemoveAlpha(this Color color) {
            return new Color(color.R, color.G, color.B, 255);
        }

        /// <summary>
        /// Gets the Euclidean distance to the given color
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetDistance(this Color color, Color other) => (float) Math.Sqrt(color.GetSquaredDistance(other));

        /// <summary>
        /// Gets the squared Euclidean distance to the given color
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSquaredDistance(this Color color, Color other) =>
            (color.R - other.R) * (color.R - other.R) +
            (color.G - other.G) * (color.G - other.G) +
            (color.B - other.B) * (color.B - other.B)
        ;

        /// <summary>
        /// Composes a color from its HSV components
        /// </summary>
        public static Color ComposeHSV(float hue, float saturation, float value) {
            hue = ((hue % 360) + 360) % 360;
            float C = value * saturation;
            float Ht = hue / 60;
            float X = C * (1 - ((Ht % 2) - 1));
            switch((int) Ht) {
                case 0: return new Color(C, X, 0);
                case 1: return new Color(X, C, 0);
                case 2: return new Color(0, C, X);
                case 3: return new Color(0, X, C);
                case 4: return new Color(X, 0, C);
                case 5: return new Color(C, 0, X);
                default: throw new InvalidOperationException("HSV Ht NOT IN VALID RANGE; THIS SHOULD NEVER HAPPEN!!!!");
            }
        }

        /// <summary>
        /// Decomposes the color into its HSV components
        /// </summary>
        public static void DecomposeHSV(this Color color, out float hue, out float saturation, out float value) {
            float Xmin = Math.Min(color.R, Math.Min(color.G, color.B)), Xmax = Math.Max(color.R, Math.Max(color.G, color.B));
            float C = Xmax - Xmin;

            if(Xmax == color.R)         hue = 60 * (0 + (color.G - color.B) / C);
            else if(Xmax == color.G)    hue = 60 * (2 + (color.B - color.R) / C);
            else                        hue = 60 * (4 + (color.R - color.G) / C);

            saturation = (Xmax == 0) ? 0 : (C / Xmax);
            value = Xmax / 255f;
        }

        /// <summary>
        /// Returns the color's HSV hue
        /// </summary>
        public static float GetHue(this Color color) {
            color.DecomposeHSV(out float hue, out _, out _);
            return hue;
        }

        /// <summary>
        /// Returns the color's HSV saturation
        /// </summary>
        public static float GetSaturation(this Color color) {
            color.DecomposeHSV(out _, out float sat, out _);
            return sat;
        }

        /// <summary>
        /// Returns the color's HSV value
        /// </summary>
        public static float GetValue(this Color color) {
            color.DecomposeHSV(out _, out _, out float val);
            return val;
        }

        /// <summary>
        /// Calculates an HSV hue shift matrix. Hue shift matrices are more efficient for shifiting a large quanitity of colors by a certain hue than the naive (de)composition approach.
        /// </summary>
        public static Matrix CalculateHueShiftMatrix(float hueShift) {
            float sinShift = (float) Math.Sin(-hueShift / 180 * Math.PI), cosShift = (float) Math.Cos(-hueShift / 180 * Math.PI);
            return new Matrix(
                cosShift + (1-cosShift) / 3, ONE_THIRD * (1-cosShift) + SQRT_ONE_THIRD * sinShift, ONE_THIRD * (1-cosShift) - SQRT_ONE_THIRD * sinShift, 0,
                ONE_THIRD * (1-cosShift) - SQRT_ONE_THIRD * sinShift, cosShift + (1-cosShift) / 3, ONE_THIRD * (1-cosShift) + SQRT_ONE_THIRD * sinShift, 0,
                ONE_THIRD * (1-cosShift) + SQRT_ONE_THIRD * sinShift, ONE_THIRD * (1-cosShift) - SQRT_ONE_THIRD * sinShift, cosShift + (1-cosShift) / 3, 0,
                0, 0, 0, 1
            );
        }

        /// <summary>
        /// Calculates the recoloring matrix to shift from this color to the specified color. This matrix can be applied to other colors to recolor an entire image.
        /// </summary>
        public static Matrix CalculateRecolorMatrix(Color srcColor, Color dstColor) {
            return CalculateHueShiftMatrix(GetHue(dstColor) - GetHue(srcColor)) * Matrix.CreateScale((float) (dstColor.R+dstColor.G+dstColor.B) / (srcColor.R+srcColor.G+srcColor.B));
        }

        /// <summary>
        /// Applies a color matrix to the color.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color ApplyMatrix(this Color color, Matrix mat) {
            return new Color() {
                R = (byte) (MathHelper.Clamp((color.R * mat.M11 + color.G * mat.M12 + color.B * mat.M13 + 255f * mat.M14) / (color.A / 255f), 0, 255f) * (color.A / 255f)),
                G = (byte) (MathHelper.Clamp((color.R * mat.M21 + color.G * mat.M22 + color.B * mat.M23 + 255f * mat.M24) / (color.A / 255f), 0, 255f) * (color.A / 255f)),
                B = (byte) (MathHelper.Clamp((color.R * mat.M31 + color.G * mat.M32 + color.B * mat.M33 + 255f * mat.M34) / (color.A / 255f), 0, 255f) * (color.A / 255f)),
                A = color.A
            };
        }

        /// <summary>
        /// Applies a color matrix to the color, if its HSV saturation and value exceed a given threshold. This can be used to not affect pure grayscale colors.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color ApplyMatrix(this Color color, Matrix mat, float satThresh, float valThresh) {
            float Xmin = Math.Min(color.R, Math.Min(color.G, color.B)), Xmax = Math.Max(color.R, Math.Max(color.G, color.B));
            float sat = (Xmax == 0) ? 0 : ((Xmax - Xmin) / Xmax);
            if(sat < satThresh || Xmax < valThresh*255f) return color;
            return color.ApplyMatrix(mat);
        }

        /// <summary>
        /// Applies a color matrix to the particle type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParticleType ApplyMatrix(this ParticleType ptype, Matrix mat) {
            ptype = new ParticleType(ptype);
            ptype.Color = ptype.Color.ApplyMatrix(mat);
            ptype.Color2 = ptype.Color2.ApplyMatrix(mat);
            return ptype;
        }

        /// <summary>
        /// Applies a color matrix to the particle type, if its HSV saturation and value exceed a given threshold. This can be used to not affect pure grayscale colors.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParticleType ApplyMatrix(this ParticleType ptype, Matrix mat, float satThresh, float valThresh) {
            ptype = new ParticleType(ptype);
            ptype.Color = ptype.Color.ApplyMatrix(mat, satThresh, valThresh);
            ptype.Color2 = ptype.Color2.ApplyMatrix(mat, satThresh, valThresh);
            return ptype;
        }
    }
}
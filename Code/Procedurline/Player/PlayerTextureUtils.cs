using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    public static class PlayerTextureUtils {
        public static Color SKIN_COLOR = Calc.HexToColor("#d9a066");
        public static Color SKIN_HIGHLIGHT_COLOR = Calc.HexToColor("#eec39a");
        public static Color SHIRT_PRIMARY_COLOR = Calc.HexToColor("#5b6ee1");
        public static Color SHIRT_SECONDARY_COLOR = Calc.HexToColor("#3f3f74");
        public static Color BAGPACK_PANTS_COLOR = Calc.HexToColor("#873724");
        public static Color TOOL_PRIMARY_COLOR = Calc.HexToColor("#677788");
        public static Color TOOL_SECONDARY_COLOR = Calc.HexToColor("#42505f");

        /// <summary>
        /// The maximum Y coordinate a face pixel can appear on
        /// </summary>
        public const int MAX_FACE_Y = 4;

        /// <summary>
        /// Merge all partitions which belong to the same component of the player's sprite.
        /// </summary>
        public static void MergePlayerComponents(this TexturePartitioning texPart) {
            //Merge components
            texPart.MergeTouchingParitions((a, b, pa, pb) => {
                if(
                    (texPart.Texture[pa] == SKIN_COLOR || texPart.Texture[pa] == SKIN_HIGHLIGHT_COLOR) &&
                    (texPart.Texture[pb] == SKIN_COLOR || texPart.Texture[pb] == SKIN_HIGHLIGHT_COLOR)
                ) return true;
                if(
                    (texPart.Texture[pa] == SHIRT_PRIMARY_COLOR || texPart.Texture[pa] == SHIRT_SECONDARY_COLOR) &&
                    (texPart.Texture[pb] == SHIRT_PRIMARY_COLOR || texPart.Texture[pb] == SHIRT_SECONDARY_COLOR)
                ) return true;
                if(
                    (texPart.Texture[pa] == TOOL_PRIMARY_COLOR || texPart.Texture[pa] == TOOL_SECONDARY_COLOR) &&
                    (texPart.Texture[pb] == TOOL_PRIMARY_COLOR || texPart.Texture[pb] == TOOL_SECONDARY_COLOR)
                ) return true;
                return false;
            });
        }

        /// <summary>
        /// Checks if the given partiton ID belongs to the player's face component.
        /// </summary>
        public static bool IsFaceComponent(TexturePartitioning texPart, int partId) {
            int pxCount = 0;
            bool hasYPixel = false, hasHighlightPixel = false;
            for(int x = 0; x < texPart.Texture.Width; x++) {
                for(int y = 0; y < texPart.Texture.Height; y++) {
                    if(texPart[x,y] != partId) continue;

                    pxCount++;
                    hasYPixel |= (y <= MAX_FACE_Y);
                    hasHighlightPixel |= (texPart.Texture[x,y] == PlayerTextureUtils.SKIN_HIGHLIGHT_COLOR);
                    if(pxCount > 4 && hasYPixel && hasHighlightPixel) return true;
                }
            }
            return false;
        }
    }
}
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Celeste;

namespace Celeste.Mod.Procedurline {
    public static class PlayerTextureHelper {
        public static int MIN_FACE_Y = 4;

        public static Color SKIN_COLOR = new Color(217, 160, 102); //#d9a066
        public static Color SKIN_HIGHLIGHT_COLOR = new Color(238, 195, 154); //#eec39a
        public static Color SHIRT_PRIMARY_COLOR = new Color(91, 110, 225); //#5b6ee1
        public static Color SHIRT_SECONDARY_COLOR = new Color(63, 63, 116); //3f3f74

        public static List<List<Point>> GetComponents(TextureData tex) {
            return TextureHelper.JoinComponents(tex.GetColorComponents(), 
                (compA, compB) => {
                    //Join skin components
                    if(
                        tex[compA[0]].IsApproximately(SKIN_COLOR, SKIN_HIGHLIGHT_COLOR) && 
                        tex[compB[0]].IsApproximately(SKIN_COLOR, SKIN_HIGHLIGHT_COLOR)
                    ) return true;

                    //Join shirt components
                    if(
                        tex[compA[0]].IsApproximately(SHIRT_PRIMARY_COLOR, SHIRT_SECONDARY_COLOR) && 
                        tex[compB[0]].IsApproximately(SHIRT_PRIMARY_COLOR, SHIRT_SECONDARY_COLOR)
                    ) return true;

                    return false;
                }
            );
        }

        public static bool IsFaceComponent(TextureData tex, List<Point> comp) {
            return comp.Count > 4 && comp.Any(p => tex[p].IsApproximately(SKIN_HIGHLIGHT_COLOR)) && comp.Any(p => p.Y <= MIN_FACE_Y);
        }

        public static bool IsShirtComponent(TextureData tex, List<Point> comp) {
            return comp.Any(p => tex[p].IsApproximately(SHIRT_PRIMARY_COLOR, SHIRT_SECONDARY_COLOR));
        }
    }
}
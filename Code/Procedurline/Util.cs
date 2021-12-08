using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Celeste;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public static class RandomHelper {
        public static Vector2 NextVector(this Random rng, float magnitude) {
            return Vector2.UnitX.Rotate(rng.NextAngle()) * magnitude;
        }
    }

    public static class CollectionHelper {
        private class ConcatEnumerator : IEnumerator {
            private IEnumerator iterA, iterB;
            private bool useB;

            public ConcatEnumerator(IEnumerator a, IEnumerator b) {
                iterA = a;
                iterB = b;
                useB = false;
            }

            public object Current => useB ? iterB.Current : iterA.Current;

            public bool MoveNext() {
                if(!useB) {
                    if(!iterA.MoveNext()) useB = true;
                    else return true;
                }
                return iterB.MoveNext();
            }

            public void Reset() {
                iterA.Reset();
                iterB.Reset();
                useB = false;
            }
        }

        public static IEnumerator Concat(this IEnumerator self, IEnumerator other) {
            return new ConcatEnumerator(self, other);
        }

        public static V GetValueSafe<K,V>(this IReadOnlyDictionary<K,V> dict, K key, V def = default) => dict.TryGetValue(key, out V val) ? val : def;
    }

    public static class ColorHelper {
        private const int FUZZINESS = 8;
        private static readonly float SQRT_ONE_THIRD = (float) Math.Sqrt(1f/3f);

        public static Matrix CalculateHueShiftMatrix(float hueShift) {
            float sinShift = (float) Math.Sin(hueShift / 180 * Math.PI), cosShift = (float) Math.Cos(hueShift / 180 * Math.PI);
            return new Matrix(
                cosShift + (1-cosShift) / 3, 1f/3f * (1-cosShift) + SQRT_ONE_THIRD * sinShift, 1f/3f * (1-cosShift) - SQRT_ONE_THIRD * sinShift, 0,
                1f/3f * (1-cosShift) - SQRT_ONE_THIRD * sinShift, cosShift + (1-cosShift) / 3, 1f/3f * (1-cosShift) + SQRT_ONE_THIRD * sinShift, 0,
                1f/3f * (1-cosShift) + SQRT_ONE_THIRD * sinShift, 1f/3f * (1-cosShift) - SQRT_ONE_THIRD * sinShift, cosShift + (1-cosShift) / 3, 0,
                0, 0, 0, 1
            );
        }

        public static Color RemoveAlpha(this Color color) {
            return new Color(color.R, color.G, color.B, 255);
        }

        public static bool IsApproximately(this Color color, params Color[] others) {
            foreach(Color other in others) {
                if(
                    Math.Abs(color.R - other.R) <= FUZZINESS &&
                    Math.Abs(color.B - other.B) <= FUZZINESS &&
                    Math.Abs(color.G - other.G) <= FUZZINESS &&
                    Math.Abs(color.A - other.A) <= FUZZINESS
                ) return true;
            }
            return false;
        }

        public static float GetHue(this Color color) {
            float minComp = Calc.Min(color.R, color.G, color.B), maxComp = Calc.Max(color.R, color.G, color.B);
            if(maxComp == color.R) return 60 * (color.G - color.B) / (maxComp-minComp);
            else if(maxComp == color.G) return 60 * (2 + (color.B - color.R) / (maxComp-minComp));
            else return 60 * (4 + (color.R - color.G) / (maxComp-minComp));
        }

        public static Color ShiftColor(this Color color, Matrix hueShift, float intensityShift, bool ignoreWhiteBlack=true) {
            Vector3 shiftedRGB = Vector3.Transform(color.ToVector3(), hueShift);
            if(!ignoreWhiteBlack && (color.IsApproximately(Color.White) || color.IsApproximately(Color.Black))) return new Color(new Vector4(shiftedRGB, color.A));
            else return new Color(new Vector4(shiftedRGB * intensityShift, color.A));
        }

        public static ParticleType ShiftColor(this ParticleType type, Matrix hueShift, float intensityShift, bool ignoreWhiteBlack=true) => new ParticleType(type) {
            Color = type.Color.ShiftColor(hueShift, intensityShift, ignoreWhiteBlack),
            Color2 = type.Color2.ShiftColor(hueShift, intensityShift, ignoreWhiteBlack)
        };

        public static Color Recolor(this Color col, Color origCol, Color newCol, bool ignoreWhiteBlack=true) => col.ShiftColor(CalculateHueShiftMatrix(newCol.GetHue() - origCol.GetHue()), (float) (newCol.R+newCol.G+newCol.B) / (origCol.R+origCol.G+origCol.B), ignoreWhiteBlack);
        public static ParticleType Recolor(this ParticleType type, Color origCol, Color newCol, bool ignoreWhiteBlack=true) => type.ShiftColor(CalculateHueShiftMatrix(newCol.GetHue() - origCol.GetHue()), (float) (newCol.R+newCol.G+newCol.B) / (origCol.R+origCol.G+origCol.B), ignoreWhiteBlack);

    }

    public static class TextureHelper {
        private static readonly Dictionary<VirtualTexture, TextureData> TEX_DATA_CACHE = new Dictionary<VirtualTexture, TextureData>();

        public static TextureData GetTextureData(this VirtualTexture tex) {
            if(!TEX_DATA_CACHE.TryGetValue(tex, out TextureData data)) {
                TEX_DATA_CACHE[tex] = data = new TextureData(tex.Width, tex.Height);
                tex.Texture_Safe.GetData<Color>(data.Pixels);
            }
            return data;
        }

        public static TextureData GetTextureData(this MTexture tex) => GetTextureData(tex.Texture).GetSubsection(tex.ClipRect);

        public static MTexture CreateMTexture(this TextureData data, string name) {
            VirtualTexture vTex = VirtualContent.CreateTexture(name, data.Width, data.Height, Color.White);
            ProcedurlineModule.UploadTexture(vTex, data);
            return new MTexture(vTex);
        }

        public static List<List<Point>> GetColorComponents(this TextureData data) {
            List<List<Point>> comps = new List<List<Point>>();
            HashSet<Point> visited = new HashSet<Point>();

            //Perform a DFS from every pixel
            foreach(Point p in data) {
                if(!visited.Contains(p)) {
                    //Create new component
                    List<Point> comp = new List<Point>();
                    ComponentDFS(p, data[p], ref comp, ref visited, ref data);
                    comps.Add(comp);
                }
            }

            return comps;
        }

        private static void ComponentDFS(Point p, Color col, ref List<Point> comp, ref HashSet<Point> visited, ref TextureData data) {
            if(!data.InBounds(p)) return;
            if(visited.Contains(p)) return;

            //If the pixel doesn't match the component, don't add it
            if(data[p] != col) return;

            //Mark the pixel visited
            visited.Add(p);

            //Add pixel to component
            comp.Add(p);

            //Spread to neighbour pixels
            ComponentDFS(new Point(p.X - 1, p.Y), col, ref comp, ref visited, ref data);
            ComponentDFS(new Point(p.X + 1, p.Y), col, ref comp, ref visited, ref data);
            ComponentDFS(new Point(p.X, p.Y - 1), col, ref comp, ref visited, ref data);
            ComponentDFS(new Point(p.X, p.Y + 1), col, ref comp, ref visited, ref data);
        }

        public static bool AreComponentsTouching(List<Point> compA, List<Point> compB) {
            if(compA.Count <= 0 || compB.Count <= 0) return false;

            //Iterate over all points in component A
            foreach(Point p in compA) {
                //Is a neighbouring point in component B?
                foreach(Point p2 in new Point[] { p, new Point(p.X - 1, p.Y), new Point(p.X + 1, p.Y), new Point(p.X, p.Y - 1), new Point(p.X, p.Y + 1) }) {
                    if(compB.Contains(p2)) return true;
                }
            }

            return false;
        }

        public static List<List<Point>> JoinComponents(List<List<Point>> comps, Func<List<Point>, List<Point>, bool> shouldJoin) {
            //Create initial merge components
            List<Tuple<bool, List<Point>>> mergeComps = new List<Tuple<bool, List<Point>>>();
            mergeComps.AddRange(comps.Select(i => new Tuple<bool, List<Point>>(false, i)));

            //Do merge rounds
            bool didMerge;
            do {
                didMerge = false;

                //Iterate over all combinations of components
                for(int i = 0; i < mergeComps.Count-1; i++) {
                    if(mergeComps[i].Item1) continue;
                    for(int j = i+1; j < mergeComps.Count; j++) {
                        if(mergeComps[j].Item1) continue;

                        //Is join condition fullfilled?
                        if(AreComponentsTouching(mergeComps[i].Item2, mergeComps[j].Item2) && shouldJoin(mergeComps[i].Item2, mergeComps[j].Item2)) {
                            //Merge components
                            mergeComps[i].Item2.AddRange(mergeComps[j].Item2);
                            mergeComps[j] = new Tuple<bool, List<Point>>(true, default);
                            didMerge = true;
                        }
                    }
                }
            } while(didMerge);

            //Create output components
            return mergeComps.Where(i => !i.Item1).Select(i => i.Item2).ToList();
        }
    }

    public static class SpriteHelper {
        private static readonly Func<Sprite, Sprite, Sprite> CLONEINTO_DELEG = (Func<Sprite, Sprite, Sprite>) typeof(Sprite).GetMethod("CloneInto", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate(typeof(Func<Sprite, Sprite, Sprite>));
        public static Sprite CloneInto(this Sprite sprite, Sprite clone) => CLONEINTO_DELEG(sprite, clone);

        public static Sprite ShiftColor(this Sprite sprite, Matrix hueShift, float intensityShift, bool ignoreWhiteBlack=true) {
            //Filter animation frames
            TextureHeap heap = new TextureHeap();
            Dictionary<Sprite.Animation, Rectangle[]> animFrames = new Dictionary<Sprite.Animation, Rectangle[]>();
            foreach(var anim in sprite.Animations) {
                Rectangle[] frames = animFrames[anim.Value] = new Rectangle[anim.Value.Frames.Length];
                for(int i = 0; i < frames.Length; i++) {
                    //Shift pixel hues
                    TextureData data = anim.Value.Frames[i].GetTextureData();
                    foreach(Point p in data) data[p] = data[p].ShiftColor(hueShift, intensityShift, ignoreWhiteBlack);
                    frames[i] = heap.AddTexture(data);
                }
            }
            MTexture heapTex = heap.CreateHeapTexture().CreateMTexture($"filteredSprite<{sprite.GetHashCode()}:{new Tuple<Matrix, float>(hueShift, intensityShift).GetHashCode()}>");

            //Create new sprite
            Sprite newSprite = new Sprite(null, null);
            sprite.CloneInto(newSprite);
            foreach(var anim in newSprite.Animations) {
                anim.Value.Frames = Enumerable.Range(0, anim.Value.Frames.Length).Select(idx => {
                    MTexture oTex = anim.Value.Frames[idx];
                    MTexture nTex = new MTexture(heapTex, oTex.AtlasPath, animFrames[anim.Value][idx], oTex.DrawOffset, oTex.Width, oTex.Height) {
                        Atlas = oTex.Atlas,
                        ScaleFix = oTex.ScaleFix
                    };
                    return nTex;
                }).ToArray();
            }
            return newSprite;
        }
        public static Sprite Recolor(this Sprite sprite, Color origCol, Color newCol, bool ignoreWhiteBlack=true) => sprite.ShiftColor(ColorHelper.CalculateHueShiftMatrix(newCol.GetHue() - origCol.GetHue()), (float) (newCol.R+newCol.G+newCol.B) / (origCol.R+origCol.G+origCol.B), ignoreWhiteBlack);
    }

    public static class AreaHelper {
        public static AreaKey ParseAreaKey(this string str) {
            string sid = str;
            AreaMode mode = AreaMode.Normal;

            int hashTagIndex = str.LastIndexOf('#');
            if(hashTagIndex >= 0 && hashTagIndex == str.Length-2) {
                switch(char.ToLower(str[hashTagIndex+1])) {
                    case 'a': str = str.Substring(hashTagIndex); mode = AreaMode.Normal; break;
                    case 'b': str = str.Substring(hashTagIndex); mode = AreaMode.BSide; break;
                    case 'c': str = str.Substring(hashTagIndex); mode = AreaMode.CSide; break;
                }
            }

            return new AreaKey() { SID = sid, Mode = mode };
        }
    }
}

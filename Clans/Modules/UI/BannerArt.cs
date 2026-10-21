using Clans.Core;
using Clans.Gameplay;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.UI {
    // Paints clan emblems onto the clan banners' cloth (Gameplay/ClanBanner) and their build menu icons.
    //
    // Vanilla's banner cloth is mirrored: both halves read the same narrow strip of its texture, which suits a
    // symmetric pattern and cannot show an emblem. So the cloth gets a copy of its mesh whose u runs edge to edge
    // across the width (v is kept). Every vanilla banner shares that mesh, so the copy and the emblem's place on it are
    // made once. Each colour then gets a blank fabric from its own vanilla banner: the plain cloth inside the
    // banner's frame (texture and normal map), stretched over the whole cloth with Graphics.Blit (each half, the far
    // one mirrored), so neither the frame's lines nor their embossing come along. That is a banner with no emblem.
    // For each colour and emblem shown, the two emblem layers are drawn over the fabric once on the GPU with UI/Default,
    // the shader the Clans window draws emblems with, and read back into a compressed texture shared by every banner
    // of that colour with that emblem.
    //
    // Clients only. If the cloth is not laid out as expected (a game update changing the banners), banners stay plain
    // vanilla banners and this says so once.
    internal static class BannerArt {
        // About four times vanilla's detail; the normal map comes from a small vanilla texture and needs less.
        private const int ClothWidth = 512;
        private const int ClothHeight = 1024;
        private const int NormalWidth = 256;
        private const int NormalHeight = 512;
        private const int IconSize = 128;
        // The plain fabric inside the vanilla banners' frames, in their textures: from this share of the way between
        // the strip's edge and its middle, to the middle, and up to this v (where it starts is each colour's,
        // BannerVariant.FabricBottomV). Every colour's texture and normal map are clear of its frame there; the curly
        // frames and the normal maps' embossing reach further in than the knotwork's drawn lines.
        private const float FieldFromEdge = 0.54f;
        private const float FieldTopV = 0.70f;
        // The emblem's width as a share of the cloth's, centred on the cloth's body between its top and where the
        // point begins. The background sprites carry their own margin, so this leaves a little fabric at the sides.
        private const float EmblemWidth = 0.9f;
        // The symbol sits inside the background as in the Clans window, whose Front layer is inset by a fifth.
        private const float FrontInset = 0.2f;
        // The menu icon: the banner standing in the icon as vanilla's do, under a short pole.
        private const float IconBottom = 0.04f;
        private const float IconClothHeight = 0.84f;
        private const float IconPoleHeight = 0.035f;
        private const float IconPoleOverhang = 0.12f;
        private static readonly Color PoleColour = new Color(0.42f, 0.29f, 0.17f, 1f);
        // Painted emblems no banner shows any more, kept for when one comes back into view.
        private const int SpareArt = 8;
        // How far the cloth's texture coordinates may stray from the mirrored layout this expects.
        private const float FitTolerance = 0.002f;
        private const string BumpMap = "_BumpMap";

        internal sealed class Cloth {
            internal MeshRenderer Renderer;
            internal MeshFilter Filter;
        }

        // One colour's blank cloth.
        internal sealed class Fabric {
            internal string Key;
            internal Material Vanilla;
            internal Texture2D Texture;
            internal Texture2D Normal;
            internal Material Plain;
        }

        // One painted emblem on one colour, shared by every banner showing it.
        internal sealed class Art {
            internal string Key;
            internal Texture2D Texture;
            internal Material Material;
            internal int Users;
            internal float UnusedSince;
        }

        private static bool prepared;
        private static bool ready;
        // The vanilla cloth mesh this was fitted to, the copy with u edge to edge, and the strip's edge (a) and width
        // (b) in vanilla's u.
        private static Mesh source;
        private static Mesh mesh;
        private static float stripEdge;
        private static float stripWidth;
        // The emblem's square on the cloth, in texture coordinates.
        private static Rect emblemRect;
        // The cloth flattened into the menu icon: each vertex's place in it, the new texture coordinates and the
        // triangles; and the emblem's square and the pole there.
        private static Vector2[] iconPoints;
        private static Vector2[] iconUvs;
        private static int[] iconTriangles;
        private static Rect iconEmblem;
        private static Rect iconPole;
        private static Material layerMaterial;

        private static readonly Dictionary<string, Fabric> fabrics = new Dictionary<string, Fabric>();
        // Normal maps laid out once per vanilla texture and field: banners of several colours share one.
        private static readonly Dictionary<string, Texture2D> normals = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Art> arts = new Dictionary<string, Art>();
        // The emblem catalog the cached art was painted from.
        private static EmblemSettings catalog;

        // The banner's cloth: the renderer with one of vanilla's banner cloth materials (the other one is the beam).
        internal static Cloth FindCloth(GameObject banner) {
            foreach (MeshRenderer renderer in banner.GetComponentsInChildren<MeshRenderer>(true)) {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Material material = renderer.sharedMaterial;
                if (filter == null || filter.sharedMesh == null || material == null) { continue; }
                if (material.name.IndexOf("banner", StringComparison.OrdinalIgnoreCase) >= 0) {
                    return new Cloth { Renderer = renderer, Filter = filter };
                }
            }
            return null;
        }

        // A colour's blank cloth, made from its vanilla banner's cloth the first time it is asked for (call it before
        // Show replaces that cloth's material). Null when banners have to stay vanilla.
        internal static Fabric FabricOf(Cloth cloth, ClansPrefabs.BannerVariant variant) {
            if (cloth == null || variant == null) { return null; }
            if (fabrics.TryGetValue(variant.Prefab, out Fabric known)) { return known; }
            if (Prepare(cloth) == false) { return null; }
            if (cloth.Filter.sharedMesh != source) {
                Logger.LogWarning($"{variant.Base} hangs a different cloth from the other banners; its clan banner looks like a plain banner.");
                fabrics[variant.Prefab] = null;
                return null;
            }
            Fabric fabric = null;
            try {
                fabric = Weave(variant, cloth.Renderer.sharedMaterial);
            } catch (Exception e) {
                Logger.LogError($"Could not make the {variant.Colour} clan banner's cloth; it looks like a plain banner: {e}");
            }
            fabrics[variant.Prefab] = fabric;
            return fabric;
        }

        // The mesh copy and the emblem's place, from the first banner seen.
        private static bool Prepare(Cloth sample) {
            if (prepared) { return ready; }
            prepared = true;
            try {
                ready = Build(sample.Filter.sharedMesh);
            } catch (Exception e) {
                Logger.LogError($"Could not prepare the clan banners' cloth; clan banners look like plain banners: {e}");
                ready = false;
            }
            return ready;
        }

        // Puts up an emblem's art on a cloth, or with none the colour's plain cloth.
        internal static void Show(Cloth cloth, Fabric fabric, Art art) {
            if (cloth == null || fabric == null) { return; }
            cloth.Filter.sharedMesh = mesh;
            cloth.Renderer.sharedMaterial = art != null ? art.Material : fabric.Plain;
        }

        internal static string Key(EmblemData emblem) {
            EmblemData clean = ClansSettings.Emblems.Sanitize(emblem);
            return $"{clean.BackId}|{clean.BackColor}|{clean.FrontId}|{clean.FrontColor}";
        }

        // The art for an emblem on a colour, painted the first time it is asked for. Null (the plain cloth) for no
        // emblem.
        internal static Art Acquire(Fabric fabric, EmblemData emblem) {
            if (fabric == null || emblem == null) { return null; }
            CatalogChanged();
            string key = fabric.Key + "|" + Key(emblem);
            if (arts.TryGetValue(key, out Art art) == false) {
                art = Paint(key, fabric, ClansSettings.Emblems.Sanitize(emblem));
                if (art == null) { return null; }
                arts[key] = art;
            }
            art.Users++;
            return art;
        }

        internal static void Release(Art art) {
            if (art == null) { return; }
            art.Users--;
            if (art.Users > 0) { return; }
            art.UnusedSince = Time.realtimeSinceStartup;
            // Painted from an older catalog: nothing will ask for it again.
            if (arts.TryGetValue(art.Key, out Art current) == false || current != art) {
                Destroy(art);
                return;
            }
            Trim();
        }

        // True when the emblem catalog is not the one the cached art was painted from. That art is dropped from the
        // cache, and each piece of it is destroyed once no banner shows it.
        internal static bool CatalogChanged() {
            if (ReferenceEquals(catalog, ClansSettings.Emblems)) { return false; }
            catalog = ClansSettings.Emblems;
            foreach (Art art in arts.Values) {
                if (art.Users <= 0) { Destroy(art); }
            }
            arts.Clear();
            return true;
        }

        private static void Trim() {
            List<Art> spare = arts.Values.Where(a => a.Users <= 0).OrderBy(a => a.UnusedSince).ToList();
            for (int i = 0; i < spare.Count - SpareArt; i++) {
                arts.Remove(spare[i].Key);
                Destroy(spare[i]);
            }
        }

        private static void Destroy(Art art) {
            UnityEngine.Object.Destroy(art.Material);
            UnityEngine.Object.Destroy(art.Texture);
        }

        // ---------------------------------------------------------------- the cloth

        private static bool Build(Mesh vanilla) {
            if (vanilla.isReadable == false) { return Unexpected("its mesh cannot be read"); }
            Vector3[] vertices = vanilla.vertices;
            Vector3[] vertexNormals = vanilla.normals;
            Vector2[] uvs = vanilla.uv;
            int count = vertices.Length;
            if (count == 0 || uvs.Length != count) { return Unexpected("its mesh has no texture coordinates"); }

            // Across the cloth as seen from the side its normals face, so the emblem reads left to right from there
            // (and mirrored from behind, as any single sheet would).
            Vector3 facing = Vector3.zero;
            foreach (Vector3 normal in vertexNormals) { facing += normal; }
            if (facing.sqrMagnitude < 0.0001f) { facing = Vector3.right; }
            Vector3 across = Vector3.Cross(Vector3.up, -facing.normalized);
            if (across.sqrMagnitude < 0.25f) { return Unexpected("it does not hang upright"); }
            across.Normalize();

            float min = float.MaxValue;
            float max = float.MinValue;
            float top = float.MinValue;
            float bottom = float.MaxValue;
            foreach (Vector3 vertex in vertices) {
                float d = Vector3.Dot(vertex, across);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
                top = Mathf.Max(top, vertex.y);
                bottom = Mathf.Min(bottom, vertex.y);
            }
            float width = max - min;
            if (width < 0.01f || top - bottom < 0.01f) { return Unexpected("it has no size"); }

            // Vanilla's u is a at both edges, rising to a + b in the middle; its v runs straight up the cloth.
            float[] fromEdge = new float[count];
            float[] us = new float[count];
            float[] heights = new float[count];
            float[] vs = new float[count];
            Vector2[] flat = new Vector2[count];
            float[] across01 = new float[count];
            // Where the point begins: the lowest vertex on either side edge.
            float bodyBottom = float.MaxValue;
            for (int i = 0; i < count; i++) {
                float s = (Vector3.Dot(vertices[i], across) - min) / width;
                across01[i] = s;
                fromEdge[i] = 1f - Mathf.Abs(2f * s - 1f);
                if (fromEdge[i] < 0.02f) { bodyBottom = Mathf.Min(bodyBottom, vertices[i].y); }
                us[i] = uvs[i].x;
                heights[i] = vertices[i].y;
                vs[i] = uvs[i].y;
                flat[i] = new Vector2(s, uvs[i].y);
            }
            Fit(fromEdge, us, out float a, out float b, out float uMiss);
            Fit(heights, vs, out float vAtZero, out float vPerMetre, out float vMiss);
            if (uMiss > FitTolerance || b < 0.01f) { return Unexpected($"its texture is not mirrored across the middle (off by {uMiss:0.####})"); }
            if (vMiss > FitTolerance || vPerMetre <= 0f) { return Unexpected($"its texture does not run straight up the cloth (off by {vMiss:0.####})"); }

            source = vanilla;
            mesh = UnityEngine.Object.Instantiate(vanilla);
            mesh.name = "Clans_BannerCloth";
            mesh.uv = flat;
            mesh.RecalculateTangents();
            stripEdge = a;
            stripWidth = b;

            // A square on the cloth: EmblemWidth of its width across, the same distance in metres up.
            float side = EmblemWidth * width;
            float centreY = (top + Mathf.Min(bodyBottom, top)) / 2f;
            float height = side * vPerMetre;
            emblemRect = new Rect(0.5f - EmblemWidth / 2f, vAtZero + vPerMetre * centreY - height / 2f, EmblemWidth, height);

            // The icon: the cloth to scale, standing on IconBottom.
            float scale = IconClothHeight / (top - bottom);
            iconPoints = new Vector2[count];
            for (int i = 0; i < count; i++) {
                iconPoints[i] = new Vector2(0.5f + (across01[i] - 0.5f) * width * scale, IconBottom + (heights[i] - bottom) * scale);
            }
            iconUvs = flat;
            iconTriangles = vanilla.triangles;
            float iconSide = side * scale;
            iconEmblem = new Rect(0.5f - iconSide / 2f, IconBottom + (centreY - bottom) * scale - iconSide / 2f, iconSide, iconSide);
            float poleWidth = width * scale * (1f + 2f * IconPoleOverhang);
            iconPole = new Rect(0.5f - poleWidth / 2f, IconBottom + IconClothHeight - IconPoleHeight / 2f, poleWidth, IconPoleHeight);
            return true;
        }

        private static bool Unexpected(string why) {
            Logger.LogWarning($"The clan banners' cloth is not laid out as expected ({why}); clan banners look like plain banners.");
            return false;
        }

        // Least squares y = a + b x, and the largest miss.
        private static void Fit(float[] x, float[] y, out float a, out float b, out float miss) {
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            int n = x.Length;
            for (int i = 0; i < n; i++) {
                sx += x[i];
                sy += y[i];
                sxx += x[i] * x[i];
                sxy += x[i] * y[i];
            }
            double denominator = n * sxx - sx * sx;
            b = Math.Abs(denominator) < 1e-12 ? 0f : (float)((n * sxy - sx * sy) / denominator);
            a = (float)((sy - b * sx) / n);
            miss = 0f;
            for (int i = 0; i < n; i++) { miss = Mathf.Max(miss, Mathf.Abs(a + b * x[i] - y[i])); }
        }

        private static Fabric Weave(ClansPrefabs.BannerVariant variant, Material vanilla) {
            if (vanilla.mainTexture == null) {
                Logger.LogWarning($"{variant.Base}'s cloth has no texture; its clan banner looks like a plain banner.");
                return null;
            }
            Rect field = Rect.MinMaxRect(stripEdge + FieldFromEdge * stripWidth, variant.FabricBottomV, stripEdge + stripWidth, FieldTopV);
            Fabric fabric = new Fabric { Key = variant.Prefab, Vanilla = vanilla };
            fabric.Texture = Relay(vanilla.mainTexture, ClothWidth, ClothHeight, false, field);
            fabric.Texture.Apply(true, true);
            Texture bump = vanilla.HasProperty(BumpMap) ? vanilla.GetTexture(BumpMap) : null;
            if (bump != null) {
                string normalKey = $"{bump.GetInstanceID()}|{variant.FabricBottomV:0.###}";
                if (normals.TryGetValue(normalKey, out Texture2D normal) == false) {
                    normal = Relay(bump, NormalWidth, NormalHeight, true, field);
                    TurnFarHalf(normal);
                    normal.Apply(true, true);
                    normals[normalKey] = normal;
                }
                fabric.Normal = normal;
            }
            fabric.Plain = Clone(fabric, fabric.Texture, "Clans_BannerCloth " + variant.Colour);
            return fabric;
        }

        // A vanilla cloth texture's field (x from the strip's edge side to its middle) stretched over a whole cloth laid
        // out for the new u: the left half edge side to middle, the right half back again, and all of v from the
        // field's bottom to its top. Copied as it is, so a normal map keeps its encoding.
        private static Texture2D Relay(Texture texture, int width, int height, bool linear, Rect field) {
            RenderTexture half = RenderTexture.GetTemporary(width / 2, height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, true, linear) {
                name = "Clans_BannerCloth",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            RenderTexture previous = RenderTexture.active;
            try {
                Graphics.Blit(texture, half, new Vector2(field.width, field.height), new Vector2(field.xMin, field.yMin));
                RenderTexture.active = half;
                result.ReadPixels(new Rect(0, 0, width / 2, height), 0, 0, false);
                Graphics.Blit(texture, half, new Vector2(-field.width, field.height), new Vector2(field.xMax, field.yMin));
                RenderTexture.active = half;
                result.ReadPixels(new Rect(0, 0, width / 2, height), width / 2, 0, false);
            } finally {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(half);
            }
            return result;
        }

        // The right half was copied mirrored, so its bumps lean the wrong way across the cloth: turn their sideways
        // part round. The shader reads a normal's x as r times a in both of Unity's layouts (DXT5nm keeps x in a with
        // r = 1, BC5 in r with a = 1), so the turned x goes back into r with a = 1.
        private static void TurnFarHalf(Texture2D normal) {
            Color32[] pixels = normal.GetPixels32();
            int width = normal.width;
            for (int y = 0; y < normal.height; y++) {
                for (int x = width / 2; x < width; x++) {
                    int i = y * width + x;
                    Color32 c = pixels[i];
                    c.r = (byte)(255 - c.r * c.a / 255);
                    c.a = 255;
                    pixels[i] = c;
                }
            }
            normal.SetPixels32(pixels);
        }

        private static Material Clone(Fabric fabric, Texture2D texture, string name) {
            Material material = new Material(fabric.Vanilla) { name = name };
            material.mainTexture = texture;
            if (fabric.Normal != null) { material.SetTexture(BumpMap, fabric.Normal); }
            return material;
        }

        // ---------------------------------------------------------------- painting

        private static Art Paint(string key, Fabric fabric, EmblemData emblem) {
            Material layer = LayerMaterial();
            if (layer == null) { return null; }
            RenderTexture target = RenderTexture.GetTemporary(ClothWidth, ClothHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D texture = new Texture2D(ClothWidth, ClothHeight, TextureFormat.RGB24, true, false) {
                name = "Clans_BannerCloth " + key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            RenderTexture previous = RenderTexture.active;
            try {
                Graphics.Blit(fabric.Texture, target);
                RenderTexture.active = target;
                DrawEmblem(layer, emblem, emblemRect);
                texture.ReadPixels(new Rect(0, 0, ClothWidth, ClothHeight), 0, 0, false);
            } finally {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
            texture.Apply(true, false);
            texture.Compress(false);
            texture.Apply(false, true);
            return new Art { Key = key, Texture = texture, Material = Clone(fabric, texture, texture.name) };
        }

        // A colour's banner with an emblem on it, as the build menu shows it. Null when it cannot be drawn.
        internal static Sprite Icon(Fabric fabric, EmblemData emblem) {
            Material layer = LayerMaterial();
            if (layer == null || fabric == null || emblem == null) { return null; }
            RenderTexture target = RenderTexture.GetTemporary(IconSize, IconSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false, false) { name = "Clans_BannerIcon" };
            RenderTexture previous = RenderTexture.active;
            try {
                RenderTexture.active = target;
                GL.Clear(false, true, Color.clear);
                GL.PushMatrix();
                GL.LoadOrtho();
                try {
                    DrawQuad(layer, Texture2D.whiteTexture, PoleColour, iconPole);
                    layer.mainTexture = fabric.Texture;
                    layer.color = Color.white;
                    layer.SetPass(0);
                    GL.Begin(GL.TRIANGLES);
                    GL.Color(Color.white);
                    foreach (int index in iconTriangles) {
                        GL.TexCoord2(iconUvs[index].x, iconUvs[index].y);
                        GL.Vertex3(iconPoints[index].x, iconPoints[index].y, 0f);
                    }
                    GL.End();
                } finally {
                    GL.PopMatrix();
                }
                DrawEmblem(layer, ClansSettings.Emblems.Sanitize(emblem), iconEmblem);
                texture.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0, false);
            } finally {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
            Unpremultiply(texture);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
        }

        internal static void DestroyIcon(Sprite icon) {
            if (icon == null) { return; }
            UnityEngine.Object.Destroy(icon.texture);
            UnityEngine.Object.Destroy(icon);
        }

        // UI/Default, which the game's own UI draws with: the emblem comes out as it looks in the Clans window.
        private static Material LayerMaterial() {
            if (layerMaterial == null) {
                Material canvas = Canvas.GetDefaultCanvasMaterial();
                if (canvas != null) { layerMaterial = new Material(canvas) { name = "Clans_BannerLayer" }; }
            }
            return layerMaterial;
        }

        private static void DrawQuad(Material layer, Texture texture, Color colour, Rect rect) {
            layer.mainTexture = texture;
            layer.color = colour;
            layer.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(Color.white);
            GL.TexCoord2(0f, 0f);
            GL.Vertex3(rect.xMin, rect.yMin, 0f);
            GL.TexCoord2(0f, 1f);
            GL.Vertex3(rect.xMin, rect.yMax, 0f);
            GL.TexCoord2(1f, 1f);
            GL.Vertex3(rect.xMax, rect.yMax, 0f);
            GL.TexCoord2(1f, 0f);
            GL.Vertex3(rect.xMax, rect.yMin, 0f);
            GL.End();
        }

        // Both emblem layers into the active render texture, whose 0-1 coordinates the box is in. The box is square on
        // whatever it is drawn for, though not necessarily in texels (the cloth's are not).
        private static void DrawEmblem(Material layer, EmblemData emblem, Rect box) {
            EmblemSettings parts = ClansSettings.Emblems;
            GL.PushMatrix();
            GL.LoadOrtho();
            try {
                DrawLayer(layer, parts.FindBackground(emblem.BackId), emblem.BackColor, box);
                Rect inner = new Rect(box.x + box.width * FrontInset, box.y + box.height * FrontInset,
                    box.width * (1f - 2f * FrontInset), box.height * (1f - 2f * FrontInset));
                DrawLayer(layer, parts.FindSymbol(emblem.FrontId), emblem.FrontColor, inner);
            } finally {
                GL.PopMatrix();
            }
        }

        // One part, tinted as EmblemView tints it and fitted into the box keeping its shape. The sprite's own mesh is
        // drawn, so tightly packed atlas sprites (item icons) come out right too.
        private static void DrawLayer(Material layer, EmblemPart part, string colour, Rect box) {
            Sprite sprite = ClansAssets.EmblemSprite(part);
            if (sprite == null || sprite.texture == null) { return; }
            Rect rect = sprite.rect;
            if (rect.width <= 0f || rect.height <= 0f) { return; }
            float aspect = rect.width / rect.height;
            Rect fit = box;
            if (aspect >= 1f) { fit.height = box.height / aspect; } else { fit.width = box.width * aspect; }
            fit.center = box.center;

            layer.mainTexture = sprite.texture;
            layer.color = part.Tint && EmblemColors.TryParse(colour, out Color tint) ? tint : Color.white;
            layer.SetPass(0);

            Vector2[] vertices = sprite.vertices;
            Vector2[] uvs = sprite.uv;
            ushort[] triangles = sprite.triangles;
            float ppu = sprite.pixelsPerUnit;
            Vector2 pivot = sprite.pivot;
            GL.Begin(GL.TRIANGLES);
            GL.Color(Color.white);
            foreach (ushort index in triangles) {
                // Sprite units around its pivot, to pixels in its rect, to the box.
                Vector2 pixel = vertices[index] * ppu + pivot;
                GL.TexCoord2(uvs[index].x, uvs[index].y);
                GL.Vertex3(fit.x + pixel.x / rect.width * fit.width, fit.y + pixel.y / rect.height * fit.height, 0f);
            }
            GL.End();
        }

        // UI/Default writes colour already multiplied by alpha, and the build menu multiplies again, which would darken
        // the icon's soft edges. Divided back out where the shader multiplied (in linear space, as Valheim renders).
        private static void Unpremultiply(Texture2D texture) {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color32[] pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) {
                Color32 c = pixels[i];
                if (c.a == 0 || c.a == 255) { continue; }
                float alpha = c.a / 255f;
                c.r = Unpremultiply(c.r, alpha, linear);
                c.g = Unpremultiply(c.g, alpha, linear);
                c.b = Unpremultiply(c.b, alpha, linear);
                pixels[i] = c;
            }
            texture.SetPixels32(pixels);
        }

        private static byte Unpremultiply(byte value, float alpha, bool linear) {
            float v = value / 255f;
            v = linear ? Mathf.LinearToGammaSpace(Mathf.Min(1f, Mathf.GammaToLinearSpace(v) / alpha)) : Mathf.Min(1f, v / alpha);
            return (byte)Mathf.RoundToInt(v * 255f);
        }
    }
}

#if TMP_MSDFA_UGUI_PATCHED
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace TMPro
{
    internal static unsafe class TMP_MSDFAAtlasRenderer
    {
        private static readonly Dictionary<TMP_FontAsset, FontCache> FontCacheLookup = new Dictionary<TMP_FontAsset, FontCache>();

        internal const string DistanceFieldShaderName = "TextMeshPro/MSDFA";

        internal static TextureFormat GetAtlasTextureFormat(GlyphRenderMode renderMode, bool isMsdfaAtlasEnabled)
        {
            if (isMsdfaAtlasEnabled && IsDistanceFieldRenderMode(renderMode))
                return TextureFormat.RGBA32;

            return ((GlyphRasterModes)renderMode & GlyphRasterModes.RASTER_MODE_COLOR) == GlyphRasterModes.RASTER_MODE_COLOR ? TextureFormat.RGBA32 : TextureFormat.Alpha8;
        }

        internal static Shader GetDistanceFieldShader(bool isMsdfaAtlasEnabled, Shader defaultShader)
        {
            if (isMsdfaAtlasEnabled)
            {
                Shader shader = Shader.Find(DistanceFieldShaderName);
                if (shader != null)
                    return shader;
            }

            return defaultShader;
        }

        internal static void ResizeAtlasTexture(Texture2D atlasTexture, int width, int height, GlyphRenderMode renderMode, bool isMsdfaAtlasEnabled)
        {
            TextureFormat textureFormat = GetAtlasTextureFormat(renderMode, isMsdfaAtlasEnabled);
            #if UNITY_2021_2_OR_NEWER
            atlasTexture.Reinitialize(width, height, textureFormat, false);
            #else
            atlasTexture.Resize(width, height, textureFormat, false);
            #endif
        }

        internal static bool TryAddGlyphToTexture(uint glyphIndex, int padding, GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode, Texture2D atlasTexture, TMP_FontAsset fontAsset, bool isMsdfaAtlasEnabled, out Glyph glyph)
        {
            if (CanUseMsdfaAtlas(renderMode, atlasTexture, isMsdfaAtlasEnabled) == false)
                return FontEngine.TryAddGlyphToTexture(glyphIndex, padding, packingMode, freeGlyphRects, usedGlyphRects, renderMode, atlasTexture, out glyph);

            if (TryGetMsdfaFont(fontAsset, out TMP_MSDFAFont msdfaFont) == false)
                return TryAddGlyphToTextureWithSdfFallback(glyphIndex, padding, packingMode, freeGlyphRects, usedGlyphRects, renderMode, atlasTexture, out glyph);

            GlyphLoadFlags loadFlags = GlyphLoadFlags.LOAD_NO_BITMAP | GlyphLoadFlags.LOAD_NO_HINTING;
            if (FontEngine.TryGetGlyphWithIndexValue(glyphIndex, loadFlags, out glyph) == false)
                return false;

            if (FontEngine.TryPackGlyphInAtlas(glyph, padding, packingMode, renderMode, atlasTexture.width, atlasTexture.height, freeGlyphRects, usedGlyphRects) == false)
                return false;

            if (RenderGlyphToMsdfaAtlas(msdfaFont, glyph, padding, atlasTexture, fontAsset.useMsdfaFillRuleSign) == false)
            {
                RenderGlyphsWithSdfFallback(new List<Glyph> { glyph }, padding, renderMode, atlasTexture);
            }
            else
            {
                atlasTexture.Apply(false, false);
            }

            return true;
        }

        internal static bool TryAddGlyphsToTexture(List<uint> glyphsToAdd, int padding, GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode, Texture2D atlasTexture, TMP_FontAsset fontAsset, bool isMsdfaAtlasEnabled, out Glyph[] glyphs)
        {
            if (CanUseMsdfaAtlas(renderMode, atlasTexture, isMsdfaAtlasEnabled) == false)
                return FontEngine.TryAddGlyphsToTexture(glyphsToAdd, padding, packingMode, freeGlyphRects, usedGlyphRects, renderMode, atlasTexture, out glyphs);

            if (TryGetMsdfaFont(fontAsset, out TMP_MSDFAFont msdfaFont) == false)
                return TryAddGlyphsToTextureWithSdfFallback(glyphsToAdd, padding, packingMode, freeGlyphRects, usedGlyphRects, renderMode, atlasTexture, out glyphs);

            GlyphLoadFlags loadFlags = GlyphLoadFlags.LOAD_NO_BITMAP | GlyphLoadFlags.LOAD_NO_HINTING;
            List<Glyph> glyphsToPack = new List<Glyph>(glyphsToAdd.Count);
            for (int i = 0; i < glyphsToAdd.Count; i++)
            {
                if (FontEngine.TryGetGlyphWithIndexValue(glyphsToAdd[i], loadFlags, out Glyph glyphToPack))
                    glyphsToPack.Add(glyphToPack);
            }

            if (glyphsToPack.Count == 0)
            {
                glyphs = new Glyph[0];
                return false;
            }

            List<Glyph> glyphsAdded = new List<Glyph>(glyphsToPack.Count);
            bool allGlyphsAdded = FontEngine.TryPackGlyphsInAtlas(glyphsToPack, glyphsAdded, padding, packingMode, renderMode, atlasTexture.width, atlasTexture.height, freeGlyphRects, usedGlyphRects);
            glyphs = glyphsAdded.ToArray();

            List<Glyph> fallbackGlyphs = null;
            for (int i = 0; i < glyphsAdded.Count; i++)
            {
                if (RenderGlyphToMsdfaAtlas(msdfaFont, glyphsAdded[i], padding, atlasTexture, fontAsset.useMsdfaFillRuleSign))
                    continue;

                if (fallbackGlyphs == null)
                    fallbackGlyphs = new List<Glyph>();

                fallbackGlyphs.Add(glyphsAdded[i]);
            }

            if (fallbackGlyphs != null)
                RenderGlyphsWithSdfFallback(fallbackGlyphs, padding, renderMode, atlasTexture);

            atlasTexture.Apply(false, false);

            return allGlyphsAdded;
        }

        private static bool TryAddGlyphToTextureWithSdfFallback(uint glyphIndex, int padding, GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode, Texture2D atlasTexture, out Glyph glyph)
        {
            Texture2D scratchTexture = CreateScratchTexture(atlasTexture);
            try
            {
                bool isAdded = FontEngine.TryAddGlyphToTexture(glyphIndex, padding, packingMode, freeGlyphRects, usedGlyphRects, renderMode, scratchTexture, out glyph);
                if (isAdded)
                    CopyGlyphsToMsdfaAtlas(scratchTexture, atlasTexture, padding, glyph);

                return isAdded;
            }
            finally
            {
                ReleaseScratchTexture(scratchTexture);
            }
        }

        private static bool TryAddGlyphsToTextureWithSdfFallback(List<uint> glyphsToAdd, int padding, GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode, Texture2D atlasTexture, out Glyph[] glyphs)
        {
            Texture2D scratchTexture = CreateScratchTexture(atlasTexture);
            try
            {
                bool allGlyphsAdded = FontEngine.TryAddGlyphsToTexture(glyphsToAdd, padding, packingMode, freeGlyphRects, usedGlyphRects, renderMode, scratchTexture, out glyphs);
                CopyGlyphsToMsdfaAtlas(scratchTexture, atlasTexture, padding, glyphs);
                return allGlyphsAdded;
            }
            finally
            {
                ReleaseScratchTexture(scratchTexture);
            }
        }

        internal static bool RenderGlyphsToTexture(List<Glyph> glyphs, int padding, GlyphRenderMode renderMode, Texture2D atlasTexture, TMP_FontAsset fontAsset, bool isMsdfaAtlasEnabled)
        {
            if (glyphs == null || glyphs.Count == 0)
            {
                atlasTexture.Apply(false, false);
                return true;
            }

            if (CanUseMsdfaAtlas(renderMode, atlasTexture, isMsdfaAtlasEnabled) == false)
            {
                FontEngine.RenderGlyphsToTexture(glyphs, padding, renderMode, atlasTexture);
                atlasTexture.Apply(false, false);
                return false;
            }

            if (TryGetMsdfaFont(fontAsset, out TMP_MSDFAFont msdfaFont) == false)
            {
                RenderGlyphsWithSdfFallback(glyphs, padding, renderMode, atlasTexture);
                return false;
            }

            List<Glyph> fallbackGlyphs = null;
            for (int i = 0; i < glyphs.Count; i++)
            {
                if (RenderGlyphToMsdfaAtlas(msdfaFont, glyphs[i], padding, atlasTexture, fontAsset.useMsdfaFillRuleSign))
                    continue;

                if (fallbackGlyphs == null)
                    fallbackGlyphs = new List<Glyph>();

                fallbackGlyphs.Add(glyphs[i]);
            }

            if (fallbackGlyphs != null)
            {
                RenderGlyphsWithSdfFallback(fallbackGlyphs, padding, renderMode, atlasTexture);
                return false;
            }

            atlasTexture.Apply(false, false);
            return true;
        }

        private static void RenderGlyphsWithSdfFallback(List<Glyph> glyphs, int padding, GlyphRenderMode renderMode, Texture2D atlasTexture)
        {
            Texture2D scratchTexture = CreateScratchTexture(atlasTexture);
            try
            {
                FontEngine.RenderGlyphsToTexture(glyphs, padding, renderMode, scratchTexture);
                CopyGlyphsToMsdfaAtlas(scratchTexture, atlasTexture, padding, glyphs.ToArray());
            }
            finally
            {
                ReleaseScratchTexture(scratchTexture);
            }
        }

        private static bool RenderGlyphToMsdfaAtlas(TMP_MSDFAFont font, Glyph glyph, int padding, Texture2D atlasTexture, bool useFillRuleSign)
        {
            if (font.TryGetGlyphShape(glyph.index, out TMP_MSDFAFont.GlyphShape shape) == false)
                return false;

            TMP_MSDFAFont.Bounds bounds = shape.Bounds;
            if (bounds.IsValid == false || bounds.Width <= 0 || bounds.Height <= 0)
                return false;

            GlyphRect glyphRect = glyph.glyphRect;
            int contentWidth = Math.Max(1, glyphRect.width);
            int contentHeight = Math.Max(1, glyphRect.height);
            float scaleX = contentWidth / bounds.Width;
            float scaleY = contentHeight / bounds.Height;
            float unitScale = Math.Min(scaleX, scaleY);
            if (unitScale <= 0)
                return false;

            float originX = glyphRect.x + (contentWidth - bounds.Width * unitScale) * 0.5f;
            float originY = glyphRect.y + (contentHeight - bounds.Height * unitScale) * 0.5f;
            int renderXMin = Math.Max(glyphRect.x - padding, 0);
            int renderYMin = Math.Max(glyphRect.y - padding, 0);
            int renderXMax = Math.Min(glyphRect.x + glyphRect.width + padding, atlasTexture.width);
            int renderYMax = Math.Min(glyphRect.y + glyphRect.height + padding, atlasTexture.height);
            int correctionMaskLength = (renderXMax - renderXMin) * (renderYMax - renderYMin);
            if (correctionMaskLength <= 0)
                return false;

            NativeArray<TMP_MSDFABurstRenderer.MsdfaSegment> segments = new NativeArray<TMP_MSDFABurstRenderer.MsdfaSegment>(shape.Segments.Count, Allocator.Temp);
            NativeArray<byte> correctionMask = new NativeArray<byte>(correctionMaskLength, Allocator.Temp);
            try
            {
                for (int i = 0; i < shape.Segments.Count; i++)
                {
                    TMP_MSDFAFont.Segment segment = shape.Segments[i];
                    segments[i] = new TMP_MSDFABurstRenderer.MsdfaSegment(segment.Type, segment.X0, segment.Y0, segment.X1, segment.Y1, segment.X2, segment.Y2, segment.ColorMask);
                }

                NativeArray<byte> atlasPixels = atlasTexture.GetRawTextureData<byte>();
                TMP_MSDFABurstRenderer.MsdfaSegment* segmentPointer = (TMP_MSDFABurstRenderer.MsdfaSegment*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(segments);
                byte* atlasPointer = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(atlasPixels);
                byte* correctionMaskPointer = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(correctionMask);
                TMP_MSDFABurstRenderer.RenderGlyphMsdfa(segmentPointer, segments.Length, atlasPointer, atlasTexture.width, atlasTexture.height, glyphRect.x, glyphRect.y, glyphRect.width, glyphRect.height, padding, originX, originY, bounds.MinX, bounds.MinY, unitScale, padding + 1, useFillRuleSign ? 1 : 0, correctionMaskPointer);
            }
            finally
            {
                correctionMask.Dispose();
                segments.Dispose();
            }

            return true;
        }

        private static bool TryGetMsdfaFont(TMP_FontAsset fontAsset, out TMP_MSDFAFont font)
        {
            font = null;
            if (fontAsset == null || fontAsset.TryGetMsdfaSourceFontPath(out string sourceFontPath) == false)
                return false;

            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(sourceFontPath);
            if (FontCacheLookup.TryGetValue(fontAsset, out FontCache cache)
                && string.Equals(cache.SourceFontPath, sourceFontPath, StringComparison.OrdinalIgnoreCase)
                && cache.LastWriteTimeUtc == lastWriteTimeUtc
                && cache.UseFillRuleSign == fontAsset.useMsdfaFillRuleSign)
            {
                font = cache.Font;
                return font != null;
            }

            byte[] fontData = fontAsset.ReadMsdfaSourceFontData();
            if (fontData == null || fontData.Length == 0)
                return false;

            if (TMP_MSDFAFont.TryCreate(fontData, fontAsset.useMsdfaFillRuleSign, out font) == false)
                return false;

            FontCacheLookup[fontAsset] = new FontCache(sourceFontPath, lastWriteTimeUtc, fontAsset.useMsdfaFillRuleSign, font);
            return true;
        }

        private static bool CanUseMsdfaAtlas(GlyphRenderMode renderMode, Texture2D atlasTexture, bool isMsdfaAtlasEnabled)
        {
            return isMsdfaAtlasEnabled && IsDistanceFieldRenderMode(renderMode) && atlasTexture.format == TextureFormat.RGBA32;
        }

        private static bool IsDistanceFieldRenderMode(GlyphRenderMode renderMode)
        {
            return renderMode == GlyphRenderMode.SDF
                   || renderMode == GlyphRenderMode.SDF8
                   || renderMode == GlyphRenderMode.SDF16
                   || renderMode == GlyphRenderMode.SDF32
                   || renderMode == GlyphRenderMode.SDFAA
                   || renderMode == GlyphRenderMode.SDFAA_HINTED;
        }

        private static Texture2D CreateScratchTexture(Texture2D atlasTexture)
        {
            Texture2D scratchTexture = new Texture2D(atlasTexture.width, atlasTexture.height, TextureFormat.Alpha8, false);
            FontEngine.ResetAtlasTexture(scratchTexture);
            return scratchTexture;
        }

        private static void CopyGlyphsToMsdfaAtlas(Texture2D sourceTexture, Texture2D atlasTexture, int padding, params Glyph[] glyphs)
        {
            if (glyphs == null || glyphs.Length == 0)
                return;

            NativeArray<byte> sourcePixels = sourceTexture.GetRawTextureData<byte>();
            NativeArray<byte> atlasPixels = atlasTexture.GetRawTextureData<byte>();
            byte* sourcePointer = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(sourcePixels);
            byte* atlasPointer = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(atlasPixels);
            int textureWidth = atlasTexture.width;
            int textureHeight = atlasTexture.height;

            for (int i = 0; i < glyphs.Length; i++)
            {
                Glyph glyph = glyphs[i];
                if (glyph == null)
                    continue;

                GlyphRect glyphRect = glyph.glyphRect;
                TMP_MSDFABurstRenderer.CopyGlyphToMsdfaAtlas(sourcePointer, atlasPointer, textureWidth, textureHeight, padding, glyphRect.x, glyphRect.y, glyphRect.width, glyphRect.height);
            }

            atlasTexture.Apply(false, false);
        }

        private static void ReleaseScratchTexture(Texture2D scratchTexture)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(scratchTexture);
            else
                UnityEngine.Object.DestroyImmediate(scratchTexture);
        }

        private readonly struct FontCache
        {
            internal readonly string SourceFontPath;
            internal readonly DateTime LastWriteTimeUtc;
            internal readonly bool UseFillRuleSign;
            internal readonly TMP_MSDFAFont Font;

            internal FontCache(string sourceFontPath, DateTime lastWriteTimeUtc, bool useFillRuleSign, TMP_MSDFAFont font)
            {
                SourceFontPath = sourceFontPath;
                LastWriteTimeUtc = lastWriteTimeUtc;
                UseFillRuleSign = useFillRuleSign;
                Font = font;
            }
        }
    }
}
#endif

#if TMP_MSDFA_UGUI_PATCHED
using System.Runtime.InteropServices;
using Unity.Burst;

namespace TMPro
{
    internal static unsafe class TMP_MSDFABurstRenderer
    {
        private static readonly CopyGlyphToMsdfaAtlasDelegate CopyGlyphToMsdfaAtlasFunction = BurstCompiler.CompileFunctionPointer<CopyGlyphToMsdfaAtlasDelegate>(TMP_MSDFABurstRenderFunctions.CopyGlyphToMsdfaAtlasBurst).Invoke;
        private static readonly RenderGlyphMsdfaDelegate RenderGlyphMsdfaFunction = BurstCompiler.CompileFunctionPointer<RenderGlyphMsdfaDelegate>(TMP_MSDFABurstRenderFunctions.RenderGlyphMsdfaBurst).Invoke;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void CopyGlyphToMsdfaAtlasDelegate(byte* sourcePixels, byte* atlasPixels, int textureWidth, int textureHeight, int padding, int glyphX, int glyphY, int glyphWidth, int glyphHeight);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void RenderGlyphMsdfaDelegate(MsdfaSegment* segments, int segmentCount, byte* atlasPixels, int textureWidth, int textureHeight, int glyphX, int glyphY, int glyphWidth, int glyphHeight, int padding, float originX, float originY, float boundsMinX, float boundsMinY, float unitScale, float pixelRange, byte* correctionMask);

        internal static void CopyGlyphToMsdfaAtlas(byte* sourcePixels, byte* atlasPixels, int textureWidth, int textureHeight, int padding, int glyphX, int glyphY, int glyphWidth, int glyphHeight)
        {
            CopyGlyphToMsdfaAtlasFunction(sourcePixels, atlasPixels, textureWidth, textureHeight, padding, glyphX, glyphY, glyphWidth, glyphHeight);
        }

        internal static void RenderGlyphMsdfa(MsdfaSegment* segments, int segmentCount, byte* atlasPixels, int textureWidth, int textureHeight, int glyphX, int glyphY, int glyphWidth, int glyphHeight, int padding, float originX, float originY, float boundsMinX, float boundsMinY, float unitScale, float pixelRange, byte* correctionMask)
        {
            RenderGlyphMsdfaFunction(segments, segmentCount, atlasPixels, textureWidth, textureHeight, glyphX, glyphY, glyphWidth, glyphHeight, padding, originX, originY, boundsMinX, boundsMinY, unitScale, pixelRange, correctionMask);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MsdfaSegment
        {
            internal int Type;
            internal float X0;
            internal float Y0;
            internal float X1;
            internal float Y1;
            internal float X2;
            internal float Y2;
            internal int ColorMask;

            internal MsdfaSegment(int type, float x0, float y0, float x1, float y1, float x2, float y2, int colorMask)
            {
                Type = type;
                X0 = x0;
                Y0 = y0;
                X1 = x1;
                Y1 = y1;
                X2 = x2;
                Y2 = y2;
                ColorMask = colorMask;
            }
        }
    }
}
#endif

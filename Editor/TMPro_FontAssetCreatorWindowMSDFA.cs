#if TMP_MSDFA_UGUI_PATCHED
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace TMPro.EditorUtilities
{
    public partial class TMPro_FontAssetCreatorWindow
    {
        private static readonly GUIContent MsdfaAtlasLabel = new GUIContent("MSDFA Atlas", "Stores distance-field atlas data in RGBA channels and uses the TextMeshPro/MSDFA shader.");

        private bool m_IsMsdfaAtlasEnabled;

        private void LoadMsdfaAtlasSettings(TMP_FontAsset fontAsset)
        {
            m_IsMsdfaAtlasEnabled = fontAsset != null && fontAsset.isMsdfaAtlasEnabled;
        }

        private void DrawMsdfaAtlasControls()
        {
            bool previousValue = m_IsMsdfaAtlasEnabled;
            m_IsMsdfaAtlasEnabled = EditorGUILayout.Toggle(MsdfaAtlasLabel, m_IsMsdfaAtlasEnabled);

            if (m_IsMsdfaAtlasEnabled != previousValue)
                m_IsFontAtlasInvalid = true;
        }

        private bool ShouldRenderMsdfaAtlas()
        {
            return m_IsMsdfaAtlasEnabled
                   && (m_GlyphRenderMode == GlyphRenderMode.SDF
                       || m_GlyphRenderMode == GlyphRenderMode.SDF8
                       || m_GlyphRenderMode == GlyphRenderMode.SDF16
                       || m_GlyphRenderMode == GlyphRenderMode.SDF32
                       || m_GlyphRenderMode == GlyphRenderMode.SDFAA
                       || m_GlyphRenderMode == GlyphRenderMode.SDFAA_HINTED);
        }

        private TMP_FontAsset CreateTemporaryMsdfaFontAsset()
        {
            TMP_FontAsset fontAsset = ScriptableObject.CreateInstance<TMP_FontAsset>();
            fontAsset.hideFlags = HideFlags.HideAndDontSave;
            fontAsset.version = "1.1.0";
            fontAsset.faceInfo = m_FaceInfo;
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.atlasRenderMode = m_GlyphRenderMode;
            fontAsset.atlasPadding = m_Padding;
            fontAsset.isMsdfaAtlasEnabled = true;
            fontAsset.sourceFontFile = m_SourceFont;
            fontAsset.m_SourceFontFile_EditorRef = m_SourceFont;
            fontAsset.m_SourceFontFileGUID = m_SourceFont == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_SourceFont));
            fontAsset.RefreshMsdfaSourceFontDataFromEditor();
            return fontAsset;
        }

        private void ApplyMsdfaAtlasSettings(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return;

            fontAsset.isMsdfaAtlasEnabled = m_IsMsdfaAtlasEnabled;
            if (m_IsMsdfaAtlasEnabled)
            {
                if (fontAsset.RefreshMsdfaSourceFontDataFromEditor() == false)
                    Debug.LogWarning("MSDFA Atlas is enabled, but source font data could not be read for dynamic glyph generation.", fontAsset);
            }
            else
            {
                fontAsset.ClearMsdfaSourceFontData();
            }

            EditorUtility.SetDirty(fontAsset);

            if (((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_BITMAP) == GlyphRasterModes.RASTER_MODE_BITMAP)
                return;

            Shader shader = TMP_MSDFAAtlasRenderer.GetDistanceFieldShader(m_IsMsdfaAtlasEnabled, Shader.Find("TextMeshPro/Distance Field"));
            if (fontAsset.material != null)
                fontAsset.material.shader = shader;

            Material[] materialReferences = TMP_EditorUtility.FindMaterialReferences(fontAsset);
            for (int i = 0; i < materialReferences.Length; i++)
            {
                if (materialReferences[i] != null)
                    materialReferences[i].shader = shader;
            }
        }
    }
}
#endif

#if TMP_MSDFA_UGUI_PATCHED
using UnityEditor;
using UnityEngine;

namespace TMPro.EditorUtilities
{
    public partial class TMP_FontAssetEditor
    {
        private static readonly GUIContent MsdfaAtlasInspectorLabel = new GUIContent("MSDFA Atlas", "Stores dynamic distance-field atlas data in RGBA channels and uses the TextMeshPro/MSDFA shader.");
        private static readonly GUIContent MsdfaFillRuleSignInspectorLabel = new GUIContent("MSDFA Fill Rule Sign", "Normalizes glyph contour winding and applies the glyph fill rule to RGB MSDF channels. Use this for glyf fonts with reversed winding or overlapping contours.");

        private SerializedProperty m_IsMsdfaAtlasEnabled_prop;
        private SerializedProperty m_UseMsdfaFillRuleSign_prop;
        private bool m_SavedIsMsdfaAtlasEnabled;
        private bool m_SavedUseMsdfaFillRuleSign;

        private void OnMsdfaInspectorEnable()
        {
            m_IsMsdfaAtlasEnabled_prop = serializedObject.FindProperty("m_IsMsdfaAtlasEnabled");
            m_UseMsdfaFillRuleSign_prop = serializedObject.FindProperty("m_UseMsdfaFillRuleSign");
        }

        private void DrawMsdfaAtlasInspectorControl()
        {
            if (m_IsMsdfaAtlasEnabled_prop == null)
                return;

            bool previousValue = m_IsMsdfaAtlasEnabled_prop.boolValue;
            EditorGUILayout.PropertyField(m_IsMsdfaAtlasEnabled_prop, MsdfaAtlasInspectorLabel);

            if (m_IsMsdfaAtlasEnabled_prop.boolValue != previousValue)
            {
                m_MaterialPresetsRequireUpdate = true;
                m_DisplayDestructiveChangeWarning = true;
            }

            if (m_UseMsdfaFillRuleSign_prop == null)
                return;

            using (new EditorGUI.DisabledScope(m_IsMsdfaAtlasEnabled_prop.boolValue == false))
            {
                bool previousFillRuleSignValue = m_UseMsdfaFillRuleSign_prop.boolValue;
                EditorGUILayout.PropertyField(m_UseMsdfaFillRuleSign_prop, MsdfaFillRuleSignInspectorLabel);

                if (m_UseMsdfaFillRuleSign_prop.boolValue != previousFillRuleSignValue)
                    m_DisplayDestructiveChangeWarning = true;
            }
        }

        private void ApplyMsdfaInspectorSettings()
        {
            if (m_fontAsset == null || m_IsMsdfaAtlasEnabled_prop == null)
                return;

            m_fontAsset.isMsdfaAtlasEnabled = m_IsMsdfaAtlasEnabled_prop.boolValue;
            if (m_UseMsdfaFillRuleSign_prop != null)
                m_fontAsset.useMsdfaFillRuleSign = m_UseMsdfaFillRuleSign_prop.boolValue;

            if (m_fontAsset.isMsdfaAtlasEnabled)
            {
                if (m_fontAsset.RefreshMsdfaSourceFontDataFromEditor() == false)
                    Debug.LogWarning("MSDFA Atlas is enabled, but source font data could not be read for dynamic glyph generation.", m_fontAsset);
            }
            else
            {
                m_fontAsset.ClearMsdfaSourceFontData();
            }

            EditorUtility.SetDirty(m_fontAsset);

            Shader shader = TMP_MSDFAAtlasRenderer.GetDistanceFieldShader(m_fontAsset.isMsdfaAtlasEnabled, Shader.Find("TextMeshPro/Distance Field"));

            if (m_fontAsset.material != null)
                m_fontAsset.material.shader = shader;

            Material[] materialReferences = TMP_EditorUtility.FindMaterialReferences(m_fontAsset);
            for (int i = 0; i < materialReferences.Length; i++)
            {
                if (materialReferences[i] != null)
                    materialReferences[i].shader = shader;
            }
        }

        private void SaveMsdfaGenerationSettings()
        {
            m_SavedIsMsdfaAtlasEnabled = m_IsMsdfaAtlasEnabled_prop != null ? m_IsMsdfaAtlasEnabled_prop.boolValue : m_fontAsset != null && m_fontAsset.isMsdfaAtlasEnabled;
            m_SavedUseMsdfaFillRuleSign = m_UseMsdfaFillRuleSign_prop != null ? m_UseMsdfaFillRuleSign_prop.boolValue : m_fontAsset != null && m_fontAsset.useMsdfaFillRuleSign;
        }

        private void RestoreMsdfaGenerationSettings()
        {
            if (m_IsMsdfaAtlasEnabled_prop != null)
                m_IsMsdfaAtlasEnabled_prop.boolValue = m_SavedIsMsdfaAtlasEnabled;
            if (m_UseMsdfaFillRuleSign_prop != null)
                m_UseMsdfaFillRuleSign_prop.boolValue = m_SavedUseMsdfaFillRuleSign;

            if (m_fontAsset != null)
            {
                m_fontAsset.isMsdfaAtlasEnabled = m_SavedIsMsdfaAtlasEnabled;
                m_fontAsset.useMsdfaFillRuleSign = m_SavedUseMsdfaFillRuleSign;
            }
        }
    }
}
#endif

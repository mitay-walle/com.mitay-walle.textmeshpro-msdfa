#if TMP_MSDFA_UGUI_PATCHED
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TMPro
{
    public partial class TMP_FontAsset
    {
        [SerializeField]
        private bool m_IsMsdfaAtlasEnabled;

        public bool isMsdfaAtlasEnabled
        {
            get => m_IsMsdfaAtlasEnabled;
            set => m_IsMsdfaAtlasEnabled = value;
        }

        internal bool TryGetMsdfaSourceFontPath(out string fullPath)
        {
            string fontPath = m_SourceFontFilePath;

            #if UNITY_EDITOR
            if (string.IsNullOrEmpty(fontPath) && SourceFont_EditorRef != null)
                fontPath = AssetDatabase.GetAssetPath(SourceFont_EditorRef);
            #endif

            if (string.IsNullOrEmpty(fontPath))
            {
                fullPath = null;
                return false;
            }

            fullPath = Path.IsPathRooted(fontPath) ? fontPath : Path.GetFullPath(fontPath);
            return File.Exists(fullPath);
        }

        internal byte[] ReadMsdfaSourceFontData()
        {
            return TryGetMsdfaSourceFontPath(out string fullPath) ? File.ReadAllBytes(fullPath) : null;
        }

        internal void ClearMsdfaSourceFontData()
        {
        }

        #if UNITY_EDITOR
        internal bool RefreshMsdfaSourceFontDataFromEditor()
        {
            return TryGetMsdfaSourceFontPath(out _);
        }
        #endif
    }
}
#endif

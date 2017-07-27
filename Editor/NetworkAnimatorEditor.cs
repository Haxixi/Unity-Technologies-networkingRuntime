#if ENABLE_UNET
using System;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityEditor
{
    [CustomEditor(typeof(NetworkAnimator), true)]
    [CanEditMultipleObjects]
    public class NetworkAnimatorEditor : Editor
    {
        NetworkAnimator m_AnimSync;
        bool m_Initialized;

        SerializedProperty  m_AnimatorProperty;

        void Init()
        {
            if (m_Initialized)
                return;

            m_Initialized = true;
            m_AnimSync = target as NetworkAnimator;

            m_AnimatorProperty = serializedObject.FindProperty("m_Animator");
        }

        public override void OnInspectorGUI()
        {
            Init();
            serializedObject.Update();
            DrawControls();
            serializedObject.ApplyModifiedProperties();
        }

        void DrawControls()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_AnimatorProperty);
            if (EditorGUI.EndChangeCheck())
            {
                m_AnimSync.ResetParameterOptions();
            }

            if (m_AnimSync.animator == null)
                return;

            var controller = m_AnimSync.animator.runtimeAnimatorController as AnimatorController;
            if (controller != null)
            {
                EditorGUI.indentLevel += 1;
                int i = 0;
                foreach (var p in controller.parameters)
                {
                    bool oldSend = m_AnimSync.GetParameterAutoSend(i);
                    bool send = EditorGUILayout.Toggle(p.name, oldSend);
                    if (send != oldSend)
                    {
                        m_AnimSync.SetParameterAutoSend(i, send);
                        EditorUtility.SetDirty(target);
                    }
                    i += 1;
                }
                EditorGUI.indentLevel -= 1;
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Separator();
                if (m_AnimSync.param0 != "") EditorGUILayout.LabelField("Param 0", m_AnimSync.param0);
                if (m_AnimSync.param1 != "") EditorGUILayout.LabelField("Param 1", m_AnimSync.param1);
                if (m_AnimSync.param2 != "") EditorGUILayout.LabelField("Param 2", m_AnimSync.param2);
                if (m_AnimSync.param3 != "") EditorGUILayout.LabelField("Param 3", m_AnimSync.param3);
                if (m_AnimSync.param4 != "") EditorGUILayout.LabelField("Param 4", m_AnimSync.param4);
            }
        }
    }
}
#endif

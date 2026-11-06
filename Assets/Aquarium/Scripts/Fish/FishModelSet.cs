// The fish models prepared in the editor (Aquarium > Build Fish Models): per model a body mesh
// and a see-through fin mesh whose pectorals know their pivots, plus their materials.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aquarium.Fish
{
    [Serializable]
    public sealed class FishModel
    {
        public string key;
        public Mesh body, fins;
        [Tooltip("Simpler bodies for fish small on the screen (a quarter, a sixteenth of the triangles); the last also casts the shadows")]
        public Mesh[] bodyLods = new Mesh[0];
        [Tooltip("Simpler fins for the same fish, as bodyLods; empty: always the full fins")]
        public Mesh[] finLods = new Mesh[0];
        public Material bodyMaterial, finMaterial;

        [NonSerialized] Material depthLast, depthEarly;

        /// <summary>
        /// The solid part of the fins in the depth texture, for depth of field: drawn after everything
        /// see-through (the depth is copied after them and nothing gets hidden), or only in the depth
        /// pre-pass where the depth comes from one.
        /// </summary>
        public void FinDepthMaterials(Material template, out Material last, out Material early)
        {
            if (!depthLast)
            {
                depthLast = FinDepthMaterial(template, 3900);
                depthEarly = FinDepthMaterial(template, 2000);
                depthEarly.SetShaderPassEnabled("UniversalForward", false);
            }
            last = depthLast;
            early = depthEarly;
        }

        Material FinDepthMaterial(Material template, int queue)
        {
            var m = new Material(template) { name = $"{key}_fin_depth", hideFlags = HideFlags.DontSave, enableInstancing = true };
            m.CopyPropertiesFromMaterial(finMaterial);
            m.shaderKeywords = finMaterial.shaderKeywords;
            m.renderQueue = queue;
            return m;
        }
    }

    [CreateAssetMenu(menuName = "Aquarium/Fish Model Set")]
    public sealed class FishModelSet : ScriptableObject
    {
        public List<FishModel> models = new List<FishModel>();
        [Tooltip("Aquarium/Fish Fin Depth with instancing on, see FishModel.FinDepthMaterials. An asset, or a build would leave out the shader's instanced variants: the materials per model only exist while running")]
        public Material finDepth;

        public FishModel Get(string key) => models.Find(m => m.key == key);
    }
}

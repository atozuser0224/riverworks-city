using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Riverworks.Editor
{
    public static class ResidentAssetBuild
    {
        const string SourceRoot="Assets/ThirdParty/Quaternius/Characters/Models";
        const string GeneratedRoot="Assets/Resources/Residents/Generated";
        const string LibraryPath="Assets/Resources/Residents/ResidentVisualLibrary.asset";
        const string ReportPath="Artifacts/ResidentAssets/import-report.json";
        static readonly string[] ModelNames={"Worker_Male","Worker_Female","Casual_Male","Chef_Female"};

        [Serializable] sealed class ImportReport
        {
            public string generatedUtc;
            public string unityVersion;
            public ModelReport[] models;
            public string[] clips;
            public string[] selectedClips;
            public string[] actionBindings;
            public string[] nullFallbackActions;
        }

        [Serializable] sealed class ModelReport
        {
            public string name;
            public string source;
            public string prefab;
            public int vertices;
            public int triangles;
            public int sourceSubmeshes;
            public float height;
            public bool avatarValid;
            public bool humanoid;
        }

        [Serializable] sealed class SkeletonInspection
        {
            public string generatedUtc;
            public SkeletonModel[] models;
        }

        [Serializable] sealed class SkeletonModel
        {
            public string name;
            public SkeletonNode[] nodes;
            public string[] skinnedMeshBones;
            public string rootBone;
        }

        [Serializable] sealed class SkeletonNode
        {
            public string name;
            public string path;
            public string parent;
            public int depth;
            public Vector3 localPosition;
            public Vector3 worldPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public int skinBoneIndex;
        }

        [Serializable] sealed class BoundsInspection
        {
            public string generatedUtc;
            public BoundsModel[] models;
        }

        [Serializable] sealed class BoundsModel
        {
            public string name;
            public Vector3 rendererLocalScale;
            public Vector3 rendererLossyScale;
            public float rendererBoundsWorldHeight;
            public float sharedMeshRawHeight;
            public float sharedMeshTransformedHeight;
            public float bakeFalseRawHeight;
            public float bakeFalseTransformedHeight;
            public float bakeFalseRotationOnlyHeight;
            public float bakeTrueRawHeight;
            public float bakeTrueTransformedHeight;
            public float bakeTrueRotationOnlyHeight;
        }

        [MenuItem("Riverworks/Inspect resident bounds")]
        public static void InspectBounds()
        {
            var results=new List<BoundsModel>();
            foreach(string modelName in ModelNames)
            {
                string sourcePath=SourceRoot+"/"+modelName+".fbx";
                AssetDatabase.ImportAsset(sourcePath,ImportAssetOptions.ForceUpdate);
                GameObject source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if(source==null) throw new FileNotFoundException("Resident FBX is missing",sourcePath);
                GameObject instance=(GameObject)PrefabUtility.InstantiatePrefab(source);
                if(instance==null) instance=UnityEngine.Object.Instantiate(source);
                var falseMesh=new Mesh();
                var trueMesh=new Mesh();
                try
                {
                    SkinnedMeshRenderer renderer=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .OrderByDescending(r=>r.sharedMesh==null?0:r.sharedMesh.vertexCount).FirstOrDefault();
                    if(renderer==null||renderer.sharedMesh==null) throw new InvalidOperationException(modelName+" contains no skinned mesh.");
                    renderer.BakeMesh(falseMesh,false); falseMesh.RecalculateBounds();
                    renderer.BakeMesh(trueMesh,true); trueMesh.RecalculateBounds();
                    results.Add(new BoundsModel
                    {
                        name=modelName,rendererLocalScale=renderer.transform.localScale,rendererLossyScale=renderer.transform.lossyScale,
                        rendererBoundsWorldHeight=renderer.bounds.size.y,
                        sharedMeshRawHeight=renderer.sharedMesh.bounds.size.y,
                        sharedMeshTransformedHeight=TransformBounds(renderer.transform,renderer.sharedMesh.bounds).size.y,
                        bakeFalseRawHeight=falseMesh.bounds.size.y,
                        bakeFalseTransformedHeight=TransformBounds(renderer.transform,falseMesh.bounds).size.y,
                        bakeFalseRotationOnlyHeight=TransformBoundsRotationOnly(renderer.transform,falseMesh.bounds).size.y,
                        bakeTrueRawHeight=trueMesh.bounds.size.y,
                        bakeTrueTransformedHeight=TransformBounds(renderer.transform,trueMesh.bounds).size.y,
                        bakeTrueRotationOnlyHeight=TransformBoundsRotationOnly(renderer.transform,trueMesh.bounds).size.y
                    });
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(falseMesh);
                    UnityEngine.Object.DestroyImmediate(trueMesh);
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
            string path="Artifacts/ResidentAssets/bounds-inspection.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonUtility.ToJson(new BoundsInspection{generatedUtc=DateTime.UtcNow.ToString("o"),models=results.ToArray()},true));
            Debug.Log("RIVERWORKS_RESIDENT_BOUNDS_INSPECTION_READY "+Path.GetFullPath(path));
        }

        [MenuItem("Riverworks/Inspect resident skeletons")]
        public static void Inspect()
        {
            var models=new List<SkeletonModel>();
            foreach(string modelName in ModelNames)
            {
                string sourcePath=SourceRoot+"/"+modelName+".fbx";
                AssetDatabase.ImportAsset(sourcePath,ImportAssetOptions.ForceUpdate);
                GameObject source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if(source==null) throw new FileNotFoundException("Resident FBX is missing",sourcePath);
                GameObject instance=(GameObject)PrefabUtility.InstantiatePrefab(source);
                if(instance==null) instance=UnityEngine.Object.Instantiate(source);
                try
                {
                    SkinnedMeshRenderer skin=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .OrderByDescending(r=>r.sharedMesh==null?0:r.sharedMesh.vertexCount).FirstOrDefault();
                    if(skin==null) throw new InvalidOperationException(modelName+" contains no skinned mesh.");
                    var boneIndices=new Dictionary<Transform,int>();
                    for(int i=0;i<skin.bones.Length;i++) if(skin.bones[i]!=null&&!boneIndices.ContainsKey(skin.bones[i])) boneIndices.Add(skin.bones[i],i);
                    Transform[] transforms=instance.GetComponentsInChildren<Transform>(true);
                    models.Add(new SkeletonModel
                    {
                        name=modelName,
                        rootBone=skin.rootBone==null?null:PathFrom(instance.transform,skin.rootBone),
                        skinnedMeshBones=skin.bones.Select((bone,index)=>index+":"+(bone==null?"<null>":PathFrom(instance.transform,bone))).ToArray(),
                        nodes=transforms.Select(node=>new SkeletonNode
                        {
                            name=node.name,path=PathFrom(instance.transform,node),
                            parent=node.parent==null?null:PathFrom(instance.transform,node.parent),
                            depth=DepthFrom(instance.transform,node),localPosition=node.localPosition,
                            worldPosition=node.position,localRotation=node.localRotation,localScale=node.localScale,
                            skinBoneIndex=boneIndices.TryGetValue(node,out int index)?index:-1
                        }).ToArray()
                    });
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            string path="Artifacts/ResidentAssets/skeleton-inspection.json";
            File.WriteAllText(path,JsonUtility.ToJson(new SkeletonInspection{generatedUtc=DateTime.UtcNow.ToString("o"),models=models.ToArray()},true));
            Debug.Log("RIVERWORKS_RESIDENT_SKELETON_INSPECTION_READY "+Path.GetFullPath(path));
        }

        [MenuItem("Riverworks/Prepare resident assets")]
        public static void Prepare()
        {
            EnsureFolder("Assets/Resources/Residents");
            if(AssetDatabase.IsValidFolder(GeneratedRoot)) AssetDatabase.DeleteAsset(GeneratedRoot);
            EnsureFolder(GeneratedRoot);

            Shader shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/ResidentVertexColor.shader");
            if(shader==null) throw new InvalidOperationException("Resident vertex-color shader is missing or did not compile.");
            var sharedMaterial=new Material(shader){name="Resident Vertex Colors"};
            sharedMaterial.SetColor("_Color",Color.white);
            string materialPath=GeneratedRoot+"/ResidentVertexColor.mat";
            AssetDatabase.CreateAsset(sharedMaterial,materialPath);

            var prefabs=new GameObject[ModelNames.Length];
            var heights=new float[ModelNames.Length];
            var reports=new List<ModelReport>();
            for(int i=0;i<ModelNames.Length;i++)
            {
                string sourcePath=SourceRoot+"/"+ModelNames[i]+".fbx";
                prefabs[i]=BuildPrefab(sourcePath,ModelNames[i],false,sharedMaterial,out heights[i],out ModelReport report);
                reports.Add(report);
            }

            GameObject guide=BuildPrefab(SourceRoot+"/Casual_Male.fbx","GuideVariant",true,sharedMaterial,out float guideHeight,out ModelReport guideReport);
            reports.Add(guideReport);
            List<AnimationClip> clips=FindAnimationClips();
            AnimationClip idle=RequireNamed(clips,"Idle_Loop");
            AnimationClip walk=RequireNamed(clips,"Walk_Loop");
            AnimationClip carry=RequireNamed(clips,"Walk_Carry_Loop");
            AnimationClip chop=RequireNamed(clips,"TreeChopping_Loop");
            AnimationClip farm=RequireNamed(clips,"Farm_Harvest");
            AnimationClip talk=RequireNamed(clips,"Idle_Talking_Loop");
            AnimationClip sit=RequireNamed(clips,"Sitting_Idle_Loop");

            var library=ScriptableObject.CreateInstance<ResidentVisualLibrary>();
            library.Models=prefabs;
            library.GuideModel=guide;
            library.ModelHeights=heights;
            library.GuideHeight=guideHeight;
            library.Idle=idle;
            library.Walk=walk;
            library.Carry=carry;
            library.Chop=chop;
            library.Dig=chop;
            library.Farm=farm;
            library.Knead=null;
            library.Hammer=null;
            library.Read=null;
            library.Talk=talk;
            library.Operate=null;
            library.Sit=sit;

            if(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LibraryPath)!=null) AssetDatabase.DeleteAsset(LibraryPath);
            AssetDatabase.CreateAsset(library,LibraryPath);
            AssetDatabase.SaveAssets();

            var selected=new[]{library.Idle,library.Walk,library.Carry,library.Chop,library.Dig,library.Farm,
                library.Knead,library.Hammer,library.Read,library.Talk,library.Operate,library.Sit};
            var reportData=new ImportReport
            {
                generatedUtc=DateTime.UtcNow.ToString("o"),
                unityVersion=Application.unityVersion,
                models=reports.ToArray(),
                clips=clips.Select(c=>AssetDatabase.GetAssetPath(c)+" :: "+c.name).ToArray(),
                selectedClips=selected.Select(c=>c==null?"<missing>":AssetDatabase.GetAssetPath(c)+" :: "+c.name).ToArray(),
                actionBindings=new[]{
                    "Idle=UAL1 Idle_Loop","Walk=UAL1 Walk_Loop","Carry=UAL2 Walk_Carry_Loop","Chop=UAL2 TreeChopping_Loop",
                    "Dig=Chop reuse plus procedural prop pose","Farm=UAL2 Farm_Harvest","Knead=null",
                    "Hammer=null","Read=null","Talk=UAL1 Idle_Talking_Loop","Operate=null","Sit=UAL1 Sitting_Idle_Loop"
                },
                nullFallbackActions=new[]{"Knead","Hammer","Read","Operate"}
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath,JsonUtility.ToJson(reportData,true));
            Debug.Log("RIVERWORKS_RESIDENT_ASSETS_READY models="+prefabs.Length+" clips="+clips.Count);
        }

        static GameObject BuildPrefab(string sourcePath,string outputName,bool guide,Material material,out float height,out ModelReport report)
        {
            GameObject source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if(source==null) throw new FileNotFoundException("Resident FBX is missing",sourcePath);
            GameObject instance=(GameObject)PrefabUtility.InstantiatePrefab(source);
            if(instance==null) instance=UnityEngine.Object.Instantiate(source);
            if(PrefabUtility.IsPartOfPrefabInstance(instance))
                PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            instance.name=outputName;
            try
            {
                Animator animator=instance.GetComponent<Animator>()??instance.GetComponentInChildren<Animator>(true);
                if(animator==null) animator=instance.AddComponent<Animator>();
                animator.applyRootMotion=false;
                animator.runtimeAnimatorController=null;
                SkinnedMeshRenderer skin=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .OrderByDescending(r=>r.sharedMesh==null?0:r.sharedMesh.vertexCount).FirstOrDefault();
                if(skin==null||skin.sharedMesh==null) throw new InvalidOperationException(sourcePath+" contains no skinned mesh.");
                int sourceSubmeshes=skin.sharedMesh.subMeshCount;

                Avatar avatar=BuildGeneratedAvatar(instance,outputName);
                animator.avatar=avatar;

                Bounds worldBounds=PoseWorldBounds(skin);
                height=Mathf.Max(.001f,worldBounds.size.y);
                if(height < .5f || height > 10f)
                    throw new InvalidOperationException(outputName+" has an invalid measured world height: "+height);
                instance.transform.position+=Vector3.up*(-worldBounds.min.y);

                Mesh baked=BakeMaterialColors(skin,guide);
                baked.name=outputName+" Single Mesh";
                string meshPath=GeneratedRoot+"/"+outputName+"_Mesh.asset";
                AssetDatabase.CreateAsset(baked,meshPath);
                skin.sharedMesh=baked;
                skin.sharedMaterials=new[]{material};
                skin.quality=SkinQuality.Bone2;
                skin.updateWhenOffscreen=false;

                foreach(Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    if(renderer!=skin) UnityEngine.Object.DestroyImmediate(renderer);
                foreach(Collider collider in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                foreach(Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
                foreach(Camera camera in instance.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(camera);
                foreach(Light light in instance.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);

                string prefabPath=GeneratedRoot+"/"+outputName+".prefab";
                PrefabUtility.SaveAsPrefabAsset(instance,prefabPath);
                GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Animator savedAnimator=prefab==null?null:prefab.GetComponentInChildren<Animator>(true);
                bool avatarValid=savedAnimator!=null&&savedAnimator.avatar!=null&&savedAnimator.avatar.isValid;
                bool humanoid=avatarValid&&savedAnimator.avatar.isHuman;
                if(!humanoid) throw new InvalidOperationException(outputName+" did not produce a valid humanoid Avatar.");
                report=new ModelReport
                {
                    name=outputName,source=sourcePath,prefab=prefabPath,vertices=baked.vertexCount,
                    triangles=baked.triangles.Length/3,sourceSubmeshes=sourceSubmeshes,
                    height=height,avatarValid=avatarValid,humanoid=humanoid
                };
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        static Mesh BakeMaterialColors(SkinnedMeshRenderer renderer,bool guide)
        {
            Mesh source=renderer.sharedMesh;
            Material[] materials=renderer.sharedMaterials;
            Vector3[] positions=source.vertices;
            Vector3[] normals=source.normals;
            Vector4[] tangents=source.tangents;
            Vector2[] uv=source.uv;
            BoneWeight[] weights=source.boneWeights;
            var outPositions=new List<Vector3>();
            var outNormals=new List<Vector3>();
            var outTangents=new List<Vector4>();
            var outUv=new List<Vector2>();
            var outWeights=new List<BoneWeight>();
            var outColors=new List<Color32>();
            var triangles=new List<int>();
            var remap=new Dictionary<long,int>();
            int sourceSubmeshes=source.subMeshCount;
            for(int submesh=0;submesh<sourceSubmeshes;submesh++)
            {
                Color color=MaterialColor(submesh<materials.Length?materials[submesh]:null,guide);
                foreach(int sourceIndex in source.GetIndices(submesh))
                {
                    long key=((long)submesh<<32)|(uint)sourceIndex;
                    if(!remap.TryGetValue(key,out int outputIndex))
                    {
                        outputIndex=outPositions.Count; remap.Add(key,outputIndex);
                        outPositions.Add(positions[sourceIndex]);
                        outNormals.Add(normals.Length==positions.Length?normals[sourceIndex]:Vector3.up);
                        outTangents.Add(tangents.Length==positions.Length?tangents[sourceIndex]:new Vector4(1,0,0,1));
                        outUv.Add(uv.Length==positions.Length?uv[sourceIndex]:Vector2.zero);
                        outWeights.Add(weights.Length==positions.Length?weights[sourceIndex]:default);
                        outColors.Add(color);
                    }
                    triangles.Add(outputIndex);
                }
            }
            var mesh=new Mesh { indexFormat=source.indexFormat };
            mesh.SetVertices(outPositions); mesh.SetNormals(outNormals); mesh.SetTangents(outTangents);
            mesh.SetUVs(0,outUv); mesh.SetColors(outColors); mesh.SetTriangles(triangles,0,true);
            mesh.boneWeights=outWeights.ToArray(); mesh.bindposes=source.bindposes; mesh.bounds=source.bounds;
            return mesh;
        }

        static Color MaterialColor(Material material,bool guide)
        {
            Color color=Color.white;
            if(material!=null)
            {
                if(material.HasProperty("_Color")) color=material.GetColor("_Color");
                else if(material.HasProperty("_BaseColor")) color=material.GetColor("_BaseColor");
                string n=material.name.ToLowerInvariant();
                if(guide&&(n.Contains("shirt")||n.Contains("cloth")||n.Contains("vest")||n.Contains("top")))
                    color=new Color(.06f,.55f,.50f,1f);
            }
            return color;
        }

        static Bounds PoseWorldBounds(SkinnedMeshRenderer renderer)
        {
            var poseMesh=new Mesh();
            try
            {
                // Compensate for the imported FBX renderer's 100x transform scale before applying its matrix.
                renderer.BakeMesh(poseMesh,true);
                poseMesh.RecalculateBounds();
                return TransformBounds(renderer.transform,poseMesh.bounds);
            }
            finally { UnityEngine.Object.DestroyImmediate(poseMesh); }
        }

        static Bounds TransformBounds(Transform transform,Bounds local)
        {
            Vector3 min=local.min,max=local.max;
            Bounds result=new Bounds(transform.TransformPoint(min),Vector3.zero);
            for(int x=0;x<2;x++) for(int y=0;y<2;y++) for(int z=0;z<2;z++)
                result.Encapsulate(transform.TransformPoint(new Vector3(x==0?min.x:max.x,y==0?min.y:max.y,z==0?min.z:max.z)));
            return result;
        }

        static Bounds TransformBoundsRotationOnly(Transform transform,Bounds local)
        {
            Matrix4x4 matrix=Matrix4x4.TRS(transform.position,transform.rotation,Vector3.one);
            Vector3 min=local.min,max=local.max;
            Bounds result=new Bounds(matrix.MultiplyPoint3x4(min),Vector3.zero);
            for(int x=0;x<2;x++) for(int y=0;y<2;y++) for(int z=0;z<2;z++)
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(x==0?min.x:max.x,y==0?min.y:max.y,z==0?min.z:max.z)));
            return result;
        }

        static Avatar BuildGeneratedAvatar(GameObject instance,string outputName)
        {
            Transform armature=FindDescendant(instance.transform,"CharacterArmature");
            Transform armatureRoot=armature==null?null:armature.Find("Bone");
            Transform pelvis=armatureRoot==null?null:armatureRoot.Find("Body");
            Transform leftLower=pelvis==null?null:pelvis.Find("UpperLeg.L/LowerLeg.L");
            Transform rightLower=pelvis==null?null:pelvis.Find("UpperLeg.R/LowerLeg.R");
            Transform leftFoot=armatureRoot==null?null:armatureRoot.Find("Foot.L");
            Transform rightFoot=armatureRoot==null?null:armatureRoot.Find("Foot.R");
            if(armature==null||armatureRoot==null||pelvis==null||leftLower==null||rightLower==null||leftFoot==null||rightFoot==null)
                throw new InvalidOperationException(outputName+" skeleton does not match the inspected Quaternius hierarchy.");

            // Foot.L/R are skinned IK controls beside Body in the source. Humanoid requires each foot below its lower leg.
            leftFoot.SetParent(leftLower,true);
            rightFoot.SetParent(rightLower,true);
            // Avoid the source mesh object's duplicate "Body" name and make the actual common hip joint explicit.
            pelvis.name="Pelvis";

            var skeletonTransforms=new List<Transform>{instance.transform};
            skeletonTransforms.AddRange(armature.GetComponentsInChildren<Transform>(true));
            var description=new HumanDescription
            {
                human=GeneratedResidentBones(),
                skeleton=skeletonTransforms.Distinct().Select(t=>new SkeletonBone
                {
                    name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale
                }).ToArray(),
                upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,
                armStretch=.05f,legStretch=.05f,feetSpacing=0f,hasTranslationDoF=false
            };
            Avatar avatar=AvatarBuilder.BuildHumanAvatar(instance,description);
            if(avatar==null||!avatar.isValid||!avatar.isHuman)
            {
                if(avatar!=null) UnityEngine.Object.DestroyImmediate(avatar);
                throw new InvalidOperationException(outputName+" custom humanoid Avatar is invalid.");
            }
            avatar.name=outputName+" Humanoid Avatar";
            AssetDatabase.CreateAsset(avatar,GeneratedRoot+"/"+outputName+"_Avatar.asset");
            return avatar;
        }

        static HumanBone[] GeneratedResidentBones()
        {
            return new[]
            {
                Human("Hips","Pelvis"), Human("Spine","Hips"), Human("Chest","Abdomen"), Human("UpperChest","Torso"),
                Human("Neck","Neck"), Human("Head","Head"),
                Human("LeftShoulder","Shoulder.L"), Human("LeftUpperArm","UpperArm.L"), Human("LeftLowerArm","LowerArm.L"), Human("LeftHand","Fist.L"),
                Human("RightShoulder","Shoulder.R"), Human("RightUpperArm","UpperArm.R"), Human("RightLowerArm","LowerArm.R"), Human("RightHand","Fist.R"),
                Human("LeftUpperLeg","UpperLeg.L"), Human("LeftLowerLeg","LowerLeg.L"), Human("LeftFoot","Foot.L"),
                Human("RightUpperLeg","UpperLeg.R"), Human("RightLowerLeg","LowerLeg.R"), Human("RightFoot","Foot.R")
            };
        }

        static HumanBone Human(string humanName,string boneName)
        {
            return new HumanBone{humanName=humanName,boneName=boneName,limit=new HumanLimit{useDefaultValues=true}};
        }

        static Transform FindDescendant(Transform root,string name)
        {
            foreach(Transform child in root.GetComponentsInChildren<Transform>(true)) if(child.name==name) return child;
            return null;
        }

        static List<AnimationClip> FindAnimationClips()
        {
            var clips=new List<AnimationClip>();
            foreach(string guid in AssetDatabase.FindAssets("t:AnimationClip",new[]{"Assets/ThirdParty/Quaternius/Characters"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                clips.AddRange(AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(c=>!c.name.StartsWith("__preview__",StringComparison.OrdinalIgnoreCase)));
            }
            return clips.GroupBy(c=>AssetDatabase.GetAssetPath(c)+"|"+c.name,StringComparer.OrdinalIgnoreCase)
                .Select(g=>g.First()).OrderBy(c=>AssetDatabase.GetAssetPath(c),StringComparer.OrdinalIgnoreCase)
                .ThenBy(c=>c.name,StringComparer.OrdinalIgnoreCase).ToList();
        }

        static AnimationClip Pick(IEnumerable<AnimationClip> clips,string[] includes,string[] excludes)
        {
            return clips.FirstOrDefault(clip=>
            {
                string name=clip.name.ToLowerInvariant();
                return includes.Any(name.Contains)&&(excludes==null||!excludes.Any(name.Contains));
            });
        }

        static AnimationClip PickNamed(IEnumerable<AnimationClip> clips,string name)
        {
            return clips.FirstOrDefault(c=>string.Equals(c.name,name,StringComparison.OrdinalIgnoreCase)||
                c.name.EndsWith("|"+name,StringComparison.OrdinalIgnoreCase));
        }

        static AnimationClip RequireNamed(IEnumerable<AnimationClip> clips,string name)
        {
            AnimationClip clip=PickNamed(clips,name);
            if(clip==null||!clip.isHumanMotion||clip.length<=0f)
                throw new InvalidOperationException("Missing valid humanoid resident animation: "+name);
            return clip;
        }

        static string PathFrom(Transform root,Transform node)
        {
            if(node==root) return root.name;
            var parts=new Stack<string>();
            Transform current=node;
            while(current!=null&&current!=root) { parts.Push(current.name); current=current.parent; }
            return root.name+"/"+string.Join("/",parts.ToArray());
        }

        static int DepthFrom(Transform root,Transform node)
        {
            int depth=0;
            while(node!=null&&node!=root) { depth++; node=node.parent; }
            return depth;
        }

        static void EnsureFolder(string path)
        {
            string current="Assets";
            foreach(string part in path.Substring("Assets/".Length).Split('/'))
            {
                string next=current+"/"+part;
                if(!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current,part);
                current=next;
            }
        }
    }

    /// <summary>Only configures the four vendored Quaternius resident source models.</summary>
    public sealed class ResidentModelAssetPostprocessor : AssetPostprocessor
    {
        const string ModelPrefix="Assets/ThirdParty/Quaternius/Characters/Models/";
        const string AnimationPrefix="Assets/ThirdParty/Quaternius/Characters/Animations/";

        void OnPreprocessModel()
        {
            bool residentModel=assetPath.StartsWith(ModelPrefix,StringComparison.Ordinal)&&assetPath.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase);
            bool animationLibrary=assetPath.StartsWith(AnimationPrefix,StringComparison.Ordinal)&&
                (assetPath.EndsWith("/UAL1_Standard.fbx",StringComparison.Ordinal)||assetPath.EndsWith("/UAL2_Standard.fbx",StringComparison.Ordinal));
            if(!residentModel&&!animationLibrary) return;
            var importer=(ModelImporter)assetImporter;
            importer.animationType=residentModel?ModelImporterAnimationType.Generic:ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation=true;
            importer.importBlendShapes=false;
            importer.importCameras=false;
            importer.importLights=false;
            importer.isReadable=true;
            importer.meshCompression=ModelImporterMeshCompression.Medium;
            importer.optimizeGameObjects=false;
            importer.materialImportMode=residentModel?ModelImporterMaterialImportMode.ImportStandard:ModelImporterMaterialImportMode.None;
            if(residentModel) importer.materialLocation=ModelImporterMaterialLocation.InPrefab;

            if(residentModel) return;
            HumanDescription description=importer.humanDescription;
            description.human=AnimationBones();
            description.upperArmTwist=.5f; description.lowerArmTwist=.5f;
            description.upperLegTwist=.5f; description.lowerLegTwist=.5f;
            description.armStretch=.05f; description.legStretch=.05f; description.feetSpacing=0f;
            importer.humanDescription=description;
        }

        static HumanBone Bone(string human,string source)
        {
            return new HumanBone { humanName=human,boneName=source,limit=new HumanLimit { useDefaultValues=true } };
        }

        static HumanBone[] AnimationBones()
        {
            return new[]
            {
                Bone("Hips","pelvis"), Bone("Spine","spine_01"), Bone("Chest","spine_02"), Bone("UpperChest","spine_03"), Bone("Neck","neck_01"), Bone("Head","Head"),
                Bone("LeftShoulder","clavicle_l"), Bone("LeftUpperArm","upperarm_l"), Bone("LeftLowerArm","lowerarm_l"), Bone("LeftHand","hand_l"),
                Bone("RightShoulder","clavicle_r"), Bone("RightUpperArm","upperarm_r"), Bone("RightLowerArm","lowerarm_r"), Bone("RightHand","hand_r"),
                Bone("LeftUpperLeg","thigh_l"), Bone("LeftLowerLeg","calf_l"), Bone("LeftFoot","foot_l"),
                Bone("RightUpperLeg","thigh_r"), Bone("RightLowerLeg","calf_r"), Bone("RightFoot","foot_r")
            };
        }
    }
}
